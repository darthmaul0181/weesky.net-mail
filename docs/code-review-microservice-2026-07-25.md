# Code review — snoopy.microservice

**Date :** 2026-07-25
**Périmètre :** `src/snoopy.microservice/` — ≈11 000 lignes hors tests, 67 fichiers de tests
**Branche :** `webmail`

> **Statut au 2026-07-26 :** les priorités **1 à 9** de la §6 ont été traitées, puis les constats
> **2.7, 3.3, 3.4, 4.4, 4.7, 4.9** et enfin **1.8, 1.9, 2.5**. Build sans avertissement,
> **1141 tests au vert** (1104 au départ, +37 nouveaux).

Chaque constat porte son état, et le détail des corrections est en §6 :

| | Signification | Constats |
|---|---|---|
| ✅ | corrigé | 1.1, 1.2, 1.3, 1.5, 1.8, 1.9, 1.12, 2.1, 2.3, 2.5, 3.1, 3.2, 3.3, 3.4, 4.1, 4.4, 4.7, 4.8, 4.9, 5.1, 5.3 |
| 🟡 | partiellement corrigé | 2.7, 4.2, 4.3, 4.5, 4.6 |
| ⚠️ | écarté, constat révisé | 2.4 |
| ⬜ | ouvert | 1.4, 1.6, 1.7, 1.10, 1.11, 2.2, 2.6, 2.8, 3.5 à 3.7, 4.10, 4.11, 5.2 |

---

## Vue d'ensemble

Le code est globalement d'un très bon niveau : séparation Controllers / Repositories / Services nette,
`Result<T>` partout, commentaires qui expliquent le *pourquoi* (les règles IMAP, les trois règles du
sanitizer, la chaîne de rôles), couverture de tests réelle. Les problèmes ci-dessous sont donc
majoritairement des **écarts ponctuels**, pas une dette structurelle — sauf sur deux points : le
nombre de connexions IMAP par requête, et la duplication mécanique dans les repositories mail et
`MailController`.

---

## 1. Sécurité

### 🔴 Élevé

#### ✅ 1.1 — Un compte désactivé (`active = 'N'`) peut toujours se connecter et utiliser l'API

> **Corrigé** — `FindByEmailAsync` exclut `active = 'N'`. Un seul point de filtrage, traversé par le login *et* par `OnTokenValidated`, ferme les deux accès.

`UserAuthenticator.AuthenticateAsync` ne consulte que `FindByEmailAsync` + `IsValidPasswordAsync` ;
ni l'un ni l'autre ne lit `MailUser.Active` (`Repositories/UsersRepository.cs:120-137`). Idem pour le
contrôle par requête dans `Authentication/Extensions/AuthorizationExtension.cs:82`
(`repo.FindByEmailAsync(email) != null`).

Conséquence : désactiver un compte dans le panneau admin **ne le déconnecte pas de la webmail**.
IMAP/SMTP refuseront (Dovecot lit `active`), mais tout ce qui ne passe pas par le serveur mail
continue de fonctionner : `/api/Account`, `/api/Aliases`, `/api/Preferences`, `/api/Identities`,
`/api/Admin` si le compte est admin — et surtout **`/api/Rules`, qui s'authentifie en ManageSieve
avec le compte *master*, pas avec le mot de passe utilisateur**. Un compte désactivé garde donc le
droit d'écrire des règles Sieve (dont des `redirect`).

#### ✅ 1.2 — STARTTLS optionnel côté ManageSieve : le mot de passe master peut partir en clair

> **Corrigé** — la session est refusée quand le serveur n'annonce pas STARTTLS, sauf `Sieve:AllowCleartext` explicite (défaut `false`).

`Services/ManageSieveClient.cs:65` : `if (HasCapability(capabilities, "STARTTLS")) { … }`.

Si le serveur n'annonce pas STARTTLS — ou si un attaquant en position réseau le retire de la bannière
de capacités (STARTTLS stripping ; la bannière arrive en clair) — le code **poursuit sans
chiffrement** et envoie ensuite `AUTHENTICATE "PLAIN"` avec `{user}\0{MasterUser}\0{MasterPassword}`
en base64 (`Services/ManageSieveClient.cs:82`). Le base64 n'est pas du chiffrement : c'est le mot de
passe master de la plateforme sur le fil.

**Attendu :** exiger TLS par défaut et n'autoriser le clair que via une option explicite
`AllowCleartext` (comme `AllowInvalidCertificate` existe déjà), en refusant la session sinon.

### 🟠 Moyen

#### ✅ 1.3 — Un changement de mot de passe ne rafraîchit pas le cookie de credentials

> **Corrigé** — `ChangePassword` ré-émet le cookie de credentials, uniquement en cas de succès.

`AccountController.ChangePassword` → `UsersRepository.ChangePasswordAsync`. Les deux seuls appels à
`IMailCredentialStore.Store` sont `Controllers/LoginController.cs:64` et
`Authentication/Middleware/SlidingSessionMiddleware.cs:77`.

