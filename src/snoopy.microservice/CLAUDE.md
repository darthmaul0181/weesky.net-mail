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
- `MailController` — IMAP access: `GET /api/Mail/Folders` (tree with roles, subscription state, counts, `UidValidity`), `POST/PUT/DELETE /api/Mail/Folders` (create / rename / delete), `PUT /api/Mail/Folders/Subscription` (visibility), `GET/PUT/DELETE /api/Mail/FolderRoles` (affectation des rôles systèmes — chaîne surcharge utilisateur → SPECIAL-USE → nom), `GET /api/Mail/Messages?folder=&page=&pageSize=` (page of envelopes, newest first, page size capped at 200), `GET /api/Mail/Messages/Detail?folder=&uid=` (sanitised body + attachment list), `GET /api/Mail/Messages/Attachment?folder=&uid=&part=` (binary, always an attachment disposition)

**Repositories** (`Repositories/`) handle all database access via EF Core. `UsersRepository` validates credentials and updates passwords; `AliasesRepository` lists/creates/deletes aliases and enforces domain ownership via the `MailDomainOwnership` join table. `SieveRepository` (behind `ISieveRepository`) is the exception — it does **not** use EF Core; it reads/writes Sieve scripts over ManageSieve (`IManageSieveClient`), detecting/compiling/parsing them through the `RuleProviders`.

**Services** (`Services/`) wrap external integrations. `DovecotQuotaClient` (typed `HttpClient`) calls the remote doveadm HTTP API (`quotaGet`) to retrieve live mailbox quota. `ManageSieveClient` (behind `IManageSieveClient`) opens authenticated ManageSieve sessions (RFC 5804, port 4190, SASL PLAIN via a master user impersonating the mailbox) exposing list/get/put/setactive/delete script verbs. `ImapConnectionFactory` opens one MailKit `ImapClient` per request and hands it to an `ImapSession`; `MailCredentialStore` encrypts the user's mail password into a cookie; `MailHtmlSanitizer` makes message bodies safe to render.

### Mail — the four rules a newcomer would otherwise breach

1. **Nothing about the mail server is configured.** `MailOptions` holds connection parameters only (host, port, `SecureSocketOptions`, timeout). The hierarchy separator, namespace, special-use folders and capabilities are all read from the live IMAP session, with a name-based fallback where the server advertises no `SPECIAL-USE`. That fallback resolves **across the whole folder list, not folder by folder** (`ResolveSpecialUses`): a mailbox provisioned by two clients holds both `Drafts` and `Brouillons`, and each role must land on exactly one folder. Server flags are claimed in a first pass and name guesses only fill roles still unclaimed, so a guess can never overrule what the server actually said. This is what will let a later slice point at arbitrary external servers whose configuration we will never hold — code that hard-codes a server fact is wrong even when it works today. `MailOptions` is consumed through **`IOptionsMonitor`**, not `IOptions`, so connection values can be corrected in `appsettings.json` without restarting and dropping live sessions.
2. **Folder paths never appear in a route segment.** The separator may be `/`. They travel in the query string (GET) or the request body (POST/PUT/DELETE). Responses carry `UidValidity` so a client can tell when its cached UIDs went stale.
3. **Two distinct failure modes.** A missing or undecryptable credentials cookie is **401** carrying `credentials_unavailable`, so the client signs in again rather than showing an opaque IMAP error. Anything the mail server refuses is **502**. `ImapSession.MessageNotFound` / `.AttachmentNotFound` are shared constants — not literals repeated in the controller — mapped to **404**, so the layer producing an error and the layer choosing a status cannot drift apart.
4. **Folder roles resolve through an ordered chain, and its output is what the client sees.** User override (per account, stored in the separate `snoopy_webmail` database via `PreferencesDbContext`/`IFolderRoleStore`) → `SPECIAL-USE` flags → multilingual name fallback, each level filling only what the previous left, tracking **both** claimed roles and claimed folders — one folder never holds two roles. `FolderRoleResolver` is pure over the tree and the stored rows; `GET /Folders` stamps its `RoleByPath` onto every node's `SpecialUse`. Overrides store the **path** (the only identifier IMAP guarantees), guarded by `uid_validity` against path reuse, with RFC 8474 `MAILBOXID` as an optional aid — never the key. Stale overrides are kept and signalled, never auto-deleted. Our own renames/deletes move or purge the rows (IMAP first, database second; a failed bookkeeping write degrades, never fails the user's operation). Database creation is manual — see `docs/superpowers/mail-2a5-database-prerequisite.md`; the service refuses to start without the `WebmailPreferencesDatabase` connection string.

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


