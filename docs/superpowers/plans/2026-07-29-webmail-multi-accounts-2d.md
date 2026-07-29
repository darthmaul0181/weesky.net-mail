# Webmail Multi-Accounts (2d) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Connect additional mail accounts (local shared mailboxes and admin-defined external domains) to a session, with encrypted-at-rest credentials, an account switch in the identity menu, and per-account folders/messages/identities/rules.

**Architecture:** A scoped `AccountConnectionResolver` turns the `X-Account-Id` header (or `account` query fallback) + the credentials cookie into one `MailAccountConnection` record consumed by the IMAP/SMTP factories — one code path for home and external servers. Connected-account passwords are AES-256-GCM-encrypted with a KEK derived (PBKDF2) from the user's main password; the KEK travels in the existing credentials cookie (payload v2). Frontend: `AuthContext` grows `switchAccount`; every mail query key is already account-scoped, so a switch creates a fresh cache. Spec: `docs/superpowers/specs/2026-07-29-webmail-multi-accounts-2d-design.md`.

**Tech Stack:** ASP.NET Core (.NET 10), MailKit, EF Core (Pomelo, InMemory for tests), xUnit+Moq; React 18/TS, TanStack Query v5, Vitest.

## Global Constraints

- All code, comments and UI copy in **English**.
- Backend style: file-scoped namespaces, records for DTOs, `internal sealed` by default, primary constructors for DI, cancellation tokens on async methods, `Result<T>` error handling, structured logging only, no secret (password, KEK, cipher) in any response or log line.
- Error codes on the wire: 401 `credentials_unavailable` (session cookie missing/unreadable — unchanged), **404** for an unknown or foreign `X-Account-Id` (indistinguishable on purpose), **409 `connected_credentials_invalid`** (GCM tag failure — main password changed outside the app), 502 for anything a mail/sieve server refuses (server message never relayed), 400 for validation.
- Account transport: header **`X-Account-Id`**, value `primary` (or absent) for the primary account, else the connected account GUID; browser-navigated URLs that cannot carry a header use the **`account` query parameter** as fallback (resolver reads header first, then query).
- DB sentinel: in `folder_role_overrides.account_id` and `sending_identities.account_id`, the **empty string `''` means primary** (a nullable column cannot join a composite PK); on the wire the same concept is spelled `primary`. Never mix the two: `''` in the database, `'primary'` on the wire.
- Crypto constants: PBKDF2-SHA256, **600 000 iterations**, 16-byte per-user salt (`users.kdf_salt`), 32-byte KEK; AES-256-GCM, cipher layout `nonce(12) ‖ tag(16) ‖ ciphertext`, stored in `VARBINARY(512)`.
- Credentials cookie payload v2 format: `wm2|<base64(password utf8)>|<base64(kek)>`; anything that does not parse as that is a v1 cookie whose whole value is the password (`Kek == null`).
- UI labels: user tab **"Connected accounts"** (replaces "Linked accounts" everywhere), admin tab **"External domains"**; actions are icon buttons (`PencilIcon`, `TrashIcon` house pattern), deletions via the shared `DeleteConfirmModal`.
- `localStorage` keys: active account `mail.activeAccount`; notification claim becomes per-account `mail.lastNotifiedUidNext.<accountId>`.
- `dotnet test` from `src/snoopy.microservice` — **never `--no-build`** when new test files were added. Frontend from `src/frontend`: `npm test`, `npm run typecheck`, `npm run lint`, `npm run build`.
- Git: stage files explicitly (never `git add -A`); never commit `.claude/settings.local.json`, `src/frontend/src/App.test.tsx`, `src/snoopy.microservice/ApiDocumentation.xml`; commit message = subject + ≤2 body lines, never starting or ending with `@`, via POSIX heredoc `git commit -F -`; **NEVER push**.

**Two deliberate deviations from the spec** (implementation constraints, keep them):
1. The spec drew `account_id` as `CHAR(36) NULL` with an FK to `connected_accounts`. MariaDB cannot put a nullable column in a composite PK and a unique index treats every NULL as distinct, so both tables use `VARCHAR(36) NOT NULL DEFAULT ''` with **application-level cascade** (deleting a connected account purges its identities and overrides in the same SaveChanges).
2. The spec names only the header; the `account` query fallback exists because staged-attachment content URLs are consumed by `<img src>` where no header can travel.

---

## Phase 1 — Database & crypto foundations

### Task 1: DDL document `webmail-connected-accounts-tables.md`

Documentation only — no code. The DDL is applied manually by an admin (the service account has no CREATE/ALTER); EF InMemory tests never touch it, so the rest of the plan does not block on it, but **it must be played on `snoopy_webmail` and `snoopy_webmail_dev` before any deployed testing**.

**Files:**
- Create: `docs/superpowers/webmail-connected-accounts-tables.md`

- [ ] **Step 1: Write the document** on the exact mould of `docs/superpowers/webmail-contacts-tables.md` (read it first for section order: Contexte / DDL / Vérification / Désinstallation / Ce qui reste à la charge de l'application; French prose, column comments in French). DDL core (repeat for both databases):

```sql
ALTER TABLE users
  ADD COLUMN kdf_salt BINARY(16) NULL
    COMMENT 'Sel PBKDF2 du KEK des comptes connectés ; généré au premier login qui suit la migration';

CREATE TABLE IF NOT EXISTS external_domains (
  id            CHAR(36)     NOT NULL COMMENT 'GUID',
  name          VARCHAR(100) NOT NULL COMMENT 'Nom d''affichage (« Gmail »)',
  imap_host     VARCHAR(255) NOT NULL,
  imap_port     SMALLINT UNSIGNED NOT NULL,
  imap_security VARCHAR(16)  NOT NULL COMMENT 'None | StartTls | SslOnConnect',
  smtp_host     VARCHAR(255) NOT NULL,
  smtp_port     SMALLINT UNSIGNED NOT NULL,
  smtp_security VARCHAR(16)  NOT NULL,
  sieve_host    VARCHAR(255) NULL COMMENT 'NULL = le domaine ne supporte pas Sieve',
  sieve_port    SMALLINT UNSIGNED NULL,
  creation_date DATETIME     NOT NULL COMMENT 'UTC, posée par le code',
  updated_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (id),
  UNIQUE KEY uq_external_domains_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS connected_accounts (
  id            CHAR(36)     NOT NULL COMMENT 'GUID — la valeur du header X-Account-Id',
  user_id       CHAR(36)     NOT NULL,
  domain_id     CHAR(36)     NULL COMMENT 'NULL = serveur maison (boîte partagée locale)',
  email         VARCHAR(255) NOT NULL COMMENT 'Login IMAP/SMTP/Sieve et adresse de l''identité par défaut',
  cipher        VARBINARY(512) NOT NULL COMMENT 'nonce(12) + tag(16) + AES-256-GCM(mot de passe)',
  creation_date DATETIME     NOT NULL COMMENT 'UTC, posée par le code',
  PRIMARY KEY (id),
  UNIQUE KEY uq_connected_accounts_target (user_id, domain_id, email),
  CONSTRAINT fk_connected_accounts_user   FOREIGN KEY (user_id)   REFERENCES users(id)            ON DELETE CASCADE,
  CONSTRAINT fk_connected_accounts_domain FOREIGN KEY (domain_id) REFERENCES external_domains(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

-- '' = compte principal. Pas de FK vers connected_accounts : la valeur sentinelle l'empêche,
-- la purge est applicative (suppression d'un compte connecté = ses lignes partent avec).
ALTER TABLE folder_role_overrides
  ADD COLUMN account_id VARCHAR(36) NOT NULL DEFAULT ''
    COMMENT ''''' = compte principal, sinon GUID connected_accounts',
  DROP PRIMARY KEY,
  ADD PRIMARY KEY (user_id, account_id, role);

ALTER TABLE sending_identities
  ADD COLUMN account_id VARCHAR(36) NOT NULL DEFAULT ''
    COMMENT ''''' = compte principal, sinon GUID connected_accounts',
  DROP PRIMARY KEY,
  ADD PRIMARY KEY (user_id, account_id, address);
```

Document in « Ce qui reste à la charge de l'application » : the multi-NULL hole of `uq_connected_accounts_target` (two identical local accounts both carry `domain_id NULL`, which MariaDB's unique index does not collide — the application enforces that uniqueness), the app-level cascade above, and the `''` sentinel.

