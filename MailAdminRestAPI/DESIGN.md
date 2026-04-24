# DESIGN — Milkyway (MailAdminRestAPI)

API REST ASP.NET Core (.NET 10) pour l'administration du courrier de weesky.net. Sert de couche HTTP au-dessus de la base `dovecot` (MariaDB/MySQL) : authentification des boîtes, changement de mot de passe, et CRUD des alias limité aux domaines possédés par l'utilisateur.

## Vue d'ensemble

```
Client HTTP ──► Controllers ──► Repositories ──► EF Core ──► MariaDB (dovecot)
                    ▲   │                            │
                    │   └──► Services ──► HttpClient ──► doveadm HTTP API (quota)
                    │                                │
              Authentication                    MailUser / MailDomain
              (JWT + Cookie)                    MailAlias / MailDomainOwnership
```

Trois responsabilités fonctionnelles :

- **Login / Logout** — émission d'un JWT signé, également posé en cookie `HttpOnly; Secure; SameSite=Strict`.
- **Compte** — consultation des infos, consultation du quota (via Dovecot distant) et changement du mot de passe de la boîte authentifiée.
- **Alias** — liste, création, suppression. Toujours borné aux domaines possédés par l'appelant (via `MailDomainOwnership`, ou le domaine propre de la boîte).

## Couches

### Controllers (`Controllers/`)

Reçoivent les requêtes HTTP et renvoient soit le DTO demandé, soit un `ResultEnveloppe` pour les erreurs. Tous héritent d'`ApiBaseController` qui expose :

- `AuthenticatedUser` — reconstruit l'`User` courant depuis les claims JWT (`ClaimTypes.Upn` = nom, `ClaimTypes.Dns` = domaine).
- `FromResult(...)` / `FromResultWithEnveloppe(...)` — traduisent un `Result` / `Result<T>` de CSharpFunctionalExtensions en `ActionResult` avec le bon code HTTP.

Contrôleurs exposés :

| Contrôleur | Route | Verbes | Auth | Notes |
|---|---|---|---|---|
| `LoginController` | `/api/login` | POST, DELETE | POST anonyme / DELETE `[Authorize]` | POST soumis au rate limiter `login` (5 req/min par IP). |
| `AccountController` | `/api/account` | GET, GET `Quota`, PATCH `ChangeSecret` | `[Authorize]` | `Quota` délègue à `IDovecotQuotaClient` ; `ChangeSecret` exige l'ancien mot de passe. |
| `AliasesController` | `/api/aliases` | GET, POST, DELETE | `[Authorize]` | Toutes les opérations passent par `UserOwnsDomain`. |

### Repositories (`Repositories/`)

Encapsulent l'accès EF Core. Retournent des `Result` / `Result<T>` — aucune exception métier n'est levée en conditions nominales (seules les null argument checks lèvent).

- `UsersRepository` : `FindByEmail`, `IsValidPassword`, `GetAccountInfo`, `ChangePassword`. Hashage `crypt`-compatible Dovecot via `CryptSharp.Core`.
- `AliasesRepository` : `GetAliases`, `AddAlias`, `DeleteAlias`, plus le garde-fou `UserOwnsDomain` qui croise `MailDomainOwnership` et le domaine direct de la boîte.

Chaque mutation émet un log structuré `Audit: <action> user=... outcome=success|failure reason=...` pour piste d'audit côté infra.

### Services (`Services/`)

Encapsulent les intégrations sortantes vers des systèmes externes. Contrairement aux repositories, ces composants ne touchent pas EF Core — ils parlent HTTP/RPC.

- `DovecotQuotaClient` (derrière `IDovecotQuotaClient`) : typed `HttpClient` qui POST une commande `quotaGet` sur l'API HTTP `doveadm` du serveur Dovecot distant (header `Authorization: X-Dovecot-API <base64(ApiKey)>`). Le résultat (`STORAGE` + `MESSAGE`) est converti en DTO `Quota` (bytes + message count, `0` = illimité). Timeout `HttpClient` : 5 s. Les erreurs upstream sont loggées et remontent en `Result.Failure<Quota>` → le contrôleur traduit en `502 Bad Gateway`.

### Authentication (`Authentication/`)

- `Services/UserAuthenticator` : vérifie les credentials puis délègue à `ITokenManager`.
- `Services/TokenManager` + `Services/TokenBuilder` : construisent un `JwtSecurityToken` (Upn, Dns, Issuer, Audience, Expiry, clé HMAC).
- `Extensions/AuthorizationExtension.AddJwtBearerAuthentication` : configure JwtBearer avec double support header `Authorization: Bearer …` **et** cookie (via `OnMessageReceived`). Le handler `OnTokenValidated` rejette tout token dont l'utilisateur a disparu de la base.
- `Models/TokenConstants` : bindé depuis `TokenConstants` d'`appsettings.json` (Issuer, Audience, Key, ExpiryInMinutes, AuthCookieName).