## C# Coding Style

### File Organization

- **File-scoped namespaces always.** Block-scoped namespaces waste indentation for zero benefit.
- **One type per file.** File name must match the type name exactly (`OrderService.cs` contains `OrderService`).
- **Order members:** constants, fields, constructors, properties, public methods, private methods. Consistent ordering reduces cognitive load when scanning a file.

### Type Declarations

- **Primary constructors for DI injection.** Eliminates boilerplate field assignments and `_field = field` ceremony.

```csharp
// DO
public sealed class OrderService(IDbContext db, TimeProvider clock) { }

// DON'T
public class OrderService
{
    private readonly IDbContext _db;
    public OrderService(IDbContext db) { _db = db; }
}
```

- **Records for DTOs and value objects.** Immutability, value equality, and `with` expressions for free.

```csharp
public sealed record CreateOrderRequest(string ProductId, int Quantity);
public sealed record Money(decimal Amount, string Currency);
```

- **`sealed` on classes not designed for inheritance.** The JIT can devirtualize calls on sealed types, and it communicates intent clearly.
- **`internal` by default, `public` only when needed.** Minimize the public API surface. If nothing outside the project references it, it should be `internal`.

### Expressions and Patterns

- **Collection expressions over constructor calls.** Shorter, compiler-optimized, and consistent across collection types.

```csharp
// DO
List<int> ids = [1, 2, 3];
int[] arr = [4, 5, 6];

// DON'T
var ids = new List<int> { 1, 2, 3 };
```

- **Pattern matching over if-else chains.** Switch expressions and `is` patterns are more readable and exhaustiveness-checked.

```csharp
// DO
var label = status switch
{
    OrderStatus.Pending => "Awaiting payment",
    OrderStatus.Shipped => "On the way",
    _ => "Unknown"
};

// DON'T
string label;
if (status == OrderStatus.Pending) label = "Awaiting payment";
else if (status == OrderStatus.Shipped) label = "On the way";
else label = "Unknown";
```

### Naming and Modifiers

- **`var` for obvious types, explicit types when clarity matters.** Use `var` when the right-hand side makes the type self-evident (`var order = new Order()`); spell it out when it does not (`HttpResponseMessage response = await ...`).
- **Async suffix on all async methods.** `GetOrderAsync`, not `GetOrder`, for methods returning `Task` or `ValueTask`. Prevents accidental sync calls.
- **PascalCase** for public members, types, namespaces, and methods. **camelCase** for local variables and parameters.
- **No `_` prefix on private fields when using primary constructors.** The parameter name is the field name.


### Other rules
- Don't hesitate to use extensions methods
- Use **clean architecture**
- **ALWAYS** Prefer record types for immutable data structures.
- **ALWAYS** use ILogger with Structured logging to log, no string interpolation and no other logging methods.
- **ALWAYS** use cancellation tokens for asynchronous methods.
- **ALWAYS** use Data Transfer Objects (DTO) for API communication, validated with attributes.
- **NEVER** use try-catch blocks solely to log and rethrow exceptions.
- If any backend changes are made, run dotnet build in the root and ensure no build errors for the entire solution.