- [ ] **Step 2: Commit** — `Webmail 2d: connected accounts DDL prerequisite`.

---

### Task 2: Entities, DbContext, stores

**Files:**
- Create: `src/snoopy.microservice/Data/Preferences/ExternalDomain.cs`
- Create: `src/snoopy.microservice/Data/Preferences/ConnectedAccount.cs`
- Modify: `src/snoopy.microservice/Data/Preferences/WebmailUser.cs` (add `KdfSalt`)
- Modify: `src/snoopy.microservice/Data/Preferences/FolderRoleOverride.cs` (add `AccountId`)
- Modify: `src/snoopy.microservice/Data/Preferences/SendingIdentity.cs` (add `AccountId`)
- Modify: `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs`
- Create: `src/snoopy.microservice/Repositories/IConnectedAccountStore.cs` + `ConnectedAccountStore.cs`
- Create: `src/snoopy.microservice/Repositories/IExternalDomainStore.cs` + `ExternalDomainStore.cs`
- Modify: `src/snoopy.microservice/Repositories/FolderRoleStore.cs` + its interface (add `accountId` parameter)
- Modify: `src/snoopy.microservice/Repositories/SendingIdentityStore.cs` + `ISendingIdentityStore.cs` (add `accountId` parameter)
- Modify: `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs` (register the two stores in `AddRepositories`)
- Tests: `snoopy.microservice.Tests/Repositories/ConnectedAccountStoreTests.cs`, `ExternalDomainStoreTests.cs`; existing `FolderRoleStoreTests` / `SendingIdentityStoreTests` updated for the new parameter

**Interfaces (produced — later tasks depend on these exact shapes):**

```csharp
[Table("external_domains")]
public sealed class ExternalDomain
{
    [Column("id")] public Guid Id { get; set; }
    [Column("name")] public string Name { get; set; } = string.Empty;
    [Column("imap_host")] public string ImapHost { get; set; } = string.Empty;
    [Column("imap_port")] public int ImapPort { get; set; }
    [Column("imap_security")] public string ImapSecurity { get; set; } = "StartTls";
    [Column("smtp_host")] public string SmtpHost { get; set; } = string.Empty;
    [Column("smtp_port")] public int SmtpPort { get; set; }
    [Column("smtp_security")] public string SmtpSecurity { get; set; } = "StartTls";
    [Column("sieve_host")] public string? SieveHost { get; set; }
    [Column("sieve_port")] public int? SievePort { get; set; }
    [Column("creation_date")] public DateTime CreationDate { get; set; }
    [Column("updated_at")] public DateTime UpdatedAt { get; set; }
}

[Table("connected_accounts")]
public sealed class ConnectedAccount
{
    [Column("id")] public Guid Id { get; set; }
    [Column("user_id")] public Guid UserId { get; set; }
    [Column("domain_id")] public Guid? DomainId { get; set; }
    /// <summary>Canonical (trimmed, lower-case), like every address in this database.</summary>
    [Column("email")] public string Email { get; set; } = string.Empty;
    [Column("cipher")] public byte[] Cipher { get; set; } = [];
    [Column("creation_date")] public DateTime CreationDate { get; set; }
}

// WebmailUser gains:
[Column("kdf_salt")] public byte[]? KdfSalt { get; set; }

// FolderRoleOverride and SendingIdentity both gain (empty string = primary):
[Column("account_id")] public string AccountId { get; set; } = string.Empty;

public interface IConnectedAccountStore
{
    Task<IReadOnlyList<ConnectedAccount>> ListAsync(Guid userId, CancellationToken cancellationToken);
    Task<ConnectedAccount?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken);
    /// <summary>Also creates the default sending identity row in the same SaveChanges.</summary>
    Task<Result<ConnectedAccount>> CreateAsync(ConnectedAccount row, CancellationToken cancellationToken);
    Task UpdateCipherAsync(ConnectedAccount row, byte[] cipher, CancellationToken cancellationToken);
    /// <summary>Rewrites every cipher of the user in one SaveChanges — the ChangeSecret re-key.</summary>
    Task ReplaceCiphersAsync(Guid userId, IReadOnlyDictionary<Guid, byte[]> ciphers, CancellationToken cancellationToken);
    /// <summary>App-level cascade: removes the row plus its sending_identities and folder_role_overrides.</summary>
    Task DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken);
}

public interface IExternalDomainStore
{
    Task<IReadOnlyList<ExternalDomain>> ListAsync(CancellationToken cancellationToken);
    Task<ExternalDomain?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<ExternalDomain>> CreateAsync(ExternalDomain domain, CancellationToken cancellationToken);   // "A domain with this name already exists"
    Task<Result> UpdateAsync(ExternalDomain domain, CancellationToken cancellationToken);                    // not found / name collision
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);                                  // failure "domain_in_use" when connected accounts reference it
}
```

`ISendingIdentityStore` and `IFolderRoleStore`: every method gains a `string accountId` parameter right after `userId` (pass `""` for primary — Task 6 maps `primary` → `""` at the controller edge). Their EF queries filter on both columns.

- [ ] **Step 1: Write the failing store tests.** In `ConnectedAccountStoreTests` (use `PreferencesTestDbContext` like `ContactStoreTests`): `CreateAsync_CreatesTheRowAndItsDefaultIdentity` (identity row lands with `AccountId == row.Id.ToString()`, `Address == row.Email`, `IsDefault == true`), `CreateAsync_RefusesADuplicateTarget` (same user+domain+email → failure), `FindAsync_ScopesByUser` (foreign user → null), `DeleteAsync_PurgesIdentitiesAndOverrides`, `ReplaceCiphersAsync_RewritesEveryCipher`. In `ExternalDomainStoreTests`: create/list/update/name-collision, `DeleteAsync_RefusesWhileAccountsAreConnected` (returns failure `"domain_in_use"`).
- [ ] **Step 2: RED** — `dotnet test --filter ConnectedAccountStore` (compile failure expected).
- [ ] **Step 3: Implement.** DbContext `OnModelCreating` additions (keys must mirror the DDL):

```csharp
modelBuilder.Entity<FolderRoleOverride>().HasKey(o => new { o.UserId, o.AccountId, o.Role });   // replaces the (UserId, Role) key
modelBuilder.Entity<SendingIdentity>().HasKey(i => new { i.UserId, i.AccountId, i.Address });    // replaces the (UserId, Address) key
modelBuilder.Entity<ExternalDomain>().HasKey(d => d.Id);
modelBuilder.Entity<ExternalDomain>().HasIndex(d => d.Name).IsUnique();
modelBuilder.Entity<ConnectedAccount>().HasKey(a => a.Id);
modelBuilder.Entity<ConnectedAccount>().HasIndex(a => new { a.UserId, a.DomainId, a.Email }).IsUnique();
modelBuilder.Entity<ConnectedAccount>()
    .HasOne<WebmailUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
modelBuilder.Entity<ConnectedAccount>()
    .HasOne<ExternalDomain>().WithMany().HasForeignKey(a => a.DomainId).OnDelete(DeleteBehavior.Restrict);
```

plus `DbSet<ExternalDomain> ExternalDomains` and `DbSet<ConnectedAccount> ConnectedAccounts`. Store implementations follow `ContactStore`'s style (constructor-injected `PreferencesDbContext`, canonicalise emails with `Trim().ToLowerInvariant()`, `DateTime.UtcNow` set by code). `CreateAsync` pre-checks the duplicate with a query (InMemory raises no unique-violation), then adds the account **and** its default `SendingIdentity { UserId, AccountId = row.Id.ToString(), Address = row.Email, DisplayName = "", IsDefault = true, UpdatedAt = now }` before one `SaveChangesAsync`. `DeleteAsync` removes the account plus `SendingIdentities`/`FolderRoleOverrides` rows where `AccountId == id.ToString()`, one `SaveChangesAsync`. `ExternalDomainStore.DeleteAsync` refuses with `"domain_in_use"` when `ConnectedAccounts.AnyAsync(a => a.DomainId == id)`.
- [ ] **Step 4: GREEN** — new tests pass, then full `dotnet test` (the updated FolderRoleStore/SendingIdentityStore signatures break their callers — fix call sites `MailController`, `IdentitiesController`, `MailSender`, `FolderRoleResolver` tests by passing `""` for now; Task 6 replaces the literal with the resolved account).
- [ ] **Step 5: Commit** — `Webmail 2d: connected-accounts schema and stores`.