### Data (`Data/`)

`ApplicationDbContext` mappe directement le schéma Dovecot existant :

| Entité | Table logique | Colonnes clefs |
|---|---|---|
| `MailUser` | `users` | `Id`, `Name`, `Password`, `DomainId`, `FullName`, `Active` (enum ⇄ string) |
| `MailDomain` | `domains` | `Id`, `Name` |
| `MailAlias` | `aliases` | `Id`, `Name`, `Domain`, `DestinationUserId` |
| `MailDomainOwnership` | `domain_ownerships` | `UserId`, `DomainId` |

Le contexte n'est pas propriétaire du schéma — les migrations Dovecot restent hors périmètre de cette API.

### Models (`Models/`)

DTOs exposés par l'API : `User`, `Credentials`, `Alias`, `Domain`, `AccountInfo`, `Quota`, `SecretChange`, `ResultEnveloppe`. Options de configuration bindées : `DovecotOptions` (`ApiUrl`, `ApiKey`).

## Décisions transverses

### Gestion d'erreurs fonctionnelle

`CSharpFunctionalExtensions.Result` descend depuis les repositories jusqu'aux contrôleurs. Les contrôleurs ne lèvent jamais pour signaler un échec métier — ils renvoient un `ResultEnveloppe` avec le message d'erreur. `ProblemDetails` est enregistré pour les exceptions non attrapées.

### Authentification à double canal

Le même JWT peut arriver par `Authorization: Bearer` (clients API) ou par cookie `HttpOnly` (front web). Un seul pipeline d'auth, configuré dans `AddJwtBearerAuthentication(cookiesSupport: true)`. Les cookies posés à la connexion sont `HttpOnly; Secure; SameSite=Strict` et expirent en même temps que le JWT.

### Portée des permissions

L'utilisateur n'agit jamais que sur :
- sa propre boîte (changement de mot de passe, consultation de compte) ;
- les domaines qu'il possède via `MailDomainOwnership` — ou son propre domaine par défaut — pour les alias.

`AliasesRepository.UserOwnsDomain` est l'unique point de contrôle. Les contrôleurs n'accèdent jamais directement au `DbContext`.

### Sécurité

- **Rate limiting** sur `POST /api/login` (fenêtre fixe, 5/min/IP).
- **CORS** : origins autorisées via `Cors:AllowedOrigins`, `AllowCredentials` requis pour le flux cookie.
- **Headers** : `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, et CSP (`default-src 'none'` hors Swagger ; CSP permissive uniquement sous `/swagger`).
- **Mots de passe** : hashés avec `CryptSharp` au format `crypt` compatible Dovecot. Longueur minimale imposée au changement (8 chars).
- **Logs d'audit** : chaque mutation (login, change_password, add_alias, delete_alias) trace `outcome=success|failure` et la raison.

### Configuration

Clés `appsettings.json` importantes :

- `ConnectionStrings:MailUserAccountsDatabase` — chaîne MySQL vers la base `dovecot`. Overridée en dev vers `10.0.0.2`.
- `TokenConstants` — Issuer, Audience, Key, ExpiryInMinutes, AuthCookieName.
- `Dovecot:ApiUrl` — URL complète de l'endpoint `doveadm/v1` distant. `Dovecot:ApiKey` — valeur du `doveadm_api_key` partagé avec le service.
- `Cors:AllowedOrigins` — tableau d'origins front autorisées.

## Points d'attention / dette connue

- Comparaisons `StringComparison.InvariantCultureIgnoreCase` dans les `Where` EF Core : dépend de l'activation de `EnableStringComparisonTranslations` côté Pomelo — toute régression du provider casserait silencieusement les recherches case-insensitive.
- `AddJwtBearerAuthentication` appelle `BuildServiceProvider()` au moment du setup : anti-pattern (scope root) à remplacer par `IPostConfigureOptions<JwtBearerOptions>` si les `TokenConstants` deviennent dynamiques.
- `GetAliases` ne retourne pas un `Result<IEnumerable<Alias>>` — l'échec est invisible. À aligner sur le reste des repositories si une vraie condition d'erreur apparaît.
- `UserOwnsDomain` : la jointure actuelle ignore le paramètre `domainName` dans la branche `DomainsOwnerships` (tout domaine possédé match). À corriger : filtrer explicitement sur `domain.Name == domainName`.
- Pas de tests automatisés. Les repositories accèdent directement à `DbContext` sans abstraction testable.