Après un changement de mot de passe, le cookie chiffré contient toujours **l'ancien** mot de passe,
et le sliding renewal le **ré-enregistre** à chaque renouvellement. La session reste valide (JWT),
mais toute opération mail échoue en « Mail authentication failed » — jusqu'à 48 h, sans que
l'utilisateur comprenne pourquoi. Fonctionnellement c'est un bug ; côté sécurité, c'est aussi un
changement de secret qui n'invalide rien.

#### ⬜ 1.4 — Aucune révocation de session

Le JWT n'a ni `jti` ni liste de révocation ; `Logout` se contente de supprimer les cookies. Un token
capturé reste valide jusqu'à `exp` (**2880 min = 48 h**), et il est accepté aussi bien en cookie
qu'en `Authorization: Bearer`. Ni un logout, ni un changement de mot de passe, ni un passage en
`active = N` ne l'invalide. Le seul garde-fou est le cache « user-exists » de 60 s
(`Authentication/Extensions/AuthorizationExtension.cs:82-84`), qui ne couvre que la **suppression**
du compte.

#### ✅ 1.5 — `[DisableRequestSizeLimit]` + `IFormFile` sur l'upload de pièces jointes

> **Corrigé** — `AttachmentSizeLimitFilter` (resource filter) plafonne le corps sur `MaxMessageSizeMb` **avant** le model binding, donc avant la bufferisation sur disque. `FormOptions.MultipartBodyLengthLimit` est aligné dessus.

`Controllers/MailController.cs:680`. Le commentaire dit « le store applique la limite en streaming » —
mais le binding `IFormFile` **bufferise l'intégralité du corps** (sur disque au-delà de 64 Ko)
*avant* que l'action ne s'exécute. La limite réelle devient `FormOptions.MultipartBodyLengthLimit`
(128 Mo par défaut, non configuré ici), pas `MaxMessageSizeMb` (25). Un utilisateur authentifié peut
donc écrire 128 Mo sur disque par requête, en parallèle, avant tout contrôle.

**Attendu :** soit `MultipartReader` en vrai streaming, soit un `[RequestSizeLimit]` calé sur
`MaxMessageSizeMb` + overhead.

#### ⬜ 1.6 — API doveadm en HTTP clair avec la clé d'API dans l'en-tête

`appsettings.json` : `"ApiUrl": "http://mail.weesky.net:8080/doveadm/v1"`. La clé
(`X-Dovecot-API <base64>`) et les données de quota transitent en clair. Acceptable seulement si le
lien est strictement privé ; à documenter comme tel, ou à passer en HTTPS.

#### ⬜ 1.7 — Aucun rate limiting hors login

Seuls `/api/Login` et `/api/BearerAuthenticator` ont une policy (5/min/IP). Les endpoints coûteux ne
sont pas bornés :

- `POST /api/Mail/Messages/Search` avec `allFolders: true` ouvre une session IMAP et fait un `SEARCH`
  + `FETCH Envelope` sur **toutes** les boîtes ;
- `POST /api/Mail/Attachments` accepte 128 Mo (cf. 1.5) ;
- `/api/Rules` ouvre une session TCP ManageSieve par appel.

Une policy globale par utilisateur authentifié serait la bonne granularité.

### 🟡 Faible

#### ✅ 1.8 — Comparaison de hash non constante en temps

> **Corrigé** — `PasswordMatches` compare via `CryptographicOperations.FixedTimeEquals` sur les octets. Corrigé avec 1.9 : la méthode reçoit désormais le hash en paramètre plutôt que l'entité, ce qui était le geste naturel pour les deux.

`Repositories/UsersRepository.cs:140` : `Crypter.Sha512.Crypt(password, hash) == hash`.
`CryptographicOperations.FixedTimeEquals` sur les octets serait la forme correcte. Exploitation à
distance peu réaliste, mais c'est un pattern qui ne doit pas rester dans du code d'authentification.

#### ✅ 1.9 — Énumération d'utilisateurs par timing

> **Corrigé** — `VerifyCredentialsAsync` remplace le couple lookup + vérification : une seule requête jointe, et le crypt SHA-512 payé **dans tous les cas**, contre `AbsentAccountHash` quand aucune boîte ne correspond. Mesuré avant/après sur les trois chemins : compte inconnu 0,085 ms → 2,87 ms, mot de passe faux 3,00 ms. L'écart passe d'un facteur 35 à ~4 %, sous la gigue réseau. Le motif d'échec reste distingué dans le log d'audit via `CredentialResult`, jamais dans la réponse.

`AuthenticateAsync` : un email inconnu renvoie immédiatement (une requête `Domains` + une `Users`),
un email connu paie un SHA-512 crypt (délibérément lent, 5000 rounds). L'écart est mesurable. Le rate
limit à 5/min limite l'exploitation ; un crypt factice sur le chemin « inconnu » l'annulerait.

#### ⬜ 1.10 — Allocation non bornée pilotée par le serveur

`Services/ManageSieveSession.cs:345` : `new byte[count]` où `count` vient du littéral `{N}` renvoyé
par le serveur, sans plafond. Notre propre serveur, donc risque faible, mais c'est une valeur réseau.

#### ⬜ 1.11 — Clé JWT sans validation au démarrage