---

### Task 3: `ConnectedAccountCipher` (KDF + AES-GCM)

**Files:**
- Create: `src/snoopy.microservice/Services/ConnectedAccountCipher.cs`
- Create: `src/snoopy.microservice/Services/ConnectedAccountErrors.cs`
- Test: `snoopy.microservice.Tests/Services/ConnectedAccountCipherTests.cs`

**Interfaces:**

```csharp
namespace weesky.Snoopy.Microservice.Services;

/// <summary>Stable error codes for the connected-accounts feature.</summary>
public static class ConnectedAccountErrors
{
    /// <summary>The stored cipher no longer decrypts under the current KEK — the main password
    /// changed outside the app. Mapped to 409 so the 401 handler never signs the user out over it.</summary>
    public const string CredentialsInvalid = "connected_credentials_invalid";
}

/// <summary>
/// Encrypts connected-account passwords with a key derived from the user's main password —
/// the server alone can never decrypt what it stores. Pure and static: no state, no DI.
/// </summary>
internal static class ConnectedAccountCipher
{
    public const int SaltLength = 16;
    public const int KekIterations = 600_000;

    public static byte[] NewSalt();                                    // RandomNumberGenerator.GetBytes(SaltLength)
    public static byte[] DeriveKek(string password, byte[] salt);      // Rfc2898DeriveBytes.Pbkdf2(..., SHA256, 32)
    public static byte[] Encrypt(byte[] kek, string secret);           // nonce(12) ‖ tag(16) ‖ ciphertext
    public static Result<string> Decrypt(byte[] kek, byte[] cipher);   // Failure(ConnectedAccountErrors.CredentialsInvalid) on any tampering/wrong key/short buffer
}
```

- [ ] **Step 1: Write the failing tests** — `EncryptThenDecrypt_RoundTrips` (unicode password), `Encrypt_ProducesADifferentCipherEachTime` (random nonce), `Decrypt_FailsUnderAnotherKek`, `Decrypt_FailsOnATamperedByte` (flip one ciphertext byte), `Decrypt_FailsOnATruncatedBuffer` (shorter than 28 bytes — must not throw), `DeriveKek_IsDeterministicPerSalt` (same in/out twice; different salt → different KEK). Use a reduced-iteration path? **No** — 600k runs in ~100 ms, fine for a handful of tests; derive one KEK in the fixture and share it.
- [ ] **Step 2: RED**, **Step 3: Implement** with `System.Security.Cryptography.AesGcm` (`new AesGcm(kek, 16)`), catching `AuthenticationTagMismatchException`/`CryptographicException` and length errors into the typed failure. **Step 4: GREEN** (full suite). **Step 5: Commit** — `Webmail 2d: KDF and AES-GCM cipher for connected accounts`.

---

### Task 4: Credentials cookie v2 (password + KEK), login and ChangeSecret

**Files:**
- Modify: `src/snoopy.microservice/Services/IMailCredentialStore.cs` + `MailCredentialStore.cs`
- Create: `src/snoopy.microservice/Models/MailCredentialPayload.cs`
- Modify: `src/snoopy.microservice/Controllers/LoginController.cs:69-72` (store payload v2)
- Modify: `src/snoopy.microservice/Authentication/Middleware/SlidingSessionMiddleware.cs:59-75` (re-store the payload as read)
- Modify: `src/snoopy.microservice/Repositories/WebmailUserStore.cs` + `IWebmailUserStore` (KDF salt)
- Modify: `src/snoopy.microservice/Controllers/AccountController.cs:93-120` (`ChangeSecret` re-keys the ciphers)
- Modify: `src/snoopy.microservice/Controllers/MailController.cs:48-58` (`TryMailPassword` reads `.Value.Password` — temporary until Task 6)
- Tests: `MailCredentialStoreTests.cs`, `LoginControllerTests.cs`, `SlidingSessionMiddlewareTests.cs`, `AccountControllerTests.cs` updated; new cases below

**Interfaces:**

```csharp
/// <summary>What the credentials cookie carries. Kek is null for a v1 cookie still in
/// circulation — derived on demand and re-issued as v2 by the resolver (Task 5).</summary>
public sealed record MailCredentialPayload(string Password, byte[]? Kek);

public interface IMailCredentialStore
{
    void Store(HttpResponse response, MailCredentialPayload payload, TimeSpan lifetime);
    Result<MailCredentialPayload> Retrieve(HttpRequest request);
    void Clear(HttpResponse response);
}

// IWebmailUserStore gains:
/// <summary>The user's KDF salt, generated and persisted on first need.</summary>
Task<byte[]> GetOrCreateKdfSaltAsync(string email, CancellationToken cancellationToken);
```

Serialisation inside `MailCredentialStore` (before Data Protection, which is unchanged): v2 = `"wm2|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(password)) + "|" + Convert.ToBase64String(kek)`; when `Kek == null`, store the bare password (v1 shape) so a downgrade is representable. `Retrieve` parses: starts with `"wm2|"` and splits into exactly 3 parts with valid base64 → v2; anything else → v1 (`new MailCredentialPayload(raw, null)`).

- [ ] **Step 1: Failing tests.** `MailCredentialStoreTests`: `Retrieve_ReadsBackTheV2Payload` (password + 32-byte kek round-trip), `Retrieve_TreatsALegacyValueAsV1` (protect a bare password with the same protector purpose `"weesky.imap.credentials"` and read `Kek == null`), `Retrieve_TreatsAPasswordStartingLikeTheMarkerAsV1` (password literally `"wm2|not|base64!"` — invalid base64 falls back to v1). `WebmailUserStoreTests`: `GetOrCreateKdfSaltAsync_GeneratesOnceAndReturnsTheSameSaltAfter`. `LoginControllerTests`: `Login_StoresTheKekAlongsideThePassword` (mock `IMailCredentialStore`, capture the payload, assert `Kek` is the PBKDF2 of the password under the stored salt). `AccountControllerTests`: `ChangePassword_ReEncryptsEveryConnectedAccountCipher` (seed two connected accounts encrypted under the old KEK; after the call both decrypt under the new one) and `ChangePassword_StoresTheNewPayload`.
- [ ] **Step 2: RED.**
- [ ] **Step 3: Implement.**
  - `LoginController.Login` (after the auth success guard): `var salt = await _webmailUsers.GetOrCreateKdfSaltAsync(credentials.Email, cancellationToken); var kek = ConnectedAccountCipher.DeriveKek(credentials.Password, salt); _credentialStore.Store(HttpContext.Response, new MailCredentialPayload(credentials.Password, kek), TimeSpan.FromMinutes(...));` (inject `CancellationToken` into the action).
  - `SlidingSessionMiddleware`: `credentials.Retrieve` now yields the payload; `credentials.Store(context.Response, password.Value, lifetime)` becomes `credentials.Store(context.Response, payload.Value, lifetime)` — the payload is re-issued untouched, v1 stays v1 here (the resolver upgrades it).
  - `AccountController.ChangePassword`, inside the success branch and **before** the cookie writes: retrieve the old payload (`credentials.Retrieve(Request)`); old KEK = `payload.Kek ?? DeriveKek(secretChange.OldPassword, salt)`; new KEK = `DeriveKek(secretChange.NewPassword, salt)` with `salt = await webmailUsers.GetOrCreateKdfSaltAsync(...)`; load `connectedAccounts.ListAsync(AuthenticatedUser.WebmailUid)`, decrypt each cipher under the old KEK — **skip (leave untouched) any row that fails to decrypt**, it was already orphaned and stays re-enterable — re-encrypt under the new KEK, `ReplaceCiphersAsync` in one SaveChanges; finally `credentials.Store(Response, new MailCredentialPayload(secretChange.NewPassword, newKek), ...)`. Inject `IConnectedAccountStore` into the controller.
  - `MailController.TryMailPassword`: `password = retrieved.IsSuccess ? retrieved.Value.Password : null;` — nothing else yet.
