# Webmail weesky — Sous-projet 2, tranche 2a : Dossiers & lecture

**Date :** 2026-07-18
**Statut :** design validé, prêt pour la planification d'implémentation
**Dépend de :** sous-projet 1 (shell), branche `webmail` (`24e5a05`)

**Branche :** le travail se poursuit sur `webmail`. `master` est la production ; le merge
n'aura lieu qu'une fois **l'ensemble du webmail** implémenté (tranches 2a à 2d). La CI déploie
donc `webmail` en continu sur l'environnement de développement
(`account-dev.mail.weesky.net`, service `snoopy.microservice-dev`), ce qui donne un
environnement de recette permanent pour chaque tranche.

---

## 1. Contexte

Le shell applicatif est livré : routing, rail vertical, contextes Auth/Theme, contrat de
tokens à deux palettes, et le portage des pages Alias/Règles/Admin/Compte. La route
`/mail` affiche un écran « à venir ».

L'objectif du sous-projet 2 est un client mail complet, aux fonctionnalités équivalentes à
Rainloop/Snappymail, dans le langage visuel mis en place par le shell.

L'inventaire réel de ce périmètre — arborescence de dossiers et leur gestion, liste paginée,
recherche, threads, lecture HTML assainie, pièces jointes, rédaction, brouillons, identités,
signatures, drapeaux, déplacements, sélection multiple, raccourcis clavier, multi-comptes —
représente quatre à six fois le sous-projet shell. Il est donc découpé en quatre tranches,
chacune avec son cycle spec → plan → implémentation, chacune utilisable seule.

**Ce document couvre la tranche 2a uniquement.**

---

## 2. Découpage du sous-projet 2

| Tranche | Contenu | Livrable |
|---|---|---|
| **2a** | Connexion IMAP, arborescence de dossiers + création/renommage/suppression + visibilité (abonnements), liste de messages paginée, volet de lecture, pièces jointes, layout 3 panneaux, couche de données | **Un webmail consultable** |
| 2b | Drapeaux (lu/non-lu, suivi), déplacement/copie, corbeille, archivage, indésirables, sélection multiple, recherche IMAP | Un webmail où l'on organise |
| 2c | Rédaction avec **éditeur riche** (police, taille, couleur, listes, liens — niveau Outlook), identités (dérivées des alias), pièces jointes sortantes, brouillons, réponse/réponse à tous/transfert, signatures, envoi SMTP | Un webmail complet |
| 2d | Domaines additionnels (CRUD admin), liaison de comptes, stockage chiffré des credentials externes, bascule dans le menu d'avatar | Le multi-comptes |

---

## 3. Décisions de conception validées

| Sujet | Décision |
|---|---|
| Dialogue serveur mail | **Le backend seul** parle IMAP/SMTP. Le frontend ne parle qu'au backend, en REST/JSON |
| Agnosticisme serveur | **Aucune connaissance de la configuration Dovecot.** Tout fait spécifique au serveur est découvert à l'exécution via les capacités IMAP |
| Configuration | Seule la **configuration de connexion** existe (hôte, port, mode TLS). Le serveur maison est une entrée pré-remplie de la même forme que celle qu'un admin saisira en 2d |
| Authentification IMAP | **Credentials de l'utilisateur**, capturés à la connexion, chiffrés via Data Protection dans un second cookie — le modèle de Rainloop/Snappymail. Un seul chemin d'authentification, identique pour le serveur maison et les serveurs externes (§ 5.3) |
| Master user | **Écarté.** Il ne fonctionne que sur le serveur maison et imposerait deux chemins d'authentification. ManageSieve le garde pour les règles Sieve — c'est un cas distinct |
| Connexions IMAP | **Une connexion par requête**, comme Rainloop. Le pooling reste une optimisation ultérieure, à justifier par des mesures |
| Couche de données frontend | **TanStack Query** — 4ᵉ dépendance runtime du projet |
| Rendu HTML | Assainissement **côté backend**, rendu dans une **iframe sandboxée** côté frontend. Images distantes bloquées par défaut |
| Connexion | **Entièrement configurable** : hôte, port, mode de sécurité (`SslOnConnect`/`StartTls`/…), délai, certificats invalides — pour IMAP comme pour la soumission. Serveur maison : `mail.weesky.net`, IMAP 143 STARTTLS, soumission 587 STARTTLS. Rechargement à chaud via `IOptionsMonitor` (§ 5.2) |

---

## 4. Périmètre de la tranche 2a

**Dans le périmètre**

