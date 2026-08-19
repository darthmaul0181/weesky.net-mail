# Réutilisation des connexions IMAP — design

Un pool de connexions IMAP authentifiées, partagé entre requêtes HTTP, pour supprimer le
`connect` + TLS + `AUTHENTICATE` payé aujourd'hui par chaque requête. Le gain vise les serveurs
distants, où cette séquence coûte cinq à huit allers-retours plus une vérification de mot de
passe volontairement lente ; sur le Dovecot local elle est déjà quasi gratuite.

Le pool est un **accélérateur, jamais un goulot** : toute saturation dégrade vers le comportement
actuel — une connexion à usage unique, fermée en fin de requête. Le pire cas du pool est le
système d'aujourd'hui.

## Ce qui a été décidé avant, et ce que cette tranche révise

La tranche 2a a posé « une connexion par requête, comme Rainloop ; le pooling reste une
optimisation ultérieure, **à justifier par des mesures** »
(`2026-07-18-webmail-mail-2a-design.md`, § 3). Deux specs ultérieures s'y sont appuyées pour
écarter `IDLE` (rafraîchissement périodique, notifications de nouveau courrier).

Cette tranche lève la première décision et **ne touche pas** aux deux autres : `IDLE` et un canal
de push restent hors périmètre, arbitrés plus tard sur les mesures produites ici.

Ce que la tranche 2a pose au § 5.3 et qui reste vrai : *rien n'est conservé côté serveur entre
deux requêtes*. Ce design la conserve pour les **secrets** et la relâche pour les **sockets** —
la distinction est le cœur du modèle de menace ci-dessous.

## Les décisions

| Décision | Retenu | Pourquoi |
|---|---|---|
| Secrets au repos côté serveur | **Aucun** | Le serveur ne détient jamais de quoi se ré-authentifier. `AccountConnectionResolver.ResolveAsync` prend un `HttpRequest` parce que tous les credentials sortent du cookie — mot de passe du compte primaire, KEK des comptes connectés. Une socket ne peut donc naître que d'une requête, jamais d'un service d'arrière-plan. |
| Réouverture après perte | **Aucune** | Redéploiement, coupure, timeout serveur : la socket disparaît et la requête suivante retombe sur `OpenAsync`, le chemin d'aujourd'hui. La dégradation est le comportement actuel, pas un incident à gérer. |
| Objet mis en cache | Le `ImapClient` | `ImapSession` reste construit par requête : son `_disposed` et son `ThrowIfDisposed` gardent leur sens, et `IImapSession` ne change pas de forme. |
| Clé | `(hôte, port, username, empreinte du credential)` | Ce qui a authentifié, jamais ce que l'URL disait. C'est déjà la règle appliquée dans la requête (`ScopedImapSessionProvider.cs:30`), portée à l'échelle supérieure. |
| Forme de l'empreinte | HMAC-SHA256, clé aléatoire tirée au démarrage | Une table durable ne conserve pas de mots de passe en clair. La clé HMAC ne quitte pas la mémoire et meurt avec le processus. |
| Sockets par identité | **Plusieurs (4)**, emprunt exclusif | La rafale d'une interaction est parallèle, pas séquentielle : une socket unique la sérialiserait et pourrait être plus lente qu'aujourd'hui. |
| Chemins d'authentification | **Exclus du pool** | `UserAuthenticator` et la sonde de compte connecté doivent authentifier réellement. Poolés, un mot de passe révoqué en amont ouvrirait encore une session pendant tout le plafond de vie. |
| Inactivité | 70 s | Au-delà du poll de 60 s (`queries.ts:77`), donc la socket reste chaude tant qu'un onglet est ouvert — le premier clic après une pause en profite, et c'est là que la latence se voit. |
| Vie absolue | 15 min | Rebranche la révocation : un mot de passe changé hors de l'application reprend effet en un quart d'heure au pire, au lieu de jamais. Coût réel : une ré-authentification par quart d'heure et par identité active. |
| Santé à l'emprunt | `NOOP` si inactif > 5 s | Déterministe. Une reprise optimiste rejouerait des commandes non idempotentes (`APPEND`, `MOVE`, `STORE`) sur les quinze points d'appel de `ImapMessageCommands`. |
| Propreté au retour | **On ne ferme aucun dossier** | `CLOSE` expurge les messages marqués `\Deleted`. Désélectionner « par propreté » est le seul geste de ce chantier capable de détruire du courrier ; les commandes rouvrent déjà leur dossier. |
| SMTP | **Hors périmètre** | Un envoi est ponctuel et l'utilisateur en attend le résultat de toute façon. Gain quasi nul, risque identique. |
| Interrupteur | `PoolEnabled`, à chaud | `IOptionsMonitor` est déjà en place : désactiver en production restaure le comportement actuel sans redéploiement ni coupure de session. |