- [ ] **Step 4: GREEN** — full `dotnet test`.
- [ ] **Step 5: Commit** — `Webmail 2d: credentials cookie carries the KEK, ChangeSecret re-keys`.

---

## Phase 2 — Resolver and account-aware backend

### Task 5: `MailAccountConnection`, factories refactor, `AccountConnectionResolver`

The structural task: one record describes every connection, the factories stop reading options, and the resolver becomes the single place an account id turns into hosts + credentials.

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/MailAccountConnection.cs`
- Create: `src/snoopy.microservice/Services/IAccountConnectionResolver.cs` + `AccountConnectionResolver.cs`
- Modify: `src/snoopy.microservice/Services/MailConnectionFactory.cs`, `ImapConnectionFactory.cs`, `SmtpConnectionFactory.cs`, their interfaces
- Modify: `src/snoopy.microservice/Services/ScopedImapSessionProvider.cs`, `IImapSessionProvider.cs`
- Modify: every repository/service opening IMAP/SMTP: `MailFolderRepository`, `MailMessageRepository`, `MailSender`, `DraftSaver`, `QuotePreparer` — mechanical `(User user, string password)` → `(User user, MailAccountConnection connection)` threading (they keep `user` where they read `WebmailUid`)
- Modify: `Configuration/ApplicationServicesConfiguration.cs` (register `AddScoped<IAccountConnectionResolver, AccountConnectionResolver>()`)
- Tests: new `AccountConnectionResolverTests.cs`; existing factory/provider/repository tests re-wired

**Interfaces:**

```csharp
/// <summary>Everything needed to open the active account's connections. One structure, one
/// code path: the home server comes from appsettings, an external domain from the database.</summary>
public sealed record MailAccountConnection(
    string AccountId,               // "primary" or the connected-account GUID string
    bool IsHomeServer,              // primary and local shared mailboxes
    string ImapHost, int ImapPort, SecureSocketOptions ImapSecurity,
    string SmtpHost, int SmtpPort, SecureSocketOptions SmtpSecurity,
    string? SieveHost, int? SievePort,
    string Username, string Password)
{
    public const string Primary = "primary";
    /// <summary>The database sentinel for this account ("" for primary, the GUID otherwise).</summary>
    public string StorageAccountId => AccountId == Primary ? string.Empty : AccountId;
}

public interface IAccountConnectionResolver
{
    public const string HeaderName = "X-Account-Id";
    public const string QueryName = "account";
    /// <summary>Failure codes: "credentials_unavailable" (401), "account_not_found" (404),
    /// ConnectedAccountErrors.CredentialsInvalid (409).</summary>
    Task<Result<MailAccountConnection>> ResolveAsync(User user, HttpRequest request, CancellationToken cancellationToken);
}
```

Factory surface after the refactor — the base keeps `IOptionsMonitor<MailOptions>` **only** for `TimeoutSeconds` and `AllowInvalidCertificate`; endpoints come from the connection:

```csharp
// MailConnectionFactory<TClient, TSession>
public async Task<Result<TSession>> OpenAsync(MailAccountConnection connection, CancellationToken cancellationToken);
protected abstract MailEndpoint Endpoint(MailAccountConnection connection);   // Imap* or Smtp* triplet
// MailEndpoint keeps its shape; IsConfigured = !string.IsNullOrWhiteSpace(Host).
// IImapConnectionFactory / ISmtpConnectionFactory: Task<Result<...>> OpenAsync(MailAccountConnection, CancellationToken)

// ScopedImapSessionProvider: keyed by (connection.ImapHost, connection.ImapPort, connection.Username, connection.Password)
Task<Result<IImapSession>> GetAsync(MailAccountConnection connection, CancellationToken cancellationToken);
// WithSessionAsync extensions: (this IImapSessionProvider provider, MailAccountConnection connection, Func<...> operation, CancellationToken ct)
```

Resolver behaviour (each line is a test):
1. `credentials.Retrieve(request)` failure → failure `credentials_unavailable`.
2. Account id = header `X-Account-Id`, else query `account`, else `primary`. Value `primary`/empty → connection from `MailOptions` (`options.CurrentValue`), `Username = user.Email`, `Password = payload.Password`, `AccountId = "primary"`, `IsHomeServer = true`, Sieve fields null.
3. Not a parseable `Guid` → failure `account_not_found`. `IConnectedAccountStore.FindAsync(user.WebmailUid, id)` null → `account_not_found` (a foreign account resolves to null by scoping — indistinguishable from unknown, by design).
4. KEK = `payload.Kek`; when null (v1 cookie): `salt = await users.GetOrCreateKdfSaltAsync(user.Email, ct)`, derive, and **re-issue the cookie as v2** via `request.HttpContext.Response` with lifetime `TimeSpan.FromMinutes(tokenConstants.Value.ExpiryInMinutes)`.
5. `ConnectedAccountCipher.Decrypt(kek, row.Cipher)` failure → failure `ConnectedAccountErrors.CredentialsInvalid`.
6. `row.DomainId == null` → home `MailOptions` endpoints, `IsHomeServer = true`, Sieve null. Else `IExternalDomainStore.FindAsync` (null → `account_not_found`), parse the three securities with `Enum.TryParse<SecureSocketOptions>(value, out ...)` restricted to `None|StartTls|SslOnConnect` (anything else → log error + `account_not_found`), `IsHomeServer = false`, Sieve fields copied.
7. `Username = row.Email`, `Password =` the decrypted secret.

Resolver DI: `IMailCredentialStore`, `IConnectedAccountStore`, `IExternalDomainStore`, `IWebmailUserStore`, `IOptionsMonitor<MailOptions>`, `IOptions<TokenConstants>`, `ILogger<AccountConnectionResolver>`.

- [ ] **Step 1: Failing resolver tests** (`AccountConnectionResolverTests`, InMemory stores + `EphemeralDataProtectionProvider`-backed real `MailCredentialStore` + `DefaultHttpContext`): primary-by-default, primary-by-header, header beats query / query works alone, unknown guid → `account_not_found`, foreign account → `account_not_found`, local connected account (home hosts, connected email as username), external account (domain hosts + securities), invalid stored security → `account_not_found`, wrong-KEK cipher → `connected_credentials_invalid`, v1 cookie → resolves **and** the response now carries a v2 cookie (retrieve it back, `Kek != null`).
- [ ] **Step 2: RED.**
- [ ] **Step 3: Implement** resolver + factory/provider refactor. In the factories, `ImapConnectionFactory.Endpoint(connection)` returns `new("IMAP", "Mail:ImapHost", connection.ImapHost, connection.ImapPort, connection.ImapSecurity, !string.IsNullOrWhiteSpace(connection.ImapHost))`; SMTP mirrors it. `OpenAsync` body (`MailConnectionFactory.cs:44-96`) changes only its first lines: the email guard becomes a guard on `connection.Username`, `ConnectAsync`/`AuthenticateAsync` read the endpoint + `connection.Username/Password`. Thread `MailAccountConnection` through the repositories mechanically — replace each `(User user, string password)` pair by `(User user, MailAccountConnection connection)` and each `provider.WithSessionAsync(user, password, ...)` by `provider.WithSessionAsync(connection, ...)`; where `StagedAttachmentStore` receives `user.WebmailUid.ToString()` as accountId (`MailSender.cs`, `QuotePreparer`, `MailController` staged endpoints), it now receives `connection.AccountId` (keep the primary spelled `"primary"` consistently — it changes the staging directory hash once, acceptable: staged uploads are 12-hour transients).
- [ ] **Step 4: GREEN** — full `dotnet test` (this task touches many fixtures: build a shared `TestConnections.Primary(email, password)` helper in `Tests/Infrastructure` producing a home-server `MailAccountConnection`, and use it everywhere a `(user, password)` pair used to travel).
- [ ] **Step 5: Commit** — `Webmail 2d: account connection resolver, factories take a connection`.

---

### Task 6: `MailController` (and identities/folder-roles scoping) on the resolver

**Files:**
- Modify: `src/snoopy.microservice/Controllers/MailController.cs` (replace `TryMailPassword` with a resolver guard; every action)
- Modify: `src/snoopy.microservice/Controllers/ApiBaseController.cs` (add `ConflictEnveloppe`)
- Modify: `src/snoopy.microservice/Services/MailSender.cs` / `OutgoingMessageFactory` (From validation source per account — see below)
- Tests: `MailControllerTests.cs` re-wired; new cases

**Interfaces:**

```csharp
// MailController — replaces TryMailPassword; every action starts with:
private async Task<(MailAccountConnection? Connection, ActionResult? Error)> TryResolveAsync(CancellationToken ct)
// maps: credentials_unavailable → UnauthorizedEnveloppe, account_not_found → NotFoundEnveloppe,
//       ConnectedAccountErrors.CredentialsInvalid → ConflictEnveloppe (409, same envelope shape)
```

The role-store scope becomes `(AuthenticatedUser.WebmailUid, connection.StorageAccountId)` wherever `roleStore.GetAsync(AuthenticatedUser.WebmailUid, ...)` was called with the `""` placeholder from Task 2 — `GetFolders` (line 123), `RefuseIfSystemFolderAsync` (line 80), the `FolderRoles` endpoints. `OutgoingMessageFactory.CreateAsync` gains the connection: for `IsHomeServer && AccountId == "primary"` the existing alias/identity validation is untouched; for a connected account the allowed From set = the stored identities of `(userId, connection.StorageAccountId)` **plus** `connection.Username`, display label resolved from the stored row (empty label → address only). Send keeps SMTP auth on `connection.Username/Password` throughout.

- [ ] **Step 1: Failing tests** — re-point `MailControllerTests` at a mocked `IAccountConnectionResolver`; new cases: `GetFolders_AnswersConflictWhenTheConnectedCredentialsAreInvalid` (`Assert.IsType<ConflictObjectResult>` — mind the exact-type rule), `GetFolders_AnswersNotFoundForAForeignAccount`, `GetFolders_ScopesTheRoleOverridesToTheAccount` (role store mock verified with the GUID storage id), `Send_RefusesAFromForeignToTheConnectedAccount` (400 `from_not_owned`), `Send_AcceptsAStoredIdentityOfTheConnectedAccount`.
- [ ] **Step 2: RED. Step 3: Implement. Step 4: GREEN (full suite). Step 5: Commit** — `Webmail 2d: mail endpoints resolve the active account`.

---

### Task 7: `ConnectedAccountsController`

**Files:**
- Create: `src/snoopy.microservice/Controllers/ConnectedAccountsController.cs`
- Create: `src/snoopy.microservice/Models/ConnectedAccountModels.cs` (the three DTO records below)
- Test: `snoopy.microservice.Tests/Controllers/ConnectedAccountsControllerTests.cs`

**Interfaces (the wire contract the frontend consumes):**

```csharp
public sealed record ConnectedAccountResponse(
    Guid Id, string Email, string DisplayName, Guid? DomainId, string? DomainName,
    bool SieveSupported, bool CredentialsValid, DateTime CreationDate);
