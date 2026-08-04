# OAuth for connected accounts — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user attach an Outlook/Office 365 mailbox by consenting at Microsoft instead of typing a password, by giving `MailAccountConnection` a second credential shape that authenticates over SASL `XOAUTH2`.

**Architecture:** `MailAccountConnection.Password` becomes a closed `MailCredential` hierarchy (`PasswordCredential` | `OAuthCredential`), switched on in `MailConnectionFactory.OpenAsync` — the one place this application authenticates to a mail server. An admin-curated `external_domains` row carries the provider's OAuth endpoints and its Data-Protection-protected client secret; a `connected_accounts` row carries the refresh token in the existing per-user cipher. A three-step handshake (Start → anonymous Callback → same-site Complete) works around `SameSite=Strict` cookies, and `OAuthTokenService` keeps a short-lived access token in a process-local cache.

**Tech Stack:** ASP.NET Core (.NET 10), MailKit 4.17 (`SaslMechanismOAuth2`), EF Core + Pomelo MySQL, `IMemoryCache`, ASP.NET Data Protection, xUnit + Moq; React 18 + TypeScript + TanStack Query + Vitest on the frontend. **No new NuGet or npm package.**

**Spec:** `docs/superpowers/specs/2026-08-04-oauth-connected-accounts-design.md` — read it before starting. Where this plan and the spec disagree, the spec is right and the plan is a bug.

## Global Constraints

- **Run `dotnet test`, never `dotnet test --no-build`, whenever a task adds a test file.** `--no-build` runs the previous binary and reports a green suite for code that was never compiled.
- **`src/snoopy.microservice/ApiDocumentation.xml` is a versioned artefact that `dotnet test` regenerates with hundreds of unrelated lines.** Run `git checkout -- src/snoopy.microservice/ApiDocumentation.xml` before every commit unless the task deliberately changed XML doc comments on public API surface.
- Backend commands run from `src/snoopy.microservice`; frontend commands from `src/frontend`.
- **C# style** (`src/snoopy.microservice/CLAUDE.md`): file-scoped namespaces, one type per file named after the type, primary constructors for DI, `sealed` on classes not designed for inheritance, `internal` unless a public type needs it, records for DTOs, collection expressions, pattern matching, `Async` suffix, `ILogger` structured logging with no string interpolation, `CancellationToken` on every async method, no try-catch that only logs and rethrows.
- **Comments only where the code cannot speak for itself, three lines maximum.** This codebase's comments explain *why*, never *what*.
- **Commit messages: two lines maximum, and neither the first nor the last character may be `@`.** Use `git commit -F -` with a heredoc, never a PowerShell here-string.
- **Never log a secret** — not a password, not a token, not a client secret, not a `code`. `MailAccountConnection.ToString` and `SieveConnection.ToString` are redacted for this reason and must stay so.
- The mail rules in `src/snoopy.microservice/CLAUDE.md` are binding, rule 4 above all: **401 credentials cookie, 404 account not found, 409 connected credentials invalid, 502 anything the server refused.** No new status is introduced by this plan.
- Frontend: a token names a role, never a colour; test files sit beside what they test.

## Packets

The eight tasks below are grouped into **four packets**, and a packet is one worker's assignment:
it is the unit that gets a fresh context and a review gate. The tasks inside a packet share a
subject and are committed separately, so a reviewer can still reject one without the other.

| packet | tasks | delivers |
|---|---|---|
| **1 — Room for a second credential** | 1, 2 | a connection carries a `MailCredential`; a cipher carries a mode and a token's worth of bytes |
| **2 — The provider and the token** | 3, 4, 5 | an OAuth row resolves to a live access token |
| **3 — The consent** | 6, 7 | a mailbox can be attached by consenting at the provider |
| **4 — The screen** | 8 | the settings page offers it |

Packets run in order: each consumes what the previous produced. Packet 2 is the heavy one — three
tasks, the token service among them — and is the one to give the strongest model.

---

## File Structure

**Created — backend**

| file | responsibility |
|---|---|
| `Models/Mail/MailCredential.cs` | the closed credential hierarchy |
| `Models/Mail/OAuthProviderConfig.cs` | a validated OAuth provider, projected from an `ExternalDomain` row |
| `Services/IOAuthTokenService.cs` / `OAuthTokenService.cs` | refresh, cache, rotation |
| `Services/IOAuthHandshakeStore.cs` / `OAuthHandshakeStore.cs` | the pending consent, `IMemoryCache`-backed |
| `Services/OAuthHandshake.cs` | the pending-consent record |
| `Services/IClientSecretProtector.cs` / `ClientSecretProtector.cs` | Data Protection over the client secret |
| `Models/ConnectedAccounts/OAuthStartRequest.cs`, `OAuthStartResponse.cs`, `OAuthCompleteRequest.cs` | the three endpoints' DTOs |

**Modified — backend**

| file | change |
|---|---|
| `Models/Mail/MailAccountConnection.cs` | `Password` → `Credential` |
| `Services/MailConnectionFactory.cs` | the SASL switch |
| `Services/MailConnectionBuilder.cs` | takes a `MailCredential`; refuses an incomplete OAuth domain |
| `Services/ConnectedAccountCipher.cs` | width, and the mode in the context |
| `Services/AccountConnectionResolver.cs` | an OAuth row acquires a token |
| `Services/ScopedImapSessionProvider.cs` | cache key follows the credential |
| `Controllers/RulesController.cs` | an OAuth connection has no ManageSieve |
| `Controllers/ConnectedAccountsController.cs` | the three endpoints, `authMode` on the responses |
| `Controllers/ApiBaseController.cs` | `ProviderUnavailable` → 502 |
| `Services/ConnectedAccountErrors.cs` | the new constant |
| `Data/Preferences/ExternalDomain.cs`, `ConnectedAccount.cs` | the new columns |
| `Configuration/ApplicationServicesConfiguration.cs` | DI registration |

**Created — docs**

- `docs/superpowers/mail-oauth-provider-prerequisite.md` — the Azure registration, the DDL, the domain row.

**Modified — frontend**

- `src/api.js`, `src/modules/settings/accounts/useConnectedAccounts.ts`, `ConnectAccountForm.tsx`, `ConnectedAccountsPage.tsx`.

---

# Packet 1 — Room for a second credential

Two tasks, two commits. Nothing acquires or stores a token yet: this packet only makes the shape
exist, and proves it by authenticating over XOAUTH2 against a fake server.

## Task 1: The closed credential type

Pure refactor plus one new capability: an `OAuthCredential` authenticates over `XOAUTH2`. Nothing stores or acquires a token yet.

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/MailCredential.cs`
- Modify: `src/snoopy.microservice/Models/Mail/MailAccountConnection.cs:9-15`
- Modify: `src/snoopy.microservice/Services/MailConnectionFactory.cs:102`
- Modify: `src/snoopy.microservice/Services/MailConnectionBuilder.cs:16-21,29-44`
- Modify: `src/snoopy.microservice/Services/AccountConnectionResolver.cs:36,56,85-86,96`
- Modify: `src/snoopy.microservice/Services/ScopedImapSessionProvider.cs:30`
- Modify: `src/snoopy.microservice/Controllers/RulesController.cs:52-61`
- Modify: `src/snoopy.microservice/Controllers/ConnectedAccountsController.cs:292-308`
- Modify: `src/snoopy.microservice/snoopy.microservice.Tests/Infrastructure/TestConnections.cs`
- Modify: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapSessionListFoldersTests.cs:120-210` (the `FakeImapServer` class)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailConnectionFactoryOAuthTests.cs` (create)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `public abstract record MailCredential` with `public sealed override string ToString()`
  - `public sealed record PasswordCredential(string Password) : MailCredential`
  - `public sealed record OAuthCredential(string AccessToken) : MailCredential`
  - `MailAccountConnection(..., string Username, MailCredential Credential)` — `Password` no longer exists
  - `MailConnectionBuilder.Home(MailOptions home, string accountId, string username, MailCredential credential)`
  - `MailConnectionBuilder.TryExternal(ExternalDomain domain, string accountId, string username, MailCredential credential, out MailAccountConnection? connection, bool allowCleartext = false)`

- [ ] **Step 1: Write the credential type**

Create `src/snoopy.microservice/Models/Mail/MailCredential.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// How this application proves an identity to a mail server. Closed: the private protected
/// constructor keeps the two cases exhaustive, so a switch over them cannot silently grow a
/// third that nobody handled.
/// </summary>
public abstract record MailCredential
{
    private protected MailCredential() { }

    /// <summary>Sealed, not merely overridden: a record generates its own ToString unless the
    /// base has sealed it, and the generated one prints the secret.</summary>
    public sealed override string ToString() => GetType().Name;
}

/// <summary>A mailbox password, replayed on every login.</summary>
public sealed record PasswordCredential(string Password) : MailCredential;

/// <summary>A short-lived OAuth 2.0 access token, presented over SASL XOAUTH2.</summary>
public sealed record OAuthCredential(string AccessToken) : MailCredential;
```

- [ ] **Step 2: Teach `FakeImapServer` to speak XOAUTH2**

In `src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapSessionListFoldersTests.cs`, on the `FakeImapServer` class (line 120):

Add a constructor flag and a recording list beside `StatusRequests`:

```csharp
    private readonly bool _oauth;

    public FakeImapServer(bool condStore = false, bool oauth = false)
    {
        _condStore = condStore;
        _oauth = oauth;
    }

    /// <summary>The SASL mechanism the client actually chose.</summary>
    public string? AuthenticateMechanism { get; private set; }
```

Change `Caps` so the OAuth server advertises the mechanism and withholds plain login:

```csharp
    private string Caps
    {
        get
        {
            var baseCaps = _condStore
                ? "IMAP4rev1 NAMESPACE SPECIAL-USE CONDSTORE"
                : "IMAP4rev1 NAMESPACE SPECIAL-USE";
            return _oauth ? $"{baseCaps} AUTH=XOAUTH2 LOGINDISABLED" : baseCaps;
        }
    }
```

Add an `AUTHENTICATE` case to the command switch, above `default:`:

```csharp
                    case "AUTHENTICATE":
                        AuthenticateMechanism = parts.Length > 2
                            ? parts[2].Split(' ')[0].ToUpperInvariant()
                            : string.Empty;
                        // The client sends the payload with the command (SASL-IR); nothing here
                        // reads it, since what is under test is the mechanism, not the secret.
                        await writer.WriteLineAsync($"{tag} OK [CAPABILITY {Caps}] AUTHENTICATE completed");
                        break;
```

- [ ] **Step 3: Write the failing test**

Create `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailConnectionFactoryOAuthTests.cs`:

```csharp
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The credential decides the SASL mechanism, and only a real dialogue can pin that down: a mock
/// would assert the call this code makes rather than the command the server receives.
/// </summary>
public sealed class MailConnectionFactoryOAuthTests
{
    private static ImapConnectionFactory CreateFactory()
    {
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue)
               .Returns(new MailOptions { TimeoutSeconds = 10, AllowCleartext = true });