- Client IMAP backend (MailKit) et gestion des credentials de session
- Arborescence de dossiers : lecture, hiérarchie, dossiers spéciaux, compteurs
- Gestion des dossiers : création, renommage/déplacement, suppression
- Visibilité des dossiers : abonnement / désabonnement (`SUBSCRIBE`/`UNSUBSCRIBE`)
- Liste de messages paginée (enveloppes seules)
- Volet de lecture : corps HTML assaini et texte brut, en-têtes, liste des pièces jointes
- Téléchargement de pièces jointes
- Layout 3 panneaux du module mail
- Couche de données TanStack Query
- Fondations : extension de `request()`, renouvellement glissant de session (§ 8)

**Hors périmètre**

- Toute modification d'état d'un message (drapeaux, déplacement, suppression) — 2b
- Recherche — 2b
- Rédaction et envoi — 2c
- Multi-comptes et domaines additionnels — 2d
- Threads de conversation, IDLE / rafraîchissement temps réel, raccourcis clavier — à répartir
  entre 2b et 2c lors de leurs specs respectives

Le volet de lecture de 2a est **en lecture seule** : aucun bouton d'action n'y figure encore.

---

## 5. Architecture backend

### 5.1 Agnosticisme : ce qui est découvert, ce qui est configuré

Rien de ce qui dépend du serveur n'est écrit en dur ni configuré. Tout est interrogé à
l'ouverture de connexion :

| Fait | Source |
|---|---|
| Séparateur de hiérarchie | `client.PersonalNamespaces[0].DirectorySeparator` |
| Préfixe de namespace | commande `NAMESPACE` |
| Dossiers spéciaux (Sent, Drafts, Trash, Junk, Archive) | `SPECIAL-USE` / `XLIST`, **avec repli sur une correspondance par nom** si le serveur ne les annonce pas |
| Mécanismes d'authentification | `client.AuthenticationMechanisms` |
| Capacités (`MOVE`, `UIDPLUS`, `SORT`…) | `client.Capabilities` |
| Format de stockage | sans objet — invisible depuis IMAP |

**C'est la contrainte structurante de la tranche.** Un compte additionnel (2d) pointe vers un
serveur arbitraire dont nous n'aurons jamais la configuration. Une architecture qui a besoin
de connaître le serveur est fausse dès 2d ; elle doit donc être agnostique dès 2a, sous peine
d'écrire le module deux fois.

### 5.2 Modèle de connexion

Une seule structure décrit un compte mail, quelle que soit sa provenance :

```
MailAccountConnection { Host, Port, SecurityMode, Username, Password }
```

Le serveur maison est une instance **pré-remplie depuis `appsettings`** (hôte, port, mode TLS)
complétée par les credentials de session ; en 2d, un domaine additionnel fournira les mêmes
champs depuis la base. Une seule structure, un seul chemin de code.

Section `appsettings.json`, modelée sur la section `Sieve` existante :

```json
"Mail": {
  "ImapHost": "mail.weesky.net",
  "ImapPort": 143,
  "ImapSecurity": "StartTls",
  "SmtpHost": "mail.weesky.net",
  "SmtpPort": 587,
  "SmtpSecurity": "StartTls",
  "TimeoutSeconds": 30,
  "AllowInvalidCertificate": false
}
```

Ce sont les **valeurs réelles du serveur maison** : IMAP sur 143 avec STARTTLS, soumission sur
587 avec STARTTLS. Pas de TLS implicite sur 993.

(`SmtpHost`/`SmtpPort`/`SmtpSecurity` sont posés dès maintenant mais consommés en 2c.)

**Aucune valeur de connexion n'est écrite en dur.** Hôte, port, mode de sécurité, délai
d'expiration et tolérance aux certificats invalides sont tous configurables, pour IMAP comme
pour la soumission. Les valeurs ci-dessus sont des valeurs par défaut, pas des constantes.

**`StartTls`, pas `StartTlsWhenAvailable`.** Le premier échoue si le serveur n'annonce pas
STARTTLS ; le second se rabat silencieusement sur une connexion en clair. Sur le port 143,
qui accepte aussi le trafic non chiffré, cette distinction décide si un défaut de
configuration expose les credentials en clair sur le réseau. C'est le mode exigeant qui est
retenu, et un serveur additionnel (2d) ne devra être configuré en `StartTlsWhenAvailable` que
délibérément.

`ImapSecurity`/`SmtpSecurity` se lient sur l'énumération `SecureSocketOptions` de MailKit et
acceptent donc `None`, `Auto`, `SslOnConnect` (TLS implicite), `StartTls` (STARTTLS exigé) et
`StartTlsWhenAvailable` (STARTTLS opportuniste). Passer d'un serveur en 993 implicite à un
serveur en 143 + STARTTLS est un changement de configuration, jamais un changement de code.

