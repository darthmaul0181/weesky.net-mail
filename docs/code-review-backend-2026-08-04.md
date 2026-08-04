# Revue de code — backend `snoopy.microservice`

**Date :** 2026-08-04 · **Branche :** `backend-refactor-1` (74 commits d'avance sur `master`)
**Périmètre :** l'intégralité de `src/snoopy.microservice` hors tests (~250 fichiers source)
**Axes demandés :** propreté, sécurité, performance, standards de l'industrie, duplication

## État de la base

| Vérification | Résultat |
|---|---|
| `dotnet build` | succès, **0 warning** |
| `dotnet test` | **1940 / 1940** verts, 11 s |
| SQL brut / concaténation | aucun — EF Core partout |
| Secrets en dur | aucun (`appsettings.json` livre des chaînes vides) |

La qualité générale est nettement au-dessus de la moyenne : découpage en couches net, `Result<T>` de bout en bout,
scoping par utilisateur systématique, aucune erreur serveur (IMAP/SMTP/Sieve/SQL) relayée au client, et un
commentaire d'intention là — et seulement là — où le code ne peut pas se justifier seul. Les constats ci-dessous
sont des écarts ponctuels, pas des défauts structurels.

---

## Sécurité

### S1 — La limite de débit `login` est neutralisée derrière le reverse proxy · **Élevé**

`SecurityConfiguration.cs:85-93` partitionne sur `context.Connection.RemoteIpAddress`, et `Program.cs` n'enregistre
aucun middleware de forwarded headers. En production, l'API est servie derrière le proxy : toutes les requêtes
portent la même IP, donc **une seule partition globale de 5 requêtes/minute**, partagée par `POST /api/login`,
`POST /api/ConnectedAccounts` et `PUT /api/ConnectedAccounts/{id}/Password`.

Deux conséquences, pas une :
- **Déni de service trivial** — 5 requêtes par minute suffisent à renvoyer 429 sur la connexion **pour tous les utilisateurs**.
- **La protection anti-force-brute n'existe pas** — elle ne discrimine aucun compte ni aucune source.

Le problème est identifié dans `docs/superpowers/2d-manual-checklist.md:172-179` comme « à traiter séparément » ;
il reste ouvert et la tranche 2d l'a aggravé en ajoutant un second chemin sur le même quota.

**Correctif :** `UseForwardedHeaders` avec `KnownProxies` renseigné (sans quoi l'en-tête devient lui-même falsifiable),
puis deux partitions superposées : par IP réelle, et par identifiant de compte tenté.

### S2 — `SendMessageRequest` n'est borné sur aucune dimension · **Moyen**

`Models/Mail/SendMessageRequest.cs` : ni le nombre de destinataires (`To`/`Cc`/`Bcc`), ni la longueur du sujet, ni
celle du corps ne sont plafonnés. `MailComposeController.NormalizeOutgoing` (`:203-207`) valide chaque adresse une
par une mais ne compte jamais la liste.

C'est un écart au standard que le reste de l'API tient : 200 UID par lot, `pageSize` ≤ 200, 50 adresses par contact,
5000 contacts par compte, mot de passe ≤ 483 octets, adresse ≤ 255 caractères. Ici un compte compromis émet vers
des milliers de destinataires en une requête, et rien ne borne non plus la cadence d'envoi.

**Correctif :** plafond de destinataires (100 cumulés est confortable), sujet à 998 octets (RFC 5322), corps aligné
sur `MaxMessageSizeMb`. En attributs `[MaxLength]` sur le DTO plutôt qu'en code contrôleur — voir C2.

### S3 — Un changement de mot de passe par l'administrateur ne révoque pas les sessions · **Moyen**

`AccountController.ChangePassword` fait tout ce qu'il faut : rotation du security stamp, `sessions.Forget`,
ré-encodage des comptes connectés, réémission des deux cookies. `AdminRepository.UpdateUserAsync:155-159` écrit
`user.Password` **sans toucher au stamp**.

Or c'est précisément le geste qu'un administrateur pose face à un compte compromis. La session de l'attaquant
survit jusqu'à l'expiration du JWT, soit **48 h**. Le cookie de credentials cesse d'ouvrir IMAP, mais tout ce qui
ne passe pas par le serveur de mail continue de répondre : préférences, contacts, identités, alias, et les
endpoints d'administration si le compte était admin.

À noter que la désactivation (`active = 'N'`), elle, coupe bien — `SessionGuard` la voit dans sa fenêtre de 60 s.
Seul le changement de mot de passe fait exception.

**Correctif :** appeler `RotateSecurityStampAsync` + `ISessionGuard.Forget` depuis `UpdateUserAsync` dès que
`request.Password` est renseigné.

### S4 — `PATCH /api/Account/ChangeSecret` n'est pas limité en débit · **Faible**

`AccountController.cs:94-98` : pas d'`[EnableRateLimiting("login")]`, alors que l'action vérifie l'ancien mot de
passe. Depuis une session volée, l'ancien mot de passe est énumérable. Le coût du crypt SHA-512 borne le débit à
quelques essais par seconde, et les deux autres endpoints qui vérifient un mot de passe sont limités, eux.

### S5 — `SieveQuoting.Quote` n'écarte pas les caractères de contrôle · **Faible (défense en profondeur)**

`RuleProviders/SieveQuoting.cs:7-18` échappe `"` et `\`, rien d'autre. Son homologue
`ManageSieveSession.QuoteName:231-243` refuse explicitement tout `char.IsControl`, et documente pourquoi.

Aucune injection n'est démontrable ici : le script part en littéral de longueur préfixée (`PUTSCRIPT`), et un CRLF
reste légal dans une quoted-string Sieve (RFC 5228 §8.1). Ce qui gêne, c'est l'asymétrie — deux fonctions de
quoting voisines, une seule fermée. Fermer la seconde coûte trois lignes et retire la question.

### S6 — Durcissements JWT · **Information**

`AuthorizationExtension.cs:35-43` : `TokenValidationParameters` ne fixe pas `ValidAlgorithms`. Avec une clé
symétrique unique, la confusion d'algorithme n'est pas exploitable, mais `ValidAlgorithms = [SecurityAlgorithms.HmacSha256]`
est une ligne. De même, rien ne contrôle au démarrage que `TokenConstants.Key` atteint 256 bits — la bibliothèque
n'exige que 128, et le service refuse déjà de démarrer sans origine CORS ni `STATE_DIRECTORY` : la clé mérite le
même traitement.

### Ce qui tient — et qu'il ne faut pas défaire

- **Connexion à temps constant** (`UsersRepository:56-82`) : hash leurre calculé même sans compte, verdict formé
  après coup, `FixedTimeEquals`. Une seule requête jointe, pour que domaine inconnu et boîte inconnue coûtent pareil.
- **Chiffrement des comptes connectés** (`ConnectedAccountCipher`) : AES-GCM, KEK dérivée en PBKDF2 600 000 tours,
  et surtout un AAD liant chaque cryptogramme à sa ligne — un accès en écriture à la base ne suffit plus à faire
  livrer le mot de passe à un hôte choisi par l'attaquant. Le repli sur le format pré-binding est propre.
- **Refus d'authentifier en clair décidé sur `client.IsSecure`** (`MailConnectionFactory:85-100`), pas sur la valeur
  configurée : un STARTTLS retiré de la bannière est attrapé. Même raisonnement côté ManageSieve.
- **Chaîne de session** : stamp de révocation dans le JWT, vérifié à chaque requête avec cache 60 s et invalidation
  immédiate en local ; `DELETE /api/login/All` pour la déconnexion globale.
- **404 plutôt que 403** partout où répondre 403 confirmerait l'existence d'un identifiant (contacts, comptes
  connectés, pièces jointes déposées) — appliqué de façon cohérente, et documenté à chaque fois.
- **Sanitiseur HTML** : trois plafonds mesurés (caractères, profondeur, nœuds), unwrap plutôt que suppression,
  retenue des images distantes avec consentement, passe pré-Ganss contre les échappements CSS. Le lecteur de tags
  suit les frontières du tokeniser, ce qui est exactement le piège où ce genre de code se fait avoir.
- **Aucun secret dans les logs** : `ToString()` redéfini sur les records porteurs de mot de passe, messages serveur
  jamais relayés, `HashScheme` qui nomme le schéma sans jamais laisser passer la valeur.

---

## Performance

### P1 — `ListMessagesAsync` retrie le dossier entier à chaque page · **Moyen**

`ImapMessageCommands.cs:331-336` : `folder.SortAsync(SearchQuery.All, [ReverseDate])` renvoie **la liste complète
des UID du dossier**, dont on découpe ensuite une fenêtre de 50. Sur une boîte de 50 à 100 k messages, chaque
changement de page paie un tri serveur complet et le transport de la liste entière.

**Pistes :** `ESORT` / `CONTEXT=SORT` avec `PARTIAL` quand le serveur les annonce, ou mémorisation de la liste triée
dans la session, clefée par (dossier, `UIDVALIDITY`, `HIGHESTMODSEQ`) — les trois sont déjà remontés par
`ListFoldersAsync`.

### P2 — Recherche multi-dossiers : rien ne borne le nombre de dossiers · **Faible**

`SearchAsync` ouvre séquentiellement chaque dossier sélectionnable, deux fois (passe SEARCH, puis passe FETCH). Le
budget d'examen des pièces jointes et le découpage équitable entre dossiers sont bien pensés ; c'est le cardinal
des dossiers qui n'a pas de plafond. Une boîte à 300 dossiers fait 600 `SELECT` sur une recherche globale.

### P3 — Pas de compression de réponse · **Faible**

`GET /api/Contacts` renvoie le carnet entier (jusqu'à 5000 contacts avec leurs adresses), `GET /api/Mail/Folders`
un arbre complet avec compteurs. Aucun `AddResponseCompression`. À vérifier côté proxy : s'il ne compresse pas,
c'est du transport perdu sur les deux appels les plus lourds de l'application.

### P4 — L'arbre des dossiers est relu deux fois par `PUT /api/Mail/FolderRoles` · **Faible**

`MailFoldersController.SetFolderRole` appelle `GetFolderStatusAsync` puis `GetTreeAsync`, et
`RefuseIfSystemFolderAsync` recharge encore l'arbre avant chaque rename/delete/masquage. Opérations rares, coût
assumable, mais un mémo au niveau du scope de requête serait gratuit — la session IMAP y vit déjà.

### Ce qui tient

Projections EF ciblées (le mot de passe et `vcard_raw` n'atteignent jamais la mémoire), `AsNoTracking` par défaut
via `ScopedStore`, N+1 traqués et commentés un par un, sous-requêtes corrélées préférées aux `IN` inlinés pour
préserver le cache de plans MariaDB, FETCH IMAP toujours bornés (jamais de `BODY.PEEK[]` complet pour afficher un
corps), budget d'examen sur le post-filtre pièces jointes, et un cache mémoire à époque pour le flag admin qui
invalide correctement les réponses en vol.

---

## Duplication

### D1 — Les deux balayeurs de fond sont le même squelette copié · **Moyen**

`StagedAttachmentSweeper.cs:14-45` et `TrustedSenderSweeper.cs:22-50` partagent à l'identique la boucle
`PeriodicTimer` + `isFirstRun`, le jitter de démarrage, le `try/catch` qui protège l'hôte, et la méthode
`RandomJitter` **dupliquée mot pour mot**. Ne diffèrent que la période, le jitter par défaut et le corps du balayage.

**Correctif :** une base `PeriodicSweeper(TimeSpan period, TimeSpan startupJitter)` avec un `SweepOnceAsync`
abstrait. ~30 lignes en moins et, surtout, une seule politique de reprise sur erreur au lieu de deux qui peuvent
diverger.

### D2 — Résolution de compte réimplémentée dans `IdentitiesController` · **Faible**

`IdentitiesController.ResolveScopeAsync:103-116` refait la séquence de `AccountConnectionResolver.ResolveAsync`
(parse GUID → `FindAsync` scopé → `AccountNotFound`) et renvoie un tuple là où le reste du code utilise
`AccountResolution<T>`, dont tout l'intérêt est que le compilateur prouve la branche non-nulle. Le commentaire
justifie de ne pas résoudre de connexion — c'est juste — mais la moitié « identifiant → ligne » est partageable, et
le retour devrait être un `AccountResolution<ConnectedAccount>`.

### Non-duplications acceptables

Les deux surcharges de `ImapSession.ExecuteAsync` et celles de `ManageSieveSession.BoundedAsync` sont identiques au
type de retour près : c'est le prix de `Result` / `Result<T>` non unifiés génériquement, pas un copier-coller.

Il faut le dire : hors D1, **le code est remarquablement factorisé**. `MailConnectionFactory` partagé entre IMAP et
SMTP là où c'était deux fois le même fichier, `MailConnectionBuilder` comme lieu unique de composition d'une
connexion, `ApiBaseController` pour les enveloppes d'erreur, `ScopedStore` pour les trois formes d'accès aux
préférences, `IdentityResolver` et `ContactValidator` comme lieux uniques des règles métier correspondantes.

---

## Propreté et conformité au style maison

Le style du `CLAUDE.md` est suivi : namespaces file-scoped, `sealed`, `internal` par défaut, records pour les DTO,
collection expressions, pattern matching, logging structuré sans interpolation, `CancellationToken` sur toutes les
méthodes asynchrones, aucun `try/catch` de simple log-and-rethrow.

### C1 — Constructeurs explicites là où le style impose les primary constructors

`LoginController`, `PreferencesController`, `AppSettingsController`, `UserAuthenticator`, `DovecotQuotaClient`,
`ManageSieveClient`. Trois cas sont justifiés et doivent le rester : `SlidingSessionMiddleware` (contrat du
middleware), `MailCredentialStore` (garde de nullité sur le fournisseur), `StagedAttachmentStore` (paramètre
optionnel pour les tests). Les six autres sont une simple dérive de style.

### C2 — DTO validés à la main plutôt que par attributs

Le `CLAUDE.md` demande « **ALWAYS** use DTO for API communication, validated with attributes ». C'est appliqué à
`MessageRequests`, `FolderRequests`, `Credentials`, `SecretChange`, `AdminUserRequest`… mais pas à
`SendMessageRequest`, `SaveDraftRequest`, `ContactRequest`, `ConnectAccountRequest`, `SetPreferenceRequest`,
`SetAppSettingRequest`, `ExternalDomainRequest` ni `ReplaceIdentitiesRequest`, qui passent par un validateur ou par
du code contrôleur.

Ce choix se défend — les messages produits sont meilleurs et les règles métier ne s'expriment pas en attributs.
La recommandation n'est donc pas de tout convertir, mais de **porter en attributs les bornes purement
dimensionnelles** (longueurs, cardinalités), ce qui est exactement ce qui manque en S2, et de laisser les
validateurs porter le reste.

### C3 — Trois conventions de comparaison de casse dans un même fichier

`AliasesRepository` :
- `:18-19` — `usr.Name == user.Name` et `domain.Name == user.Domain` : dépend de la collation MariaDB ;
- `:53`, `:107`, `:150-151` — `string.Equals(..., InvariantCultureIgnoreCase)` : force un `LOWER()` des deux côtés ;
- `:123` — `string.Equals(user.Domain, domainName)` : ordinal, sensible à la casse.

Sans conséquence avec la collation `_ci` actuelle. Mais `GetAliasesAsync` est ce qui autorise un `fromAddress` à
l'envoi (`OutgoingMessageFactory:78-80`) : c'est un chemin d'autorisation, et il ne devrait pas être le seul du
fichier à dépendre d'un réglage de base de données. La collation est d'ailleurs déjà traitée comme une propriété
de sécurité ailleurs — voir `AdminFlagQueryTranslationTests`.

### C4 — Le sel KDF est dérivé de l'adresse saisie, pas de celle résolue

`LoginController.cs:77` appelle `GetOrCreateKdfSaltAsync(credentials.Email, …)` alors que le compte vient d'être
résolu en base sous sa forme canonique. `Canonical()` fait converger les deux dans tous les cas courants, mais la
divergence, si elle survient, produit un sel jamais persisté : le KEK du cookie n'ouvre alors plus rien et **tous**
les comptes connectés répondent 409 pour la durée de la session. L'effet est hors de proportion avec la cause ;
`AuthenticateAsync` devrait remonter l'adresse résolue.

### Détails

- `AliasesRepository.cs:33` : `throw new ArgumentNullException("alias")` — littéral là où la ligne 90 du même
  fichier utilise `nameof(alias)`.
- `StagedAttachmentStore.SweepOrphans` supprime les fichiers mais laisse les répertoires de compte vides derrière lui.
- `SlidingSessionMiddleware` lit `DateTimeOffset.UtcNow` alors qu'un `TimeProvider` est enregistré et injecté dans
  `TokenManager` — le middleware n'est donc pas testable sur l'horloge, contrairement à ce qu'il renouvelle.

---

## Priorisation suggérée

| # | Constat | Sévérité | Effort |
|---|---|---|---|
| 1 | S1 — forwarded headers + partition du rate limiter | Élevé | ~1 h |
| 2 | S3 — rotation du stamp au changement de mot de passe admin | Moyen | ~30 min |
| 3 | S2 — bornes sur `SendMessageRequest` (+ C2) | Moyen | ~1 h |
| 4 | D1 — base commune aux deux balayeurs | Moyen | ~45 min |
| 5 | S4, S5, S6 — durcissements | Faible | ~1 h cumulé |
| 6 | C3, C4 — cohérence casse et source du sel | Faible | ~1 h |
| 7 | P1 — pagination IMAP sans tri complet | Moyen | à cadrer, non trivial |