## Architecture

Le seam existe déjà et il est au bon endroit. `ScopedImapSessionProvider` est le seul objet du
système qui connaisse à la fois l'identité et la durée de vie de la requête ; c'est lui, et lui
seul, qui parle au pool.

```
Contrôleurs / dépôts
        │  (inchangés)
        ▼
IImapSessionProvider  ──►  ScopedImapSessionProvider   (scoped, portée requête)
                                    │  emprunte / rend
                                    ▼
                           IImapConnectionPool          (singleton)
                                    │  ouvre en dernier recours
                                    ▼
                           IImapConnectionFactory       (inchangée)
                                    ▲
                                    │  accès direct, jamais poolé
                     UserAuthenticator, sonde de compte connecté
```

Le pool n'est **pas** un décorateur de `IImapConnectionFactory`. C'était la forme la plus
élégante, mais elle poolait silencieusement les chemins d'authentification — un défaut invisible
et grave. Le service distinct rend l'exclusion structurelle.

### Emprunt, retour, possession

L'emprunt est exclusif : une socket empruntée n'est visible d'aucune autre requête, ce qui rend
l'entrelacement de commandes IMAP impossible plutôt qu'improbable.

1. `ScopedImapSessionProvider` emprunte à la première utilisation, au lieu d'appeler `OpenAsync` ;
2. il détient l'emprunt pour toute la requête — verrou et revalidation de clé existants inchangés ;
3. à la destruction du scope, il **rend** l'emprunt au lieu de fermer.

`ImapSession.DisposeAsync` ferme aujourd'hui le client. Il reçoit désormais *comment relâcher le
client* — fermer, ou rendre au pool — plutôt que de le décider. C'est le seul changement de
signature du chantier, et il est interne au namespace `Services`.

### Le cas OAuth

`OAuthCredential` porte un jeton court renouvelé régulièrement ; chaque renouvellement produit
une empreinte différente, donc une entrée neuve, et la socket précédente vieillit sans être
réutilisée.

C'est correct — on ne réutilise jamais une socket dont on ne peut plus prouver qu'elle correspond
au credential courant — et c'est du gaspillage sur les comptes externes OAuth. **Accepté tel
quel** : une notion d'identité stable détachée du secret est précisément ce qui rendrait possible
de servir la boîte de A à B.

## Plafonds et horloges

| Plafond | Valeur | Au-delà |
|---|---|---|
| Sockets par identité | 4 | Connexion à usage unique, sans attente |
| Sockets au total | 200 | Éviction LRU des inactives, puis usage unique |

