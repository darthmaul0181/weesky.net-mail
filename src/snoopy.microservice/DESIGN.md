# DESIGN — weesky.mail.snoopy (snoopy.microservice)

ASP.NET Core (.NET 10) REST API for weesky.net mail administration. Acts as an HTTP layer on top of the `dovecot` database (MariaDB/MySQL): mailbox authentication, password change, alias CRUD scoped to domains owned by the user, and Sieve mail-filtering rules over the ManageSieve protocol.

## Overview

```
HTTP client ──► Controllers ──► Repositories ──► EF Core ──► MariaDB (dovecot)
                    ▲   │                            │
                    │   ├──► Services ──► HttpClient ──► doveadm HTTP API (quota)
                    │   │                            │
                    │   └──► SieveRepository ──► ManageSieveClient ──► ManageSieve (TCP, port 4190)
                    │              │                 │
              Authentication      └► RuleProviders   MailUser / MailDomain
              (JWT + Cookie)         (Weesky/Rainloop) MailAlias / MailDomainOwnership
```

Four functional responsibilities:

- **Login / Logout** — issues a signed JWT, also set as an `HttpOnly; Secure; SameSite=Strict` cookie.
- **Account** — mailbox info, quota lookup (via remote Dovecot), IMAP folder list, and password change for the authenticated mailbox.
- **Aliases** — list, create, delete. Always scoped to domains owned by the caller (via `MailDomainOwnership`, or the mailbox's own domain).
- **Rules** — read/write Sieve mail-filtering scripts over the ManageSieve protocol. Rules are modelled as a provider-agnostic `SieveRule[]` and compiled/parsed by a `IRuleProvider` (Weesky native or Rainloop/Snappymail-interop). See [`/DESIGN-rules.md`](../../DESIGN-rules.md) for the full feature analysis and interop trade-offs.

## Layers

### Controllers (`Controllers/`)

Receive HTTP requests and return either the requested DTO or a `ResultEnveloppe` for errors. All inherit from `ApiBaseController`, which exposes:

- `AuthenticatedUser` — rebuilds the current `User` from JWT claims (`ClaimTypes.Upn` = name, `ClaimTypes.Dns` = domain).
- `FromResult(...)` / `FromResultWithEnveloppe(...)` — translate a `Result` / `Result<T>` from CSharpFunctionalExtensions into an `ActionResult` with the correct HTTP status code.

Exposed controllers:

| Controller | Route | Verbs | Auth | Notes |
|---|---|---|---|---|
| `LoginController` | `/api/login` | POST, DELETE | POST anonymous / DELETE `[Authorize]` | POST goes through the `login` rate limiter (5 req/min per IP). |
| `AccountController` | `/api/account` | GET, GET `Quota`, GET `Folders`, PATCH `ChangeSecret` | `[Authorize]` | `Quota` delegates to `IDovecotQuotaClient`; `Folders` lists IMAP mailboxes (for the rules editor folder picker); `ChangeSecret` requires the old password. |
| `AliasesController` | `/api/aliases` | GET, POST, DELETE | `[Authorize]` | Every operation goes through `UserOwnsDomain`. |
| `AdminController` | `/api/Admin/users`, `/api/Admin/domains`, `/api/Admin/ownerships` | GET, POST, PUT, DELETE | `[Authorize]` + `admin='Y'` check | User CRUD + quota proxy; domain CRUD; alias domain ownership GET/PUT/DELETE. All endpoints check `IsAdmin()` and return 401 if false. |
| `RulesController` | `/api/Rules` | GET, PUT, DELETE, GET/PUT `Raw`, POST `CompatibilityCheck`, GET `Providers` | `[Authorize]` | Sieve rules CRUD via `ISieveRepository`. `GET`/`PUT` exchange structured `SieveRule[]`; `Raw` exchanges the unparsed script for Advanced scripts; `CompatibilityCheck` previews which rules a target provider can't represent; `Providers` lists registered providers with the default flag. |

### Repositories (`Repositories/`)

Wrap EF Core access. Return `Result` / `Result<T>` — no business exceptions are thrown under nominal conditions (only null argument checks throw).

- `UsersRepository`: `FindByEmail`, `IsValidPassword`, `GetAccountInfo`, `ChangePassword`. Passwords are stored **plaintext** — the MariaDB `INSERT_PASSWORD`/`UPDATE_PASSWORD` triggers do the SHA-512 crypt encryption.
- `AliasesRepository`: `GetAliases`, `AddAlias`, `DeleteAlias`, plus the `UserOwnsDomain` guard that joins `MailDomainOwnership` with the mailbox's direct domain.
- `SieveRepository` (behind `ISieveRepository`): the only repository that does **not** touch EF Core — it talks to the mail server over ManageSieve via `IManageSieveClient`. Opens a fresh authenticated session per call. `GetRuleSetAsync` lists scripts, picks the active one, detects its provider (`RuleProviderRegistry.Detect`) and parses it into a `SieveRuleSet` (Structured if the provider can parse it, otherwise Advanced with the raw body). `SaveRulesAsync` compiles the `SieveRule[]` with the target provider, PUTs + activates the script, then `CleanupOtherManagedScriptsAsync` removes the other providers' managed scripts (never the active one, never an unknown/Advanced script). `GetRawScriptAsync`/`SaveRawScriptAsync` handle the unparsed-script path; `DeleteAllRulesAsync` deactivates and deletes.

Every mutation emits a structured log `Audit: <action> user=... outcome=success|failure reason=...` for infra-side audit trails.

### Services (`Services/`)

Wrap outbound integrations with external systems. Unlike repositories, these components don't touch EF Core — they speak HTTP/RPC.

- `DovecotQuotaClient` (behind `IDovecotQuotaClient`): typed `HttpClient` that POSTs a `quotaGet` command to the remote Dovecot server's `doveadm` HTTP API (header `Authorization: X-Dovecot-API <base64(ApiKey)>`). The result (`STORAGE` + `MESSAGE`) is converted into a `Quota` DTO (bytes + message count, `0` = unlimited). `HttpClient` timeout: 5s. Upstream errors are logged and bubble up as `Result.Failure<Quota>` → the controller translates them into `502 Bad Gateway`.
- `ManageSieveClient` (behind `IManageSieveClient`): opens an authenticated **ManageSieve** session (RFC 5804) to the mail server (TCP, default port 4190, STARTTLS). Authentication is SASL PLAIN with a configured **master user** (`auth_master_users`) impersonating the target mailbox, so the service never needs the user's password. `OpenSessionAsync` returns an `IManageSieveSession` that exposes `ListScriptsAsync`, `GetScriptAsync`, `PutScriptAsync`, `SetActiveAsync`, and `DeleteScriptAsync` — the verbs `SieveRepository` drives. The session implements the line/literal framing of the protocol and surfaces server errors as `Result.Failure`. Connection settings come from `SieveOptions`.

### Rule providers (`RuleProviders/`)

A provider turns the shared, format-agnostic `SieveRule[]` model into an on-disk Sieve script and back. Each implements `IRuleProvider`:

- `CanHandle(script)` — does this provider recognise the script's marker? (used by detection)
- `Compile(rules)` — produce the `.sieve` text written to the server.
- `Parse(script)` — read a script back into `SieveRule[]` (the inverse of `Compile`).
- `CanRepresent(rule)` — can this provider losslessly store the rule? (drives compatibility checks)

Two providers:

- `WeeskyRuleProvider` — native format, **superset**: emits everything (multiple actions, extended flags, `body`/`envelope`/`subaddress`/`date`/`duplicate` conditions, `fileinto :create`, `:regex`). The whole structured model round-trips through a first-line `# WEESKY-RULES-V1:<base64 JSON>` marker. `CanRepresent` always succeeds. Default script name `weesky-rules`.
- `RainloopRuleProvider` — **Snappymail-interop** format (detected by the `# RAINLOOP:SIEVE` marker): each rule is stored twice — a base64 JSON `BEGIN:HEADER` block (the source of truth, re-read by us *and* Snappymail) plus an executable Sieve body. `CanRepresent` is a **strict whitelist** (only the fields the Snappymail JSON schema can hold), so any future Weesky feature is flagged incompatible by default. Default script name `rainloop.user`.

`RuleProviderRegistry` (behind `IRuleProviderRegistry`) holds all providers and exposes `Default` (`weesky`, the compilation fallback), `NewAccountDefault` (`rainloop`, so a brand-new mailbox is visible in the Snappymail webmail), `GetById`, and `Detect(script)` (returns the provider whose marker matches, or `null`). When the requested provider is missing it falls back to `All[0]`.

### Authentication (`Authentication/`)

- `Services/UserAuthenticator`: verifies credentials, then delegates to `ITokenManager`.
- `Services/TokenManager` + `Services/TokenBuilder`: build a `JwtSecurityToken` (Upn, Dns, Issuer, Audience, Expiry, HMAC key).
- `Extensions/AuthorizationExtension.AddJwtBearerAuthentication`: configures JwtBearer with dual support for the `Authorization: Bearer …` header **and** cookies (via `OnMessageReceived`). The `OnTokenValidated` handler rejects any token whose user no longer exists in the database.
- `Models/TokenConstants`: bound from `TokenConstants` in `appsettings.json` (Issuer, Audience, Key, ExpiryInMinutes, AuthCookieName).

### Data (`Data/`)

`ApplicationDbContext` maps directly to the existing Dovecot schema:

| Entity | Logical table | Key columns |
|---|---|---|
| `MailUser` | `users` | `Id`, `Name`, `Password`, `DomainId`, `FullName`, `Active` (enum ⇄ string) |
| `MailDomain` | `domains` | `Id`, `Name` |
| `MailAlias` | `aliases` | `Id`, `Name`, `Domain`, `DestinationUserId` |
| `MailDomainOwnership` | `domain_ownerships` | `UserId`, `DomainId` — only for alias domains (domains not used as a primary mailbox by any user) |

The context doesn't own the schema — Dovecot migrations are out of scope for this API.

### Models (`Models/`)

DTOs exposed by the API: `User`, `Credentials`, `Alias`, `Domain`, `AccountInfo`, `Quota`, `SecretChange`, `ResultEnveloppe`. Sieve rules: `SieveRule`, `SieveCondition` (`SieveConditionField`, `SieveConditionOperator`), `SieveAction` (`SieveActionType`), `SieveRuleSet` (`SieveScriptKind` = Structured | Advanced), `SieveRawScript`, `SaveRulesRequest`, `CompatibilityCheckRequest`/`CompatibilityCheckResult` (+ `IncompatibleRule`), `RuleProviderInfo`. Bound configuration options: `DovecotOptions` (`ApiUrl`, `ApiKey`), `SieveOptions` (ManageSieve host/port/TLS).

## Cross-cutting decisions

### Functional error handling

`CSharpFunctionalExtensions.Result` flows from repositories down to controllers. Controllers never throw to signal a business failure — they return a `ResultEnveloppe` with the error message. `ProblemDetails` is registered for unhandled exceptions.

### Dual-channel authentication

The same JWT can arrive via `Authorization: Bearer` (API clients) or via an `HttpOnly` cookie (web front). A single auth pipeline, configured in `AddJwtBearerAuthentication(cookiesSupport: true)`. Cookies set at login are `HttpOnly; Secure; SameSite=Strict` and expire at the same time as the JWT.

### Permission scope

The user can only act on:
- their own mailbox (password change, account info);
- the domains they own via `MailDomainOwnership` — or their own domain by default — for aliases.

`AliasesRepository.UserOwnsDomain` is the only enforcement point. Controllers never access the `DbContext` directly.

### Security

- **Rate limiting** on `POST /api/login` (fixed window, 5/min/IP).
- **CORS**: allowed origins via `Cors:AllowedOrigins`, `AllowCredentials` required for the cookie flow.
- **Headers**: `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, and CSP (`default-src 'none'` outside Swagger; permissive CSP only under `/swagger`).
- **Passwords**: stored **plaintext** — the MariaDB `INSERT_PASSWORD`/`UPDATE_PASSWORD` triggers apply the SHA-512 crypt encryption (`$6$…`). The service must never hash, or it would double-encrypt and break login. Minimum length enforced on change (8 chars).
- **Audit logs**: every mutation (login, change_password, add_alias, delete_alias) logs `outcome=success|failure` and the reason.

### Configuration

Key `appsettings.json` entries:

- `ConnectionStrings:MailUserAccountsDatabase` — MySQL connection string to the `dovecot` database. Overridden in dev to `10.0.0.2`.
- `TokenConstants` — Issuer, Audience, Key, ExpiryInMinutes, AuthCookieName.
- `Dovecot:ApiUrl` — full URL of the remote `doveadm/v1` endpoint. `Dovecot:ApiKey` — value of the `doveadm_api_key` shared with the service.
- `Sieve` — ManageSieve connection used by `ManageSieveClient` for the rules feature: `Host`, `Port` (`4190`), `MasterUser`/`MasterPassword` (impersonation), `ScriptName` (`weesky-rules`), `TimeoutSeconds`, `AllowInvalidCertificate` (dev only).
- `Cors:AllowedOrigins` — array of allowed frontend origins.

## Known caveats / tech debt

- `StringComparison.InvariantCultureIgnoreCase` comparisons in EF Core `Where` clauses: depend on `EnableStringComparisonTranslations` being enabled on the Pomelo side — any provider regression would silently break case-insensitive lookups.
- `AddJwtBearerAuthentication` calls `BuildServiceProvider()` at setup time: anti-pattern (root scope) to be replaced with `IPostConfigureOptions<JwtBearerOptions>` if `TokenConstants` become dynamic.
- `GetAliases` does not return a `Result<IEnumerable<Alias>>` — failures are invisible. Should be aligned with the other repositories if a real error condition appears.
- `UserOwnsDomain`: the current join ignores the `domainName` parameter in the `DomainsOwnerships` branch (any owned domain matches). To fix: filter explicitly on `domain.Name == domainName`.
- Repositories access `DbContext` directly, without a testable abstraction. Tests use EF Core InMemory; real DB behaviour (e.g. case-insensitive collation) is not covered.