> **Écarté pour l'instant** — la validation avait été ajoutée puis retirée : un seuil plus strict que celui de `Microsoft.IdentityModel` (16 octets) bloquerait au login un déploiement qui fonctionne aujourd'hui. À traiter comme une validation au démarrage, avec la valeur de production sous les yeux.

`TokenBuilder.AddKey` fait `Encoding.UTF8.GetBytes(key)` sans vérifier la longueur. Une clé
< 32 octets fera échouer HMAC-SHA256 au premier login (pas au boot), et une clé courte est faible.
À valider dans `Program.cs`, au même endroit que `STATE_DIRECTORY` et la connection string — le
projet a déjà le bon réflexe « refuser de démarrer » pour deux autres cas.

#### ✅ 1.12 — 500 déclenchables par un `null` explicite dans le JSON

> **Corrigé** — les trois DTO de lot partagent une base `MessageBatchRequest` dont le setter de `Uids` coalesce ; `AttachmentIds` est normalisé comme les autres listes.

`MailController.NormalizeOutgoing` neutralise le cas pour `To`/`Cc`/`Bcc`/`References` (le
commentaire l'explique très bien), mais **pas** pour :

- `AttachmentIds` (`foreach` → NRE) ;
- `Uids` dans `SetMessageFlagsRequest` / `MoveMessagesRequest` / `DeleteMessagesRequest`
  (`request.Uids.Count` → NRE, `Controllers/MailController.cs:509, 553, 587`).

Un `{"uids": null}` renvoie 500 au lieu de 400.

### Points positifs à souligner

- Le sanitizer entrant/sortant est sérieux : cull des `url()` par valeur, unwrap plutôt que drop,
  deux allowlists distinctes.
- Le blocage d'injection d'en-têtes via `MimeUtils.ParseMessageId`
  (`OutgoingMessageFactory.ApplyThreadingHeaders`) est exactement le bon réflexe.
- Le namespace scellé par compte sur les pièces staged, avec 204 sur id étranger, est correct.
- Les cookies sont `HttpOnly` + `Secure` + `SameSite=Strict`.
- Les en-têtes de sécurité globaux sont en place (`nosniff`, `X-Frame-Options`, CSP, `Referrer-Policy`).
- La propriété « ne jamais renvoyer le message d'erreur du serveur mail » est tenue partout.

---

## 2. Performance

### ✅ 2.1 — Une connexion IMAP (TCP + TLS + SASL) par appel de méthode de repository

> **Corrigé** — `IImapSessionProvider` / `ScopedImapSessionProvider` : une connexion authentifiée par requête HTTP, fermée par le conteneur en fin de scope.

*C'est le point le plus coûteux.*

`ImapConnectionFactory.OpenAsync` est appelé à l'intérieur de chaque méthode de
`MailFolderRepository` (6×) et `MailMessageRepository` (11×). Une seule requête HTTP en enchaîne donc
plusieurs :

| Requête | Sessions ouvertes |
|---|---|
| `PUT /Mail/Folders` (rename) | 2 IMAP (`RefuseIfSystemFolder` → `GetTree`, puis `Rename`) |
| `DELETE /Mail/Folders` | 2 IMAP |
| `PUT /Mail/FolderRoles` | 2 IMAP (`GetFolderStatus`, `GetTree`) |
| `POST /Mail/Send` | 1 SMTP + 2 IMAP (`GetTree`, `Append`) |
| `POST /Mail/Drafts` | 2 IMAP (`GetTree`, `SaveDraft`) |

Chaque session = handshake TCP + STARTTLS + authentification. La correction propre est une session
IMAP **scoped à la requête** (ou un `WithSessionAsync` qui prend un délégué multi-opérations) plutôt
qu'une par méthode.

### ⬜ 2.2 — `GetTreeAsync` est cher et appelé très souvent

`ImapSession.ListFoldersAsync` demande `Count | Unread | UidValidity | UidNext | MailboxId |
HighestModSeq` sur **toutes** les boîtes — soit un `STATUS` par dossier côté serveur. C'est
l'endpoint que le frontend rafraîchit en polling, et c'est aussi ce que `RefuseIfSystemFolderAsync`,
`SetFolderRole`, `MailSender` et `DraftSaver` appellent en interne. Un cache court (quelques
secondes, par utilisateur) sur l'arbre couperait l'essentiel.

### ✅ 2.3 — N+1 dans `AdminRepository.GetAllVirtualDomainsAsync`

> **Corrigé** — une seule requête groupée sur `DomainsOwnerships`, partagée avec la lecture mono-domaine pour que les deux ne puissent pas diverger.

`Repositories/AdminRepository.cs:230-236` : une requête `GetDomainOwnersAsync` **par domaine**, dans
une boucle `foreach`. À remplacer par une seule requête groupée sur `DomainsOwnerships`.

### ⚠️ 2.4 — `EnableStringComparisonTranslations` + `StringComparison.InvariantCultureIgnoreCase` empêche l'usage des index

> **Écarté, et le constat était trop catégorique.** Le domaine est résolu d'abord par une égalité indexable, donc le `LOWER()` ne porte que sur les utilisateurs d'un seul domaine : le coût réel est faible. Surtout, passer à `==` rendrait le login sensible à la casse pour d'éventuelles lignes `username` historiques en majuscules — invérifiable sans la base de production, et le prix d'une erreur est un utilisateur enfermé dehors. Le vrai correctif (normalisation à l'écriture + migration) dépasse une revue de code.

`Repositories/UsersRepository.cs:129`, et le même pattern dans `AliasesRepository` (5 occurrences).
Pomelo traduit ces comparaisons en `LOWER(col) = LOWER(@p)` → **scan de table**, l'index sur
`username` ne sert plus. Or `FindByEmailAsync` est sur le chemin chaud : appelé à chaque requête
authentifiée (mitigé par le cache 60 s) et à chaque `Send`. Les collations MySQL étant déjà
insensibles à la casse par défaut (`utf8mb4_general_ci`), ces `StringComparison` sont probablement
inutiles et peuvent être retirés.

### ✅ 2.5 — 4 requêtes SQL par login

> **Corrigé** — `FindMailUserAsync` fait une seule requête jointe au lieu de deux lectures séquentielles, et le login n'en fait plus qu'un appel au lieu de deux : 4 requêtes → 1. `GetAccountInfo`, `ChangeFullName` et `ChangePassword` en profitent au passage.

`FindByEmailAsync` (Domains + Users) puis `IsValidPasswordAsync` (Domains + Users à nouveau, sur le
même utilisateur). Une seule jointure suffirait.

### ⬜ 2.6 — 2 requêtes par requête HTTP froide

`AuthorizationExtension` → `FindByEmailAsync` = Domains puis Users. Plus 2 autres pour
`AdminRequirementHandler.IsAdminAsync` sur chaque appel admin, non caché.

### 🟡 2.7 — `GetAttachmentAsync` charge la pièce entière en mémoire

> **Partiellement corrigé** — le `ToArray()` disparaît : le `MemoryStream` décodé est rendu tel quel et `MailAttachmentContent.Content` devient un `Stream`, ce qui supprime une copie complète sur le LOH par téléchargement tout en gardant un flux *seekable*, donc un `Content-Length` et la progression côté navigateur. Le vrai streaming socket→réponse reste hors de portée : `GetBodyPartAsync` matérialise déjà la pièce, MailKit n'expose pas d'autre chemin.

`Services/ImapSession.cs:917` : `MemoryStream` + `ToArray()` avant de la renvoyer. Sur une pièce de
25 Mo × N utilisateurs, c'est autant sur le LOH. Le streaming direct vers la réponse serait
préférable.

### ⬜ 2.8 — Recherche multi-dossiers

`SearchAsync` en mode merge fait un `FETCH Envelope|InternalDate` sur **tous** les résultats de
**tous** les dossiers avant de paginer. C'est nécessaire pour trier par date d'envoi (le commentaire
l'assume), mais sur une grosse boîte c'est lourd et non borné — voir 1.7.

---

## 3. Code dupliqué

### ✅ 3.1 — `MailMessageRepository` / `MailFolderRepository` : 17 méthodes au corps identique

> **Corrigé** — les 17 méthodes deviennent des délégations d'une ligne via `WithSessionAsync` (−180 lignes), et le point d'ouverture unique est ce qui a permis 2.1.

```csharp
if (user == null) throw new ArgumentNullException(nameof(user));
var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
if (sessionResult.IsFailure) return Result.Failure<T>(sessionResult.Error);
await using var session = sessionResult.Value;
return await session.XxxAsync(...);
```

Seule la dernière ligne varie. Un unique `WithSessionAsync<T>(user, password, session => …, ct)`
réduit ces deux fichiers de ~280 lignes à ~80 — et corrige au passage 2.1, puisque le point
d'ouverture devient unique.

### ✅ 3.2 — `MailController` : le préambule credentials répété 19 fois

> **Corrigé** — `TryMailPassword` remplace les 19 préambules ; quatre helpers d'enveloppe sur `ApiBaseController` ramènent 105 sites à 6 dans toute la couche contrôleur. Guard plutôt que filtre à dessein : les tests invoquent les actions directement, un filtre sortirait le contrôle des 137 tests qui le couvrent.

```csharp
var password = _credentials.Retrieve(Request);
if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));
```

Un `IAsyncActionFilter` (ou un helper `TryGetMailPassword(out …)` sur `ApiBaseController`) suffirait.

**Corollaire :** `CreateErrorEnveloppe` apparaît **88 fois** dans ce seul fichier — des helpers
`BadRequestEnveloppe(msg)` / `BadGateway(msg)` sur la classe de base rendraient les actions bien plus
lisibles.

### ✅ 3.3 — `ImapSession` : le triplet `catch` répété 12 fois

> **Corrigé** — un `ExecuteAsync` partagé porte le contrat d'échec ; les 17 méthodes le délèguent (−129 lignes). Les sentinelles restent **par opération** : chaque méthode conserve exactement l'ensemble qu'elle traduisait déjà, pour que ce soit un refactor et non un changement de comportement. Ces chemins n'ont pas de couverture — `ImapSession` prend un client MailKit concret — donc le contrat lui-même est épinglé par 9 tests dédiés.

```csharp
catch (FolderNotFoundException) { return Result.Failure(FolderNotFound); }
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
catch (Exception ex) { _logger.LogError(ex, "…"); return Result.Failure("…"); }
```

Encapsulable dans un `ExecuteAsync(Func<Task<Result<T>>>, string errorMessage, …)`.

### ✅ 3.4 — `ImapConnectionFactory` et `SmtpConnectionFactory` sont le même fichier à 90 %

> **Corrigé** — `MailConnectionFactory<TClient, TSession>` porte la structure commune ; chaque fabrique ne déclare plus que son endpoint, son client et sa session. Le `ValidateCertificate` dupliqué à l'identique n'existe plus qu'en un exemplaire.

Même structure, même `ValidateCertificate` (dupliqué à l'identique), même gestion d'ownership, mêmes
messages. Une classe de base générique ou un helper partagé pour `ValidateCertificate` élimine le
risque de divergence.

### ⬜ 3.5 — `LoginController` vs `BearerAuthenticatorController`

Deux endpoints publics d'authentification, le second n'étant que le premier sans les cookies. Il
double la surface d'attaque de login. **Est-il encore utilisé ?** Sinon, à supprimer.

### ⬜ 3.6 — `SieveRepository.SaveRulesAsync` et `PutAndActivateAsync`

Dupliquent la séquence ouvrir-session / PUTSCRIPT / SETACTIVE / log.

### ⬜ 3.7 — `ManageSieveClient` et `ManageSieveSession`

Dupliquent `ReadLineAsync`, `TryParseStatus`/`IsTerminator`, `StartsWithKeyword`,
`TryParseLiteralPrefix`, `Utf8`, `CrLf` — avec deux implémentations *différentes* de la lecture de
ligne (non bufferisée / bufferisée). Le commentaire justifie l'écart, mais les parseurs de statut,
eux, devraient être partagés.

---

## 4. Refactoring / standards

### ✅ 4.1 — `.editorconfig` désactive toutes les alertes nullable

> **Corrigé** — les six suppressions passent en `warning` et les 74 avertissements découverts sont tous traités. Dont **trois déréférencements potentiels réels** que la suppression masquait : `MimePart.Content` dans `ImapSession.GetAttachmentAsync` et `QuotePreparer.StagePartAsync`, `MessagePart.Message` dans `StageAttachmentAsync`.

```ini
dotnet_diagnostic.CS8618.severity = none   # champ non-nullable non initialisé
dotnet_diagnostic.CS8600.severity = none
dotnet_diagnostic.CS8602.severity = none
dotnet_diagnostic.CS8603.severity = none
dotnet_diagnostic.CS8604.severity = none
dotnet_diagnostic.CS8625.severity = none
```

Le `.csproj` active `<Nullable>enable</Nullable>`… puis le `.editorconfig` en supprime tout le
bénéfice. C'est l'écart le plus net par rapport à « state of the art » : le compilateur ne dit plus
rien sur `string Message` (`ResultEnveloppe`), `string Name` (`User`), `string Password`
(`Credentials`), et les `#nullable` du reste du code deviennent décoratifs. Réactiver en `warning`,
dossier par dossier, est un chantier long mais c'est celui qui rapporte le plus.

### 🟡 4.2 — Base de code à deux vitesses sur les primary constructors

> **Partiel** — les fichiers touchés par les autres correctifs sont passés en primary constructor (`MailController`, `AccountController`, les deux repositories mail, les nouveaux services). Le reste de la base est inchangé.

Le `CLAUDE.md` du projet impose les primary constructors ; **32 fichiers sur 39** utilisent encore le
style `private readonly` + constructeur. Sept fichiers sont déjà à la nouvelle norme
(`SendingIdentityStore`, `WebmailUserStore`, `MailSender`, `DraftSaver`, `OutgoingMessageFactory`,
`QuotePreparer`, `IdentitiesController`, `DatabaseHealthCheck`). Refactor mécanique, sans risque, à
faire en une passe.

### 🟡 4.3 — `TokenBuilder` est le fichier le plus daté du projet

> **Partiel** — `#region` retiré, champ mort `_key` supprimé, `_claims` en `readonly` + collection expression, champs nullables. `AddClaims` est conservé (un test le couvre) et `DateTime.UtcNow` n'est pas passé à `TimeProvider`.

- `#region Members` ;
- champs non-nullable non initialisés ;
- `_key` assigné puis jamais lu (champ mort) ;
- `AddClaims(params Claim[])` jamais appelé ;
- `AddExpiry` utilise `DateTime.UtcNow` alors que `TimeProvider.System` est déjà enregistré dans le
  conteneur (donc non testable au temps).

### ✅ 4.4 — `System.IdentityModel.Tokens.Jwt` / `JwtSecurityTokenHandler` est l'API legacy

> **Corrigé** — `TokenBuilder` écrit via `JsonWebTokenHandler`, `Build()` rend directement le jeton sérialisé et `JwtSecurityTokenExtensions` disparaît. Le paquet `System.IdentityModel.Tokens.Jwt` est remplacé par `Microsoft.IdentityModel.JsonWebTokens` : plus qu'une seule pile JWT, celle avec laquelle le middleware validait déjà. Effet de bord bienvenu : le jeton porte enfin `iat` et `nbf`.

Microsoft recommande `Microsoft.IdentityModel.JsonWebTokens` / `JsonWebTokenHandler` (plus rapide,
moins allouant, et déjà utilisé en interne par `AddJwtBearer` en .NET 8+). Le projet mélange donc
deux piles JWT.

### 🟡 4.5 — `ResultEnveloppe`

> **Partiel** — `Message` et `Result` sont nullables, `CreateSuccessEnveloppe` prend un `string?`. La classe n'est toujours ni `sealed` ni un record, et `ResultEnveloppe<T>` reste du code mort.

Classe mutable non `sealed`, `CreateSuccessEnveloppe(string message = null)`, propriétés `set`
publiques — alors que le `CLAUDE.md` dit « ALWAYS prefer record types for immutable data
structures ». Et **`ResultEnveloppe<T>` est du code mort** : la seule référence dans tout le dépôt
est son propre test.

### 🟡 4.6 — Modificateurs d'accès

> **Partiel** — `ApiBaseController` est `abstract`. `AuthenticatedUser` reste `public` et les deux `DbContext` sont inchangés.

`ApiBaseController` n'est pas `abstract` et `AuthenticatedUser` est `public` alors qu'il devrait être
`protected`. `ApplicationDbContext` / `PreferencesDbContext` ne sont ni `sealed` ni `internal`.

### ✅ 4.7 — `Program.cs` (283 lignes)

> **Corrigé** — 283 → 61 lignes. La configuration part dans `Configuration/` : logging, bases, options, services mail, providers de règles, repositories, sécurité, documentation d'API.

Mélange logging, deux DbContexts, ~30 enregistrements DI, CORS, rate limiting, Swagger, Data
Protection et le pipeline. Des extensions `AddMailServices()`, `AddPreferencesDatabase()`,
`AddSecurityHeaders()` le ramèneraient à une cinquantaine de lignes lisibles. Le `CLAUDE.md`
encourage explicitement les méthodes d'extension.

### ✅ 4.8 — `.LogTo(Console.WriteLine, LogLevel.Warning)` sur les deux DbContexts

> **Corrigé** — les `.LogTo(Console.WriteLine, …)` ont disparu avec le découpage : les avertissements EF repassent par `ILogger`, donc par Serilog et ses fichiers.

`Program.cs` — court-circuite Serilog : ces avertissements EF n'atterrissent dans aucun fichier de
log. Contradiction directe avec la règle « ALWAYS use ILogger with structured logging ».

### ✅ 4.9 — Mapping d'erreur incohérent entre contrôleurs

> **Corrigé** — `SieveErrors` porte les sentinelles partagées (non configuré, injoignable, credentials refusés, connexion non sécurisée) et le contrôleur les mappe en 502 ; tout le reste reste un 400. Au passage les messages du serveur ne sont plus renvoyés au client : ils vont au log.

`MailController` applique rigoureusement la règle 401 / 404 / 502 ; `RulesController` renvoie **400**
pour une panne d'infrastructure (`Get`, `GetRaw` : `BadRequest(result.Error)` alors que l'erreur est
« Unable to connect to rules service »). Un `502` serait cohérent avec le reste.

### ⬜ 4.10 — Deux `ServerVersion.AutoDetect` synchrones au démarrage

`Program.cs` : deux connexions bloquantes, et le service refuse de démarrer si la base est
momentanément indisponible au boot. `ServerVersion.Parse("11.4.0-mariadb")` en configuration évite
les deux.

### ⬜ 4.11 — Détails de moindre portée

> Seul `User(string email)` est passé à `ArgumentNullException.ThrowIfNull`, incidemment. Le reste — dont le contrat `Equals`/`GetHashCode` rompu d'`Alias` — est ouvert.

- `throw new ArgumentNullException("alias")` avec une string littérale au lieu de `nameof`
  (`Repositories/AliasesRepository.cs:186`) ;
- `Alias.GetHashCode()` sensible à la casse alors que `Equals` ne l'est pas — **contrat rompu** :
  deux `Alias` égaux peuvent avoir des hashcodes différents, ce qui casse tout `HashSet`/`Dictionary` ;
- `Result.Failure($"...")` avec interpolation sur des chaînes constantes ;
- `new List<LastLoginEntry>()` au lieu de `[]` ;
- `new[] { uniqueId }` au lieu de `[uniqueId]`.

---

## 5. Bugs fonctionnels relevés au passage

### ✅ 5.1 — `AdminRepository.UpdateUserAsync` écrase silencieusement les champs absents

> **Corrigé** — `QuotaMb`/`Active`/`Admin` sont nullables et `null` signifie « inchangé ». Quatre tests couvrent le PUT partiel et les défauts à la création.

`Repositories/AdminRepository.cs:120-122`. `AdminUserRequest.QuotaMb` vaut 1024 par défaut, `Active`
`true`, `Admin` `false`. Un `PUT /api/Admin/users/{id}` qui ne renvoie pas ces champs **remet le
quota à 1024 et retire les droits admin**. `FullName` et `Password` sont correctement traités en
« null = inchangé » ; les trois autres ne le sont pas. `bool?` / `int?` corrigeraient l'asymétrie.

### ⬜ 5.2 — `DeleteUserAsync` ne nettoie ni les alias ni les propriétés de domaine

La ligne `snoopy_webmail` est supprimée en best-effort (bien), mais `Aliases` (`DestinationUserId`)
et `DomainsOwnerships` (`UserId`) restent orphelins — sauf si la base porte des FK
`ON DELETE CASCADE`, ce que le modèle EF ne déclare pas.

### ✅ 5.3 — Changement de mot de passe ⇒ mail cassé jusqu'à 48 h

> **Corrigé** — voir 1.3.

Voir 1.3. C'est le bug le plus visible pour un utilisateur final.

---

## 6. Priorisation — et ce qui a été corrigé

| # | Sujet | Réf. | Statut |
|---|---|---|---|
| 1 | Vérifier `Active` au login **et** dans `OnTokenValidated` | 1.1 | ✅ fait |
| 2 | Rafraîchir le cookie credentials au changement de mot de passe | 1.3 / 5.3 | ✅ fait |
| 3 | Exiger TLS en ManageSieve, option explicite pour le clair | 1.2 | ✅ fait |
| 4 | `AdminUserRequest` en `bool?` / `int?` | 5.1 | ✅ fait |
| 5 | `WithSessionAsync` + session IMAP scoped requête | 3.1 / 2.1 | ✅ fait |
| 6 | Coalescer `Uids` / `AttachmentIds`, borner l'upload | 1.5 / 1.12 | ✅ fait |
| 7 | N+1 virtual domains / `StringComparison` en LINQ | 2.3 / 2.4 | ⚠️ N+1 fait, `StringComparison` **non fait** |
| 8 | Guard credentials + helpers d'enveloppe | 3.2 | ✅ fait |
| 9 | Réactiver les diagnostics nullable | 4.1 | ✅ fait |
| 10 | Primary constructors partout, `Program.cs` en extensions | 4.2 / 4.7 | ⚠️ `Program.cs` fait, primary constructors partiels |

### Détail des corrections

- **P1** — `UsersRepository.FindByEmailAsync` exclut désormais `active = 'N'`. Un seul point de
  filtrage ferme les deux trous : le login et la vérification par requête d'`OnTokenValidated`
  passent tous deux par cette méthode. Le motif d'audit devient `unknown_or_inactive_user`, indifférencié
  à dessein pour ne pas révéler qu'un compte existe mais est désactivé.
- **P2** — `AccountController.ChangePassword` ré-émet le cookie de credentials avec le nouveau mot
  de passe, et uniquement en cas de succès. Deux tests verrouillent les deux branches.
- **P3** — `ManageSieveClient` refuse la session quand le serveur n'annonce pas STARTTLS, sauf
  `Sieve:AllowCleartext` explicite (défaut `false`). Le downgrade silencieux qui envoyait le mot de
  passe master en clair n'est plus possible.
- **P4** — `QuotaMb`/`Active`/`Admin` sont nullables ; `null` signifie « inchangé ». Quatre tests
  couvrent le PUT partiel et les défauts à la création.
- **P5** — Nouveau `IImapSessionProvider` / `ScopedImapSessionProvider` (scoped, `IAsyncDisposable`) :
  une seule connexion IMAP authentifiée par requête HTTP, fermée par le conteneur. Les 17 méthodes
  des deux repositories mail deviennent des délégations d'une ligne via `WithSessionAsync`
  (−180 lignes). Un rename, un `SetFolderRole` ou un `Send` payaient 2 à 3 handshakes TCP+TLS+SASL,
  ils n'en paient plus qu'un. 9 tests couvrent le nouveau contrat (réutilisation, concurrence,
  fermeture, mémorisation d'un échec, changement d'identifiants).
- **P6** — Les trois DTO de lot partagent une base `MessageBatchRequest` dont le setter de `Uids`
  coalesce ; `AttachmentIds` est normalisé comme les autres listes. `[DisableRequestSizeLimit]` est
  remplacé par `AttachmentSizeLimitFilter`, un *resource filter* qui plafonne le corps **avant** le
  model binding (donc avant la bufferisation sur disque), aligné sur `MaxMessageSizeMb`.
- **P7 (partiel)** — `GetAllVirtualDomainsAsync` fait une seule requête groupée au lieu d'une par
  domaine. **Le volet `StringComparison` n'a pas été fait** : mon constat 2.4 était trop catégorique.
  Le domaine est résolu en premier par une égalité indexable, donc le `LOWER()` ne s'applique qu'aux
  utilisateurs d'un seul domaine — le coût réel est faible. Surtout, passer à `==` rendrait le login
  sensible à la casse pour d'éventuelles lignes `username` historiques en majuscules, ce que je ne
  peux pas vérifier sans la base de production. Le vrai correctif (normalisation à l'écriture +
  migration des données) dépasse une revue de code.
- **P8** — Quatre helpers d'enveloppe sur `ApiBaseController` : 105 sites de
  `ResultEnveloppe.CreateErrorEnveloppe(...)` ramenés à 6, dans toute la couche contrôleur. Les 19
  préambules credentials de `MailController` deviennent un `TryMailPassword` d'une ligne. Choix
  assumé du guard plutôt que d'un `IAsyncActionFilter` : les tests de contrôleur invoquent les
  actions directement, aucun filtre ne s'y exécuterait — le contrôle serait sorti des 137 tests qui
  le couvrent. `ApiBaseController` passe `abstract`, `MailController` en primary constructor.
- **P9** — Les six suppressions du `.editorconfig` passent en `warning`. Les 74 avertissements
  découverts sont tous corrigés : DTO et entités initialisés ou rendus nullables selon ce qui est
  vrai (`MailUser.FullName`, `AccountInfo.FullName`, `ResultEnveloppe.Message` sont réellement
  optionnels), `FindByEmailAsync` déclare enfin son `User?`, `GetUser()` aussi, et
  `AuthenticatedUser` lève une exception explicite plutôt que de laisser filer un NRE. **Deux vrais
  déréférencements potentiels que la suppression masquait** ont été corrigés : `MimePart.Content`
  dans `ImapSession.GetAttachmentAsync` et `QuotePreparer.StagePartAsync`, `MessagePart.Message`
  dans `QuotePreparer.StageAttachmentAsync`. Le constructeur sans paramètre de `User` est supprimé —
  il était la seule façon d'obtenir un `User` sans `Name` ni `Domain`.

### Seconde passe — 2.7, 3.3, 3.4, 4.4, 4.7, 4.9

- **4.9** — `SieveErrors` porte quatre sentinelles partagées entre le client ManageSieve et le
  contrôleur, sur le modèle de `ImapSession.MessageNotFound` : une panne du service devient 502, une
  requête réellement invalide reste 400. Les messages du serveur ne repartent plus vers le client.
- **2.7** — partiel, voir l'annotation du constat : une copie complète en moins par téléchargement,
  mais MailKit matérialise la pièce avant qu'on puisse la lire.
- **3.3** — `ExecuteAsync` porte le contrat d'échec commun ; `ImapSession` passe de 1130 à 995 lignes.
  Les sentinelles restent par opération pour que ce soit un refactor pur. Ces chemins n'ayant aucune
  couverture, le contrat est épinglé par 9 tests dédiés — c'est là que tout le risque se concentre.
- **3.4** — `MailConnectionFactory<TClient, TSession>` : les deux fabriques ne déclarent plus que ce
  qui les distingue.
- **4.7** — `Program.cs` passe de 283 à 61 lignes, la configuration part dans `Configuration/`.
  **4.8** tombe avec : les `.LogTo(Console.WriteLine, …)` disparaissent, donc les avertissements EF
  repassent par Serilog.
- **4.4** — une seule pile JWT désormais : `JsonWebTokenHandler` pour écrire comme pour valider, et
  `System.IdentityModel.Tokens.Jwt` est retiré du `csproj`.

### Troisième passe — 1.8, 1.9, 2.5

Les trois se corrigeaient dans le même geste, et séparément aucun n'aurait tenu : rendre la
comparaison constante (1.8) ne sert à rien tant que l'existence du compte se lit sur le temps de
réponse, et c'est la fusion des deux appels (1.9) qui fait tomber le compte de requêtes (2.5).

`VerifyCredentialsAsync` remplace `FindByEmailAsync` + `IsValidPasswordAsync` sur le chemin du
login. Elle fait une seule requête jointe, calcule le crypt **avant** de décider — contre un hash
leurre quand aucune boîte ne correspond — et ne forme le verdict qu'une fois les deux terminés.

Mesures médianes sur 25 tirages, provider en mémoire (donc l'écart mesuré est bien celui du crypt,
pas celui de la base) :

| Chemin | Avant | Après |
|---|---|---|
| Compte inconnu | 0,085 ms | 2,87 ms |
| Domaine inconnu | 0,085 ms | 2,89 ms |
| Mot de passe faux | ~3 ms | 3,00 ms |

Facteur 35 avant, ~4 % après — bien en dessous de la gigue réseau.

**Ce qui n'est pas testé automatiquement :** l'égalisation elle-même. Une assertion de timing serait
instable sur une machine partagée. Ce qui est épinglé, c'est le mécanisme dont elle dépend :
`AbsentAccountHash_IsARealSha512CryptHashOfProductionCost` vérifie que le leurre reste un vrai
`$6$` aux mêmes paramètres de coût que les hashs stockés. Si cette propriété tombe, l'égalisation
tombe avec, et c'est le seul endroit où elle pourrait tomber en silence.

### Non fait volontairement

- Le volet `StringComparison` de P7 (ci-dessus).
- La validation de longueur de la clé JWT (**1.11**) : je l'avais ajoutée en nettoyant
  `TokenBuilder`, puis retirée. Elle est hors du périmètre 1–9 et un seuil plus strict que celui de
  `Microsoft.IdentityModel` (16 octets) bloquerait au login un déploiement qui fonctionne
  aujourd'hui. À traiter comme une validation **au démarrage**, avec la valeur de prod sous les yeux.