public sealed record ConnectAccountRequest(Guid? DomainId, string Email, string Password);
public sealed record ConnectedAccountPasswordRequest(string Password);
```

Routes (`[Route("api/[controller]")] [ApiController] [Authorize]`, primary-constructor DI: `IConnectedAccountStore`, `IExternalDomainStore`, `ISendingIdentityStore`, `IMailCredentialStore`, `IWebmailUserStore`, `IImapConnectionFactory`, `IOptionsMonitor<MailOptions>`, `ILogger<>`):

| Route | Behaviour |
|---|---|
| `GET /api/ConnectedAccounts` | 200 list. `DisplayName` = the default identity row's label (`ISendingIdentityStore.GetAsync(uid, id.ToString())`, row whose `Address == account.Email`; empty when absent). `DomainName` null for local. `SieveSupported` = local `|| domain.SieveHost != null`. `CredentialsValid` = `Decrypt(kek, cipher).IsSuccess` — **no network call**. Needs the cookie: retrieval failure → 401 `credentials_unavailable`. |
| `POST /api/ConnectedAccounts` | Validate: parseable address (`MailboxAddress.TryParse`), non-empty password, canonical email ≠ `AuthenticatedUser.Email` when `DomainId == null` ("You are already signed in to this mailbox"), unknown `DomainId` → 400. **Verify by really opening IMAP** with a probe `MailAccountConnection` (home options or the domain's IMAP triplet, `IsHomeServer` accordingly, `AccountId = "probe"`) and dispose the session at once; failure → 502 (generic message, unchanged rule). Encrypt with the KEK, `CreateAsync` (duplicate → 400), 200 with the response record. `[EnableRateLimiting("login")]` — each attempt is an outbound IMAP authentication. |
| `PUT /api/ConnectedAccounts/{id}/Password` | Ownership via `FindAsync` (404), same IMAP probe with the **new** password (502 on refusal), re-encrypt, `UpdateCipherAsync`, 204. Rate-limited like POST. |
| `DELETE /api/ConnectedAccounts/{id}` | 404 unknown/foreign, else `DeleteAsync`, 204. |
| `GET /api/ConnectedAccounts/Domains` | 200 `[{ id, name }]` from `IExternalDomainStore.ListAsync` — the choice list for the connect form. **Never the hosts/ports**: users see names, only admins see configuration. No cookie/crypto involved. |

- [ ] **Step 1: Failing tests** (mock the factory; InMemory stores; real `MailCredentialStore` over `EphemeralDataProtectionProvider` to plant a v2 cookie): domains choice list carries names only, list happy path + `List_ReportsInvalidCredentialsWithoutOpeningAnyConnection` (factory mock verified never called; row encrypted under another KEK → `CredentialsValid == false`), `Connect_VerifiesImapBeforeStoring` (factory failure → 502 **and** store empty), `Connect_RefusesTheCallersOwnMailbox`, `Connect_RefusesADuplicate`, `Connect_CreatesTheDefaultIdentity`, `UpdatePassword_ReEncrypts`, `Delete_AnswersNotFoundForAForeignAccount`, and `Responses_CarryNoSecret` (serialise the 200 payload, assert no `cipher`/`password` member).
- [ ] **Step 2: RED. Step 3: Implement. Step 4: GREEN. Step 5: Commit** — `Webmail 2d: connected accounts endpoints`.

---

### Task 8: Admin — external domains CRUD

**Files:**
- Modify: `src/snoopy.microservice/Controllers/AdminController.cs` (four actions after the virtual-domains block, same `[Authorize(Policy = AdminRequirement.PolicyName)]` class scope)
- Create: `src/snoopy.microservice/Models/ExternalDomainModels.cs`
- Test: `AdminControllerTests.cs` (new region)

**Interfaces:**

```csharp
public sealed record ExternalDomainResponse(
    Guid Id, string Name, string ImapHost, int ImapPort, string ImapSecurity,
    string SmtpHost, int SmtpPort, string SmtpSecurity, string? SieveHost, int? SievePort);
public sealed record ExternalDomainRequest(
    string Name, string ImapHost, int ImapPort, string ImapSecurity,
    string SmtpHost, int SmtpPort, string SmtpSecurity, string? SieveHost, int? SievePort);