**Rechargement à chaud.** Les autres options du service utilisent `IOptions<T>`, figé au
démarrage. Ici les options sont consommées via **`IOptionsMonitor<MailOptions>`**, de sorte
qu'une correction dans `appsettings.json` prenne effet sans redémarrer le service — donc sans
interrompre les sessions en cours. C'est un écart assumé à la convention existante, justifié
par le fait que ces valeurs seront ajustées en exploitation, contrairement aux autres.

### 5.3 Credentials de session — mécanisme et modèle de menace

Le mot de passe utilisateur est nécessaire pour ouvrir IMAP et n'est **pas récupérable en
base** : MariaDB stocke du SHA-512 crypt produit par les triggers. Il est donc capturé au
moment de la connexion, où `LoginController` l'a déjà en main.

**Mécanisme :** ASP.NET Core Data Protection (`IDataProtector`, purpose `"weesky.imap.credentials"`).

1. `POST /api/Login` chiffre le mot de passe et le pose dans un second cookie
   `HttpOnly; Secure; SameSite=Strict`, de durée alignée sur le cookie JWT.
2. Chaque requête mail déchiffre le cookie pour ouvrir IMAP. Rien n'est conservé côté serveur
   entre deux requêtes.
3. `DELETE /api/Login` supprime **les deux** cookies.
4. Le key ring est persisté (`PersistKeysToFileSystem`) dans le répertoire de service, afin
   que les redémarrages — donc chaque déploiement — n'invalident pas les sessions.
5. Un échec de déchiffrement (rotation de clé, key ring effacé) renvoie **401 avec un code
   distinguable** (`ResultEnveloppe` portant `credentials_unavailable`), sur lequel le
   frontend force une reconnexion propre plutôt que d'afficher des erreurs IMAP opaques.

#### Modèle de menace

