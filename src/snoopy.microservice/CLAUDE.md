# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build                    # Build the project
dotnet build -c Release         # Release build
dotnet run                      # Run on localhost:5104 (opens Swagger UI)
dotnet clean                    # Clean build artifacts
dotnet test                     # Run all tests (always use this, NOT --no-build, when new test files have been added)
dotnet test --no-build          # Run tests without recompiling (safe only when no new test files were added since last build)
```

Tests live in the `snoopy.microservice.Tests` sub-project (xUnit 2.9.3, Moq 4.20.72, EF Core InMemory). Repository tests use a per-test in-memory database via `TestDbContext`. Controller tests use `Moq` and `ControllerTestHelpers.CreateAuthenticatedContext`.

Release procedure lives in the `ship-microservice` skill (`.claude/skills/ship-microservice/SKILL.md` at the repo root) — invoked by the user saying "ship the microservice", "déploie le backend", or `/ship-microservice`. A separate `ship-frontend` skill will handle the frontend release.

## Architecture

**weesky.mail.snoopy** is an ASP.NET Core (.NET 10) REST API for weesky.net mail administration. It manages email accounts, aliases, and domains backed by a Dovecot/MariaDB database.

### Layers

**Controllers** (`Controllers/`) receive HTTP requests and return `ResultEnveloppe<T>` responses via helpers in `ApiBaseController`. The main controllers are:
- `LoginController` — `POST /api/login` (issue JWT), `DELETE /api/login` (revoke cookie)
- `AccountController` — `GET /api/account` (info), `GET /api/account/quota` (Dovecot quota), `GET /api/account/folders` (IMAP folder list for the rules editor), `PATCH /api/account/changesecret` (password change), `POST /api/Account/FullName` (change display name)
- `AliasesController` — `GET/POST/DELETE /api/aliases` (alias CRUD, scoped to caller's owned domains)
- `AdminController` — admin-only CRUD (class-level `[Authorize(Policy = AdminRequirement.PolicyName)]`; non-admins get **403**, unauthenticated callers 401): users (`GET/POST/PUT/DELETE /api/Admin/users`, `GET /api/Admin/users/{id}/quota`), domains (`GET/POST/PUT/DELETE /api/Admin/domains`), virtual alias domains (`GET /api/Admin/domains/virtuals`, `PUT /api/Admin/domains/virtuals/{domainId}`, `DELETE /api/Admin/domains/virtuals/{domainId}/{userId}`)
- `RulesController` — Sieve mail-filtering rules: `GET/PUT/DELETE /api/Rules` (structured `SieveRule[]`), `GET/PUT /api/Rules/Raw` (unparsed script for Advanced scripts), `POST /api/Rules/CompatibilityCheck` (preview rules a target provider can't represent), `GET /api/Rules/Providers` (registered providers + default flag). See `DESIGN.md` and the repo-root `DESIGN-rules.md`.
- `MailController` — IMAP access: `GET /api/Mail/Folders` (tree with roles, subscription state, counts, `UidValidity`), `POST/PUT/DELETE /api/Mail/Folders` (create / rename / delete), `PUT /api/Mail/Folders/Subscription` (visibility), `GET /api/Mail/Messages?folder=&page=&pageSize=` (page of envelopes, newest first, page size capped at 200), `GET /api/Mail/Messages/Detail?folder=&uid=` (sanitised body + attachment list), `GET /api/Mail/Messages/Attachment?folder=&uid=&part=` (binary, always an attachment disposition)

**Repositories** (`Repositories/`) handle all database access via EF Core. `UsersRepository` validates credentials and updates passwords; `AliasesRepository` lists/creates/deletes aliases and enforces domain ownership via the `MailDomainOwnership` join table. `SieveRepository` (behind `ISieveRepository`) is the exception — it does **not** use EF Core; it reads/writes Sieve scripts over ManageSieve (`IManageSieveClient`), detecting/compiling/parsing them through the `RuleProviders`.

**Services** (`Services/`) wrap external integrations. `DovecotQuotaClient` (typed `HttpClient`) calls the remote doveadm HTTP API (`quotaGet`) to retrieve live mailbox quota. `ManageSieveClient` (behind `IManageSieveClient`) opens authenticated ManageSieve sessions (RFC 5804, port 4190, SASL PLAIN via a master user impersonating the mailbox) exposing list/get/put/setactive/delete script verbs. `ImapConnectionFactory` opens one MailKit `ImapClient` per request and hands it to an `ImapSession`; `MailCredentialStore` encrypts the user's mail password into a cookie; `MailHtmlSanitizer` makes message bodies safe to render.

### Mail — the three rules a newcomer would otherwise breach

1. **Nothing about the mail server is configured.** `MailOptions` holds connection parameters only (host, port, `SecureSocketOptions`, timeout). The hierarchy separator, namespace, special-use folders and capabilities are all read from the live IMAP session, with a name-based fallback where the server advertises no `SPECIAL-USE`. That fallback resolves **across the whole folder list, not folder by folder** (`ResolveSpecialUses`): a mailbox provisioned by two clients holds both `Drafts` and `Brouillons`, and each role must land on exactly one folder. Server flags are claimed in a first pass and name guesses only fill roles still unclaimed, so a guess can never overrule what the server actually said. This is what will let a later slice point at arbitrary external servers whose configuration we will never hold — code that hard-codes a server fact is wrong even when it works today. `MailOptions` is consumed through **`IOptionsMonitor`**, not `IOptions`, so connection values can be corrected in `appsettings.json` without restarting and dropping live sessions.
2. **Folder paths never appear in a route segment.** The separator may be `/`. They travel in the query string (GET) or the request body (POST/PUT/DELETE). Responses carry `UidValidity` so a client can tell when its cached UIDs went stale.
3. **Two distinct failure modes.** A missing or undecryptable credentials cookie is **401** carrying `credentials_unavailable`, so the client signs in again rather than showing an opaque IMAP error. Anything the mail server refuses is **502**. `ImapSession.MessageNotFound` / `.AttachmentNotFound` are shared constants — not literals repeated in the controller — mapped to **404**, so the layer producing an error and the layer choosing a status cannot drift apart.

**Authentication to IMAP uses the user's own password**, captured at login (it is unrecoverable from the database — MariaDB stores SHA-512 crypt) and kept in a Data-Protection-encrypted cookie. This is the Rainloop/Snappymail model, and the only one that generalises to external servers; a master user was considered and rejected because it works solely on the home server and would require a second authentication path. **The key ring must be persisted** — see `docs/superpowers/mail-2a-server-prerequisite.md`; without `StateDirectory=` in the systemd unit the service refuses to start outside Development.

**RuleProviders** (`RuleProviders/`) compile/parse the shared `SieveRule[]` model to/from on-disk Sieve. `WeeskyRuleProvider` is the native superset (everything); `RainloopRuleProvider` is the Snappymail-interop format with a strict `CanRepresent` whitelist. `RuleProviderRegistry` holds them: `Default`=`weesky` (compilation fallback), `NewAccountDefault`=`rainloop` (new mailboxes are visible in the Snappymail webmail), plus `GetById`/`Detect`.

**Authentication** (`Authentication/`) configures JWT bearer + HTTP-only cookie auth. `UserAuthenticator` validates credentials; `TokenManager` + `TokenBuilder` issue signed JWTs. Token constants (issuer, audience, expiry, signing key, cookie name) come from `appsettings.json` under the `"Token"` key. Admin endpoints use the `"Admin"` authorization policy (`Authentication/Authorization/`): `AdminRequirementHandler` resolves the Upn/Dns claims and checks `IAdminRepository.IsAdminAsync` per request.

**Data** (`Data/`) contains the EF Core `ApplicationDbContext` and entity classes (`MailUser`, `MailDomain`, `MailAlias`, `MailDomainOwnership`) that map directly to the Dovecot database schema.

### Key Patterns

- **Functional error handling:** Repository and service methods return `Result<T>` / `Result` from `CSharpFunctionalExtensions`. Controllers unwrap these and call `Ok(result)` / `Problem(result)` helpers from `ApiBaseController`.
- **JWT claims:** `ClaimTypes.Upn` = username, `ClaimTypes.Dns` = domain **name** (e.g. `"weesky.be"`, NOT the 3-char domain ID). `AdminRepository.IsAdminAsync` resolves name → ID via the `Domains` table before querying users. All EF repository methods are async (`Async` suffix, `Task<T>` return types).
- **Password storage — CRITICAL:** The microservice **must store passwords as plaintext**. MariaDB triggers `INSERT_PASSWORD` and `UPDATE_PASSWORD` on the `users` table automatically encrypt the value using SHA-512 crypt (`$6$...`) before it is persisted. Any server-side hashing (e.g. CryptSharp) would double-encrypt and break login. Always assign `Password = request.Password` directly — never hash.
- **Database:** MySQL via Pomelo EF Core provider, targeting the `dovecot` database. Development overrides in `appsettings.Development.json` point to `10.0.0.2`.
- **Assert.IsType&lt;T&gt; in tests:** Checks the **exact** runtime type. `BadRequest(body)` returns `BadRequestObjectResult` (a subtype of `ObjectResult`); always use `Assert.IsType<BadRequestObjectResult>` for those. Only `StatusCode(400)` / the `FromResult()` helper returns a plain `ObjectResult`.