```

Routes: `GET /api/Admin/domains/external` (200 list), `POST` (200 created), `PUT /{id}` (204/404), `DELETE /{id}` (204; `domain_in_use` → 400 `"2 accounts are connected to this domain"` style message built from a count — fetch the count in the store failure path or a dedicated `CountConnectedAsync`; a simple fixed message `"Accounts are still connected to this domain"` is acceptable). Validation in one private static `Validate(ExternalDomainRequest)` returning `Result`: name 1–100; hosts non-empty ≤255 and `Uri.CheckHostName(...) != UriHostNameType.Unknown`; ports 1–65535; securities in `{None, StartTls, SslOnConnect}`; Sieve host/port **both present or both absent**; sieve port 1–65535 when present.

- [ ] **Step 1: Failing tests** — CRUD happy paths, each validation refusal (name, host, port, security, half-a-sieve), delete-in-use → `Assert.IsType<BadRequestObjectResult>`, non-admin covered by the existing class-level policy tests.
- [ ] **Step 2: RED. Step 3: Implement. Step 4: GREEN. Step 5: Commit** — `Webmail 2d: external domains admin CRUD`.

---

### Task 9: Identities per account

**Files:**
- Modify: `src/snoopy.microservice/Controllers/IdentitiesController.cs`
- Modify: `src/snoopy.microservice/Services/IdentityResolver.cs` (two new static methods)
- Tests: `IdentitiesControllerTests.cs`, `IdentityResolverTests.cs`

**Interfaces:** the wire shape (`IdentityListResponse`, rows with `address/displayName/isDefault/isPrimary/stale/labelIsCustom`) is **unchanged** — the frontend keeps one reader. New statics:

```csharp
// IdentityResolver
/// <summary>Connected-account list: the account address first (isPrimary, isDefault, label from
/// its stored row), then the extra rows sorted by label; stale is always false here.</summary>
public static IReadOnlyList<ResolvedIdentity> ResolveConnected(
    IReadOnlyList<SendingIdentity> stored, string accountEmail);
/// <summary>Connected-account save: parseable addresses, no duplicates, must contain the account
/// address; isDefault is forced onto the account-address row whatever the request said.</summary>
public static Result<IReadOnlyList<SendingIdentity>> ValidateConnected(
    IReadOnlyList<IdentityInput> requested, string accountEmail);
```

Controller: read the account id (header `X-Account-Id` directly — **no credentials cookie here**, these are database verbs); `primary`/absent → existing paths untouched. A GUID: ownership check via `IConnectedAccountStore.FindAsync(AuthenticatedUser.WebmailUid, id)` (404 when null — inject the store), then GET = `ResolveConnected(store.GetAsync(uid, id.ToString()), account.Email)`, PUT = `ValidateConnected` then `store.ReplaceAsync(uid, id.ToString(), rows)`.

- [ ] **Step 1: Failing tests** — resolver: connected list ordering, label fallback (empty label → address as label, `labelIsCustom false`), validate refusals (missing account address, duplicate, unparsable) and the forced default. Controller: GET/PUT scoped to the GUID storage id (store mock verified), 404 foreign id, primary path untouched (existing tests stay green unmodified).
- [ ] **Step 2: RED. Step 3: Implement. Step 4: GREEN. Step 5: Commit** — `Webmail 2d: identities scoped by connected account`.

---

### Task 10: Sieve rules per account

**Files:**
- Modify: `src/snoopy.microservice/Services/IManageSieveClient.cs` + `ManageSieveClient.cs`
- Create: `src/snoopy.microservice/Models/SieveConnection.cs`
- Modify: `src/snoopy.microservice/Repositories/ISieveRepository.cs` + `SieveRepository.cs` (methods take a `SieveConnection` instead of building one from `User`)
- Modify: `src/snoopy.microservice/Controllers/RulesController.cs`
- Tests: `ManageSieveClientTests.cs`, `SieveRepositoryTests.cs`, `RulesControllerTests.cs`

**Interfaces:**

```csharp
/// <summary>One ManageSieve target. SASL PLAIN sends authzid \0 authcid \0 password: the master
/// path impersonates (authzid = mailbox, authcid = master), the own-credentials path does not
/// (authzid empty, authcid = the account itself). Built by RulesController, nowhere else.</summary>
public sealed record SieveConnection(
    string Host, int Port, string AuthorizationIdentity, string AuthenticationIdentity, string Password);