**Ce qui change par rapport à aujourd'hui.** La base ne contient qu'un SHA-512 crypt : une
fonction à sens unique. Un attaquant qui vole intégralement MariaDB n'obtient aucun mot de
passe utilisable, seulement des empreintes à casser. À partir de cette tranche, il existe
pendant la durée d'une session un **chemin réversible** entre ce que le système détient et le
mot de passe réel de l'utilisateur. Pour l'exploiter il faut réunir **le key ring** (disque du
serveur) **et le cookie chiffré** (navigateur de l'utilisateur, ou capté en vol) ; avec les
deux, on obtient des credentials qui ouvrent IMAP, la soumission SMTP et le webmail.

**Cette propriété est inhérente à tout webmail qui proxifie IMAP**, pas à cette
implémentation. Un serveur qui ouvre une connexion IMAP au nom d'un utilisateur doit détenir
de quoi s'authentifier comme lui.

| Webmail | Modèle |
|---|---|
| **Rainloop / Snappymail** | Credentials chiffrés stockés **côté client**, dans un cookie — identique à ce qui est retenu ici |
| **Roundcube** | Mot de passe chiffré avec une clé de configuration, stocké dans la **session serveur** : le serveur détient clé et chiffré au repos |
| **SOGo** | Credentials en session serveur, même famille |
| **Gmail / Outlook web** | Non comparable : ils possèdent le stockage des messages, il n'y a aucun IMAP à proxifier |

**Propriété de la conception retenue :** le serveur ne détient **jamais les deux moitiés au
repos** — la clé est sur son disque, le chiffré est dans le navigateur. Une compromission de
sauvegarde ou de disque seul ne donne rien. C'est un cran plus solide que le modèle Roundcube.

**Les seules alternatives réelles**, pour mémoire :

- **Master user / PREAUTH** — ne fonctionne que sur un serveur que l'on contrôle, donc jamais
  pour un compte additionnel (2d). Imposerait deux chemins d'authentification. Écarté.
- **OAuth2 / XOAUTH2** — un jeton limité et révocable remplace le mot de passe ; Dovecot sait
  faire `OAUTHBEARER`. Suppose de monter un fournisseur OAuth pour le serveur maison, et ne
  résout rien pour un serveur externe arbitraire, où l'on retombe sur un mot de passe. Hors de
  proportion aujourd'hui ; à reconsidérer si le besoin apparaît.

#### Le key ring : de quoi il s'agit

Data Protection n'utilise pas *une* clé de chiffrement mais un **ensemble de clés** — un
trousseau, d'où le terme *key ring*.

À un instant donné, une seule clé du trousseau est **active** : c'est elle qui chiffre les
nouvelles données. Toutes les autres restent présentes uniquement pour **déchiffrer** ce qui a
été chiffré de leur vivant. Chaque clé porte un identifiant (GUID) et des dates de création,
d'activation et d'expiration — et, mécanisme central, **chaque donnée chiffrée embarque dans
son en-tête le GUID de la clé qui l'a produite**. Pour déchiffrer, Data Protection lit ce
GUID, cherche la clé correspondante dans le trousseau, et s'en sert.

Physiquement, avec `PersistKeysToFileSystem`, c'est un répertoire contenant un fichier XML par
clé :

```
/var/lib/snoopy.microservice/keys/
  key-9c2f7a13-4e8b-4a21-b0d5-1f2c3e4a5b6c.xml
  key-b41e0d88-7c93-4f10-8e22-9a0b1c2d3e4f.xml
```

Chaque fichier contient les métadonnées de la clé et son matériel secret (`<masterKey><value>`,
en base64). Sur Windows ce matériel serait enveloppé par DPAPI ; **sur Linux il est en clair**
— c'est la raison pour laquelle le permissionnement du répertoire est le contrôle qui compte.

Trois conséquences directes pour cette tranche :

- **La rotation ne casse rien.** Tous les 90 jours une nouvelle clé devient active, mais
  l'ancienne reste dans le trousseau : un cookie chiffré la semaine précédente se déchiffre
  toujours. C'est précisément parce qu'il s'agit d'un trousseau et non d'une clé unique.
- **La perte du trousseau casse tout.** Si le répertoire disparaît ou change de place,
  l'application en crée un nouveau, vide ; les cookies existants nomment des clés introuvables.
- **Partager le trousseau, c'est partager le pouvoir de déchiffrer** — d'où l'exigence de
  trousseaux distincts entre production et développement.

Tout ce qui suit se ramène donc à une seule question : **où vit ce répertoire, et qui peut le
lire ?**

#### Persistance du key ring

Le mécanisme repose sur un trousseau qui survit aux redémarrages. S'il est perdu, tous les
cookies de credentials deviennent indéchiffrables.

**Le comportement par défaut fonctionne, mais par accident.** Data Protection cherche
successivement Azure, IIS, puis `$HOME/.aspnet/DataProtection-Keys`, et à défaut génère des
clés éphémères en mémoire. L'unité systemd déclare `User=root` et systemd renseigne `$HOME`
depuis la base utilisateurs dès que `User=` est présent : les clés atterrissent donc
aujourd'hui dans `/root/.aspnet/DataProtection-Keys` et **persistent correctement**.

Le risque n'est donc pas une déconnexion imminente, il est plus sournois : cet emplacement est
**implicite, non documenté et non maîtrisé**. Le jour où quelqu'un durcit le service en
remplaçant `User=root` par un utilisateur dédié — ce qui serait une bonne chose —, les clés
changent de place silencieusement et toutes les sessions mail tombent sans cause évidente.
L'objet de ce qui suit est de rendre l'emplacement explicite et stable, pas de parer à une
catastrophe imminente.

**Le key ring ne doit pas vivre sous le répertoire de déploiement.** Le `tar` d'extraction ne
supprime rien, mais les étapes suivantes du déploiement appliquent
`find $DEPLOY_PATH -type f -exec chmod 660` et `chown -R root:$DEPLOY_USER` **récursivement** :
les fichiers de clés verraient leurs droits et leur propriétaire réécrits à chaque livraison.
Correct par accident aujourd'hui, cassé silencieusement demain.

**Mécanisme retenu : `StateDirectory=` de systemd.**

```ini
StateDirectory=snoopy.microservice
StateDirectoryMode=0700
```

systemd crée `/var/lib/snoopy.microservice`, l'attribue au `User=` du service, le préserve
entre les redémarrages et expose son chemin dans `$STATE_DIRECTORY`. Il est hors du chemin de
déploiement, donc intouché par le `tar`, le `chmod -R` et le `chown -R`.

```csharp
var stateDir = Environment.GetEnvironmentVariable("STATE_DIRECTORY")?.Split(':')[0];

// Le repli est réservé au développement : WorkingDirectory pointe sur le répertoire de
// déploiement, où les clés ne doivent jamais atterrir (chmod/chown récursifs à chaque livraison).
if (string.IsNullOrEmpty(stateDir) && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException(
        "STATE_DIRECTORY is not set. Add StateDirectory= to the systemd unit.");

var keyRing = string.IsNullOrEmpty(stateDir)
    ? Path.Combine(builder.Environment.ContentRootPath, "keys")   // développement uniquement
    : Path.Combine(stateDir, "keys");

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyRing))
    .SetApplicationName($"snoopy.microservice.{builder.Environment.EnvironmentName}");
```

**Garde-fou au démarrage.** Hors développement, l'absence de `STATE_DIRECTORY` ou un répertoire
non accessible en écriture fait **échouer le démarrage**. Un échec bruyant vaut mieux qu'un
repli silencieux dont la conséquence n'apparaîtrait qu'au déploiement suivant.

L'unité porte `Restart=always` et `RestartSec=60` : une mauvaise configuration produira donc
une boucle de redémarrage toutes les minutes, visible dans syslog. C'est le comportement voulu
— à ne pas prendre pour un défaut.

**Isolation dev / prod.** `snoopy.microservice` et `snoopy.microservice-dev` cohabitent sur le
même hôte et doivent avoir des key rings distincts — sinon une compromission de
l'environnement de développement permettrait de déchiffrer les cookies de production.
`StateDirectory=` le garantit par le nom de l'unité ; le `SetApplicationName` dérivé de
l'environnement est une seconde barrière.

**Les clés au repos.** Linux n'a pas d'équivalent DPAPI : les clés sont stockées en XML clair.
`ProtectKeysWithCertificate` existe mais déplace le problème — la clé privée du certificat vit
sur le même disque, lisible par le même service. Le contrôle réaliste est le permissionnement
(`StateDirectoryMode=0700` et un `User=` dédié). Le chiffrement par certificat ne protégerait
que d'un attaquant capable de lire des fichiers sans pouvoir s'exécuter en tant que service :
il est **écarté** comme une couche qui rassure sans protéger.

**Rotation et perte.** La rotation automatique (90 jours) n'invalide rien : elle ajoute une
clé active et conserve les anciennes pour le déchiffrement. Seule la perte du ring casse les
sessions, et elle est récupérable — échec de déchiffrement → 401 `credentials_unavailable` →
reconnexion propre. Coût réel : une reconnexion pour tout le monde, pas une perte de données.
**Le key ring n'est donc pas sauvegardé** : une copie de sauvegarde serait un second exemplaire
de clés déchiffrant des credentials, pour un bénéfice qui vaut une reconnexion.

**Prérequis serveur.** `StateDirectory=` vit dans l'unité systemd, qui n'est pas versionnée et
le restera (décision assumée). C'est donc une **modification manuelle côté serveur**, à
appliquer aux **deux** unités (`snoopy.microservice` et `snoopy.microservice-dev`) avant le
premier déploiement de cette tranche, que le plan portera comme une tâche explicite et non
comme du code. Vérification associée : le répertoire existe, appartient au bon utilisateur, et
le service journalise au démarrage le chemin de key ring effectivement retenu.

Migration : les clés existantes éventuellement présentes sous `/root/.aspnet/DataProtection-Keys`
ne sont **pas** reprises. Rien n'en dépend aujourd'hui — aucun composant du service n'utilise
Data Protection avant cette tranche — donc il n'y a rien à migrer.

#### Configuration : ce qui va où

`EnvironmentFile=/etc/snoopy.microservice/secrets.env` est le mécanisme d'injection des
secrets, hors du répertoire de déploiement — d'où leur survie au `tar`. ASP.NET Core mappe
`Sieve__MasterPassword` sur `Sieve:MasterPassword`.

**Les variables d'environnement priment sur `appsettings.json` et ne sont lues qu'au
démarrage.** Une valeur placée dans `secrets.env` échapperait donc au rechargement à chaud
d'`IOptionsMonitor` (§ 5.2). Règle qui en découle : **les valeurs de connexion `Mail:*` restent
dans `appsettings.json`** — elles ne sont pas secrètes, et c'est ce qui rend leur ajustement
possible sans redémarrage. `secrets.env` ne reçoit que ce qui est réellement secret.

#### Posture connue

Le service tourne en `User=root`. Ce n'est pas introduit par cette tranche, mais le rapport
risque/bénéfice change quand le processus détient des credentials utilisateurs déchiffrables.
Un utilisateur dédié serait un durcissement réel ; il impliquerait d'accorder le `chown -R
root:$DEPLOY_USER` du déploiement et le `User=` de l'unité, ce qui dépasse le périmètre de 2a.
Consigné ici pour que la décision reste visible plutôt que oubliée.

### 5.4 Structure du code

Conventions existantes à suivre à la lettre (`Controllers → Repositories → Services`,
`Result<T>`, `ApiBaseController.FromResult`, options non validées + garde à l'appel,
502 pour une défaillance de système externe) :

```
Services/
  IImapConnectionFactory.cs / ImapConnectionFactory.cs   ouvre et authentifie un ImapClient
  IMailCredentialStore.cs   / MailCredentialStore.cs     chiffre/déchiffre le cookie
  IHtmlSanitizer.cs         / HtmlSanitizer.cs           assainissement du corps HTML
Repositories/
  IMailFolderRepository.cs  / MailFolderRepository.cs    arborescence + CRUD + abonnements
  IMailMessageRepository.cs / MailMessageRepository.cs   liste, message, pièces jointes
Models/Mail/
  MailOptions, MailFolderNode, MailMessageSummary, MailMessageDetail,
  MailAttachmentInfo, MailFolderPage
Controllers/
  MailController.cs
```

`ImapConnectionFactory` renvoie un `Result<IImapSession>` sur le modèle exact de
`IManageSieveClient.OpenSessionAsync` : garde sur options non configurées, message générique
côté client et détail journalisé, transfert de propriété de la connexion à la session,
`await using` par méthode de repository.

**Seam de test :** `ManageSieveClient` (socket/TLS/SASL) n'a aucun test unitaire, la logique
testable ayant été isolée dans une classe travaillant sur un `Stream`. On reproduit ce
découpage : les repositories dépendent d'une interface `IImapSession` mockable ; la fabrique
concrète, elle, n'est pas testée unitairement.

### 5.5 Endpoints

Tous sous `[Authorize]`, route `api/[controller]`.

| Verbe | Route | Rôle |
|---|---|---|
| `GET` | `/api/Mail/Folders` | arborescence complète : chemin, nom affiché, enfants, rôle spécial, abonné, total, non-lus, `UidValidity` |
| `POST` | `/api/Mail/Folders` | création — corps `{ parentPath, name }` |
| `PUT` | `/api/Mail/Folders` | renommage/déplacement — corps `{ path, newParentPath, newName }` |
| `DELETE` | `/api/Mail/Folders` | suppression — corps `{ path }` |
| `PUT` | `/api/Mail/Folders/Subscription` | visibilité — corps `{ path, subscribed }` |
| `GET` | `/api/Mail/Messages?folder=&page=&pageSize=` | page d'enveloppes, du plus récent au plus ancien |
| `GET` | `/api/Mail/Messages/Detail?folder=&uid=` | message complet : HTML assaini, texte brut, en-têtes, pièces jointes |
| `GET` | `/api/Mail/Messages/Attachment?folder=&uid=&index=` | flux binaire, `Content-Disposition: attachment` |

**Le chemin de dossier voyage en corps ou en query, jamais en segment de route** : le
séparateur de hiérarchie peut être `/`, ce qui casserait le routage. C'est une contrainte
d'API, pas un détail d'implémentation.

**`UidValidity` accompagne chaque réponse liée à un dossier.** Si le serveur la change, les
UID mis en cache côté client deviennent invalides et le frontend doit vider ce dossier de son
cache. Sans cela, un webmail affiche des messages faux après une opération serveur.

### 5.6 Assainissement du HTML

Le corps HTML d'un message est du contenu hostile par construction.

- **Backend** : assainissement par liste blanche avant sérialisation — suppression des
  `<script>`, gestionnaires `on*`, `<iframe>`, `<object>`, `<embed>`, `<form>`, des URL
  `javascript:`/`data:` (hors images inline), et des CSS d'échappement de conteneur.
- **Images distantes bloquées par défaut** : chaque `src` externe est déplacé vers
  `data-blocked-src` et le nombre d'images bloquées est renvoyé, afin que le frontend propose
  « afficher les images » sans nouvel aller-retour serveur.
- **Frontend** : rendu dans une `<iframe sandbox>` sans `allow-same-origin` ni
  `allow-scripts`, jamais en `dangerouslySetInnerHTML`. Deux barrières indépendantes.

La bibliothèque d'assainissement est le seul ajout NuGet à décider au moment du plan (candidat :
`HtmlSanitizer` de mganss) ; MailKit/MimeKit sont les autres.

**La liste blanche est un contrat partagé avec la tranche 2c.** L'éditeur riche de la rédaction
devra produire du HTML qui passe ce même filtre, et la citation d'un message dans une réponse
ou un transfert fait transiter du contenu assaini vers l'éditeur puis de nouveau vers l'envoi.
La liste blanche définie ici est donc conçue comme un **sous-ensemble commun** à la lecture et
à l'écriture, pas comme un réglage local au lecteur — voir § 6.5.

---

## 6. Architecture frontend

### 6.1 Layout 3 panneaux

Le shell fournit un unique `<Outlet/>` dans `.app-content` ; **la colonne contextuelle est à
la charge du module** (c'est ainsi que `SettingsLayout` procède). `MailLayout` construit donc
ses trois colonnes à l'intérieur :

```
src/modules/mail/
  MailLayout.tsx          3 colonnes, en TypeScript
  folders/FolderTree.tsx  arborescence, repli, menu contextuel, dialogues CRUD
  list/MessageList.tsx    liste virtualisée/paginée
  reader/MessageReader.tsx iframe sandboxée, en-têtes, pièces jointes
  api/mailApi.ts          appels typés
  queries.ts              clés et hooks TanStack Query
```

Deux contraintes du shell à respecter : `.app-content` porte `overflow: auto` — `MailLayout`
doit le neutraliser sur son conteneur pour obtenir trois colonnes à défilement indépendant ;
et `.app-shell` fait `height: 100vh`, donc `height: 100%` se propage correctement.

Route : `/mail` devient un layout avec enfants (`/mail/:folderPath?`), le dossier courant
vivant dans l'URL — c'est le bénéfice du routing acquis en sous-projet 1 (liens profonds,
bouton retour).

### 6.2 Couche de données

TanStack Query, avec des clés portant **l'identifiant du compte actif dès maintenant**
(`useAuth().activeAccount.id`), afin que 2d n'impose aucune réécriture :

```
['mail', accountId, 'folders']
['mail', accountId, 'messages', folderPath, page]
['mail', accountId, 'message', folderPath, uid]
```

Ce que la bibliothèque nous apporte ici, et que le `useEffect` maison actuel ne donne pas :
invalidation croisée (une action sur un message doit rafraîchir la ligne **et** le compteur du
dossier), déduplication, annulation lors d'un changement rapide de dossier,
stale-while-revalidate, et pagination.

### 6.3 Tokens à ajouter

Le contrat de tokens impose qu'un token nomme un rôle, jamais une couleur, et qu'un ajout soit
décliné dans **les deux palettes × les deux modes**. `--accent-unread` existe déjà et n'a
aucun consommateur : il a été provisionné pour ce module.

À ajouter : `--list-row-hover`, `--list-row-selected-bg`, `--list-row-selected-fg`,
`--list-row-unread-bg`, `--list-separator`, `--badge-count-bg`, `--badge-count-fg`,
`--reader-header-border`, `--quote-text`, `--attachment-chip-bg`.

Le CSS du module va dans un nouveau `src/styles/mail.css` — `index.css` fait 2225 lignes et ne
doit plus grossir.

### 6.4 Identité visuelle

Aucune nouvelle décision esthétique : la vue mail **est** la maquette validée au sous-projet 1.
Le rail vertical, la colonne de dossiers, la liste avec ses pastilles de non-lus et le volet de
lecture y figuraient déjà ; le shell n'en construisait que le cadre. `--accent-unread`
(`#e2674a` en palette night) est précisément la pastille corail de cette maquette, provisionnée
alors et sans consommateur depuis — cette tranche est celle où elle sert.

### 6.5 Éditeur riche — contraintes posées ici, réalisation en 2c

La rédaction appartient à 2c, mais deux de ses contraintes se décident maintenant parce
qu'elles engagent le lecteur de 2a.

**Le HTML d'un email n'est pas du HTML web.** Les clients destinataires — Outlook desktop en
tête, qui s'appuie sur le moteur de rendu de Word — ignorent les feuilles de style externes,
traitent mal les balises `<style>` et ne comprennent qu'un sous-ensemble ancien de CSS. Un
message doit donc reposer sur des **styles en ligne** et un jeu de balises restreint. Choisir
une police dans l'éditeur est trivial ; faire en sorte que le destinataire la voie exige une
étape de **sérialisation vers du HTML email** (inlining du CSS, restriction aux propriétés
supportées). C'est cette étape qui fait le travail, pas la barre d'outils.

**Conséquences pour 2a :** la liste blanche du sanitizer (§ 5.6) est le contrat commun aux deux
tranches. Une réponse ou un transfert fait transiter le corps assaini du message d'origine vers
l'éditeur, puis de nouveau vers l'envoi ; si les deux extrémités ne s'accordent pas sur le même
sous-ensemble, le formatage se dégrade à chaque aller-retour.

**Orientation, à confirmer dans la spec 2c :** TipTap (ProseMirror). Headless — la barre
d'outils reste dans notre langage visuel au lieu d'importer celui d'une bibliothèque —,
extensions activables à la carte pour n'autoriser que ce qui survit en email, et sortie HTML
propre à passer dans l'inliner. Ce serait la 5ᵉ dépendance runtime du projet.

À traiter également en 2c : la génération de la **partie texte brut** du `multipart/alternative`
à partir du contenu riche.

### 6.6 Icônes

`src/icons/` compte 9 icônes en 20×20 sans props. Le module mail en demande beaucoup plus
(dossier, dossier ouvert, pièce jointe, chevron, enveloppe ouverte/fermée, corbeille, actualiser)
et à des tailles variées. **Les icônes existantes gagnent une prop `size` avec la valeur
actuelle par défaut** — même reconciliation additive que celle appliquée à `TrashIcon` lors du
sous-projet 1.

---

## 7. Ce que 2a change dans l'existant

- **`api.js` — `request()` est étendu** : exposition du code de statut HTTP sur l'erreur levée
  (aujourd'hui perdu, donc un 404 « message supprimé » est indistinguable d'un 500), support
  d'`AbortSignal`, et un helper séparé pour les réponses binaires (pièces jointes).
- **`GET /api/Account/Folders`** existe déjà et passe par doveadm (`mailboxList`), pour le
  sélecteur de dossiers des règles Sieve. Il ne renvoie que des noms plats — ni hiérarchie, ni
  abonnements, ni compteurs. Il est **conservé tel quel** en 2a : le migrer vers IMAP est un
  travail de 2b, quand les règles et le mail partageront la même notion de dossier.
- **Le quota continue de passer par doveadm** (`DovecotQuotaClient`) — inchangé.
- **`ComingSoon module="Mail"`** disparaît, ainsi que le bloc de `ComingSoon.tsx` qui pointe
  vers Alias et Règles. Les tests du shell qui l'assertent doivent être adaptés, pas supprimés.
- **`src/frontend/CLAUDE.md`** décrit Mail comme une page placeholder — à mettre à jour.

---

## 8. Fondations de session à traiter dans cette tranche

Deux constats du sous-projet 1, qui deviennent bloquants ici :

- **Le JWT expire en 30 minutes sans renouvellement.** Un webmail ouvert la journée y sera
  déconnecté en pleine lecture. Correction retenue : **renouvellement glissant** des deux
  cookies (JWT et credentials) sur toute requête authentifiée au-delà de la moitié de leur
  durée de vie. Pas de refresh token, pas de nouvel endpoint, pas de store — le mécanisme le
  plus simple qui règle le problème.
- **`OnTokenValidated` interroge la base à chaque requête** pour vérifier que l'utilisateur
  existe. Correction : mise en cache mémoire de ce contrôle, TTL 60 secondes. Mesure de
  précaution proportionnée ; à ne pas transformer en chantier.

---

## 9. Vérification

1. `npm run lint`, `npm run typecheck`, `npm run test`, `npm run build` — verts.
2. `dotnet test` — verts, avec des tests de repository sur `IImapSession` mocké couvrant :
   arborescence à plusieurs niveaux, dossier spécial détecté par flag **et** par repli sur le
   nom, création/renommage/suppression, abonnement, pagination, message multipart avec pièces
   jointes.
3. Tests d'assainissement HTML : un corpus de charges hostiles (script inline, `onerror`,
   `javascript:`, iframe, CSS d'échappement) doit ressortir inerte.
4. Les 4 combinaisons de thème sur la vue mail — c'est là que se voient les couleurs en dur.
5. Bout en bout contre le serveur de dev : arborescence conforme à celle d'un client IMAP de
   référence (Thunderbird), dossier créé depuis le webmail visible dans ce client et
   réciproquement, message avec pièce jointe lu et téléchargé, message HTML hostile rendu inerte.
6. Liens profonds : `/mail/<dossier>` restaure le dossier ; le bouton retour est cohérent.
7. Session : après expiration ou échec de déchiffrement des credentials, retour propre à
   `/login` — pas d'erreur IMAP affichée à l'utilisateur.
8. **Key ring** : le service journalise au démarrage le chemin retenu ; ce chemin est bien sous
   `/var/lib/…` et non sous le répertoire de déploiement ; **une session mail survit à un
   `systemctl restart`** — c'est le test qui prouve que la persistance fonctionne, et il doit
   être rejoué après chaque modification de l'unité systemd.

---

## 10. Suite

| Tranche | Dépend de |
|---|---|
| 2b — Actions & organisation | 2a |
| 2c — Écriture | 2a (2b souhaitable) |
| 2d — Multi-comptes | 2a, 2c |
| 3 — Calendrier | 1 |
| 4 — Contacts | 1, 2c |