        return new ImapConnectionFactory(
            monitor.Object, Mock.Of<IMailHtmlSanitizer>(), NullLogger<ImapConnectionFactory>.Instance);
    }

    private static MailAccountConnection On(int port, MailCredential credential) =>
        TestConnections.Primary("alice@weesky.be", credential) with
        {
            ImapHost = "127.0.0.1", ImapPort = port, ImapSecurity = SecureSocketOptions.None
        };

    [Fact]
    public async Task OpenAsync_AuthenticatesAnOAuthCredentialOverXOAuth2()
    {
        using var server = new FakeImapServer(oauth: true);
        server.Start();

        var result = await CreateFactory()
            .OpenAsync(On(server.Port, new OAuthCredential("ya29.token")), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("XOAUTH2", server.AuthenticateMechanism);
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task OpenAsync_AuthenticatesAPasswordCredentialWithoutSasl()
    {
        using var server = new FakeImapServer();
        server.Start();

        var result = await CreateFactory()
            .OpenAsync(On(server.Port, new PasswordCredential("hunter2")), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(server.AuthenticateMechanism);
        await result.Value.DisposeAsync();
    }

    [Fact]
    public void ToString_OfACredentialNeverPrintsTheSecret()
    {
        Assert.DoesNotContain("hunter2", new PasswordCredential("hunter2").ToString());
        Assert.DoesNotContain("ya29.token", new OAuthCredential("ya29.token").ToString());
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~MailConnectionFactoryOAuthTests"`
Expected: **compilation failure** — `TestConnections.Primary` takes a `string`, `OAuthCredential` does not exist. That is the failure to fix.

- [ ] **Step 5: Change the connection record**

In `src/snoopy.microservice/Models/Mail/MailAccountConnection.cs`, replace the `string Password` positional parameter with `MailCredential Credential`. Leave every other member, including the redacted `ToString`, exactly as it is.

- [ ] **Step 6: Switch on the credential in the factory**

In `src/snoopy.microservice/Services/MailConnectionFactory.cs`, add `using System.Diagnostics;` and replace line 102:

```csharp
                await (connection.Credential switch
                {
                    OAuthCredential oauth => client.AuthenticateAsync(
                        new SaslMechanismOAuth2(connection.Username, oauth.AccessToken), connectCts.Token),
                    PasswordCredential password => client.AuthenticateAsync(
                        connection.Username, password.Password, connectCts.Token),
                    _ => throw new UnreachableException()
                });
```

The comment above it — about `AuthenticateAsync` sending the password whether or not the login succeeds — still applies and stays.

- [ ] **Step 7: Follow the compiler through every consumer**

Run `dotnet build` and fix each error. The expected set, and what each becomes:

1. `Services/MailConnectionBuilder.cs` — both methods take `MailCredential credential` in place of `string password` and pass it through.
2. `Services/AccountConnectionResolver.cs` — `HomeConnection(..., string password)` becomes `HomeConnection(..., MailCredential credential)`; the three call sites wrap the decrypted secret: `new PasswordCredential(payload.Password)` at line 36, `new PasswordCredential(secret.Value)` at lines 56 and 96.
3. `Services/ScopedImapSessionProvider.cs:30` — the key tuple's last element becomes `connection.Credential`. Records give it value equality, so the tuple still compares by content.
4. `Controllers/ConnectedAccountsController.cs:292-308` — `BuildProbe(ExternalDomain? domain, string email, string password)` keeps its `string password` parameter (the caller genuinely holds a password) and wraps once: `new PasswordCredential(password)` at each `MailConnectionBuilder` call.
5. `Controllers/RulesController.cs:52-61` — see Step 8.
6. `snoopy.microservice.Tests/Infrastructure/TestConnections.cs` — every factory takes `MailCredential credential`.
7. Test call sites — `ConnectedAccountsControllerTests.cs:353,574` and `AccountConnectionResolverTests.cs:151,172,436,443` assert on `c.Password`; they become `c.Credential == new PasswordCredential("gmailpw")` and `Assert.Equal(new PasswordCredential("provider-pw"), result.Value.Credential)`.

- [ ] **Step 8: Refuse ManageSieve for an OAuth credential**

In `src/snoopy.microservice/Controllers/RulesController.cs`, `TryResolveAsync` reads `account.Password` twice (lines 54 and 60). Insert one guard after `var sieve = sieveOptions.Value;` (line 36), and use the matched value below:

```csharp
        // ManageSieve here is SASL PLAIN, hand-assembled: an access token is not a password, and
        // no provider reached over OAuth offers ManageSieve anyway.
        if (account.Credential is not PasswordCredential mailbox)
            return AccountResolution<SieveConnection>.Failure(NotFoundEnveloppe(SieveErrors.Unsupported));
```

Then lines 54 and 60 read `mailbox.Password`. The primary-account branch (line 46) is untouched — it uses the master password, not the account's.

- [ ] **Step 9: Update `TestConnections`**

```csharp
    public static MailAccountConnection Primary(string email, MailCredential credential)
    {
        var options = HomeOptions();
        return new MailAccountConnection(
            MailAccountConnection.Primary, IsHomeServer: true,
            options.ImapHost, options.ImapPort, options.ImapSecurity,
            options.SmtpHost, options.SmtpPort, options.SmtpSecurity,
            SieveHost: null, SievePort: null, email, credential);
    }

    /// <summary>The overwhelmingly common case in the suite: a password mailbox.</summary>
    public static MailAccountConnection Primary(string email, string password) =>
        Primary(email, new PasswordCredential(password));
```

Add the same `string password` convenience overload to `Connected`, `ConnectedWithSieve` and `ConnectedLocal`, each delegating to a `MailCredential` version. That keeps the existing call sites compiling unchanged and is why this refactor does not touch dozens of test files.

- [ ] **Step 10: Run the whole suite**

Run: `dotnet test`
Expected: PASS, including the three new tests. The suite is large; a failure elsewhere means a consumer was missed, not that this task is done.

- [ ] **Step 11: Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice
git commit -F - <<'EOF'
Give a mail connection a credential instead of a password

Closed MailCredential hierarchy; an OAuth one authenticates over XOAUTH2.
EOF
```

---

## Task 2: The cipher carries a mode, and a token's worth of bytes

**Files:**
- Modify: `src/snoopy.microservice/Data/Preferences/ConnectedAccount.cs`
- Modify: `src/snoopy.microservice/Services/ConnectedAccountCipher.cs:22-62`
- Modify: `src/snoopy.microservice/Controllers/ConnectedAccountsController.cs:147-153` (stamp the mode on a created row)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ConnectedAccountCipherTests.cs` (modify)

**Interfaces:**
- Consumes: `MailCredential` from Task 1 (not directly referenced here).
- Produces:
  - `public enum MailAuthMode { Password, OAuth2 }` in `Models/Mail/MailAuthMode.cs`
  - `ConnectedAccount.AuthMode` (type `MailAuthMode`, column `auth_mode`, default `Password`)
  - `ConnectedAccountCipher.MaxSecretLength == 8163`
  - `ConnectedAccountCipher.Context(ConnectedAccount row)` — unchanged output for a `Password` row

- [ ] **Step 1: Write the failing tests**

Append to `src/snoopy.microservice/snoopy.microservice.Tests/Services/ConnectedAccountCipherTests.cs` (keep every existing test):

```csharp
    [Fact]
    public void Context_OfAPasswordRow_IsUnchangedFromTheBoundFormat()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var userId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var domainId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var row = new ConnectedAccount
        {
            Id = accountId, UserId = userId, DomainId = domainId,
            Email = "alice@example.test", AuthMode = MailAuthMode.Password
        };

        Assert.Equal(
            $"{accountId:D}|{userId:D}|{domainId:D}|alice@example.test",
            Encoding.UTF8.GetString(ConnectedAccountCipher.Context(row)));
    }

    [Fact]
    public void Context_OfAnOAuthRow_CarriesTheModeBeforeTheAddress()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var userId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var domainId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var row = new ConnectedAccount
        {
            Id = accountId, UserId = userId, DomainId = domainId,
            Email = "alice@example.test", AuthMode = MailAuthMode.OAuth2
        };

        Assert.Equal(
            $"{accountId:D}|{userId:D}|{domainId:D}|oauth2|alice@example.test",
            Encoding.UTF8.GetString(ConnectedAccountCipher.Context(row)));
    }

    [Fact]
    public void Decrypt_UnderTheWrongMode_Fails()
    {
        var kek = ConnectedAccountCipher.DeriveKek("main", ConnectedAccountCipher.NewSalt());
        var row = new ConnectedAccount
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), DomainId = Guid.NewGuid(),
            Email = "alice@example.test", AuthMode = MailAuthMode.OAuth2
        };
        var cipher = ConnectedAccountCipher.Encrypt(kek, "refresh-token", ConnectedAccountCipher.Context(row));

        row.AuthMode = MailAuthMode.Password;

        Assert.True(ConnectedAccountCipher.Decrypt(kek, cipher, ConnectedAccountCipher.Context(row)).IsFailure);
    }

    [Fact]
    public void Encrypt_AcceptsARefreshTokenSizedSecret()
    {
        var kek = ConnectedAccountCipher.DeriveKek("main", ConnectedAccountCipher.NewSalt());
        var context = ConnectedAccountCipher.Context(Guid.NewGuid(), Guid.NewGuid(), null, "a@b.test");
        var secret = new string('t', ConnectedAccountCipher.MaxSecretLength);

        var cipher = ConnectedAccountCipher.Encrypt(kek, secret, context);

        Assert.Equal(secret, ConnectedAccountCipher.Decrypt(kek, cipher, context).Value);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConnectedAccountCipher.Encrypt(kek, secret + "t", context));
    }
```

Add `using weesky.Snoopy.Microservice.Models.Mail;` to the file if it is not already there.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ConnectedAccountCipherTests"`
Expected: compilation failure — `MailAuthMode` and `ConnectedAccount.AuthMode` do not exist.

- [ ] **Step 3: Add the mode enum**

Create `src/snoopy.microservice/Models/Mail/MailAuthMode.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>How a mailbox is authenticated. Password is first so that it is <c>default</c>.</summary>
public enum MailAuthMode
{
    Password,
    OAuth2
}
```

- [ ] **Step 4: Add the column to the entity**

In `src/snoopy.microservice/Data/Preferences/ConnectedAccount.cs`, after `DomainId`:

```csharp
    /// <summary>Frozen at creation: a row that describes itself cannot be reinterpreted by an
    /// admin flipping its domain's mode.</summary>
    [Column("auth_mode")]
    public MailAuthMode AuthMode { get; set; }
```

Update the `Cipher` doc comment to say it holds a password *or a refresh token*. EF maps an enum to an int by default and the column is a string, so add the conversion in `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs`, beside the other `ConnectedAccount` configuration (line 74):

```csharp
        modelBuilder.Entity<ConnectedAccount>()
            .Property(a => a.AuthMode)
            .HasConversion<string>()
            .HasMaxLength(16);
```

- [ ] **Step 5: Widen the cipher and put the mode in the context**

In `src/snoopy.microservice/Services/ConnectedAccountCipher.cs`:

Replace the `MaxSecretLength` constant and its comment:

```csharp
    // connected_accounts.cipher is VARBINARY(8192); 8192 - 1 (version) - 12 (nonce) - 16 (tag) = 8163.
    // Not a precaution: a Microsoft refresh token is an encrypted blob that routinely exceeds 1 KB.
    public const int MaxSecretLength = 8163;
```

Add a mode-aware overload of `Context` and route `Context(row)` through it:

```csharp
    /// <summary>
    /// Everything that decides where a secret is sent and as whom … (existing comment kept)
    ///
    /// The mode segment is written only for OAuth, and before the address: every row bound before
    /// the mode existed still opens, and the address stays last so no two rows can collide.
    /// </summary>
    public static byte[] Context(
        Guid accountId, Guid userId, Guid? domainId, string email,
        MailAuthMode authMode = MailAuthMode.Password) =>
        Encoding.UTF8.GetBytes(
            $"{accountId:D}|{userId:D}|{domainId?.ToString("D") ?? string.Empty}|"
            + (authMode is MailAuthMode.OAuth2 ? "oauth2|" : string.Empty)
            + email);

    public static byte[] Context(ConnectedAccount row) =>
        Context(row.Id, row.UserId, row.DomainId, IdentityResolver.Canonical(row.Email), row.AuthMode);
```

`Context(ConnectedAccount)` dereferences `row`, so keep the existing `ArgumentNullException.ThrowIfNull(row)` if one is there and add one if not.

- [ ] **Step 6: Stamp the mode on a created row**

In `src/snoopy.microservice/Controllers/ConnectedAccountsController.cs`, the row built at line 147 gains `AuthMode = MailAuthMode.Password`. It is the enum's default, so this is documentation rather than behaviour — write it anyway: the OAuth path in Task 7 builds the same record with the other value, and a reader comparing the two should not have to know which value is `default`.

- [ ] **Step 7: Run the tests**

Run: `dotnet test`
Expected: PASS. `ConnectedAccountStoreTests` and `AccountConnectionResolverTests` exercise the cipher end to end and must stay green — a red one there means `Context` changed for a password row.

- [ ] **Step 8: Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice
git commit -F - <<'EOF'
Bind a connected-account cipher to its auth mode, and widen it

A refresh token needs 8163 bytes; a password row's context is unchanged.
EOF
```

---

# Packet 2 — The provider and the token

Three tasks, three commits, and the packet's whole claim is the last one: an OAuth connected
account resolves to a live access token, or to the 409 or 502 that says why it could not. This is
the heaviest packet; do the tasks in order and run the full suite at each commit.

## Task 3: The provider record

The `external_domains` row learns to describe an OAuth provider, and the client secret is protected at rest. Nothing consumes it yet.

**Files:**
- Modify: `src/snoopy.microservice/Data/Preferences/ExternalDomain.cs`
- Create: `src/snoopy.microservice/Models/Mail/OAuthProviderConfig.cs`
- Create: `src/snoopy.microservice/Services/IClientSecretProtector.cs`
- Create: `src/snoopy.microservice/Services/ClientSecretProtector.cs`
- Modify: `src/snoopy.microservice/Services/MailConnectionBuilder.cs`
- Modify: `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs:51-80`
- Create: `docs/superpowers/mail-oauth-provider-prerequisite.md`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Models/Mail/OAuthProviderConfigTests.cs` (create)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ClientSecretProtectorTests.cs` (create)

**Interfaces:**
- Consumes: `MailAuthMode` (Task 2).
- Produces:
  - `ExternalDomain.AuthMode`, `.OAuthAuthorizationUrl`, `.OAuthTokenUrl`, `.OAuthScopes`, `.OAuthClientId`, `.OAuthClientSecret` (`byte[]?`)
  - `public sealed record OAuthProviderConfig(string AuthorizationUrl, string TokenUrl, string Scopes, string ClientId, byte[] ClientSecret)` with `public static bool TryFrom(ExternalDomain domain, out OAuthProviderConfig? config)`
  - `IClientSecretProtector` with `byte[] Protect(string secret)` and `string? Unprotect(byte[] protectedSecret)`

- [ ] **Step 1: Write the failing tests**

Create `src/snoopy.microservice/snoopy.microservice.Tests/Models/Mail/OAuthProviderConfigTests.cs`:

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models.Mail;

public sealed class OAuthProviderConfigTests
{
    private static ExternalDomain Complete() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Outlook",
        AuthMode = MailAuthMode.OAuth2,
        OAuthAuthorizationUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
        OAuthTokenUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
        OAuthScopes = "offline_access openid email",
        OAuthClientId = "client-id",
        OAuthClientSecret = [1, 2, 3]
    };

    [Fact]
    public void TryFrom_ReadsACompleteRow()
    {
        Assert.True(OAuthProviderConfig.TryFrom(Complete(), out var config));
        Assert.Equal("client-id", config!.ClientId);
        Assert.Equal("offline_access openid email", config.Scopes);
    }

    [Fact]
    public void TryFrom_RefusesAPasswordDomain()
    {
        var domain = Complete();
        domain.AuthMode = MailAuthMode.Password;

        Assert.False(OAuthProviderConfig.TryFrom(domain, out var config));
        Assert.Null(config);
    }

    [Theory]
    [InlineData("OAuthAuthorizationUrl")]
    [InlineData("OAuthTokenUrl")]
    [InlineData("OAuthScopes")]
    [InlineData("OAuthClientId")]
    public void TryFrom_RefusesARowMissingAnyStringField(string missing)
    {
        var domain = Complete();
        typeof(ExternalDomain).GetProperty(missing)!.SetValue(domain, null);

        Assert.False(OAuthProviderConfig.TryFrom(domain, out _));
    }

    [Fact]
    public void TryFrom_RefusesARowWithNoClientSecret()
    {
        var domain = Complete();
        domain.OAuthClientSecret = null;

        Assert.False(OAuthProviderConfig.TryFrom(domain, out _));
    }

    [Fact]
    public void TryFrom_RefusesANonHttpsEndpoint()
    {
        var domain = Complete();
        domain.OAuthTokenUrl = "http://login.microsoftonline.com/common/oauth2/v2.0/token";

        Assert.False(OAuthProviderConfig.TryFrom(domain, out _));
    }
}
```

Create `src/snoopy.microservice/snoopy.microservice.Tests/Services/ClientSecretProtectorTests.cs`:

```csharp
using Microsoft.AspNetCore.DataProtection;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ClientSecretProtectorTests
{
    private static ClientSecretProtector Create() =>
        new(DataProtectionProvider.Create(nameof(ClientSecretProtectorTests)));

    [Fact]
    public void Protect_RoundTrips()
    {
        var protector = Create();

        Assert.Equal("s3cr3t", protector.Unprotect(protector.Protect("s3cr3t")));
    }

    [Fact]
    public void Protect_ProducesSomethingThatIsNotThePlaintext()
    {
        var protector = Create();

        Assert.DoesNotContain("s3cr3t", System.Text.Encoding.UTF8.GetString(protector.Protect("s3cr3t")));
    }

    [Fact]
    public void Unprotect_OfRubbish_AnswersNull()
    {
        Assert.Null(Create().Unprotect([1, 2, 3, 4]));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~OAuthProviderConfigTests|FullyQualifiedName~ClientSecretProtectorTests"`
Expected: compilation failure — neither type exists.

- [ ] **Step 3: Add the domain columns**

In `src/snoopy.microservice/Data/Preferences/ExternalDomain.cs`, after `SievePort`:

```csharp
    /// <summary>Password unless an admin declared this provider an OAuth one.</summary>
    [Column("auth_mode")]
    public MailAuthMode AuthMode { get; set; }

    [Column("oauth_authorization_url")]
    public string? OAuthAuthorizationUrl { get; set; }

    [Column("oauth_token_url")]
    public string? OAuthTokenUrl { get; set; }

    /// <summary>Space-separated, sent to the provider verbatim.</summary>
    [Column("oauth_scopes")]
    public string? OAuthScopes { get; set; }

    [Column("oauth_client_id")]
    public string? OAuthClientId { get; set; }

    /// <summary>Data-Protection-protected. Never logged, never returned by any endpoint.</summary>
    [Column("oauth_client_secret")]
    public byte[]? OAuthClientSecret { get; set; }
```

Add `using weesky.Snoopy.Microservice.Models.Mail;`. In `PreferencesDbContext`, beside the `ExternalDomain` configuration (line 72), add the same string conversion Task 2 added for `ConnectedAccount`:

```csharp
        modelBuilder.Entity<ExternalDomain>()
            .Property(d => d.AuthMode)
            .HasConversion<string>()
            .HasMaxLength(16);
```

- [ ] **Step 4: Write the projection**

Create `src/snoopy.microservice/Models/Mail/OAuthProviderConfig.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// An OAuth provider an admin fully described. The projection exists so that "is this row
/// usable" is answered once, by the type system, rather than by five null checks spread over
/// the handshake, the refresh and the connect form.
/// </summary>
public sealed record OAuthProviderConfig(
    string AuthorizationUrl, string TokenUrl, string Scopes, string ClientId, byte[] ClientSecret)
{
    /// <summary>
    /// False for a password domain and for an OAuth one missing any of its five fields — the
    /// caller logs it as administrator error and answers account_not_found, exactly as it does
    /// for a domain whose transport security no longer parses.
    /// </summary>
    public static bool TryFrom(ExternalDomain domain, [NotNullWhen(true)] out OAuthProviderConfig? config)
    {
        ArgumentNullException.ThrowIfNull(domain);
        config = null;

        if (domain.AuthMode is not MailAuthMode.OAuth2
            || !IsHttps(domain.OAuthAuthorizationUrl) || !IsHttps(domain.OAuthTokenUrl)
            || string.IsNullOrWhiteSpace(domain.OAuthScopes)
            || string.IsNullOrWhiteSpace(domain.OAuthClientId)
            || domain.OAuthClientSecret is not { Length: > 0 } secret)
            return false;

        config = new OAuthProviderConfig(
            domain.OAuthAuthorizationUrl!, domain.OAuthTokenUrl!, domain.OAuthScopes!,
            domain.OAuthClientId!, secret);
        return true;
    }

    // An endpoint reached in the clear would put the client secret and the refresh token on the
    // wire; there is no AllowCleartext opt-in for this the way there is for IMAP.
    private static bool IsHttps([NotNullWhen(true)] string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps;
}
```

- [ ] **Step 5: Write the protector**

Create `src/snoopy.microservice/Services/IClientSecretProtector.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Services;

/// <summary>Protects an application secret at rest — the OAuth client secret, and nothing else.</summary>
public interface IClientSecretProtector
{
    byte[] Protect(string secret);

    /// <summary>Null when the blob does not open: a rotated key ring, or a corrupted row.</summary>
    string? Unprotect(byte[] protectedSecret);
}
```

Create `src/snoopy.microservice/Services/ClientSecretProtector.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// A purpose of its own on the existing key ring, distinct from the credentials cookie's: the two
/// secrets have different lifetimes and different blast radii, and a shared purpose would let a
/// blob from one be replayed as the other.
/// </summary>
internal sealed class ClientSecretProtector : IClientSecretProtector
{
    private const string Purpose = "weesky.oauth.clientsecret";

    private readonly IDataProtector _protector;

    public ClientSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    public byte[] Protect(string secret) => _protector.Protect(Encoding.UTF8.GetBytes(secret));

    public string? Unprotect(byte[] protectedSecret)
    {
        ArgumentNullException.ThrowIfNull(protectedSecret);
        try
        {
            return Encoding.UTF8.GetString(_protector.Unprotect(protectedSecret));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
```

- [ ] **Step 6: Refuse an unusable OAuth domain when building a connection**

In `src/snoopy.microservice/Services/MailConnectionBuilder.cs`, `TryExternal` currently validates transport security only. Add the mode check at the top of the method, after `connection = null;`:

```csharp
        // An OAuth domain the admin left half-configured is as unusable as one whose security
        // value does not parse, and answers the same way — the caller logs and 404s.
        if (domain.AuthMode is MailAuthMode.OAuth2 && !OAuthProviderConfig.TryFrom(domain, out _))
            return false;
```

- [ ] **Step 7: Register the protector**

In `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs`, in `AddMailServices`, beside the other singletons:

```csharp
        services.AddSingleton<IClientSecretProtector, ClientSecretProtector>();
```

- [ ] **Step 8: Run the tests**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 9: Write the operator prerequisite document**

Create `docs/superpowers/mail-oauth-provider-prerequisite.md`. Copy the *What an operator must do* section of the spec verbatim — the Entra registration with its two-API permission step, the two `ALTER TABLE` statements, and the Outlook domain row's values — and add a leading paragraph saying that database creation is manual in this project, the way `mail-2a5-database-prerequisite.md` does, and that the client secret must be written through the admin screen rather than into the column, since the column holds protected bytes and not text.

- [ ] **Step 10: Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice docs
git commit -F - <<'EOF'
Let an external domain describe an OAuth provider

Five columns plus a protected client secret, validated once by OAuthProviderConfig.
EOF
```

---

## Task 4: Refreshing an access token

**Files:**
- Create: `src/snoopy.microservice/Services/IOAuthTokenService.cs`
- Create: `src/snoopy.microservice/Services/OAuthTokenService.cs`
- Create: `src/snoopy.microservice/Models/Mail/OAuthTokenResponse.cs`
- Modify: `src/snoopy.microservice/Services/ConnectedAccountErrors.cs`
- Modify: `src/snoopy.microservice/Controllers/ApiBaseController.cs:69-74`
- Modify: `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/OAuthTokenServiceTests.cs` (create)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Infrastructure/StubHttpMessageHandler.cs` (create)

**Interfaces:**
- Consumes: `OAuthProviderConfig`, `IClientSecretProtector` (Task 3); `ConnectedAccountCipher.Context(row)` (Task 2).
- Produces:
  - `ConnectedAccountErrors.ProviderUnavailable == "oauth_provider_unavailable"`
  - `public sealed record OAuthTokenResponse(string AccessToken, string? RefreshToken, int ExpiresInSeconds, string? IdToken)`
  - `IOAuthTokenService.GetAccessTokenAsync(ConnectedAccount row, OAuthProviderConfig provider, byte[] kek, CancellationToken ct)` → `Task<Result<string>>`
  - `IOAuthTokenService.ExchangeCodeAsync(OAuthProviderConfig provider, string code, string codeVerifier, string redirectUri, CancellationToken ct)` → `Task<Result<OAuthTokenResponse>>`

- [ ] **Step 1: Write the stub handler**

Create `src/snoopy.microservice/snoopy.microservice.Tests/Infrastructure/StubHttpMessageHandler.cs`:

```csharp
using System.Net;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>Answers a queued script of responses and records what was asked.</summary>
internal sealed class StubHttpMessageHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
{
    private int _served;

    public List<string> Bodies { get; } = [];

    public int Calls => _served;

    public static Func<HttpResponseMessage> Json(HttpStatusCode status, string body) =>
        () => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Bodies.Add(request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken));

        var index = Interlocked.Increment(ref _served) - 1;
        if (index >= responses.Length) throw new InvalidOperationException("No scripted response left.");
        return responses[index]();
    }
}
```

- [ ] **Step 2: Write the failing tests**

Create `src/snoopy.microservice/snoopy.microservice.Tests/Services/OAuthTokenServiceTests.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class OAuthTokenServiceTests
{
    private static readonly byte[] Kek =
        ConnectedAccountCipher.DeriveKek("main", ConnectedAccountCipher.NewSalt());

    private static OAuthProviderConfig Provider() => new(
        "https://provider.test/authorize", "https://provider.test/token",
        "offline_access", "client-id", [9, 9, 9]);

    private static ConnectedAccount Row(string refreshToken)
    {
        var row = new ConnectedAccount
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), DomainId = Guid.NewGuid(),
            Email = "alice@outlook.test", AuthMode = MailAuthMode.OAuth2
        };
        row.Cipher = ConnectedAccountCipher.Encrypt(
            Kek, refreshToken, ConnectedAccountCipher.Context(row));
        return row;
    }

    private static (OAuthTokenService Service, Mock<IConnectedAccountStore> Accounts) Create(
        StubHttpMessageHandler handler)
    {
        var accounts = new Mock<IConnectedAccountStore>();
        var protector = new Mock<IClientSecretProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<byte[]>())).Returns("client-secret");

        var service = new OAuthTokenService(
            new HttpClient(handler),
            new MemoryCache(new MemoryCacheOptions()),
            accounts.Object,
            protector.Object,
            NullLogger<OAuthTokenService>.Instance);

        return (service, accounts);
    }

    [Fact]
    public async Task GetAccessTokenAsync_RefreshesAndAnswersTheAccessToken()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"access_token":"at-1","refresh_token":"rt-2","expires_in":3600}"""));
        var (service, _) = Create(handler);

        var result = await service.GetAccessTokenAsync(
            Row("rt-1"), Provider(), Kek, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("at-1", result.Value);
        Assert.Contains("grant_type=refresh_token", handler.Bodies[0]);
        Assert.Contains("client_secret=client-secret", handler.Bodies[0]);
    }

    [Fact]
    public async Task GetAccessTokenAsync_RewritesTheCipherWhenTheRefreshTokenRotates()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """{"access_token":"at-1","refresh_token":"rt-2","expires_in":3600}"""));
        var (service, accounts) = Create(handler);
        var row = Row("rt-1");

        await service.GetAccessTokenAsync(row, Provider(), Kek, CancellationToken.None);

        accounts.Verify(a => a.UpdateCipherAsync(
            row,
            It.Is<byte[]>(c => ConnectedAccountCipher.Decrypt(
                Kek, c, ConnectedAccountCipher.Context(row)).Value == "rt-2"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAccessTokenAsync_LeavesTheCipherAloneWhenNoTokenRotates()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(
            HttpStatusCode.OK, """{"access_token":"at-1","expires_in":3600}"""));
        var (service, accounts) = Create(handler);

        await service.GetAccessTokenAsync(Row("rt-1"), Provider(), Kek, CancellationToken.None);

        accounts.Verify(a => a.UpdateCipherAsync(
            It.IsAny<ConnectedAccount>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAccessTokenAsync_CachesSoASecondCallDoesNotExchange()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(
            HttpStatusCode.OK, """{"access_token":"at-1","expires_in":3600}"""));
        var (service, _) = Create(handler);
        var row = Row("rt-1");

        await service.GetAccessTokenAsync(row, Provider(), Kek, CancellationToken.None);
        var again = await service.GetAccessTokenAsync(row, Provider(), Kek, CancellationToken.None);

        Assert.Equal("at-1", again.Value);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GetAccessTokenAsync_UnderABurst_ExchangesExactlyOnce()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(
            HttpStatusCode.OK, """{"access_token":"at-1","expires_in":3600}"""));
        var (service, _) = Create(handler);
        var row = Row("rt-1");

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            service.GetAccessTokenAsync(row, Provider(), Kek, CancellationToken.None)));

        Assert.All(results, r => Assert.Equal("at-1", r.Value));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task GetAccessTokenAsync_OnInvalidGrant_AnswersCredentialsInvalid()
    {
        var handler = new StubHttpMessageHandler(StubHttpMessageHandler.Json(
            HttpStatusCode.BadRequest, """{"error":"invalid_grant"}"""));
        var (service, _) = Create(handler);

        var result = await service.GetAccessTokenAsync(
            Row("rt-1"), Provider(), Kek, CancellationToken.None);

        Assert.Equal(ConnectedAccountErrors.CredentialsInvalid, result.Error);
    }

    [Fact]
    public async Task GetAccessTokenAsync_OnAServerError_AnswersProviderUnavailable()
    {
        var handler = new StubHttpMessageHandler(
            StubHttpMessageHandler.Json(HttpStatusCode.InternalServerError, "{}"));
        var (service, _) = Create(handler);

        var result = await service.GetAccessTokenAsync(
            Row("rt-1"), Provider(), Kek, CancellationToken.None);

        Assert.Equal(ConnectedAccountErrors.ProviderUnavailable, result.Error);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheCipherDoesNotOpen_AnswersCredentialsInvalid()
    {
        var handler = new StubHttpMessageHandler();
        var (service, _) = Create(handler);
        var otherKek = ConnectedAccountCipher.DeriveKek("other", ConnectedAccountCipher.NewSalt());

        var result = await service.GetAccessTokenAsync(
            Row("rt-1"), Provider(), otherKek, CancellationToken.None);

        Assert.Equal(ConnectedAccountErrors.CredentialsInvalid, result.Error);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task GetAccessTokenAsync_WhenTheClientSecretDoesNotOpen_AnswersProviderUnavailable()
    {
        var handler = new StubHttpMessageHandler();
        var accounts = new Mock<IConnectedAccountStore>();
        var protector = new Mock<IClientSecretProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<byte[]>())).Returns((string?)null);
        var service = new OAuthTokenService(
            new HttpClient(handler), new MemoryCache(new MemoryCacheOptions()),
            accounts.Object, protector.Object, NullLogger<OAuthTokenService>.Instance);

        var result = await service.GetAccessTokenAsync(
            Row("rt-1"), Provider(), Kek, CancellationToken.None);

        Assert.Equal(ConnectedAccountErrors.ProviderUnavailable, result.Error);
        Assert.Equal(0, handler.Calls);
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~OAuthTokenServiceTests"`
Expected: compilation failure — `OAuthTokenService` does not exist.

- [ ] **Step 4: Add the error constant and its status**

In `src/snoopy.microservice/Services/ConnectedAccountErrors.cs`:

```csharp
    /// <summary>The identity provider would not answer, or answered something unusable. Mapped to
    /// 502, like anything else refused by a server we merely talk to.</summary>
    public const string ProviderUnavailable = "oauth_provider_unavailable";
```

In `src/snoopy.microservice/Controllers/ApiBaseController.cs`, add the arm to `ConnectedAccountError` and extend its doc comment to four statuses:

```csharp
        ConnectedAccountErrors.ProviderUnavailable => BadGatewayEnveloppe(resolverError),
```

- [ ] **Step 5: Write the token response record**

Create `src/snoopy.microservice/Models/Mail/OAuthTokenResponse.cs`:

```csharp
using System.Text.Json.Serialization;

namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>The fields of an RFC 6749 token response this application reads. Everything else the
/// provider sends is ignored on purpose.</summary>
public sealed record OAuthTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("expires_in")] int ExpiresInSeconds,
    [property: JsonPropertyName("id_token")] string? IdToken);
```

- [ ] **Step 6: Write the service interface**

Create `src/snoopy.microservice/Services/IOAuthTokenService.cs`:

```csharp
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The only place this application talks to an identity provider's token endpoint.
/// Failures are <see cref="ConnectedAccountErrors.CredentialsInvalid"/> (the user must consent
/// again) or <see cref="ConnectedAccountErrors.ProviderUnavailable"/> (try later).
/// </summary>
public interface IOAuthTokenService
{
    /// <summary>A live access token for this row, from cache when one is still good.</summary>
    Task<Result<string>> GetAccessTokenAsync(
        ConnectedAccount row, OAuthProviderConfig provider, byte[] kek, CancellationToken cancellationToken);

    /// <summary>The authorization-code half of the handshake. No row exists yet.</summary>
    Task<Result<OAuthTokenResponse>> ExchangeCodeAsync(
        OAuthProviderConfig provider, string code, string codeVerifier, string redirectUri,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 7: Write the service**

Create `src/snoopy.microservice/Services/OAuthTokenService.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Caching.Memory;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services;

internal sealed class OAuthTokenService(
    HttpClient http,
    IMemoryCache cache,
    IConnectedAccountStore accounts,
    IClientSecretProtector protector,
    ILogger<OAuthTokenService> logger) : IOAuthTokenService
{
    /// <summary>Refreshed this far before expiry, so a token cannot die inside a long IMAP session.</summary>
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(2);

    /// <summary>One gate per account: a burst of parallel mail requests must exchange once, not
    /// once each. Static because the service is registered as a typed client.</summary>
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    public async Task<Result<string>> GetAccessTokenAsync(
        ConnectedAccount row, OAuthProviderConfig provider, byte[] kek, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(provider);

        var key = CacheKey(row);
        if (cache.TryGetValue<string>(key, out var cached) && cached is not null)
            return Result.Success(cached);

        var gate = Gates.GetOrAdd(row.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(key, out cached) && cached is not null)
                return Result.Success(cached);

            var refreshToken = ConnectedAccountCipher.Decrypt(
                kek, row.Cipher, ConnectedAccountCipher.Context(row));
            if (refreshToken.IsFailure)
                return Result.Failure<string>(ConnectedAccountErrors.CredentialsInvalid);

            var exchanged = await PostAsync(provider, new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken.Value,
                ["scope"] = provider.Scopes
            }, cancellationToken);
            if (exchanged.IsFailure) return Result.Failure<string>(exchanged.Error);

            var token = exchanged.Value;
            if (token.RefreshToken is { Length: > 0 } rotated && rotated != refreshToken.Value)
                await accounts.UpdateCipherAsync(
                    row,
                    ConnectedAccountCipher.Encrypt(kek, rotated, ConnectedAccountCipher.Context(row)),
                    cancellationToken);

            cache.Set(key, token.AccessToken, Lifetime(token.ExpiresInSeconds));
            return Result.Success(token.AccessToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<Result<OAuthTokenResponse>> ExchangeCodeAsync(
        OAuthProviderConfig provider, string code, string codeVerifier, string redirectUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return PostAsync(provider, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri,
            ["scope"] = provider.Scopes
        }, cancellationToken);
    }

    private static string CacheKey(ConnectedAccount row) => $"oauth:{row.UserId:N}:{row.Id:N}";

    /// <summary>Never negative, and never longer than the provider promised.</summary>
    private static TimeSpan Lifetime(int expiresInSeconds)
    {
        var life = TimeSpan.FromSeconds(expiresInSeconds) - Margin;
        return life > TimeSpan.Zero ? life : TimeSpan.FromSeconds(30);
    }

    private async Task<Result<OAuthTokenResponse>> PostAsync(
        OAuthProviderConfig provider, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        if (protector.Unprotect(provider.ClientSecret) is not { Length: > 0 } clientSecret)
        {
            logger.LogError(
                "The OAuth client secret for {TokenUrl} does not open — the key ring was rotated or the row is corrupt",
                provider.TokenUrl);
            return Result.Failure<OAuthTokenResponse>(ConnectedAccountErrors.ProviderUnavailable);
        }

        form["client_id"] = provider.ClientId;
        form["client_secret"] = clientSecret;

        try
        {
            using var response = await http.PostAsync(
                provider.TokenUrl, new FormUrlEncodedContent(form), cancellationToken);

            if (!response.IsSuccessStatusCode)
                return Result.Failure<OAuthTokenResponse>(
                    await DescribeFailureAsync(response, provider, cancellationToken));

            var token = await response.Content.ReadFromJsonAsync<OAuthTokenResponse>(cancellationToken);
            if (token is null || string.IsNullOrEmpty(token.AccessToken))
            {
                logger.LogError("The token endpoint {TokenUrl} answered no access token", provider.TokenUrl);
                return Result.Failure<OAuthTokenResponse>(ConnectedAccountErrors.ProviderUnavailable);
            }

            return Result.Success(token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogError(ex, "Could not reach the token endpoint {TokenUrl}", provider.TokenUrl);
            return Result.Failure<OAuthTokenResponse>(ConnectedAccountErrors.ProviderUnavailable);
        }
    }

    /// <summary>
    /// invalid_grant is the one refusal the user can act on: the consent is gone and only a new
    /// one brings the mailbox back. Everything else is the provider's problem, not theirs.
    /// </summary>
    private async Task<string> DescribeFailureAsync(
        HttpResponseMessage response, OAuthProviderConfig provider, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var invalidGrant = body.Contains("\"invalid_grant\"", StringComparison.Ordinal);

        // The body carries the provider's own error code and nothing of the user's.
        logger.LogWarning(
            "Token endpoint {TokenUrl} refused with {Status}; invalid_grant={InvalidGrant}",
            provider.TokenUrl, (int)response.StatusCode, invalidGrant);

        return invalidGrant
            ? ConnectedAccountErrors.CredentialsInvalid
            : ConnectedAccountErrors.ProviderUnavailable;
    }
}
```

- [ ] **Step 8: Register the typed client**

In `AddMailServices`, beside the `DovecotQuotaClient` registration:

```csharp
        services.AddHttpClient<IOAuthTokenService, OAuthTokenService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
```

`IMemoryCache` is already registered — `SecurityConfiguration.cs:31` calls `AddMemoryCache()`, and `SessionGuard` and `AdminRepository` both take one. Do not register it a second time.

- [ ] **Step 9: Run the tests**

Run: `dotnet test`
Expected: PASS, the nine new tests included.

- [ ] **Step 10: Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice
git commit -F - <<'EOF'
Refresh an OAuth access token, once per burst

Cached to expiry minus a margin; a rotated refresh token rewrites the row's cipher.
EOF
```

---

## Task 5: An OAuth row resolves to an OAuth credential

**Files:**
- Modify: `src/snoopy.microservice/Services/AccountConnectionResolver.cs:88-107`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/AccountConnectionResolverTests.cs` (modify)

**Interfaces:**
- Consumes: `IOAuthTokenService` (Task 4), `OAuthProviderConfig` (Task 3), `OAuthCredential` (Task 1).
- Produces: `AccountConnectionResolver`'s constructor gains an `IOAuthTokenService oauth` parameter — every test constructing it must pass one.

- [ ] **Step 1: Write the failing tests**

Append to `AccountConnectionResolverTests.cs`. Follow the file's existing fixture helpers rather than inventing new ones — it already builds a user, a cookie-carrying `HttpRequest`, a `ConnectedAccount` row and an `ExternalDomain`. Three tests:

```csharp
    [Fact]
    public async Task ResolveAsync_OfAnOAuthAccount_CarriesTheAccessToken()
    {
        // Arrange the existing external-account fixture, then flip the domain and the row to OAuth
        // and stub the token service to answer "at-1".
        // Assert: result.Value.Credential == new OAuthCredential("at-1")
    }

    [Fact]
    public async Task ResolveAsync_WhenTheProviderRefusesTheRefreshToken_Answers409()
    {
        // Token service answers Result.Failure(ConnectedAccountErrors.CredentialsInvalid).
        // Assert: result.Error == ConnectedAccountErrors.CredentialsInvalid
    }

    [Fact]
    public async Task ResolveAsync_WhenTheProviderIsUnreachable_AnswersProviderUnavailable()
    {
        // Token service answers Result.Failure(ConnectedAccountErrors.ProviderUnavailable).
        // Assert: result.Error == ConnectedAccountErrors.ProviderUnavailable
    }
```

Write these out fully against the file's own helpers — the three comment blocks above are the assertions to reach, not the code to commit. A fourth is worth adding: an OAuth **domain** whose columns are incomplete must answer `account_not_found` without ever calling the token service (`Verify(..., Times.Never)`).

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~AccountConnectionResolverTests"`
Expected: compilation failure — the resolver takes no `IOAuthTokenService`.

- [ ] **Step 3: Wire the resolver**

In `src/snoopy.microservice/Services/AccountConnectionResolver.cs`, add `IOAuthTokenService oauth` to the primary constructor and replace `ExternalConnection`:

```csharp
    private async Task<Result<MailAccountConnection>> ExternalConnection(
        Data.Preferences.ConnectedAccount row, string secret, CancellationToken cancellationToken)
```

becomes a method that receives the KEK rather than the decrypted secret, because an OAuth row's secret is a refresh token the token service decrypts itself. The call site at line 58 changes from

```csharp
        return await ExternalConnection(row, secret.Value, cancellationToken);
```

to

```csharp
        return await ExternalConnection(row, secret.Value, kek, cancellationToken);
```

and the method becomes:

```csharp
    private async Task<Result<MailAccountConnection>> ExternalConnection(
        Data.Preferences.ConnectedAccount row, string secret, byte[] kek,
        CancellationToken cancellationToken)
    {
        var domain = await domains.FindAsync(row.DomainId!.Value, cancellationToken);
        if (domain is null)
            return Result.Failure<MailAccountConnection>(ConnectedAccountErrors.AccountNotFound);

        var credential = await CredentialFor(row, domain, secret, kek, cancellationToken);
        if (credential.IsFailure) return Result.Failure<MailAccountConnection>(credential.Error);

        if (!MailConnectionBuilder.TryExternal(
                domain, row.Id.ToString(), row.Email, credential.Value, out var connection,
                options.CurrentValue.AllowCleartext))
        {
            logger.LogError(
                "External domain {DomainName} ({DomainId}) holds an unusable security or OAuth value",
                domain.Name, domain.Id);
            return Result.Failure<MailAccountConnection>(ConnectedAccountErrors.AccountNotFound);
        }

        return connection;
    }

    /// <summary>
    /// The stored secret is a password on a password row and a refresh token on an OAuth one, so
    /// the row's own mode decides — never the domain's, which an admin may have flipped since.
    /// </summary>
    private async Task<Result<MailCredential>> CredentialFor(
        Data.Preferences.ConnectedAccount row, ExternalDomain domain, string secret, byte[] kek,
        CancellationToken cancellationToken)
    {
        if (row.AuthMode is not MailAuthMode.OAuth2)
            return Result.Success<MailCredential>(new PasswordCredential(secret));

        if (!OAuthProviderConfig.TryFrom(domain, out var provider))
        {
            logger.LogError(
                "External domain {DomainName} ({DomainId}) is OAuth but incompletely configured",
                domain.Name, domain.Id);
            return Result.Failure<MailCredential>(ConnectedAccountErrors.AccountNotFound);
        }

        var token = await oauth.GetAccessTokenAsync(row, provider, kek, cancellationToken);
        return token.IsSuccess
            ? Result.Success<MailCredential>(new OAuthCredential(token.Value))
            : Result.Failure<MailCredential>(token.Error);
    }
```

Note the decrypt at line 50 still runs for an OAuth row — it is what proves the cipher opens under the session key, and its `bound` flag still drives `BindCipherAsync`. The token service decrypts a second time; that is one AES-GCM open on a cache miss, and it keeps the token service usable without the resolver.

- [ ] **Step 4: Run the tests**

Run: `dotnet test`
Expected: PASS. Every existing resolver test must stay green — the password path is untouched.

- [ ] **Step 5: Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice
git commit -F - <<'EOF'
Resolve an OAuth connected account to a live access token

The row's own mode decides, never the domain's, which an admin may have flipped.
EOF
```

---

# Packet 3 — The consent

Two tasks, two commits. At the end of this packet the backend is complete: a mailbox can be
attached, and re-attached, by consenting at the provider. Nothing offers it on screen yet.

## Task 6: The pending handshake

**Files:**
- Create: `src/snoopy.microservice/Services/OAuthHandshake.cs`
- Create: `src/snoopy.microservice/Services/IOAuthHandshakeStore.cs`
- Create: `src/snoopy.microservice/Services/OAuthHandshakeStore.cs`
- Modify: `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/OAuthHandshakeStoreTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `public sealed record OAuthHandshake(string State, Guid UserId, Guid DomainId, Guid? AccountId, string CodeVerifier, string CodeChallenge, OAuthTokenResponse? Tokens, string? Email)`
  - `IOAuthHandshakeStore.Start(Guid userId, Guid domainId, Guid? accountId)` → `OAuthHandshake`
  - `IOAuthHandshakeStore.Find(string state)` → `OAuthHandshake?`
  - `IOAuthHandshakeStore.Attach(string state, OAuthTokenResponse tokens, string email)` → `bool`
  - `IOAuthHandshakeStore.Consume(string state, Guid userId)` → `OAuthHandshake?`

- [ ] **Step 1: Write the failing tests**

Create `src/snoopy.microservice/snoopy.microservice.Tests/Services/OAuthHandshakeStoreTests.cs`:

```csharp
using Microsoft.Extensions.Caching.Memory;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class OAuthHandshakeStoreTests
{
    private static OAuthHandshakeStore Create() => new(new MemoryCache(new MemoryCacheOptions()));

    private static OAuthTokenResponse Tokens() => new("at", "rt", 3600, null);

    [Fact]
    public void Start_MintsAnUnguessableStateAndAPkcePair()
    {
        var store = Create();
        var user = Guid.NewGuid();

        var first = store.Start(user, Guid.NewGuid(), null);
        var second = store.Start(user, Guid.NewGuid(), null);

        Assert.NotEqual(first.State, second.State);
        Assert.True(first.State.Length >= 22);
        Assert.NotEqual(first.CodeVerifier, first.CodeChallenge);
        Assert.DoesNotContain('=', first.CodeChallenge);
    }

    [Fact]
    public void Consume_AfterAttach_AnswersTheTokens()
    {
        var store = Create();
        var user = Guid.NewGuid();
        var started = store.Start(user, Guid.NewGuid(), null);

        Assert.True(store.Attach(started.State, Tokens(), "alice@outlook.test"));
        var consumed = store.Consume(started.State, user);

        Assert.Equal("rt", consumed!.Tokens!.RefreshToken);
        Assert.Equal("alice@outlook.test", consumed.Email);
    }

    [Fact]
    public void Consume_IsSingleUse()
    {
        var store = Create();
        var user = Guid.NewGuid();
        var started = store.Start(user, Guid.NewGuid(), null);
        store.Attach(started.State, Tokens(), "alice@outlook.test");

        Assert.NotNull(store.Consume(started.State, user));
        Assert.Null(store.Consume(started.State, user));
    }

    [Fact]
    public void Consume_ByAnotherUser_AnswersNullAndDoesNotBurnTheEntry()
    {
        var store = Create();
        var user = Guid.NewGuid();
        var started = store.Start(user, Guid.NewGuid(), null);
        store.Attach(started.State, Tokens(), "alice@outlook.test");

        Assert.Null(store.Consume(started.State, Guid.NewGuid()));
        Assert.NotNull(store.Consume(started.State, user));
    }

    [Fact]
    public void Attach_ToAnUnknownState_AnswersFalse()
    {
        Assert.False(Create().Attach("nope", Tokens(), "alice@outlook.test"));
    }

    [Fact]
    public void Find_OfAnUnknownState_AnswersNull()
    {
        Assert.Null(Create().Find("nope"));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~OAuthHandshakeStoreTests"`
Expected: compilation failure.

- [ ] **Step 3: Write the record**

Create `src/snoopy.microservice/Services/OAuthHandshake.cs`:

```csharp
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// One consent in flight. It exists because the provider's redirect carries no cookie —
/// SameSite=Strict — so the request that brings the code back can neither identify the user nor
/// derive their key; this is what carries both across the three steps.
/// </summary>
/// <param name="AccountId">Set when re-authenticating an existing row rather than attaching one.</param>
public sealed record OAuthHandshake(
    string State,
    Guid UserId,
    Guid DomainId,
    Guid? AccountId,
    string CodeVerifier,
    string CodeChallenge,
    OAuthTokenResponse? Tokens,
    string? Email);
```

- [ ] **Step 4: Write the interface**

Create `src/snoopy.microservice/Services/IOAuthHandshakeStore.cs`:

```csharp
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The consents in flight. Process-local and never persisted: the tokens it briefly holds have no
/// rest to be encrypted at, and a restart mid-handshake costs one restarted consent.
/// </summary>
public interface IOAuthHandshakeStore
{
    OAuthHandshake Start(Guid userId, Guid domainId, Guid? accountId);

    OAuthHandshake? Find(string state);

    /// <summary>False when the state is unknown or expired.</summary>
    bool Attach(string state, OAuthTokenResponse tokens, string email);

    /// <summary>
    /// Removes and answers the handshake, but only for the user who started it — the check
    /// without which one user could complete another's consent. A mismatch leaves the entry.
    /// </summary>
    OAuthHandshake? Consume(string state, Guid userId);
}
```

- [ ] **Step 5: Write the store**

Create `src/snoopy.microservice/Services/OAuthHandshakeStore.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

internal sealed class OAuthHandshakeStore(IMemoryCache cache) : IOAuthHandshakeStore
{
    /// <summary>Long enough for a real sign-in with a second factor, short enough that an
    /// abandoned consent is not a live entry an hour later.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public OAuthHandshake Start(Guid userId, Guid domainId, Guid? accountId)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var handshake = new OAuthHandshake(
            State: Base64Url(RandomNumberGenerator.GetBytes(16)),
            UserId: userId,
            DomainId: domainId,
            AccountId: accountId,
            CodeVerifier: verifier,
            CodeChallenge: Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            Tokens: null,
            Email: null);

        cache.Set(Key(handshake.State), handshake, Lifetime);
        return handshake;
    }

    public OAuthHandshake? Find(string state) =>
        string.IsNullOrEmpty(state) ? null : cache.Get<OAuthHandshake>(Key(state));

    public bool Attach(string state, OAuthTokenResponse tokens, string email)
    {
        if (Find(state) is not { } handshake) return false;

        cache.Set(Key(state), handshake with { Tokens = tokens, Email = email }, Lifetime);
        return true;
    }

    public OAuthHandshake? Consume(string state, Guid userId)
    {
        if (Find(state) is not { } handshake || handshake.UserId != userId) return null;

        cache.Remove(Key(state));
        return handshake;
    }

    private static string Key(string state) => $"oauth-handshake:{state}";

    /// <summary>RFC 7636 requires base64url without padding for the challenge.</summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
```

- [ ] **Step 6: Register it**

In `AddMailServices`: `services.AddSingleton<IOAuthHandshakeStore, OAuthHandshakeStore>();` — singleton, because a scoped store would forget every handshake at the end of the request that started it.

- [ ] **Step 7: Run the tests**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice
git commit -F - <<'EOF'
Hold a consent in flight across the cookieless callback

State plus PKCE, single-use, ten minutes, and only its own user may complete it.
EOF
```

---

## Task 7: The three endpoints

**Files:**
- Create: `src/snoopy.microservice/Models/ConnectedAccounts/OAuthStartRequest.cs`
- Create: `src/snoopy.microservice/Models/ConnectedAccounts/OAuthStartResponse.cs`
- Create: `src/snoopy.microservice/Models/ConnectedAccounts/OAuthCompleteRequest.cs`
- Modify: `src/snoopy.microservice/Controllers/ConnectedAccountsController.cs`
- Modify: `src/snoopy.microservice/Models/Mail/MailOptions.cs`
- Modify: `src/snoopy.microservice/Models/ConnectedAccountModels.cs:11,36` (`ConnectedAccountResponse` and `ExternalDomainChoice`; the file holds several records against the one-type-per-file rule — that is pre-existing, leave it alone)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/ConnectedAccountsOAuthTests.cs` (create)

**Interfaces:**
- Consumes: `IOAuthHandshakeStore` (Task 6), `IOAuthTokenService` (Task 4), `OAuthProviderConfig` (Task 3), `MailAuthMode` (Task 2).
- Produces:
  - `POST /api/ConnectedAccounts/OAuth/Start` — body `OAuthStartRequest(Guid? DomainId, Guid? AccountId)`, answers `OAuthStartResponse(string AuthorizationUrl, string State)`
  - `GET /api/ConnectedAccounts/OAuth/Callback?code=&state=&error=` — `[AllowAnonymous]`, answers 302
  - `POST /api/ConnectedAccounts/OAuth/Complete` — body `OAuthCompleteRequest(string State)`, answers `ConnectedAccountResponse`
  - `ConnectedAccountResponse` gains `MailAuthMode AuthMode`; `ExternalDomainChoice` gains `MailAuthMode AuthMode`
  - `MailOptions.WebmailBaseUrl` and `MailOptions.OAuthRedirectUri` — two new settings

- [ ] **Step 1: Add the two settings**

`MailOptions` gains:

```csharp
    /// <summary>Where the callback sends the browser back to, e.g. https://account.mail.weesky.net.
    /// The settings page's path is appended by the controller.</summary>
    public string WebmailBaseUrl { get; set; } = string.Empty;

    /// <summary>The redirect URI registered with every provider. Must match byte for byte, which
    /// is why it is configured rather than rebuilt from the incoming request.</summary>
    public string OAuthRedirectUri { get; set; } = string.Empty;
```

Add both to `appsettings.json` and `appsettings.Development.json` under `"Mail"`, empty in the committed files — an operator fills them in, and `docs/superpowers/mail-oauth-provider-prerequisite.md` (Task 3) gains a line saying so.

- [ ] **Step 2: Write the DTOs**

`OAuthStartRequest.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.ConnectedAccounts;

/// <summary>Exactly one of the two: a domain to attach a new mailbox from, or an account to
/// re-authenticate.</summary>
public sealed record OAuthStartRequest(Guid? DomainId, Guid? AccountId);
```

`OAuthStartResponse.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.ConnectedAccounts;

/// <summary>The URL the client navigates to, and the handle the callback will hand back.</summary>
public sealed record OAuthStartResponse(string AuthorizationUrl, string State);
```

`OAuthCompleteRequest.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.ConnectedAccounts;

public sealed record OAuthCompleteRequest(string State);
```

- [ ] **Step 3: Write the failing tests**

Create `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/ConnectedAccountsOAuthTests.cs`. Build the controller with the existing `ConnectedAccountsControllerTests` fixture helpers (`ControllerTestHelpers.CreateAuthenticatedContext`, the mocked stores, the credentials-cookie stub) plus mocks for `IOAuthHandshakeStore` and `IOAuthTokenService`. Assert:

1. `Start` with a `DomainId` in `Password` mode → `BadRequestObjectResult`.
2. `Start` with an unknown `DomainId` → `BadRequestObjectResult` carrying the `UnknownDomain` message.
3. `Start` with neither id, and with both → `BadRequestObjectResult`.
4. `Start` with an `AccountId` the caller does not own → `NotFoundObjectResult`.
5. `Start` on a complete OAuth domain → `OkObjectResult` whose `AuthorizationUrl` contains `client_id=`, `code_challenge=`, `code_challenge_method=S256`, `state=`, `response_type=code`, `access_type=offline`, and the URL-encoded scopes and redirect URI.
6. `Callback` with an unknown `state` → `RedirectResult` to the settings page carrying an error parameter, and `ExchangeCodeAsync` never called.
7. `Callback` with `error=access_denied` → `RedirectResult` with an error parameter, and no exchange.
8. `Callback` on a good state → `ExchangeCodeAsync` called once, `Attach` called once, `RedirectResult` carrying the state.
9. `Complete` with an unknown or foreign state → `NotFoundObjectResult` (`Consume` answered null).
10. `Complete` on a handshake the callback never filled → `BadRequestObjectResult`.
11. `Complete` on a good handshake → `OkObjectResult`; `CreateAsync` received a row with `AuthMode == MailAuthMode.OAuth2` and a cipher that decrypts to the refresh token under the session KEK and the OAuth context.
12. `Complete` where the handshake carries an `AccountId` and the provider reported a different address → `BadRequestObjectResult`, and no cipher written.
13. `Complete` where the provider reported the caller's own primary address on the home server → `BadRequestObjectResult`.
14. `List` answers `AuthMode` on every row, and `Domains` answers `AuthMode` on every choice.

Use `Assert.IsType<BadRequestObjectResult>` and `Assert.IsType<NotFoundObjectResult>` — `Assert.IsType<T>` checks the **exact** runtime type, and the helpers on `ApiBaseController` return the framework's concrete results, not plain `ObjectResult`.

- [ ] **Step 4: Run to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ConnectedAccountsOAuthTests"`
Expected: compilation failure — the three actions do not exist.

- [ ] **Step 5: Widen the two response records**

`ConnectedAccountResponse` gains `MailAuthMode AuthMode` and `ExternalDomainChoice` gains `MailAuthMode AuthMode`. Update `Describe` (`ConnectedAccountsController.cs:256`) to pass `row.AuthMode`, and the `Domains` projection (line 253) to pass `d.AuthMode`.

- [ ] **Step 6: Write `Start`**

Add to `ConnectedAccountsController`, keeping the class's existing helper style:

```csharp
    /// <summary>
    /// Begins a consent. Answers the URL to navigate to; nothing is written until Complete.
    /// </summary>
    /// <param name="request">the domain to attach from, or the account to re-authenticate</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The authorization URL and its state</response>
    /// <response code="400">Not exactly one of domainId/accountId, or a domain that is not OAuth</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such account</response>
    /// <response code="429">Too many authentication attempts</response>
    [HttpPost("OAuth/Start")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<OAuthStartResponse>> OAuthStart(
        OAuthStartRequest request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequestEnveloppe("Request body is required");
        if (request.DomainId is null == request.AccountId is null)
            return BadRequestEnveloppe("Name either a domain to connect from or an account to reconnect");

        var domainId = request.DomainId;
        if (request.AccountId is { } accountId)
        {
            var row = await accounts.FindAsync(AuthenticatedUser.WebmailUid, accountId, cancellationToken);
            if (row?.DomainId is null) return NotFoundEnveloppe(ConnectedAccountErrors.AccountNotFound);
            domainId = row.DomainId;
        }

        var domain = await domains.FindAsync(domainId!.Value, cancellationToken);
        if (domain is null) return BadRequestEnveloppe(UnknownDomain);
        if (!OAuthProviderConfig.TryFrom(domain, out var provider))
            return BadRequestEnveloppe("This server does not sign in with a provider account");

        var handshake = handshakes.Start(AuthenticatedUser.WebmailUid, domain.Id, request.AccountId);
        return Ok(new OAuthStartResponse(AuthorizationUrl(provider, handshake), handshake.State));
    }

    /// <summary>
    /// access_type and prompt=consent are Google's way of guaranteeing a refresh token on a
    /// repeated consent; Microsoft ignores both, and offline_access in the scopes is what earns
    /// it there. Sending both keeps one provider-neutral URL builder.
    /// </summary>
    private static string AuthorizationUrl(OAuthProviderConfig provider, OAuthHandshake handshake) =>
        QueryHelpers.AddQueryString(provider.AuthorizationUrl, new Dictionary<string, string?>
        {
            ["client_id"] = provider.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = RedirectUri,
            ["scope"] = provider.Scopes,
            ["state"] = handshake.State,
            ["code_challenge"] = handshake.CodeChallenge,
            ["code_challenge_method"] = "S256",
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        });
```

`RedirectUri` reads `options.CurrentValue.OAuthRedirectUri`; make it a private property on the controller so the three actions cannot spell it differently. Add `using Microsoft.AspNetCore.WebUtilities;` for `QueryHelpers`, `using Microsoft.AspNetCore.Authorization;` is already there.

Inject `IOAuthHandshakeStore handshakes` and `IOAuthTokenService oauth` into the primary constructor.

- [ ] **Step 7: Write `Callback`**

```csharp
    /// <summary>
    /// Where the provider sends the browser back. Anonymous by necessity: this is a cross-site
    /// top-level navigation and both session cookies are SameSite=Strict, so nothing here can
    /// identify the caller. It therefore writes nothing — it exchanges the code and parks the
    /// result for the same-site Complete call that follows.
    /// </summary>
    /// <response code="302">Back to the settings page, carrying the state or an error</response>
    [HttpGet("OAuth/Callback")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<ActionResult> OAuthCallback(
        [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return BackToSettings(state: null);

        if (handshakes.Find(state) is not { } handshake) return BackToSettings(state: null);

        var domain = await domains.FindAsync(handshake.DomainId, cancellationToken);
        if (domain is null || !OAuthProviderConfig.TryFrom(domain, out var provider))
            return BackToSettings(state: null);

        var exchanged = await oauth.ExchangeCodeAsync(
            provider, code, handshake.CodeVerifier, RedirectUri, cancellationToken);
        if (exchanged.IsFailure) return BackToSettings(state: null);

        if (MailboxFrom(exchanged.Value.IdToken) is not { } email) return BackToSettings(state: null);

        // Audited like the password probe: this is the other way a mailbox becomes attached.
        logger.LogInformation(
            "Audit: oauth_callback domain={DomainName} target={Target} outcome=success",
            domain.Name, email);

        return handshakes.Attach(state, exchanged.Value, email)
            ? BackToSettings(state)
            : BackToSettings(state: null);
    }

    /// <summary>A null state is the generic failure: the page says the sign-in did not complete
    /// and offers to start again. Naming the cause would describe another user's session.</summary>
    private ActionResult BackToSettings(string? state)
    {
        var url = $"{options.CurrentValue.WebmailBaseUrl.TrimEnd('/')}/settings/connected-accounts";
        return Redirect(QueryHelpers.AddQueryString(url,
            state is null ? "oauthError" : "oauthState", state ?? "1"));
    }

    /// <summary>
    /// The mailbox the user actually signed in to, read from the id_token's email claim.
    /// The signature is not validated: the token came back over TLS on a direct call to the token
    /// endpoint, which OpenID Connect accepts as sufficient for a confidential client.
    /// </summary>
    private static string? MailboxFrom(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken)) return null;

        try
        {
            var claims = new JsonWebTokenHandler().ReadJsonWebToken(idToken);
            var email = claims.GetClaim("email")?.Value
                        ?? claims.GetClaim("preferred_username")?.Value;
            return MailboxAddress.TryParse(RecipientAddressParser.Options, email ?? string.Empty, out var parsed)
                ? IdentityResolver.Canonical(parsed.Address)
                : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
```

Add `using Microsoft.IdentityModel.JsonWebTokens;`. `ReadJsonWebToken` throws `ArgumentException` on a malformed token; confirm the exact type by running the malformed-token test and widen the catch if the runtime disagrees — never to a bare `catch`.

- [ ] **Step 8: Write `Complete`**

```csharp
    /// <summary>
    /// Finishes a consent. Same-site, so the credentials cookie travels and the refresh token can
    /// be encrypted under the session key — which is the whole reason this is a second call.
    /// </summary>
    /// <response code="200">The connected account</response>
    /// <response code="400">The handshake never completed, the mailbox is already connected, or it
    /// is not the mailbox this reconnection was started for</response>
    /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
    /// <response code="404">No such handshake</response>
    [HttpPost("OAuth/Complete")]
    [EnableRateLimiting("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConnectedAccountResponse>> OAuthComplete(
        OAuthCompleteRequest request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequestEnveloppe("Request body is required");

        var kek = await ResolveKekAsync(cancellationToken);
        if (kek.IsFailure) return UnauthorizedEnveloppe(kek.Error);

        if (handshakes.Consume(request.State ?? string.Empty, AuthenticatedUser.WebmailUid)
            is not { } handshake)
            return NotFoundEnveloppe(ConnectedAccountErrors.AccountNotFound);

        if (handshake.Tokens?.RefreshToken is not { Length: > 0 } refreshToken
            || handshake.Email is not { Length: > 0 } email)
            return BadRequestEnveloppe("The sign-in did not complete. Try again.");

        if (Encoding.UTF8.GetByteCount(refreshToken) > ConnectedAccountCipher.MaxSecretLength)
            return BadRequestEnveloppe("This provider's token is too large to store");

        var domain = await domains.FindAsync(handshake.DomainId, cancellationToken);
        if (domain is null) return BadRequestEnveloppe(UnknownDomain);

        return handshake.AccountId is { } accountId
            ? await ReconnectAsync(accountId, email, refreshToken, kek.Value, cancellationToken)
            : await AttachAsync(handshake.DomainId, domain, email, refreshToken, kek.Value, cancellationToken);
    }

    private async Task<ActionResult<ConnectedAccountResponse>> AttachAsync(
        Guid domainId, ExternalDomain domain, string email, string refreshToken, byte[] kek,
        CancellationToken cancellationToken)
    {
        var row = new ConnectedAccount
        {
            Id = Guid.NewGuid(),
            UserId = AuthenticatedUser.WebmailUid,
            DomainId = domainId,
            Email = email,
            AuthMode = MailAuthMode.OAuth2
        };
        row.Cipher = ConnectedAccountCipher.Encrypt(
            kek, refreshToken, ConnectedAccountCipher.Context(row));

        var created = await accounts.CreateAsync(row, cancellationToken);
        return created.IsFailure
            ? BadRequestEnveloppe(created.Error)
            : Ok(Describe(created.Value, domain, string.Empty, credentialsValid: true));
    }

    /// <summary>
    /// The cipher context is bound to the address, so a token for another mailbox would encrypt
    /// under a context this row can never reproduce: it would open once and never again.
    /// </summary>
    private async Task<ActionResult<ConnectedAccountResponse>> ReconnectAsync(
        Guid accountId, string email, string refreshToken, byte[] kek, CancellationToken cancellationToken)
    {
        var row = await accounts.FindAsync(AuthenticatedUser.WebmailUid, accountId, cancellationToken);
        if (row is null) return NotFoundEnveloppe(ConnectedAccountErrors.AccountNotFound);

        if (!string.Equals(row.Email, email, StringComparison.Ordinal))
            return BadRequestEnveloppe($"You signed in as {email}, but this account is {row.Email}");

        await accounts.UpdateCipherAsync(
            row,
            ConnectedAccountCipher.Encrypt(kek, refreshToken, ConnectedAccountCipher.Context(row)),
            cancellationToken);

        var domain = row.DomainId is { } id ? await domains.FindAsync(id, cancellationToken) : null;
        return Ok(Describe(row, domain, string.Empty, credentialsValid: true));
    }
```

`AttachAsync` inherits the duplicate check from `ConnectedAccountStore.CreateAsync`, which already answers `AlreadyConnected`. The "already signed in to this mailbox" rule for the caller's own primary address applies to home-server rows only, and an OAuth row always names an external domain — so it cannot be reached here; do not add a check that no input can trigger.

- [ ] **Step 9: Run the tests**

Run: `dotnet test`
Expected: PASS. `MailRouteSurfaceTests` pins the mail controllers' route surface, not this one — if it goes red, a route was added to the wrong controller.

- [ ] **Step 10: Commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice
git commit -F - <<'EOF'
Attach a mailbox by consent instead of by password

Start, an anonymous callback that writes nothing, and a same-site Complete that encrypts.
EOF
```

---

# Packet 4 — The screen

One task, one commit.

## Task 8: The frontend

**Files:**
- Modify: `src/frontend/src/api.js:342-355`
- Modify: `src/frontend/src/modules/settings/accounts/useConnectedAccounts.ts`
- Modify: `src/frontend/src/modules/settings/accounts/ConnectAccountForm.tsx`
- Modify: `src/frontend/src/modules/settings/accounts/ConnectedAccountsPage.tsx`
- Test: `src/frontend/src/modules/settings/accounts/ConnectedAccountsPage.test.tsx` (modify)
- Test: `src/frontend/src/api.test.js` (modify)

**Interfaces:**
- Consumes: the three endpoints and the two widened response records (Task 7).
- Produces: `api.startOAuthConnect(body)`, `api.completeOAuthConnect(state)`; `ConnectedAccount.authMode` and `ConnectableDomain.authMode` of type `'Password' | 'OAuth2'`.

- [ ] **Step 1: Write the failing tests**

In `src/frontend/src/api.test.js`, beside the existing connected-account tests, assert that `startOAuthConnect({ domainId: 'd1' })` POSTs to `/api/ConnectedAccounts/OAuth/Start` with that body, and `completeOAuthConnect('s1')` POSTs `{ state: 's1' }` to `/api/ConnectedAccounts/OAuth/Complete`.

In `ConnectedAccountsPage.test.tsx`, add four tests:

1. Choosing an `OAuth2` domain in the connect form replaces the Email and Password fields with a single button named `Sign in with Outlook`, and the Connect button is gone.
2. Clicking that button calls `api.startOAuthConnect` and assigns the returned `authorizationUrl` to the location (stub the navigation — see step 4).
3. An account with `credentialsValid: false` and `authMode: 'OAuth2'` shows a **Reconnect** button, not the key icon that opens the password dialog; an account with `authMode: 'Password'` still shows the key icon.
4. Mounting the page with `?oauthState=s1` in the URL calls `api.completeOAuthConnect('s1')` and toasts the connected address; mounting with `?oauthError=1` shows an error and calls nothing.

- [ ] **Step 2: Run to verify they fail**

Run: `npm test -- src/modules/settings/accounts src/api.test.js`
Expected: FAIL — the API methods and the UI branches do not exist.

- [ ] **Step 3: Add the API methods**

In `src/frontend/src/api.js`, beside the other connected-account methods:

```javascript
  startOAuthConnect: ({ domainId = null, accountId = null }) =>
    request('POST', '/api/ConnectedAccounts/OAuth/Start', { domainId, accountId }),

  completeOAuthConnect: (state) =>
    request('POST', '/api/ConnectedAccounts/OAuth/Complete', { state }),
```

- [ ] **Step 4: Add the types and hooks**

In `useConnectedAccounts.ts`:

```typescript
export type MailAuthMode = 'Password' | 'OAuth2'
```

Add `authMode: MailAuthMode` to both `ConnectedAccount` and `ConnectableDomain`. Add two mutations following the file's existing shape, both `onSettled: refreshList(client)`:

```typescript
export function useStartOAuthConnect() {
  return useMutation({
    mutationFn: (target: { domainId?: string; accountId?: string }) => api.startOAuthConnect(target),
  })
}

export function useCompleteOAuthConnect() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: (state: string) => api.completeOAuthConnect(state),
    onSettled: refreshList(client),
  })
}
```

`useStartOAuthConnect` deliberately does **not** invalidate: nothing has changed yet, and the page is about to be replaced by the provider's.

Add one navigation seam so the tests are not fighting jsdom, which refuses a real assignment to `location.href`:

```typescript
/** The one place the browser leaves for the provider — stubbed in tests. */
export const leaveTo = (url: string) => { window.location.assign(url) }
```

- [ ] **Step 5: Branch the connect form**

In `ConnectAccountForm.tsx`, derive the selected domain and its mode:

```typescript
  const selected = (domains ?? []).find(d => d.id === domainId)
  const isOAuth = selected?.authMode === 'OAuth2'
```

Keep the Server select unconditionally. Render the Email and Password fields and the Connect button only when `!isOAuth`; when `isOAuth`, render instead a single primary button labelled `Sign in with {selected.name}` whose handler is:

```typescript
  async function startOAuth() {
    setError(null)
    try {
      const { authorizationUrl } = await startConnect.mutateAsync({ domainId })
      leaveTo(authorizationUrl)
    } catch (failure) {
      setError(errorText(failure))
    }
  }
```

Replace the note under the form for the OAuth branch: `Weesky never sees your password — you sign in at {name} and grant access.` A `<form>` with one button still needs its `onSubmit` wired to `startOAuth` so Enter works, matching the dialog conventions in `src/frontend/CLAUDE.md`.

- [ ] **Step 6: Branch the page**

In `ConnectedAccountsPage.tsx`:

Replace the hard-coded warning text so it reads the mode:

```tsx
                  {!account.credentialsValid && (
                    <span className="connected-account-warn">
                      {account.authMode === 'OAuth2'
                        ? 'This mailbox needs to be reconnected.'
                        : 'Your main password changed — enter this account’s password again.'}
                    </span>
                  )}
```

Replace the repair button with the branch — the key icon for a password account, a `Reconnect` button for an OAuth one calling `startConnect.mutateAsync({ accountId: account.id })` then `leaveTo(...)`.

Handle the return from the provider with one effect, which must run once and strip its parameter so a refresh does not replay a consumed state:

```tsx
  const [params, setParams] = useSearchParams()

  useEffect(() => {
    const state = params.get('oauthState')
    const failed = params.get('oauthError')
    if (!state && !failed) return

    setParams(new URLSearchParams(), { replace: true })
    if (failed) { addToast('The sign-in did not complete. Try again.', 'error'); return }

    complete.mutateAsync(state!)
      .then(account => addToast(`${account.email} is connected`))
      .catch(failure => addToast(errorText(failure), 'error'))
    // The parameter is stripped above, so this must not re-run on params changing.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])
```

Import `useSearchParams` from `react-router-dom` and `useEffect` from `react`.

- [ ] **Step 7: Run the frontend checks**

Run: `npm test -- src/modules/settings/accounts src/api.test.js`
Expected: PASS.

Run: `npm run lint && npm run typecheck`
Expected: clean. A `ConnectedAccount` fixture missing `authMode` anywhere in the suite is a typecheck error — fix the fixtures, do not widen the type.

- [ ] **Step 8: Run the whole frontend suite**

Run: `npm test`
Expected: PASS. `AuthContext` builds its account list from `GET /api/ConnectedAccounts`; if a test there constructs a `ConnectedAccount`, it needs the new field too.

- [ ] **Step 9: Commit**

```bash
git add src/frontend
git commit -F - <<'EOF'
Offer a provider sign-in where the password field does not fit

The form and the repair action branch on the account's auth mode.
EOF
```

---

## Self-review notes

Checked against the spec, section by section:

- *The credential* → Task 1. *The provider record* → Task 3. *The account record* → Task 2. *The consent flow* and *the pending handshake* → Tasks 6 and 7. *Which address the account carries* → Task 7 step 7. *Keeping the token fresh* → Task 4. *Errors* → Task 4 step 4 plus Task 5. *ManageSieve* → Task 1 step 8. *The frontend* → Task 8. *Testing* → each task's own steps. *What an operator must do* → Task 3 step 9.
- The one spec item with no code task is the **`ALTER TABLE` pair**, which is deliberate: database creation is manual in this project (`docs/superpowers/mail-2a5-database-prerequisite.md`), so the DDL ships as documentation in Task 3 and an operator applies it. **The backend will not start correctly against a database missing these columns** — apply the DDL to the dev database before running Task 2's tests against anything but the in-memory provider.
- Type consistency: `MailCredential`/`PasswordCredential`/`OAuthCredential` (Task 1), `MailAuthMode` (Task 2), `OAuthProviderConfig.TryFrom` (Task 3), `IOAuthTokenService.GetAccessTokenAsync`/`ExchangeCodeAsync` (Task 4), `IOAuthHandshakeStore.Start`/`Find`/`Attach`/`Consume` (Task 6) are spelled identically wherever they are consumed downstream.
- Task 5's test step describes its three tests rather than spelling them out, because they must be written against `AccountConnectionResolverTests`' own fixture helpers, which the implementer will have in front of them. Every assertion is named. Task 7's step 3 is the same shape and for the same reason — fourteen numbered assertions against an existing 600-line fixture.