// IManageSieveClient
Task<Result<IManageSieveSession>> OpenSessionAsync(SieveConnection connection, CancellationToken cancellationToken);
// The legacy OpenSessionAsync(string targetUser, ...) remains as a thin builder over SieveOptions:
// new SieveConnection(_options.Host, _options.Port, targetUser, _options.MasterUser, _options.MasterPassword)
```

`RulesController` composes: inject `IAccountConnectionResolver` and `IOptions<SieveOptions>`; every action resolves first (same error mapping as Task 6 — 401/404/409). `connection.IsHomeServer` → master path `new SieveConnection(sieve.Host, sieve.Port, connection.Username, sieve.MasterUser, sieve.MasterPassword)`; external with `SieveHost != null` → `new SieveConnection(connection.SieveHost, connection.SievePort!.Value, "", connection.Username, connection.Password)`; external without → `NotFoundEnveloppe("sieve_unsupported")`. `ISieveRepository` methods swap their `User` parameter for the `SieveConnection` (they used it only to reach ManageSieve; the provider-detection logic is untouched).

- [ ] **Step 1: Failing tests** — client: the SASL PLAIN bytes for both shapes (the existing test fixture already fakes the server; assert `\0authcid\0` with empty authzid on the own-credentials shape), config-missing guard only applies to the legacy builder. Controller: primary → master path (options verified), local connected → master path with the connected email as authzid, external+sieve → own-credentials with the domain host, external without sieve → 404, invalid-credentials account → 409.
- [ ] **Step 2: RED. Step 3: Implement. Step 4: GREEN (full suite). Step 5: Commit** — `Webmail 2d: sieve rules follow the active account`.

---

## Phase 3 — Frontend foundations

### Task 11: API client — account transport + new endpoints

**Files:**
- Modify: `src/frontend/src/api.js`
- Test: `src/frontend/src/api.test.js`

**Interfaces (produced):**

- `request(method, path, body, options)` and `requestBlob(path, options)` accept `options.accountId`; when set and `!== 'primary'` they add `'X-Account-Id': options.accountId`. `uploadAttachment` (XHR) mirrors it via `xhr.setRequestHeader`. `mailAttachmentUrl(...)` and the staged-content URL builder append `&account=<id>` under the same condition.
- Every `api.*` method under `/api/Mail/*`, plus `getIdentities`/`replaceIdentities` and the five rules methods, accepts a **trailing `options` object** (those that already take `{ signal }` just gain the field) and forwards it.
- New methods:

```js
getConnectedAccounts: () => request('GET', '/api/ConnectedAccounts'),
connectAccount: (domainId, email, password) =>
  request('POST', '/api/ConnectedAccounts', { domainId, email, password }),
updateConnectedAccountPassword: (id, password) =>
  request('PUT', `/api/ConnectedAccounts/${id}/Password`, { password }),
deleteConnectedAccount: (id) => request('DELETE', `/api/ConnectedAccounts/${id}`),
getConnectableDomains: () => request('GET', '/api/ConnectedAccounts/Domains'),
adminGetExternalDomains: () => request('GET', '/api/Admin/domains/external'),
adminCreateExternalDomain: (domain) => request('POST', '/api/Admin/domains/external', domain),
adminUpdateExternalDomain: (id, domain) => request('PUT', `/api/Admin/domains/external/${id}`, domain),
adminDeleteExternalDomain: (id) => request('DELETE', `/api/Admin/domains/external/${id}`),
```

- [ ] **Step 1: Failing tests** — header emitted for a GUID accountId, absent for `'primary'` and for unset; the four connected-account methods hit their routes; `mailAttachmentUrl` carries `account=` only for a non-primary id.
- [ ] **Step 2: RED. Step 3: Implement. Step 4: GREEN (`npm test`). Step 5: Commit** — `Webmail 2d: API client carries the active account`.

---

### Task 12: `AuthContext` — accounts list, switch, persistence

**Files:**
- Modify: `src/frontend/src/contexts/AuthContext.tsx`
- Modify: `src/frontend/src/modules/mail/notify/channels.ts` (claim per account)
- Modify: `src/frontend/src/modules/mail/notify/useMailNotifications.ts` (pass the account id)
- Tests: `AuthContext.test.tsx`, `channels`-related notify tests; update the suites that mock `useAuth` with `activeAccount: { id: 'primary' }` (`MailLayout.test.tsx`, `MessageReader.test.tsx`, `useSetFlags.test.tsx`, `useMarkSeenOnOpen.test.tsx`, `useInlineImages.test.tsx`, `SystemFoldersModal.test.tsx`, `FoldersPage.test.tsx`, `AliasesPage.test.jsx` — the mock object gains nothing mandatory, but any suite asserting the *shape* must keep compiling)

**Interfaces (produced — every later frontend task reads these):**

```ts
export interface ActiveAccount {
  id: string                      // 'primary' or the connected-account GUID
  email: string
  displayName: string             // connected: the default identity's label, '' falls back to email at render
  isPrimary: boolean
  domainName: string | null       // null for primary and local shared mailboxes
  credentialsValid: boolean       // false → "Password needed", not switchable
  sieveSupported: boolean         // primary: always true
}
// AuthContextValue gains:
switchAccount: (id: string) => void
```

Behaviour:
- The provider runs `useQuery({ queryKey: ['connectedAccounts'], queryFn: () => api.getConnectedAccounts(), enabled: isLoggedIn, staleTime: 60_000 })`. `accounts` = `[primaryAccount, ...rows.map(mapRow)]` where `primaryAccount` extends today's derivation with `{ isPrimary: true, domainName: null, credentialsValid: true, sieveSupported: true }` and `mapRow` maps the `ConnectedAccountResponse` fields (`displayName: row.displayName || row.email`).
- `activeAccountId` state seeded from `localStorage.getItem('mail.activeAccount') ?? 'primary'`. `activeAccount` = `accounts.find(a => a.id === activeAccountId) ?? primaryAccount`. An effect resets the state to `'primary'` (and clears the storage key) when the accounts list **has loaded** and the stored id is absent from it — never while it is still loading, or a reload would flash the primary.
- `switchAccount(id)`: no-op on the current id or an unknown/`credentialsValid === false` target; else `queryClient.removeQueries({ queryKey: ['mail', previousId] })`, set state, `localStorage.setItem('mail.activeAccount', id)`.
- The session-end effect (the existing `wasLoggedIn` block) additionally does `localStorage.removeItem('mail.activeAccount')` and resets the state to `'primary'`.
- `channels.ts`: `CLAIM_KEY` becomes `const claimKey = (accountId: string) => \`mail.lastNotifiedUidNext.${accountId}\``; `claimNotification(accountId, uidValidity, uidNext)` and `forgetNotificationClaim()` now iterates `localStorage` keys removing every one starting with `'mail.lastNotifiedUidNext'` (covers the legacy unscoped key too). `useMailNotifications` passes `useAccountId()` through.

- [ ] **Step 1: Failing tests** — accounts list merges primary + fetched rows; switch persists and re-reads on mount; unknown persisted id falls back after load; switch removes the old account's `['mail', id]` queries (spy on `removeQueries`); switch refuses an invalid-credentials target; sign-out clears the persisted id; claim keys are per-account and `forgetNotificationClaim` sweeps the prefix.
- [ ] **Step 2: RED. Step 3: Implement. Step 4: GREEN (`npm test`, `npm run typecheck`). Step 5: Commit** — `Webmail 2d: AuthContext switches accounts`.

---

### Task 13: `IdentityMenu` — interactive account list, both mounts

**Files:**
- Modify: `src/frontend/src/layouts/IdentityMenu.tsx`
- Modify: `src/frontend/src/modules/settings/SettingsLayout.tsx` (mount the menu at the foot of the nav)
- Modify: `src/frontend/src/styles/shell.css` (`.identity-account` becomes a button skin; a `.identity-badge` warn chip; the settings-nav footer container)
- Test: `src/frontend/src/layouts/IdentityMenu.test.tsx`

Behaviour (mockups validated 2026-07-29):
- The band shows the **active** account: initials from its `displayName || email`, first line `displayName || email`, second line `email` for the primary, `` `${email} · ${domainName ?? 'Weesky'}` `` for a connected one. Pill carries `is-connected` class when `!isPrimary` (accent background in CSS).
- Menu rows become `<button type="button" role="menuitem">`: active row keeps `is-active` + a ✓; a `credentialsValid === false` row is not a switch — it shows a `Password needed` chip and navigates to `/settings/accounts`; any other row calls `switchAccount(acc.id)` then closes.
- Below the rows: separator, `Connected accounts…` (`navigate('/settings/accounts')`), separator, Sign out (unchanged).
- `SettingsLayout` renders `<div className="settings-nav-foot"><IdentityMenu /></div>` after the NavLinks (flex column, `margin-top: auto`).

- [ ] **Step 1: Failing tests** — clicking a row switches (mock `useAuth`, assert `switchAccount` called and menu closed); the invalid row navigates instead of switching; the band reflects a connected active account; "Connected accounts…" navigates; Sign out behaviour preserved (the old assertions move, never shrink — "no test lost without a replacement").
- [ ] **Step 2: RED. Step 3: Implement (CSS included). Step 4: GREEN. Step 5: Commit** — `Webmail 2d: identity menu switches accounts`.

---

### Task 14: Mail module switch mechanics

**Files:**
- Modify: `src/frontend/src/modules/mail/MailLayout.tsx`
- Modify: `src/frontend/src/modules/mail/queries.ts` (thread `accountId` into every `api.*` call option)
- Modify: `src/frontend/src/modules/contacts/queries.ts` (no header — contacts stay user-level; just confirm no change needed beyond the key, which already carries the id)
- Test: `MailLayout.test.tsx`, `queries.test.tsx` additions

Behaviour:
- Every `queryFn`/`mutationFn` in `queries.ts` passes `{ accountId }` (already in scope from `useAccountId()`) to its `api.*` call — mutations capture it at render, which is the correct in-flight semantics; add one test pinning it (`useSetFlags` fires with the account id it was rendered under even after a switch).
- `MailLayout`: an effect on `accountId` change (not on mount — track a ref) navigates to `/mail` with `replace: true`, dropping `folder`/`uid` search params; the inbox-resolution logic already picks INBOX from there.
- **409 surface:** when the folders query error is an `ApiError` with `status === 409 && code === 'connected_credentials_invalid'`, `MailLayout` renders a full-pane state in place of the three columns: title `Password needed`, body `Your main password changed, so this account's password must be entered again.`, a `.btn-primary` linking to `/settings/accounts`. Add the same guard to the contacts-agnostic retry logic: a 409 is never retried (extend the existing `retry` predicate that already refuses 401).

- [ ] **Step 1: Failing tests** — account switch resets the URL to `/mail`; 409 renders the full-pane state and no folder tree; `useSetFlags` in-flight capture.
- [ ] **Step 2: RED. Step 3: Implement. Step 4: GREEN. Step 5: Commit** — `Webmail 2d: mail module follows the account switch`.

---

## Phase 4 — Settings surfaces

### Task 15: Connected accounts page

**Files:**
- Create: `src/frontend/src/modules/settings/accounts/ConnectedAccountsPage.tsx`
- Create: `src/frontend/src/modules/settings/accounts/useConnectedAccounts.ts` (the query + 4 mutations over `['connectedAccounts']`; mutations invalidate `onSettled`)
- Create: `src/frontend/src/modules/settings/accounts/ConnectAccountForm.tsx`
- Modify: `src/frontend/src/routes.tsx:46` (`{ path: 'accounts', element: <ConnectedAccountsPage /> }` — drop the `ComingSoon`)
- Tests: `ConnectedAccountsPage.test.tsx`

Content (mockups validated; follow the `ApplicationTab`/`IdentitiesPage` TS+TanStack pattern and the tile skin `.admin-list`/`.admin-list-item`):
- Heading "Connected accounts", subtitle "Read and send mail from other mailboxes without signing out."
- One tile per account: bold `displayName || email`, secondary line `` `${email} · ${domainName ?? 'Weesky (shared mailbox)'} · connected on <date>` ``; actions are **icons**: `TrashIcon` (via `DeleteConfirmModal`), plus — only when `credentialsValid === false` — a key-shaped re-enter action opening a one-field password dialog (admin dialog shape: `.field-h`, one `.btn-primary`, ✕ only exit) that calls `updateConnectedAccountPassword`; the tile then carries a warn line "Your main password changed — enter this account's password again."
- `ConnectAccountForm` (revealed by a "Connect an account" `.btn-primary`): Server `<select>` — first option `Weesky (local)` (value `''` → `domainId: null` in the POST), then the names from `api.getConnectableDomains()` (`GET /api/ConnectedAccounts/Domains`, produced by Task 7; query key `['connectableDomains']`).
  Fields: Server select, Email, Password (`type="password"`), submit `Connect`, hint "The connection is verified before the account is saved."; API errors surface in the form's error line (generic 502 text included).
- After any successful mutation the `['connectedAccounts']` invalidation refreshes `AuthContext`'s list automatically (same key — that is why the key is shared).

- [ ] **Step 1: Failing tests** — renders tiles from the query; connect posts `{domainId: null | guid, email, password}` and closes the form; delete goes through the confirm modal; the re-enter action appears only on an invalid tile and PUTs the password; server error shows in the form.
- [ ] **Step 2: RED. Step 3: Implement (add `api.getConnectableDomains` + backend route/test if not present). Step 4: GREEN. Step 5: Commit** — `Webmail 2d: connected accounts settings page`.

---

### Task 16: Admin — External domains tab

**Files:**
- Create: `src/frontend/src/modules/settings/admin/ExternalDomainsTab.tsx`
- Create: `src/frontend/src/modules/settings/admin/ExternalDomainDialog.tsx`
- Modify: `src/frontend/src/modules/settings/admin/AdminPage.jsx` (tab entry `External domains` between `Virtual domains` and `Application`; `ADMIN_HELP` line: "Define the external mail providers users may connect accounts from.")
- Tests: `ExternalDomainsTab.test.tsx`

TS + TanStack (`['adminExternalDomains']`, mutations invalidate `onSettled`), tile skin: **name only** + `PencilIcon`/`TrashIcon` (mockup: no configuration in the tiles), `DeleteConfirmModal` for delete — a `domain_in_use` 400 surfaces as the API's message in a toast. `ExternalDomainDialog` (add/edit, admin dialog shape): Display name; IMAP host/port/security; SMTP host/port/security (security `<select>`: `None`/`StartTls`/`SslOnConnect` labelled `None` / `STARTTLS` / `SSL/TLS`); Sieve section labelled "Sieve filters (optional)" with host+port and the hint "Leave empty if the provider does not support Sieve filters — the Rules tab will be hidden for accounts on this domain."; client-side mirror of the backend validation (both-or-neither sieve, port ranges) so the common refusals never round-trip.

- [ ] **Step 1: Failing tests** — list renders names; create posts the full DTO; edit pre-fills; delete confirms; the sieve both-or-neither refusal shows inline.
- [ ] **Step 2: RED. Step 3: Implement. Step 4: GREEN. Step 5: Commit** — `Webmail 2d: external domains admin tab`.

---

### Task 17: Settings navigation per account + guarded routes

**Files:**
- Modify: `src/frontend/src/modules/settings/SettingsLayout.tsx`
- Create: `src/frontend/src/layouts/RequirePrimary.tsx` (mirror of `RequireAdmin`: renders `<Outlet/>` when `activeAccount?.isPrimary !== false`, else `<Navigate to="/settings/general" replace />`)
- Modify: `src/frontend/src/routes.tsx` (wrap `account` + `aliases` under `RequirePrimary`; `rules` under a sieve guard — inline in `SettingsLayout`'s nav *and* a `RequireSieve` sibling of `RequirePrimary` testing `activeAccount?.sieveSupported !== false`)
- Tests: `SettingsLayout.test.tsx`, `RequirePrimary.test.tsx`

Nav rules (mockup 5): label becomes **Connected accounts**; `Account` and `Aliases` render only when `activeAccount?.isPrimary !== false`; `Administration` additionally keeps its `isAdmin` condition; `Rules` renders when primary **or** `sieveSupported`. The `!== false` shape matters: while the account list is loading, `activeAccount` may be null and the primary layout must show, not flash away. Switching accounts while standing on a now-guarded route is covered by the route guards re-evaluating (`Navigate` to `/settings/general`).

- [ ] **Step 1: Failing tests** — nav for a connected account hides Account/Aliases/Administration and keeps Identities; Rules hidden when `sieveSupported === false`; deep link to `/settings/account` under a connected account redirects to General; the loading state (null activeAccount) shows the full primary nav.
- [ ] **Step 2: RED. Step 3: Implement. Step 4: GREEN. Step 5: Commit** — `Webmail 2d: settings navigation follows the active account`.

---

### Task 18: Identities page (connected variant) and Rules gate

**Files:**
- Modify: `src/frontend/src/modules/settings/identities/IdentitiesPage.tsx`
- Modify: `src/frontend/src/modules/settings/identities/IdentityDialog.tsx` (prop `freeAddress?: boolean` — a plain email `<input>` instead of the alias combobox)
- Modify: `src/frontend/src/modules/settings/rules/RulesPage.jsx` (its api calls already gained the options param in Task 11 — pass `{ accountId }` from `useAccountId()`; nothing else changes, the backend swaps the target)
- Tests: `IdentitiesPage.test.tsx`, `IdentityDialog.test.tsx`

`IdentitiesPage` under a connected account (`useAuth().activeAccount?.isPrimary === false`): same tile list (the wire shape is identical — Task 9), with three differences: the `isPrimary` row's tag reads **`Account address`** and offers edit (label only — the dialog opens with the address field disabled) but no delete and no star (it is always the default); added rows offer edit+delete, no star; the add dialog uses `freeAddress` with the hint "Any address you are allowed to send from. The server has the final say — if it refuses this address, sending will fail." The page's existing whole-set `PUT` mechanics (`edited` list, refusal resync) are untouched — `api.replaceIdentities(payload, { accountId })`.

- [ ] **Step 1: Failing tests** — connected variant: account-address tile locked as described; add dialog free-types an address; the PUT carries the account header (api mock asserts options). Primary variant: existing tests stay green unmodified.
- [ ] **Step 2: RED. Step 3: Implement. Step 4: GREEN. Step 5: Commit** — `Webmail 2d: per-account identities UI and rules gate`.

---

## Phase 5 — Verification

### Task 19: Full verification pass

- [ ] **Step 1:** Backend — `dotnet test` from `src/snoopy.microservice` (never `--no-build`); zero failures, no test lost without a replacement (compare test counts against `master`).
- [ ] **Step 2:** Frontend — `npm run lint`, `npm run typecheck`, `npm test`, `npm run test:coverage` (no regression), `npm run build`.
- [ ] **Step 3:** Sweep for leaks: `grep -ri "cipher\|kek" src/snoopy.microservice/Controllers src/snoopy.microservice/Models` — no DTO carries either; `grep -rn "Linked accounts"` in `src/frontend/src` — zero hits (label fully renamed).
- [ ] **Step 4:** Manual checklist (spec § 8, on the dev environment once the DDL of Task 1 has been applied): connect a local shared mailbox and a real external account; switch both ways (fresh cache, INBOX selected, band updated); send/receive from the connected account (From locked to its identities, Sent copy filed on its server); add a free identity and send with it; Sieve rules on an external domain with sieve config, Rules tab absent without; change the main password in the app (connected accounts survive), reset it externally (Password needed → re-enter); delete a domain with connected accounts (refused); the four theme × palette combinations on the three new screens.
- [ ] **Step 5: Commit** any fixes, then report status honestly (remaining issues named, per the house rule).

---

## Self-review notes (already applied)

- **Spec coverage:** §2 schema → Tasks 1–2; §3 crypto → Tasks 3–5; §4 resolver/endpoints/errors/send → Tasks 5–10; §5 frontend → Tasks 11–18; §6 edge cases → distributed (domain RESTRICT T2/T8, v1 cookie T4/T5, 409 surfaces T6/T14/T15); §8 tests → per-task + T19.
- **Sentinel consistency:** `''` in DB (`StorageAccountId`), `'primary'` on the wire (`MailAccountConnection.Primary`, `useAccountId()` default) — conversion happens in exactly one place per direction (resolver / controller edge).
- **Type consistency:** `ConnectedAccountResponse` field list identical in Task 7 (producer) and Tasks 12/15 (consumers); `MailAccountConnection` shape identical in Tasks 5/6/7/10; `SieveConnection` built only in `RulesController`.