4 par identité est dimensionné sur la rafale parallèle, et gardé loin du
`mail_max_userip_connections` de Dovecot (10 par défaut, compté par utilisateur et par IP — et le
service sort d'une seule IP, donc ces 10 sont partagés par tous les onglets de cet utilisateur).

| Horloge | Point de départ | Valeur |
|---|---|---|
| Inactivité | dernier retour au pool | 70 s |
| Vie absolue | authentification de la socket | 15 min |

Les deux sont évaluées **à l'emprunt et au retour, jamais en cours de requête**. Une socket qui
franchit son plafond absolu pendant le téléchargement d'une pièce jointe de 20 Mo n'est pas
coupée : ce serait transformer une garantie de sécurité en bug visible. Elle est refusée au
retour et fermée. À l'emprunt, une socket hors délai est fermée et remplacée par une connexion
neuve — la requête paie le coût d'aujourd'hui.

### Balayage

Sans balayeur, une socket dont l'utilisateur a fermé l'onglet ne serait fermée qu'au prochain
emprunt, c'est-à-dire jamais, et le TTL n'aurait aucun effet.

Un `IHostedService` sur `PeriodicTimer`, cadence 15 s, sur le patron des balayeurs existants
(`StagedAttachmentSweeper`, `TrustedSenderSweeper`). Fermeture propre par `LOGOUT`. Aucune
exception ne remonte : l'échec de fermeture d'une socket n'arrête ni le balayage des autres, ni
l'hôte.

## Invalidation

La clé primaire est l'empreinte du credential, mais un utilisateur possède plusieurs identités —
compte primaire et chaque compte connecté, chacun son secret. Le pool porte donc un **index
secondaire par utilisateur**, qui ne contient que l'identifiant : aucun secret, rien qui ne soit
déjà dans les journaux existants.

| Événement | Effet | Pourquoi |
|---|---|---|
| `DELETE /Login` | Ferme les sockets de l'empreinte courante | Rangement de bonne foi. Le JWT reste valide jusqu'à expiration : ce n'est pas une révocation et ne doit pas être présenté comme telle. |
| `DELETE /Login/All` | Ferme **toutes** les sockets de l'utilisateur, via l'index secondaire | Celui-ci est la révocation : il fait tourner le tampon de sécurité, donc plus aucun emprunt n'est possible. Sans purge, les sockets ouvertes survivraient 15 min à un geste dont l'intention est « une session est entre les mains de quelqu'un d'autre ». |
| Mot de passe changé hors de l'application | Rien d'immédiat | Personne ne nous prévient. C'est ce que borne le plafond de vie absolue, et sa seule justification. |

## Configuration

Cinq réglages dans `MailOptions`, rechargés à chaud par `IOptionsMonitor` :

| Réglage | Défaut |
|---|---|
| `PoolEnabled` | `true` |
| `PoolIdleSeconds` | `70` |
| `PoolMaxLifetimeMinutes` | `15` |
| `PoolMaxPerIdentity` | `4` |
| `PoolMaxTotal` | `200` |

## Journalisation

**L'empreinte n'est jamais journalisée.** Elle n'est pas réversible — clé HMAC tirée au
démarrage, jamais persistée — mais l'écrire permettrait de corréler l'activité d'un utilisateur
entre lignes de journal, sans rien apporter que le nom d'utilisateur n'apporte déjà. Le nom
d'utilisateur reste autorisé : `MailConnectionFactory` en journalise déjà.

Compteurs, et non événements : taille du pool, taux de réutilisation, fermetures par inactivité /
plafond absolu / éviction, échecs de `NOOP`, et **connexions à usage unique servies par
saturation**. Ce dernier est le signal d'alarme : s'il n'est pas proche de zéro, les plafonds sont
mal dimensionnés et le pool ne sert à rien.

## Tests

`ImapSessionDisposeTests` monte déjà un vrai serveur IMAP scripté sur une socket TCP locale
(`SilentLogoutImapServer`). Un serveur de ce type qui compte les commandes reçues rend testable
tout ce qui compte, sans mock du protocole.

1. **Isolation entre identités** — un second credential ne reçoit jamais la socket du premier.
   Le test qui protège contre la seule faute vraiment grave du chantier ; à écrire en premier.
2. **Réutilisation** — N emprunts successifs dans la fenêtre : un seul `AUTHENTICATE`. Au-delà du
   plafond absolu, deux.
3. **Aucune expurgation** — un message marqué `\Deleted` survit à un cycle retour/emprunt, et le
   serveur ne reçoit aucun `CLOSE`.
4. **Parallélisme** — 4 emprunts simultanés sur une identité obtiennent 4 sockets distinctes ; le
   5ᵉ obtient une connexion à usage unique **sans attendre**. On assère l'absence d'attente.
5. **Socket morte** — le serveur cesse de répondre : le `NOOP` d'emprunt le détecte, une connexion
   neuve prend le relais, la commande métier s'exécute **exactement une fois**.
6. **Balayage** — après le TTL, le serveur observe un `LOGOUT` sans qu'aucune requête ait eu lieu.
7. **Vie absolue non appliquée en cours de requête** — une opération longue traverse son plafond
   et se termine normalement.
8. **`PoolEnabled=false`** — une authentification par requête, à l'identique d'aujourd'hui.
9. **`LogoutEverywhere`** — les sockets des comptes primaire et connectés sont fermées
   immédiatement.

## Hors périmètre

- **`IDLE` et canal de push** — arbitrés plus tard, sur les mesures ci-dessous. Aucun SSE,
  WebSocket ou SignalR n'existe dans le projet : `IDLE` fait remonter l'événement jusqu'au
  backend, le pousser jusqu'à l'onglet est un second chantier.
- **Pooling SMTP.**
- **Secrets détenus côté serveur entre requêtes**, sous quelque forme que ce soit.

## Ce qu'on mesure, et ce que ça arbitre

Ces mesures ne sont pas un bonus : la tranche `IDLE` a été renvoyée à « décidée sur des
chiffres », et ce sont ces chiffres.

- Décomposition d'une requête distante en `connect` / TLS / `AUTHENTICATE` / première commande
  utile — le seul moyen de savoir si le pool attaque le bon poste.
- Taux de réutilisation réel, par identité et global.
- Sockets simultanées en pointe, à confronter aux limites du serveur.

Si la réutilisation est bonne et la latence perçue redevient acceptable sur les serveurs
distants, `IDLE` ne se discutera plus que sur le temps réel — c'est-à-dire sur une
fonctionnalité, pas sur une performance. C'est un bien meilleur terrain de décision.
