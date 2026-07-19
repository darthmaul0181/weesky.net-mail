# Webmail Mail 2a — Folders and Reading — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a readable webmail — IMAP connection with per-session user credentials, a full folder tree with create/rename/delete and subscription visibility, a paginated message list, and a reading pane with sanitised HTML and attachment download — inside the shell built in sub-project 1.

**Architecture:** The backend alone speaks IMAP; the frontend speaks REST/JSON to it. Everything server-specific (hierarchy separator, namespace, special-use folders, capabilities) is discovered at runtime through MailKit, never configured — because slice 2d points at arbitrary external servers. The user's own password authenticates to IMAP, captured at login and carried in a Data-Protection-encrypted cookie. One IMAP connection per request, no pooling. The frontend adds TanStack Query as its first real data layer and renders message HTML inside a sandboxed iframe.

**Tech Stack:** .NET 10 / ASP.NET Core, MailKit + MimeKit, HtmlSanitizer, CSharpFunctionalExtensions `Result<T>`, EF Core (untouched here); React 18 + TypeScript, react-router-dom 6, TanStack Query 5, Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-07-18-webmail-mail-2a-design.md` — read § 5 and § 6 before starting any backend or frontend task respectively.

## Global Constraints

- **Backend commands run from `src/snoopy.microservice`; frontend commands from `src/frontend`.** Never mix.
- Backend: `dotnet test` (never `--no-build` when test files were added), `dotnet build`.
- Frontend: `npm run lint`, `npm run typecheck`, `npm run test`, `npm run build`. Baseline at plan start: **304 frontend tests**, all green.
- **New frontend code is TypeScript strict.** Existing `.jsx` is not converted.
- **UI copy is English.** The spec is French; the product is not.
- **Token rule: a token names a role, never a color.** No hard-coded colour in any component. A new token is declared in **both** palette files × **both** modes — 4 blocks — in `src/styles/theme-night.css` and `src/styles/theme-classic.css`.
- **No server configuration is assumed.** Hierarchy separator, namespace prefix, special-use folders and capabilities are read from the live connection. Only host/port/security mode are configured.
- **Folder paths never travel in a route segment** — the separator may be `/`. Query string for GET, request body for POST/PUT/DELETE.
- **`UidValidity` accompanies every folder-scoped response.** The frontend drops its cached UIDs for a folder when it changes.
- **Passwords are never logged, never returned in a response body, never placed in an error message.**
- Backend conventions, non-negotiable: `Controllers → Repositories → Services`; repositories return `Result<T>` with a client-safe English error string; controllers unwrap via `ApiBaseController.FromResult`; **502 for external-system failures** (IMAP), 400 for bad input, 404 for a missing entity; options bound with `AddOptions<T>().Bind(...)` and **guarded at call time, not validated at startup**; `ArgumentNullException` for programmer errors, `Result.Failure` for operational ones.
- Backend test conventions: xUnit + Moq, `Method_Condition_ExpectedOutcome` naming, `ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be")` for controller tests, protocol logic tested against a mocked interface — never a real socket.
- **MailKit API surface:** the code in this plan targets MailKit 4.x. Verify each call against the installed version before assuming a compile error is your own mistake; if a signature differs, adapt and note it in your report.
- **No test lost without a replacement.** A deleted test must have a deleted subject, and the surviving behaviour must be covered by a new test in the same task.
- Commit messages: imperative mood, ending with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- **Mid-plan state:** between Task 4 and Task 13 the backend serves mail endpoints that no UI consumes, and `/mail` still shows `ComingSoon`. That is expected. The slice is coherent again at Task 15.

---

### Task 1: Mail options, Data Protection key ring, DI

**Files:**
- Create: `Models/Mail/MailOptions.cs`
- Modify: `appsettings.json` (new `Mail` section)
- Modify: `Program.cs` (options binding, Data Protection, DI block at lines 74-94)
- Modify: `snoopy.microservice.csproj` (MailKit package)
- Test: `snoopy.microservice.Tests/Models/MailOptionsTests.cs`

**Interfaces:**
- Produces: `MailOptions` with `ImapHost`, `ImapPort`, `ImapSecurity`, `SmtpHost`, `SmtpPort`, `SmtpSecurity`, `TimeoutSeconds`, `AllowInvalidCertificate`; bound from section `"Mail"`; consumed via **`IOptionsMonitor<MailOptions>`** (deliberate deviation from the codebase's `IOptions<T>` — these values are adjusted in operation and must reload without a restart, see spec § 5.2).

- [ ] **Step 1: Add the MailKit package**

```bash
dotnet add package MailKit
```

Confirm the version resolved and record it in your report. MimeKit arrives as a transitive dependency.

- [ ] **Step 2: Write the failing test**

```csharp
// snoopy.microservice.Tests/Models/MailOptionsTests.cs
using Microsoft.Extensions.Configuration;
using weesky.Snoopy.Microservice.Models.Mail;

namespace snoopy.microservice.Tests.Models
{
    public class MailOptionsTests
    {
        private static MailOptions Bind(Dictionary<string, string?> values)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var options = new MailOptions();
            config.GetSection("Mail").Bind(options);
            return options;
        }

        [Fact]
        public void Defaults_AreTheHomeServerValues()
        {
            var options = new MailOptions();

            Assert.Equal(143, options.ImapPort);
            Assert.Equal(SecureSocketOptions.StartTls, options.ImapSecurity);
            Assert.Equal(587, options.SmtpPort);
            Assert.Equal(SecureSocketOptions.StartTls, options.SmtpSecurity);
            Assert.Equal(30, options.TimeoutSeconds);
            Assert.False(options.AllowInvalidCertificate);
        }

        [Fact]
        public void Bind_ReadsSecurityModeFromString()
        {
            var options = Bind(new()
            {
                ["Mail:ImapHost"] = "imap.example.org",
                ["Mail:ImapPort"] = "993",
                ["Mail:ImapSecurity"] = "SslOnConnect",
            });

            Assert.Equal("imap.example.org", options.ImapHost);
            Assert.Equal(993, options.ImapPort);
            Assert.Equal(SecureSocketOptions.SslOnConnect, options.ImapSecurity);
        }

        [Fact]
        public void IsConfigured_IsFalseWhenImapHostMissing()
        {
            Assert.False(new MailOptions { ImapHost = "" }.IsImapConfigured);
            Assert.True(new MailOptions { ImapHost = "mail.weesky.net" }.IsImapConfigured);
        }
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~MailOptionsTests`
Expected: FAIL — `MailOptions` does not exist.

- [ ] **Step 4: Implement the options class**

```csharp
// Models/Mail/MailOptions.cs
using MailKit.Security;

namespace weesky.Snoopy.Microservice.Models.Mail
{
    /// <summary>
    /// Connection settings for the mail server. Only connection parameters live here:
    /// everything server-specific (hierarchy separator, namespaces, special-use folders,
    /// capabilities) is discovered at runtime from the IMAP session, so that slice 2d can
    /// point at arbitrary external servers we will never hold configuration for.
    /// </summary>
    public class MailOptions
    {
        /// <summary>IMAP host name.</summary>
        public string ImapHost { get; set; } = string.Empty;

        /// <summary>IMAP port. 143 for STARTTLS, 993 for implicit TLS.</summary>
        public int ImapPort { get; set; } = 143;

        /// <summary>
        /// IMAP transport security. StartTls fails when the server does not advertise
        /// STARTTLS; StartTlsWhenAvailable silently falls back to cleartext, which on port
        /// 143 would put credentials on the wire. Prefer StartTls.
        /// </summary>
        public SecureSocketOptions ImapSecurity { get; set; } = SecureSocketOptions.StartTls;

        /// <summary>Submission host name. Consumed in slice 2c.</summary>
        public string SmtpHost { get; set; } = string.Empty;

        /// <summary>Submission port. Consumed in slice 2c.</summary>
        public int SmtpPort { get; set; } = 587;

        /// <summary>Submission transport security. Consumed in slice 2c.</summary>
        public SecureSocketOptions SmtpSecurity { get; set; } = SecureSocketOptions.StartTls;

        /// <summary>Connect and command timeout, in seconds.</summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Accept invalid server certificates. Development only — logged as a warning on
        /// every connection when enabled.
        /// </summary>
        public bool AllowInvalidCertificate { get; set; }

        /// <summary>True when enough is configured to attempt an IMAP connection.</summary>
        public bool IsImapConfigured => !string.IsNullOrWhiteSpace(ImapHost);
    }
}
```

Add `using MailKit.Security;` to the test file too.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~MailOptionsTests` → 3 passed.

- [ ] **Step 6: Add the appsettings section**

In `appsettings.json`, after the `Sieve` section:

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

These are **not** secrets and must stay in `appsettings.json`, never in `/etc/snoopy.microservice/secrets.env`: environment variables outrank appsettings and are read once at startup, so an env-provided value would silently defeat the hot reload this task sets up.

- [ ] **Step 7: Bind the options and configure the key ring in `Program.cs`**

Add to the options block (after line 76, the `SieveOptions` binding):

```csharp
builder.Services.AddOptions<MailOptions>().Bind(builder.Configuration.GetSection("Mail"));
```

Then, immediately before `var app = builder.Build();` (line 164):

```csharp
// Data Protection key ring. It encrypts the IMAP credentials cookie, so it must survive
// restarts: losing it makes every live credentials cookie undecryptable and forces every
// user to sign in again. systemd's StateDirectory= provides a directory outside the deploy
// path (which the release chmod/chown walk recursively) and owned by the service user.
var stateDirectory = Environment.GetEnvironmentVariable("STATE_DIRECTORY")?.Split(':')[0];

if (string.IsNullOrEmpty(stateDirectory) && !builder.Environment.IsDevelopment())
{
    throw new InvalidOperationException(
        "STATE_DIRECTORY is not set. Add 'StateDirectory=snoopy.microservice' to the systemd unit. " +
        "Refusing to start rather than falling back to a key ring under the deployment directory.");
}

var keyRingPath = string.IsNullOrEmpty(stateDirectory)
    ? Path.Combine(builder.Environment.ContentRootPath, "keys")   // development only
    : Path.Combine(stateDirectory, "keys");

Directory.CreateDirectory(keyRingPath);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath))
    .SetApplicationName($"snoopy.microservice.{builder.Environment.EnvironmentName}");
```

And after `var app = builder.Build();`, log the resolved path so the deployment can be verified:

```csharp
app.Logger.LogInformation("Data Protection key ring: {KeyRingPath}", keyRingPath);
```

Add `using Microsoft.AspNetCore.DataProtection;` and `using weesky.Snoopy.Microservice.Models.Mail;` at the top of `Program.cs`.

- [ ] **Step 8: Verify**

Run: `dotnet build` → succeeds. `dotnet test` → all pass (**442 existing** + 3 new = 445; the "461" quoted during exploration was a count of `[Fact]`/`[Theory]` attributes, not of tests).

`dotnet run` cannot be used to check the key-ring log line locally: `ServerVersion.AutoDetect` opens a database connection *before* `builder.Build()`, so the app will not start without the dev database. Verify the log line on the dev server instead — it is item 1 of the manual checklist in Task 16.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "Add mail connection options and a persisted key ring"
```

---

### Task 2: IMAP credentials cookie

**Files:**
- Create: `Services/IMailCredentialStore.cs`, `Services/MailCredentialStore.cs`
- Modify: `Controllers/LoginController.cs` (both actions)
- Modify: `Program.cs` (DI)
- Modify: `Models/ResultEnveloppe.cs` — read it first; add an error **code** alongside the message only if it does not already carry one
- Test: `snoopy.microservice.Tests/Services/MailCredentialStoreTests.cs`, `snoopy.microservice.Tests/Controllers/LoginControllerTests.cs` (extend)

**Interfaces:**
- Consumes: `IDataProtectionProvider` (registered in Task 1), `IOptions<TokenConstants>` for the cookie lifetime.
- Produces:
  ```csharp
  public interface IMailCredentialStore
  {
      void Store(HttpResponse response, string password, TimeSpan lifetime);
      Result<string> Retrieve(HttpRequest request);
      void Clear(HttpResponse response);
  }
  ```
  `Retrieve` returns `Result.Failure<string>("credentials_unavailable")` when the cookie is absent or undecryptable. The cookie is named `MailCredentials`, `HttpOnly; Secure; SameSite=Strict`.

- [ ] **Step 1: Write the failing test**

```csharp
// snoopy.microservice.Tests/Services/MailCredentialStoreTests.cs
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using weesky.Snoopy.Microservice.Services;

namespace snoopy.microservice.Tests.Services
{
    public class MailCredentialStoreTests
    {
        private static MailCredentialStore CreateSut(IDataProtectionProvider? provider = null)
            => new(provider ?? new EphemeralDataProtectionProvider());

        [Fact]
        public void Store_WritesAnHttpOnlySecureStrictCookie()
        {
            var sut = CreateSut();
            var context = new DefaultHttpContext();

            sut.Store(context.Response, "hunter2", TimeSpan.FromMinutes(30));

            var setCookie = context.Response.Headers.SetCookie.ToString();
            Assert.Contains("MailCredentials=", setCookie);
            Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Store_DoesNotWriteThePasswordInClear()
        {
            var sut = CreateSut();
            var context = new DefaultHttpContext();

            sut.Store(context.Response, "hunter2", TimeSpan.FromMinutes(30));

            Assert.DoesNotContain("hunter2", context.Response.Headers.SetCookie.ToString());
        }

        [Fact]
        public void Retrieve_ReturnsThePasswordStoredByTheSameProvider()
        {
            var provider = new EphemeralDataProtectionProvider();
            var writer = CreateSut(provider);
            var response = new DefaultHttpContext().Response;
            writer.Store(response, "hunter2", TimeSpan.FromMinutes(30));

            var cookieValue = ExtractCookieValue(response, "MailCredentials");
            var request = new DefaultHttpContext().Request;
            request.Headers.Cookie = $"MailCredentials={cookieValue}";

            var result = CreateSut(provider).Retrieve(request);

            Assert.True(result.IsSuccess);
            Assert.Equal("hunter2", result.Value);
        }

        [Fact]
        public void Retrieve_FailsWhenCookieIsAbsent()
        {
            var result = CreateSut().Retrieve(new DefaultHttpContext().Request);

            Assert.True(result.IsFailure);
            Assert.Equal("credentials_unavailable", result.Error);
        }

        [Fact]
        public void Retrieve_FailsWhenTheKeyRingChanged()
        {
            var response = new DefaultHttpContext().Response;
            CreateSut(new EphemeralDataProtectionProvider()).Store(response, "hunter2", TimeSpan.FromMinutes(30));

            var request = new DefaultHttpContext().Request;
            request.Headers.Cookie = $"MailCredentials={ExtractCookieValue(response, "MailCredentials")}";

            // A different provider stands in for a lost or rotated-away key ring.
            var result = CreateSut(new EphemeralDataProtectionProvider()).Retrieve(request);

            Assert.True(result.IsFailure);
            Assert.Equal("credentials_unavailable", result.Error);
        }

        [Fact]
        public void Clear_ExpiresTheCookie()
        {
            var context = new DefaultHttpContext();

            CreateSut().Clear(context.Response);

            Assert.Contains("MailCredentials=", context.Response.Headers.SetCookie.ToString());
            Assert.Contains("expires=", context.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractCookieValue(HttpResponse response, string name)
        {
            var header = response.Headers.SetCookie.ToString();
            var start = header.IndexOf($"{name}=", StringComparison.Ordinal) + name.Length + 1;
            var end = header.IndexOf(';', start);
            return end < 0 ? header[start..] : header[start..end];
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~MailCredentialStoreTests`
Expected: FAIL — `MailCredentialStore` does not exist.

- [ ] **Step 3: Implement the store**

```csharp
// Services/IMailCredentialStore.cs
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;

namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>
    /// Carries the user's mail password between requests, encrypted into a cookie.
    ///
    /// The password cannot be recovered from the database — MariaDB stores SHA-512 crypt —
    /// so it is captured at login. It is encrypted with Data Protection and kept in the
    /// client's cookie rather than in server-side session state, so the server never holds
    /// both the key and the ciphertext at rest. See spec section 5.3.
    /// </summary>
    public interface IMailCredentialStore
    {
        /// <summary>Encrypts the password into the credentials cookie.</summary>
        void Store(HttpResponse response, string password, TimeSpan lifetime);

        /// <summary>
        /// Decrypts the credentials cookie. Fails with "credentials_unavailable" when the
        /// cookie is absent or no longer decryptable, which the caller must surface as a 401
        /// so the client can sign in again rather than show an opaque IMAP error.
        /// </summary>
        Result<string> Retrieve(HttpRequest request);

        /// <summary>Expires the credentials cookie.</summary>
        void Clear(HttpResponse response);
    }
}
```

```csharp
// Services/MailCredentialStore.cs
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace weesky.Snoopy.Microservice.Services
{
    public class MailCredentialStore : IMailCredentialStore
    {
        /// <summary>Cookie name. Distinct from the JWT cookie so both can be cleared independently.</summary>
        public const string CookieName = "MailCredentials";

        private const string Purpose = "weesky.imap.credentials";

        private readonly IDataProtector _protector;

        public MailCredentialStore(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector(Purpose);
        }

        public void Store(HttpResponse response, string password, TimeSpan lifetime)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));

            response.Cookies.Append(CookieName, _protector.Protect(password), BuildOptions(DateTimeOffset.UtcNow.Add(lifetime)));
        }

        public Result<string> Retrieve(HttpRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (!request.Cookies.TryGetValue(CookieName, out var protectedValue) || string.IsNullOrEmpty(protectedValue))
            {
                return Result.Failure<string>("credentials_unavailable");
            }

            try
            {
                return Result.Success(_protector.Unprotect(protectedValue));
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // Key ring lost or rotated away. Never log the payload.
                return Result.Failure<string>("credentials_unavailable");
            }
        }

        public void Clear(HttpResponse response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));

            response.Cookies.Append(CookieName, string.Empty, BuildOptions(DateTimeOffset.UnixEpoch));
        }

        private static CookieOptions BuildOptions(DateTimeOffset expires) => new()
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = expires
        };
    }
}
```

- [ ] **Step 4: Run the store tests**

Run: `dotnet test --filter FullyQualifiedName~MailCredentialStoreTests` → 6 passed.

- [ ] **Step 5: Wire the store into login and logout**

Register in `Program.cs` beside the other scoped services (after line 86):

```csharp
builder.Services.AddScoped<IMailCredentialStore, MailCredentialStore>();
```

In `LoginController`, inject `IMailCredentialStore _credentialStore` alongside the existing dependencies, then inside the `if (result.IsSuccess)` block of `Login`, **after** appending the auth cookie:

```csharp
_credentialStore.Store(
    HttpContext.Response,
    credentials.Password,
    TimeSpan.FromMinutes(_tokenConstants.Value.ExpiryInMinutes));
```

And in `Logout`, before `return NoContent();`:

```csharp
_credentialStore.Clear(HttpContext.Response);
```

Update the XML doc on `Login` to mention that a credentials cookie is issued alongside the auth cookie.

- [ ] **Step 6: Extend the login controller tests**

Read `snoopy.microservice.Tests/Controllers/LoginControllerTests.cs` first and follow its existing construction pattern. Add:

```csharp
[Fact]
public async Task Login_OnSuccess_StoresTheCredentialsCookie()
{
    // arrange per the existing successful-login test, plus:
    var credentialStore = new Mock<IMailCredentialStore>();
    // ... build the controller with credentialStore.Object ...

    await controller.Login(new Credentials { Email = "alice@weesky.be", Password = "hunter2" });

    credentialStore.Verify(s => s.Store(It.IsAny<HttpResponse>(), "hunter2", It.IsAny<TimeSpan>()), Times.Once);
}

[Fact]
public async Task Login_OnFailure_DoesNotStoreTheCredentialsCookie()
{
    // arrange per the existing failed-login test
    await controller.Login(new Credentials { Email = "alice@weesky.be", Password = "wrong" });

    credentialStore.Verify(s => s.Store(It.IsAny<HttpResponse>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
}

[Fact]
public void Logout_ClearsTheCredentialsCookie()
{
    controller.Logout();

    credentialStore.Verify(s => s.Clear(It.IsAny<HttpResponse>()), Times.Once);
}
```

- [ ] **Step 7: Verify**

Run: `dotnet test` → all green. `dotnet build` → succeeds.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "Carry mail credentials in an encrypted cookie"
```

---

### Task 3: IMAP session abstraction and connection factory

**Files:**
- Create: `Services/IImapSession.cs`, `Services/ImapSession.cs`, `Services/IImapConnectionFactory.cs`, `Services/ImapConnectionFactory.cs`
- Create: `Models/Mail/MailFolderNode.cs`
- Modify: `Program.cs` (DI)
- Test: `snoopy.microservice.Tests/Services/ImapSessionTests.cs`

**Interfaces:**
- Consumes: `IOptionsMonitor<MailOptions>` (Task 1), `IMailCredentialStore` (Task 2).
- Produces:
  ```csharp
  public interface IImapSession : IAsyncDisposable
  {
      char DirectorySeparator { get; }
      Task<Result<IReadOnlyList<MailFolderNode>>> ListFoldersAsync(CancellationToken ct);
      // message operations arrive in Tasks 7-8; folder mutations in Task 5
  }

  public interface IImapConnectionFactory
  {
      Task<Result<IImapSession>> OpenAsync(string email, string password, CancellationToken ct);
  }
  ```

**Why the seam exists:** `ManageSieveClient` — socket, TLS, SASL — has no unit tests, because the testable protocol logic was deliberately split into a class that works on a `Stream`. Reproduce that split: repositories depend on `IImapSession`, which is mocked in their tests; `ImapConnectionFactory` itself is not unit-tested.

- [ ] **Step 1: Define the folder node model**

```csharp
// Models/Mail/MailFolderNode.cs
namespace weesky.Snoopy.Microservice.Models.Mail
{
    /// <summary>A folder as the client sees it. Children are nested, not flattened.</summary>
    public class MailFolderNode
    {
        /// <summary>Full IMAP path, separator included. Opaque to the client — never parsed by it.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>Leaf name for display.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Well-known role when the server advertises one, or when a name fallback matched:
        /// "inbox", "sent", "drafts", "trash", "junk", "archive". Null for ordinary folders.
        /// </summary>
        public string? SpecialUse { get; set; }

        /// <summary>False when the folder holds no messages (a container-only node).</summary>
        public bool Selectable { get; set; } = true;

        /// <summary>Whether the user subscribed to this folder — drives visibility in the UI.</summary>
        public bool Subscribed { get; set; }

        /// <summary>Total message count. Null when not selectable.</summary>
        public int? Total { get; set; }

        /// <summary>Unread message count. Null when not selectable.</summary>
        public int? Unread { get; set; }

        /// <summary>
        /// Folder UID validity. When it changes, every cached UID for this folder is stale
        /// and the client must drop them.
        /// </summary>
        public uint UidValidity { get; set; }

        public List<MailFolderNode> Children { get; set; } = new();
    }
}
```

- [ ] **Step 2: Write the failing test for special-use resolution**

The one piece of `ImapSession` worth unit-testing in isolation is the special-use resolution, because it has a fallback path that only fires on servers that do not advertise `SPECIAL-USE`. Extract it as a static helper so it is testable without a connection.

```csharp
// snoopy.microservice.Tests/Services/ImapSessionTests.cs
using MailKit;
using weesky.Snoopy.Microservice.Services;

namespace snoopy.microservice.Tests.Services
{
    public class ImapSessionTests
    {
        [Theory]
        [InlineData(FolderAttributes.Sent, "Whatever", "sent")]
        [InlineData(FolderAttributes.Drafts, "Whatever", "drafts")]
        [InlineData(FolderAttributes.Trash, "Whatever", "trash")]
        [InlineData(FolderAttributes.Junk, "Whatever", "junk")]
        [InlineData(FolderAttributes.Archive, "Whatever", "archive")]
        public void ResolveSpecialUse_PrefersTheServerFlag(FolderAttributes attributes, string name, string expected)
        {
            Assert.Equal(expected, ImapSession.ResolveSpecialUse(attributes, name, isInbox: false));
        }

        [Theory]
        [InlineData("Sent", "sent")]
        [InlineData("Sent Messages", "sent")]
        [InlineData("Drafts", "drafts")]
        [InlineData("Trash", "trash")]
        [InlineData("Deleted Messages", "trash")]
        [InlineData("Junk", "junk")]
        [InlineData("Spam", "junk")]
        [InlineData("Archive", "archive")]
        public void ResolveSpecialUse_FallsBackToTheNameWhenNoFlag(string name, string expected)
        {
            Assert.Equal(expected, ImapSession.ResolveSpecialUse(FolderAttributes.None, name, isInbox: false));
        }

        [Fact]
        public void ResolveSpecialUse_MatchesTheNameCaseInsensitively()
        {
            Assert.Equal("trash", ImapSession.ResolveSpecialUse(FolderAttributes.None, "TRASH", isInbox: false));
        }

        [Fact]
        public void ResolveSpecialUse_ReturnsInboxForTheInbox()
        {
            Assert.Equal("inbox", ImapSession.ResolveSpecialUse(FolderAttributes.None, "INBOX", isInbox: true));
        }

        [Fact]
        public void ResolveSpecialUse_ReturnsNullForAnOrdinaryFolder()
        {
            Assert.Null(ImapSession.ResolveSpecialUse(FolderAttributes.None, "Projects", isInbox: false));
        }
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~ImapSessionTests`
Expected: FAIL — `ImapSession` does not exist.

- [ ] **Step 4: Implement the session**

```csharp
// Services/IImapSession.cs
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>
    /// An open, authenticated IMAP session. One session per repository method, disposed at
    /// the end of it — there is no pooling (spec section 3).
    /// </summary>
    public interface IImapSession : IAsyncDisposable
    {
        /// <summary>Hierarchy separator, read from the server's personal namespace.</summary>
        char DirectorySeparator { get; }

        /// <summary>The full folder tree, subscribed and unsubscribed alike.</summary>
        Task<Result<IReadOnlyList<MailFolderNode>>> ListFoldersAsync(CancellationToken cancellationToken);
    }
}
```

```csharp
// Services/ImapSession.cs
using CSharpFunctionalExtensions;
using MailKit;
using MailKit.Net.Imap;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services
{
    public sealed class ImapSession : IImapSession
    {
        private readonly ImapClient _client;
        private readonly ILogger _logger;
        private bool _disposed;

        public ImapSession(ImapClient client, ILogger logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            DirectorySeparator = client.PersonalNamespaces.Count > 0
                ? client.PersonalNamespaces[0].DirectorySeparator
                : '/';
        }

        public char DirectorySeparator { get; }

        public async Task<Result<IReadOnlyList<MailFolderNode>>> ListFoldersAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            try
            {
                var personal = _client.PersonalNamespaces[0];
                var folders = await _client.GetFoldersAsync(
                    personal, StatusItems.Count | StatusItems.Unread | StatusItems.UidValidity,
                    subscribedOnly: false, cancellationToken);

                var nodes = new Dictionary<string, MailFolderNode>(StringComparer.Ordinal);
                var roots = new List<MailFolderNode>();

                foreach (var folder in folders.OrderBy(f => f.FullName, StringComparer.Ordinal))
                {
                    var selectable = (folder.Attributes & FolderAttributes.NonExistent) == 0
                                     && (folder.Attributes & FolderAttributes.NoSelect) == 0;

                    var node = new MailFolderNode
                    {
                        Path = folder.FullName,
                        Name = folder.Name,
                        SpecialUse = ResolveSpecialUse(folder.Attributes, folder.Name, IsInbox(folder)),
                        Selectable = selectable,
                        Subscribed = folder.IsSubscribed,
                        Total = selectable ? folder.Count : null,
                        Unread = selectable ? folder.Unread : null,
                        UidValidity = folder.UidValidity
                    };

                    nodes[folder.FullName] = node;

                    var parentPath = ParentPath(folder.FullName, DirectorySeparator);
                    if (parentPath != null && nodes.TryGetValue(parentPath, out var parent))
                    {
                        parent.Children.Add(node);
                    }
                    else
                    {
                        roots.Add(node);
                    }
                }

                return Result.Success<IReadOnlyList<MailFolderNode>>(roots);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list IMAP folders");
                return Result.Failure<IReadOnlyList<MailFolderNode>>("Unable to read the mailbox folders");
            }
        }

        private static bool IsInbox(IMailFolder folder)
            => string.Equals(folder.FullName, "INBOX", StringComparison.OrdinalIgnoreCase);

        internal static string? ParentPath(string fullName, char separator)
        {
            var index = fullName.LastIndexOf(separator);
            return index <= 0 ? null : fullName[..index];
        }

        /// <summary>
        /// Maps a folder to a well-known role. The server's SPECIAL-USE flag wins; when the
        /// server advertises none, fall back to matching well-known names, which is the only
        /// option on servers without the extension.
        /// </summary>
        public static string? ResolveSpecialUse(FolderAttributes attributes, string name, bool isInbox)
        {
            if (isInbox) return "inbox";

            if ((attributes & FolderAttributes.Sent) != 0) return "sent";
            if ((attributes & FolderAttributes.Drafts) != 0) return "drafts";
            if ((attributes & FolderAttributes.Trash) != 0) return "trash";
            if ((attributes & FolderAttributes.Junk) != 0) return "junk";
            if ((attributes & FolderAttributes.Archive) != 0) return "archive";

            return name.ToLowerInvariant() switch
            {
                "inbox" => "inbox",
                "sent" or "sent messages" or "sent items" => "sent",
                "drafts" or "draft" => "drafts",
                "trash" or "deleted" or "deleted messages" or "deleted items" => "trash",
                "junk" or "spam" or "junk e-mail" => "junk",
                "archive" or "archives" => "archive",
                _ => null
            };
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ImapSession));
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_client.IsConnected)
                {
                    await _client.DisconnectAsync(quit: true);
                }
            }
            catch
            {
                // Best effort — the connection is being torn down anyway.
            }

            _client.Dispose();
        }
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test --filter FullyQualifiedName~ImapSessionTests` → all pass.

- [ ] **Step 6: Implement the connection factory**

```csharp
// Services/IImapConnectionFactory.cs
using CSharpFunctionalExtensions;

namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>Opens an authenticated IMAP session for one user, for one request.</summary>
    public interface IImapConnectionFactory
    {
        Task<Result<IImapSession>> OpenAsync(string email, string password, CancellationToken cancellationToken);
    }
}
```

```csharp
// Services/ImapConnectionFactory.cs
using CSharpFunctionalExtensions;
using MailKit.Net.Imap;
using Microsoft.Extensions.Options;
using System.Net.Security;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>
    /// Opens one IMAP connection per request — no pooling, the Rainloop model (spec section 3).
    /// Modelled on ManageSieveClient.OpenSessionAsync: guard on unconfigured options, generic
    /// message to the client with the detail logged, ownership of the client transferred to
    /// the session on success.
    /// </summary>
    public class ImapConnectionFactory : IImapConnectionFactory
    {
        private readonly IOptionsMonitor<MailOptions> _options;
        private readonly ILogger<ImapConnectionFactory> _logger;

        public ImapConnectionFactory(IOptionsMonitor<MailOptions> options, ILogger<ImapConnectionFactory> logger)
        {
            _options = options;
            _logger = logger;
        }

        public async Task<Result<IImapSession>> OpenAsync(string email, string password, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required", nameof(email));

            var options = _options.CurrentValue;

            if (!options.IsImapConfigured)
            {
                _logger.LogError("IMAP is not configured (Mail:ImapHost missing)");
                return Result.Failure<IImapSession>("Mail service is not configured");
            }

            ImapClient? client = null;

            try
            {
                client = new ImapClient
                {
                    ServerCertificateValidationCallback = ValidateCertificate
                };
                client.Timeout = options.TimeoutSeconds * 1000;

                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    connectCts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                    await client.ConnectAsync(options.ImapHost, options.ImapPort, options.ImapSecurity, connectCts.Token);
                    await client.AuthenticateAsync(email, password, connectCts.Token);
                }

                var session = new ImapSession(client, _logger);
                client = null; // ownership transferred
                return Result.Success<IImapSession>(session);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (MailKit.Security.AuthenticationException)
            {
                // Never echo the server's message: it can disclose account state.
                _logger.LogWarning("IMAP authentication failed for {Email}", email);
                return Result.Failure<IImapSession>("Mail authentication failed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to connect to IMAP at {Host}:{Port}", options.ImapHost, options.ImapPort);
                return Result.Failure<IImapSession>("Unable to connect to the mail service");
            }
            finally
            {
                client?.Dispose();
            }
        }

        private bool ValidateCertificate(object sender, System.Security.Cryptography.X509Certificates.X509Certificate? certificate,
            System.Security.Cryptography.X509Certificates.X509Chain? chain, SslPolicyErrors errors)
        {
            if (errors == SslPolicyErrors.None) return true;

            if (_options.CurrentValue.AllowInvalidCertificate)
            {
                _logger.LogWarning("Accepting an invalid IMAP certificate ({Errors}) — AllowInvalidCertificate is on", errors);
                return true;
            }

            _logger.LogError("Rejected the IMAP server certificate: {Errors}", errors);
            return false;
        }
    }
}
```

- [ ] **Step 7: Register in DI**

In `Program.cs`, beside `AddSingleton<IManageSieveClient, ManageSieveClient>()`:

```csharp
builder.Services.AddSingleton<IImapConnectionFactory, ImapConnectionFactory>();
```

- [ ] **Step 8: Verify and commit**

Run: `dotnet build` → succeeds. `dotnet test` → all green.

```bash
git add -A
git commit -m "Add the IMAP session abstraction and connection factory"
```

---

### Task 4: Folder tree endpoint

**Files:**
- Create: `Repositories/IMailFolderRepository.cs`, `Repositories/MailFolderRepository.cs`
- Create: `Controllers/MailController.cs`
- Modify: `Program.cs` (DI)
- Test: `snoopy.microservice.Tests/Repositories/MailFolderRepositoryTests.cs`, `snoopy.microservice.Tests/Controllers/MailControllerTests.cs`

**Interfaces:**
- Consumes: `IImapConnectionFactory.OpenAsync(email, password, ct)` and `IImapSession.ListFoldersAsync(ct)` (Task 3), `IMailCredentialStore.Retrieve(request)` (Task 2), `ApiBaseController.AuthenticatedUser` (`User` with `.Email`).
- Produces:
  ```csharp
  public interface IMailFolderRepository
  {
      Task<Result<IReadOnlyList<MailFolderNode>>> GetTreeAsync(User user, string password, CancellationToken ct);
  }
  ```
  and `GET /api/Mail/Folders`.

**Error contract, used by every later task:** the credentials failure string is exactly `"credentials_unavailable"` and is returned as **401** so the client can force a clean re-login. IMAP failures are **502**. The message carried by `ResultEnveloppe` is itself the code — no new model field is needed.

- [ ] **Step 1: Write the failing repository test**

```csharp
// snoopy.microservice.Tests/Repositories/MailFolderRepositoryTests.cs
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace snoopy.microservice.Tests.Repositories
{
    public class MailFolderRepositoryTests
    {
        private static readonly User Alice = new("alice@weesky.be");

        private static (MailFolderRepository repo, Mock<IImapConnectionFactory> factory, Mock<IImapSession> session) CreateSut()
        {
            var session = new Mock<IImapSession>();
            session.SetupGet(s => s.DirectorySeparator).Returns('/');

            var factory = new Mock<IImapConnectionFactory>();
            factory.Setup(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IImapSession>(session.Object));

            return (new MailFolderRepository(factory.Object, Mock.Of<ILogger<MailFolderRepository>>()), factory, session);
        }

        [Fact]
        public async Task GetTreeAsync_ReturnsTheSessionTree()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListFoldersAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>(new List<MailFolderNode>
                   {
                       new() { Path = "INBOX", Name = "INBOX", SpecialUse = "inbox", Unread = 4 }
                   }));

            var result = await repo.GetTreeAsync(Alice, "hunter2", CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Single(result.Value);
            Assert.Equal("inbox", result.Value[0].SpecialUse);
        }

        [Fact]
        public async Task GetTreeAsync_OpensTheSessionForTheAuthenticatedUser()
        {
            var (repo, factory, session) = CreateSut();
            session.Setup(s => s.ListFoldersAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>(new List<MailFolderNode>()));

            await repo.GetTreeAsync(Alice, "hunter2", CancellationToken.None);

            factory.Verify(f => f.OpenAsync("alice@weesky.be", "hunter2", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetTreeAsync_PropagatesAConnectionFailure()
        {
            var (repo, factory, _) = CreateSut();
            factory.Setup(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure<IImapSession>("Mail authentication failed"));

            var result = await repo.GetTreeAsync(Alice, "wrong", CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("Mail authentication failed", result.Error);
        }

        [Fact]
        public async Task GetTreeAsync_DisposesTheSession()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListFoldersAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>(new List<MailFolderNode>()));

            await repo.GetTreeAsync(Alice, "hunter2", CancellationToken.None);

            session.Verify(s => s.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task GetTreeAsync_ThrowsWhenUserIsNull()
        {
            var (repo, _, _) = CreateSut();

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => repo.GetTreeAsync(null!, "hunter2", CancellationToken.None));
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~MailFolderRepositoryTests`
Expected: FAIL — `MailFolderRepository` does not exist.

- [ ] **Step 3: Implement the repository**

```csharp
// Repositories/IMailFolderRepository.cs
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Repositories
{
    public interface IMailFolderRepository
    {
        /// <summary>The user's full folder tree, subscribed and unsubscribed alike.</summary>
        Task<Result<IReadOnlyList<MailFolderNode>>> GetTreeAsync(User user, string password, CancellationToken cancellationToken);
    }
}
```

```csharp
// Repositories/MailFolderRepository.cs
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories
{
    /// <summary>
    /// Folder access over IMAP. One session per method, opened and disposed inside it —
    /// the same shape as SieveRepository over ManageSieve.
    /// </summary>
    public class MailFolderRepository : IMailFolderRepository
    {
        private readonly IImapConnectionFactory _factory;
        private readonly ILogger<MailFolderRepository> _logger;

        public MailFolderRepository(IImapConnectionFactory factory, ILogger<MailFolderRepository> logger)
        {
            _factory = factory;
            _logger = logger;
        }

        public async Task<Result<IReadOnlyList<MailFolderNode>>> GetTreeAsync(User user, string password, CancellationToken cancellationToken)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure<IReadOnlyList<MailFolderNode>>(sessionResult.Error);
            await using var session = sessionResult.Value;

            return await session.ListFoldersAsync(cancellationToken);
        }
    }
}
```

- [ ] **Step 4: Run the repository tests**

Run: `dotnet test --filter FullyQualifiedName~MailFolderRepositoryTests` → 5 passed.

- [ ] **Step 5: Write the failing controller test**

```csharp
// snoopy.microservice.Tests/Controllers/MailControllerTests.cs
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using snoopy.microservice.Tests.Infrastructure;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace snoopy.microservice.Tests.Controllers
{
    public class MailControllerTests
    {
        private readonly Mock<IMailFolderRepository> _folders = new();
        private readonly Mock<IMailCredentialStore> _credentials = new();

        private MailController CreateController()
        {
            _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>())).Returns(Result.Success("hunter2"));

            return new MailController(_folders.Object, _credentials.Object)
            {
                ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be")
            };
        }

        [Fact]
        public async Task GetFolders_ReturnsTheTree()
        {
            _folders.Setup(f => f.GetTreeAsync(It.IsAny<User>(), "hunter2", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>(new List<MailFolderNode>
                    {
                        new() { Path = "INBOX", Name = "INBOX", SpecialUse = "inbox" }
                    }));

            var result = await CreateController().GetFolders(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var tree = Assert.IsAssignableFrom<IReadOnlyList<MailFolderNode>>(ok.Value);
            Assert.Single(tree);
        }

        [Fact]
        public async Task GetFolders_Returns401WhenCredentialsAreUnavailable()
        {
            var controller = CreateController();
            _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                        .Returns(Result.Failure<string>("credentials_unavailable"));

            var result = await controller.GetFolders(CancellationToken.None);

            var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
            var envelope = Assert.IsType<ResultEnveloppe>(unauthorized.Value);
            Assert.Equal("credentials_unavailable", envelope.Message);
        }

        [Fact]
        public async Task GetFolders_Returns502WhenImapFails()
        {
            _folders.Setup(f => f.GetTreeAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Failure<IReadOnlyList<MailFolderNode>>("Unable to connect to the mail service"));

            var result = await CreateController().GetFolders(CancellationToken.None);

            var status = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
        }

        [Fact]
        public async Task GetFolders_NeverPassesTheCredentialsToTheResponse()
        {
            _folders.Setup(f => f.GetTreeAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>(new List<MailFolderNode>()));

            var result = await CreateController().GetFolders(CancellationToken.None);

            Assert.DoesNotContain("hunter2", System.Text.Json.JsonSerializer.Serialize(
                Assert.IsType<OkObjectResult>(result.Result).Value));
        }
    }
}
```

Check `ResultEnveloppe`'s actual property name before writing the assertion on `envelope.Message`; if it differs, use the real one and note it in your report.

- [ ] **Step 6: Implement the controller**

```csharp
// Controllers/MailController.cs
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class MailController : ApiBaseController
    {
        private readonly IMailFolderRepository _folders;
        private readonly IMailCredentialStore _credentials;

        public MailController(IMailFolderRepository folders, IMailCredentialStore credentials)
        {
            _folders = folders;
            _credentials = credentials;
        }

        /// <summary>
        /// Returns the caller's folder tree: hierarchy, well-known roles, subscription state
        /// and message counts. Folder paths are opaque to the client — the hierarchy
        /// separator is whatever the server uses, so paths are never parsed client-side.
        /// </summary>
        /// <param name="cancellationToken">cancellation token</param>
        /// <response code="200">The folder tree</response>
        /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
        /// <response code="502">The mail server could not be reached</response>
        [HttpGet("Folders")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<ActionResult<IReadOnlyList<MailFolderNode>>> GetFolders(CancellationToken cancellationToken)
        {
            var password = _credentials.Retrieve(Request);
            if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

            var result = await _folders.GetTreeAsync(AuthenticatedUser, password.Value, cancellationToken);
            return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
        }
    }
}
```

- [ ] **Step 7: Register the repository**

In `Program.cs`, beside the other scoped repositories:

```csharp
builder.Services.AddScoped<IMailFolderRepository, MailFolderRepository>();
```

- [ ] **Step 8: Verify and commit**

Run: `dotnet test` → all green. `dotnet build` → succeeds.

```bash
git add -A
git commit -m "Serve the IMAP folder tree"
```

---

### Task 5: Folder creation, rename, delete and subscription

**Files:**
- Modify: `Services/IImapSession.cs`, `Services/ImapSession.cs` (four verbs)
- Modify: `Repositories/IMailFolderRepository.cs`, `Repositories/MailFolderRepository.cs`
- Modify: `Controllers/MailController.cs` (four actions)
- Create: `Models/Mail/FolderRequests.cs`
- Test: extend `MailFolderRepositoryTests.cs` and `MailControllerTests.cs`

**Interfaces:**
- Produces, on `IImapSession`:
  ```csharp
  Task<Result<string>> CreateFolderAsync(string parentPath, string name, CancellationToken ct); // returns the new full path
  Task<Result<string>> RenameFolderAsync(string path, string newParentPath, string newName, CancellationToken ct);
  Task<Result> DeleteFolderAsync(string path, CancellationToken ct);
  Task<Result> SetSubscriptionAsync(string path, bool subscribed, CancellationToken ct);
  ```
  mirrored one-for-one on `IMailFolderRepository` with `(User user, string password, …)`, and exposed as `POST/PUT/DELETE /api/Mail/Folders` and `PUT /api/Mail/Folders/Subscription`.

- [ ] **Step 1: Define the request models**

```csharp
// Models/Mail/FolderRequests.cs
namespace weesky.Snoopy.Microservice.Models.Mail
{
    /// <summary>
    /// Folder paths travel in the body, never in a route segment: the hierarchy separator
    /// may be '/', which would break routing.
    /// </summary>
    public class CreateFolderRequest
    {
        /// <summary>Parent folder path. Empty creates at the namespace root.</summary>
        public string ParentPath { get; set; } = string.Empty;

        /// <summary>Leaf name of the new folder. Must not contain the hierarchy separator.</summary>
        public string Name { get; set; } = string.Empty;
    }

    public class RenameFolderRequest
    {
        public string Path { get; set; } = string.Empty;

        /// <summary>New parent path. Same as the current one for a pure rename.</summary>
        public string NewParentPath { get; set; } = string.Empty;

        public string NewName { get; set; } = string.Empty;
    }

    public class DeleteFolderRequest
    {
        public string Path { get; set; } = string.Empty;
    }

    public class FolderSubscriptionRequest
    {
        public string Path { get; set; } = string.Empty;

        /// <summary>True subscribes the folder, false hides it from the folder list.</summary>
        public bool Subscribed { get; set; }
    }
}
```

- [ ] **Step 2: Write the failing session tests for name validation**

The separator check is the piece worth isolating: a name containing the separator would silently create a nested folder instead of failing.

```csharp
// add to snoopy.microservice.Tests/Services/ImapSessionTests.cs
[Theory]
[InlineData("Projects", '/', true)]
[InlineData("Pro/jects", '/', false)]
[InlineData("Pro.jects", '.', false)]
[InlineData("Pro.jects", '/', true)]
[InlineData("", '/', false)]
[InlineData("   ", '/', false)]
public void IsValidLeafName_RejectsSeparatorsAndBlanks(string name, char separator, bool expected)
{
    Assert.Equal(expected, ImapSession.IsValidLeafName(name, separator));
}

[Theory]
[InlineData("", "Projects", '/', "Projects")]
[InlineData("INBOX", "Projects", '/', "INBOX/Projects")]
[InlineData("INBOX", "Projects", '.', "INBOX.Projects")]
public void CombinePath_JoinsWithTheServerSeparator(string parent, string name, char separator, string expected)
{
    Assert.Equal(expected, ImapSession.CombinePath(parent, name, separator));
}
```

- [ ] **Step 3: Run to verify it fails, then implement the helpers and verbs**

Add to `ImapSession`:

```csharp
/// <summary>
/// A leaf name may not contain the hierarchy separator: it would silently create a nested
/// folder instead of the one the user asked for.
/// </summary>
public static bool IsValidLeafName(string name, char separator)
    => !string.IsNullOrWhiteSpace(name) && !name.Contains(separator);

public static string CombinePath(string parentPath, string name, char separator)
    => string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}{separator}{name}";

public async Task<Result<string>> CreateFolderAsync(string parentPath, string name, CancellationToken cancellationToken)
{
    ThrowIfDisposed();

    if (!IsValidLeafName(name, DirectorySeparator))
    {
        return Result.Failure<string>($"A folder name cannot be empty or contain '{DirectorySeparator}'");
    }

    try
    {
        var parent = string.IsNullOrEmpty(parentPath)
            ? _client.GetFolder(_client.PersonalNamespaces[0])
            : await _client.GetFolderAsync(parentPath, cancellationToken);

        var created = await parent.CreateAsync(name, isMessageFolder: true, cancellationToken);
        await created.SubscribeAsync(cancellationToken);   // a folder the user just created should be visible
        return Result.Success(created.FullName);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to create folder {Name} under {Parent}", name, parentPath);
        return Result.Failure<string>("Unable to create the folder");
    }
}

public async Task<Result<string>> RenameFolderAsync(string path, string newParentPath, string newName, CancellationToken cancellationToken)
{
    ThrowIfDisposed();

    if (!IsValidLeafName(newName, DirectorySeparator))
    {
        return Result.Failure<string>($"A folder name cannot be empty or contain '{DirectorySeparator}'");
    }

    try
    {
        var folder = await _client.GetFolderAsync(path, cancellationToken);
        var newParent = string.IsNullOrEmpty(newParentPath)
            ? _client.GetFolder(_client.PersonalNamespaces[0])
            : await _client.GetFolderAsync(newParentPath, cancellationToken);

        await folder.RenameAsync(newParent, newName, cancellationToken);
        return Result.Success(folder.FullName);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to rename folder {Path}", path);
        return Result.Failure<string>("Unable to rename the folder");
    }
}

public async Task<Result> DeleteFolderAsync(string path, CancellationToken cancellationToken)
{
    ThrowIfDisposed();

    try
    {
        var folder = await _client.GetFolderAsync(path, cancellationToken);

        if ((folder.Attributes & FolderAttributes.Inbox) != 0)
        {
            return Result.Failure("The inbox cannot be deleted");
        }

        await folder.DeleteAsync(cancellationToken);
        return Result.Success();
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to delete folder {Path}", path);
        return Result.Failure("Unable to delete the folder");
    }
}

public async Task<Result> SetSubscriptionAsync(string path, bool subscribed, CancellationToken cancellationToken)
{
    ThrowIfDisposed();

    try
    {
        var folder = await _client.GetFolderAsync(path, cancellationToken);

        if (subscribed) await folder.SubscribeAsync(cancellationToken);
        else await folder.UnsubscribeAsync(cancellationToken);

        return Result.Success();
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to set subscription on {Path}", path);
        return Result.Failure("Unable to change the folder visibility");
    }
}
```

Declare all four on `IImapSession` with XML docs.

- [ ] **Step 4: Extend the repository**

Each method follows the Task 4 shape exactly — open, check failure, `await using`, delegate:

```csharp
public async Task<Result<string>> CreateFolderAsync(User user, string password, string parentPath, string name, CancellationToken cancellationToken)
{
    if (user == null) throw new ArgumentNullException(nameof(user));

    var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
    if (sessionResult.IsFailure) return Result.Failure<string>(sessionResult.Error);
    await using var session = sessionResult.Value;

    return await session.CreateFolderAsync(parentPath, name, cancellationToken);
}
```

Write `RenameFolderAsync`, `DeleteFolderAsync` and `SetSubscriptionAsync` the same way (`Result.Failure(sessionResult.Error)` without the type argument for the two non-generic ones).

- [ ] **Step 5: Add repository tests**

One per verb, following the Task 4 pattern: delegation to the session with the right arguments, propagation of a connection failure, and disposal. Plus the two failure cases that do **not** reach the server:

```csharp
[Fact]
public async Task CreateFolderAsync_PropagatesTheSessionValidationFailure()
{
    var (repo, _, session) = CreateSut();
    session.Setup(s => s.CreateFolderAsync("", "Pro/jects", It.IsAny<CancellationToken>()))
           .ReturnsAsync(Result.Failure<string>("A folder name cannot be empty or contain '/'"));

    var result = await repo.CreateFolderAsync(Alice, "hunter2", "", "Pro/jects", CancellationToken.None);

    Assert.True(result.IsFailure);
}
```

- [ ] **Step 6: Add the controller actions**

Four actions on `MailController`, each repeating the credentials guard from Task 4. Status codes: **400** for a validation failure (bad name, deleting the inbox), **502** for an IMAP failure. Since both arrive as `Result.Failure`, distinguish them by checking whether the repository reached the server — simplest correct approach: the session returns validation failures with a message starting `"A folder name cannot"` or equal to `"The inbox cannot be deleted"`, so instead of string-matching, **add a `MailFailureKind` to the result**. To avoid over-engineering, this plan takes the simpler route: validation happens in the **controller** before the repository is called, so the repository only ever returns 502-worthy failures.

Move the name check into the controller:

```csharp
[HttpPost("Folders")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status502BadGateway)]
public async Task<ActionResult<string>> CreateFolder(CreateFolderRequest request, CancellationToken cancellationToken)
{
    if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));
    if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder name is required"));

    var password = _credentials.Retrieve(Request);
    if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

    var result = await _folders.CreateFolderAsync(AuthenticatedUser, password.Value, request.ParentPath ?? string.Empty, request.Name, cancellationToken);
    return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
}
```

The separator check stays in `ImapSession` as a defence in depth — the controller cannot know the separator without a connection, so the two layers check different things: the controller checks emptiness, the session checks the separator.

Write `RenameFolder` (`[HttpPut("Folders")]`), `DeleteFolder` (`[HttpDelete("Folders")]`) and `SetFolderSubscription` (`[HttpPut("Folders/Subscription")]`) on the same shape. `DeleteFolder` and `SetFolderSubscription` return **204** on success: `return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway, successStatusCode: StatusCodes.Status204NoContent);` — note this overload exists only on the **non-generic** `FromResult`.

- [ ] **Step 7: Add controller tests**

For each action: success, `credentials_unavailable` → 401, IMAP failure → 502, and for create/rename a blank name → 400 with the repository never called (`_folders.Verify(..., Times.Never)`).

- [ ] **Step 8: Verify and commit**

Run: `dotnet test` → all green.

```bash
git add -A
git commit -m "Add folder creation, rename, delete and subscription"
```

---

### Task 6: HTML sanitiser

**Files:**
- Create: `Services/IMailHtmlSanitizer.cs`, `Services/MailHtmlSanitizer.cs`
- Create: `Models/Mail/SanitizedHtml.cs`
- Modify: `snoopy.microservice.csproj` (HtmlSanitizer package), `Program.cs` (DI)
- Test: `snoopy.microservice.Tests/Services/MailHtmlSanitizerTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface IMailHtmlSanitizer
  {
      SanitizedHtml Sanitize(string html);
  }

  public class SanitizedHtml
  {
      public string Html { get; set; } = string.Empty;
      public int BlockedImageCount { get; set; }
  }
  ```

**This task is security-critical.** A message body is hostile input by construction. The whitelist defined here is also the contract slice 2c's rich editor must produce (spec § 6.5) — it is a shared subset, not a reader-local setting.

- [ ] **Step 1: Add the package**

```bash
dotnet add package HtmlSanitizer
```

- [ ] **Step 2: Write the failing tests**

```csharp
// snoopy.microservice.Tests/Services/MailHtmlSanitizerTests.cs
using weesky.Snoopy.Microservice.Services;

namespace snoopy.microservice.Tests.Services
{
    public class MailHtmlSanitizerTests
    {
        private readonly MailHtmlSanitizer _sut = new();

        [Theory]
        [InlineData("<script>alert(1)</script><p>hi</p>")]
        [InlineData("<p onclick=\"alert(1)\">hi</p>")]
        [InlineData("<iframe src=\"https://evil.example\"></iframe><p>hi</p>")]
        [InlineData("<object data=\"evil\"></object><p>hi</p>")]
        [InlineData("<embed src=\"evil\"><p>hi</p>")]
        [InlineData("<form action=\"https://evil.example\"><input name=\"p\"></form><p>hi</p>")]
        [InlineData("<a href=\"javascript:alert(1)\">click</a><p>hi</p>")]
        [InlineData("<p style=\"position:fixed;top:0\">hi</p>")]
        public void Sanitize_StripsHostileContent(string hostile)
        {
            var result = _sut.Sanitize(hostile).Html;

            Assert.DoesNotContain("script", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onclick", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("iframe", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<object", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<embed", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<form", result, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("position", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Sanitize_KeepsFormattingTheEditorMustAlsoProduce()
        {
            const string formatted =
                "<p><strong>bold</strong> <em>italic</em> <u>underline</u></p>" +
                "<ul><li>one</li></ul><blockquote>quoted</blockquote>" +
                "<p style=\"font-family:Arial;font-size:14px;color:#333333\">styled</p>" +
                "<table><tr><td>cell</td></tr></table>";

            var result = _sut.Sanitize(formatted).Html;

            Assert.Contains("<strong>", result);
            Assert.Contains("<em>", result);
            Assert.Contains("<u>", result);
            Assert.Contains("<li>", result);
            Assert.Contains("blockquote", result);
            Assert.Contains("font-family", result);
            Assert.Contains("font-size", result);
            Assert.Contains("color", result);
            Assert.Contains("<td>", result);
        }

        [Fact]
        public void Sanitize_MovesRemoteImagesToDataBlockedSrcAndCountsThem()
        {
            var result = _sut.Sanitize(
                "<img src=\"https://tracker.example/pixel.gif\"><img src=\"http://other.example/a.png\">");

            Assert.Equal(2, result.BlockedImageCount);
            Assert.DoesNotContain("src=\"https://tracker.example", result.Html);
            Assert.Contains("data-blocked-src", result.Html);
        }

        [Fact]
        public void Sanitize_KeepsInlineCidImages()
        {
            var result = _sut.Sanitize("<img src=\"cid:logo@example\">");

            Assert.Equal(0, result.BlockedImageCount);
            Assert.Contains("cid:logo@example", result.Html);
        }

        [Fact]
        public void Sanitize_ForcesLinksToOpenSafely()
        {
            var result = _sut.Sanitize("<a href=\"https://example.org\">link</a>").Html;

            Assert.Contains("rel=\"noopener noreferrer\"", result);
            Assert.Contains("target=\"_blank\"", result);
        }

        [Fact]
        public void Sanitize_ReturnsEmptyForNullOrEmptyInput()
        {
            Assert.Equal(string.Empty, _sut.Sanitize(null!).Html);
            Assert.Equal(string.Empty, _sut.Sanitize("").Html);
        }
    }
}
```

- [ ] **Step 3: Run to verify it fails, then implement**

```csharp
// Models/Mail/SanitizedHtml.cs
namespace weesky.Snoopy.Microservice.Models.Mail
{
    /// <summary>A message body made safe to render, plus what was withheld.</summary>
    public class SanitizedHtml
    {
        public string Html { get; set; } = string.Empty;

        /// <summary>
        /// Remote images moved to data-blocked-src. The client offers "show images" and
        /// swaps them back in without another round trip.
        /// </summary>
        public int BlockedImageCount { get; set; }
    }
}
```

```csharp
// Services/IMailHtmlSanitizer.cs
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>
    /// Makes a message body safe to render. The allowed tag and style set is a contract
    /// shared with the rich editor of slice 2c: replying round-trips a sanitised body
    /// through the editor and back out, so formatting degrades on every pass if the two
    /// ends disagree.
    /// </summary>
    public interface IMailHtmlSanitizer
    {
        SanitizedHtml Sanitize(string html);
    }
}
```

```csharp
// Services/MailHtmlSanitizer.cs
using Ganss.Xss;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services
{
    public class MailHtmlSanitizer : IMailHtmlSanitizer
    {
        private const string BlockedSrcAttribute = "data-blocked-src";

        private readonly HtmlSanitizer _sanitizer;

        public MailHtmlSanitizer()
        {
            _sanitizer = new HtmlSanitizer();

            _sanitizer.AllowedTags.Clear();
            foreach (var tag in new[]
            {
                "p", "br", "hr", "div", "span",
                "strong", "b", "em", "i", "u", "s", "sub", "sup",
                "h1", "h2", "h3", "h4", "h5", "h6",
                "ul", "ol", "li", "blockquote", "pre", "code",
                "a", "img",
                "table", "thead", "tbody", "tfoot", "tr", "td", "th", "caption"
            }) _sanitizer.AllowedTags.Add(tag);

            _sanitizer.AllowedAttributes.Clear();
            foreach (var attribute in new[]
            {
                "href", "src", "alt", "title", "style",
                "colspan", "rowspan", "align", "valign", "width", "height",
                BlockedSrcAttribute
            }) _sanitizer.AllowedAttributes.Add(attribute);

            // Inline styles only, and only the properties email clients actually honour.
            // Anything positional is excluded: it would let a message escape its container.
            _sanitizer.AllowedCssProperties.Clear();
            foreach (var property in new[]
            {
                "color", "background-color",
                "font-family", "font-size", "font-style", "font-weight",
                "text-align", "text-decoration",
                "margin", "margin-top", "margin-bottom", "margin-left", "margin-right",
                "padding", "padding-top", "padding-bottom", "padding-left", "padding-right",
                "border", "border-collapse", "border-color", "border-style", "border-width",
                "list-style-type", "line-height", "vertical-align"
            }) _sanitizer.AllowedCssProperties.Add(property);

            _sanitizer.AllowedSchemes.Clear();
            _sanitizer.AllowedSchemes.Add("http");
            _sanitizer.AllowedSchemes.Add("https");
            _sanitizer.AllowedSchemes.Add("mailto");
            _sanitizer.AllowedSchemes.Add("cid");

            _sanitizer.RemovingAttribute += (_, e) =>
            {
                // Keep the data-blocked-src rewrite from being undone by the scheme filter.
                if (string.Equals(e.Attribute.Name, BlockedSrcAttribute, StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                }
            };
        }

        public SanitizedHtml Sanitize(string html)
        {
            if (string.IsNullOrEmpty(html)) return new SanitizedHtml();

            var cleaned = _sanitizer.Sanitize(html);

            var document = new HtmlAgilityPack.HtmlDocument();
            document.LoadHtml(cleaned);

            var blocked = 0;
            var images = document.DocumentNode.SelectNodes("//img");
            if (images != null)
            {
                foreach (var image in images)
                {
                    var src = image.GetAttributeValue("src", string.Empty);
                    if (src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(src)) continue;

                    image.SetAttributeValue(BlockedSrcAttribute, src);
                    image.Attributes.Remove("src");
                    blocked++;
                }
            }

            var links = document.DocumentNode.SelectNodes("//a[@href]");
            if (links != null)
            {
                foreach (var link in links)
                {
                    link.SetAttributeValue("target", "_blank");
                    link.SetAttributeValue("rel", "noopener noreferrer");
                }
            }

            return new SanitizedHtml { Html = document.DocumentNode.OuterHtml, BlockedImageCount = blocked };
        }
    }
}
```

**Corrections applied during execution.**

`HtmlSanitizer` pulls **AngleSharp**, not HtmlAgilityPack. Use AngleSharp for the second pass
rather than adding a second HTML parser — using the same parser as the sanitiser means the two
passes cannot disagree about the tree.

**Version:** `HtmlSanitizer 9.1.949-beta`, not the 9.0.892 stable. The stable pins AngleSharp
to exactly `[0.17.1]`, which carries GHSA-pgww-w46g-26qg: *mXSS via annotation-xml HTML
Integration Point Bypass* — AngleSharp builds a different DOM than the browser does for the
same markup, which is a sanitiser bypass in the sanitiser's own parser. Fixed in AngleSharp
1.5.0; the beta depends on 1.5.1. The exact-version pin makes overriding AngleSharp
impossible, so the beta is the only fixed path. Decided with the user, together with adding
**DOMPurify on the client** (Task 15) so the two barriers use different parsers in different
engines — a parse divergence in one then cannot propagate through the other.

A regression test covers the `annotation-xml` vector specifically.

**Test-assertion trap, hit and fixed:** `data-blocked-src="…"` *contains* the substring
`src="…"`, so `Assert.DoesNotContain("src=\"…")` fails against correct output. Assert on
`" src="` with the leading space — attribute names are whitespace-delimited.

- [ ] **Step 4: Run the tests, register in DI**

Run: `dotnet test --filter FullyQualifiedName~MailHtmlSanitizerTests` → all pass.

```csharp
builder.Services.AddSingleton<IMailHtmlSanitizer, MailHtmlSanitizer>();
```

- [ ] **Step 5: Verify and commit**

Run: `dotnet test` → all green.

```bash
git add -A
git commit -m "Add the message body sanitiser"
```

---

### Task 7: Paginated message list

**Files:**
- Create: `Models/Mail/MailMessageSummary.cs`, `Models/Mail/MailFolderPage.cs`
- Modify: `Services/IImapSession.cs`, `Services/ImapSession.cs`
- Create: `Repositories/IMailMessageRepository.cs`, `Repositories/MailMessageRepository.cs`
- Modify: `Controllers/MailController.cs`, `Program.cs`
- Test: `snoopy.microservice.Tests/Services/ImapSessionTests.cs` (extend), `snoopy.microservice.Tests/Repositories/MailMessageRepositoryTests.cs`, `MailControllerTests.cs` (extend)

**Interfaces:**
- Produces on `IImapSession`: `Task<Result<MailFolderPage>> ListMessagesAsync(string folderPath, int page, int pageSize, CancellationToken ct)`.
- Produces the repository interface — **these exact names are used by Tasks 7 and 8's controller code**:
  ```csharp
  public interface IMailMessageRepository
  {
      Task<Result<MailFolderPage>> ListAsync(User user, string password, string folderPath, int page, int pageSize, CancellationToken ct);
      // added in Task 8:
      Task<Result<MailMessageDetail>> GetAsync(User user, string password, string folderPath, uint uid, CancellationToken ct);
      Task<Result<MailAttachmentContent>> GetAttachmentAsync(User user, string password, string folderPath, uint uid, string partSpecifier, CancellationToken ct);
  }
  ```
  Declare only `ListAsync` in this task; the other two arrive in Task 8.
- **`MailController` gains a second constructor dependency in this task:** `IMailMessageRepository _messages`, alongside the `IMailFolderRepository` and `IMailCredentialStore` from Task 4. Update every existing `MailControllerTests` factory accordingly — the constructor signature change breaks them otherwise.
- Endpoint: `GET /api/Mail/Messages?folder=&page=&pageSize=`.

**Ordering:** newest first. IMAP sequence numbers run oldest-first, so a page maps to a **window at the end** of the folder, fetched then reversed. The arithmetic is the one thing here worth testing in isolation.

- [ ] **Step 1: Define the models**

```csharp
// Models/Mail/MailMessageSummary.cs
namespace weesky.Snoopy.Microservice.Models.Mail
{
    /// <summary>One row of the message list. Envelope-level only — no body is fetched.</summary>
    public class MailMessageSummary
    {
        /// <summary>IMAP UID. Valid only for the UidValidity of its page.</summary>
        public uint Uid { get; set; }

        public string Subject { get; set; } = string.Empty;

        /// <summary>Display name of the first sender, falling back to the address.</summary>
        public string FromName { get; set; } = string.Empty;

        public string FromAddress { get; set; } = string.Empty;

        /// <summary>Date the message claims, falling back to the server's internal date.</summary>
        public DateTimeOffset Date { get; set; }

        public bool Seen { get; set; }
        public bool Flagged { get; set; }
        public bool Answered { get; set; }
        public bool HasAttachments { get; set; }

        /// <summary>Size in octets.</summary>
        public uint Size { get; set; }

        /// <summary>Short body extract for the list row. Empty when the server cannot supply one.</summary>
        public string Preview { get; set; } = string.Empty;
    }
}
```

```csharp
// Models/Mail/MailFolderPage.cs
namespace weesky.Snoopy.Microservice.Models.Mail
{
    /// <summary>One page of a folder, newest message first.</summary>
    public class MailFolderPage
    {
        public string FolderPath { get; set; } = string.Empty;

        /// <summary>
        /// UID validity at the time of the read. When this changes, every UID the client
        /// cached for this folder is meaningless and must be discarded.
        /// </summary>
        public uint UidValidity { get; set; }

        /// <summary>Total messages in the folder, all pages combined.</summary>
        public int Total { get; set; }

        /// <summary>Zero-based page index.</summary>
        public int Page { get; set; }

        public int PageSize { get; set; }

        public List<MailMessageSummary> Messages { get; set; } = new();
    }
}
```

- [ ] **Step 2: Write the failing test for the page window**

```csharp
// add to snoopy.microservice.Tests/Services/ImapSessionTests.cs
[Theory]
// total, page, pageSize  =>  startIndex, endIndex
[InlineData(100, 0, 50, 50, 99)]   // newest 50
[InlineData(100, 1, 50, 0, 49)]    // next 50
[InlineData(100, 2, 50, -1, -1)]   // past the end
[InlineData(30, 0, 50, 0, 29)]     // fewer messages than a page
[InlineData(0, 0, 50, -1, -1)]     // empty folder
[InlineData(75, 1, 50, 0, 24)]     // partial last page
public void ComputePageWindow_MapsNewestFirstPagesToSequenceRanges(
    int total, int page, int pageSize, int expectedStart, int expectedEnd)
{
    var (start, end) = ImapSession.ComputePageWindow(total, page, pageSize);

    Assert.Equal(expectedStart, start);
    Assert.Equal(expectedEnd, end);
}
```

- [ ] **Step 3: Run to verify it fails, then implement**

```csharp
// add to ImapSession
/// <summary>
/// Maps a newest-first page onto an IMAP sequence range, which runs oldest-first.
/// Returns (-1, -1) when the page lies past the end of the folder.
/// </summary>
public static (int Start, int End) ComputePageWindow(int total, int page, int pageSize)
{
    if (total <= 0 || page < 0 || pageSize <= 0) return (-1, -1);

    var end = total - 1 - (page * pageSize);
    if (end < 0) return (-1, -1);

    var start = Math.Max(0, end - pageSize + 1);
    return (start, end);
}

public async Task<Result<MailFolderPage>> ListMessagesAsync(string folderPath, int page, int pageSize, CancellationToken cancellationToken)
{
    ThrowIfDisposed();

    try
    {
        var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var result = new MailFolderPage
        {
            FolderPath = folder.FullName,
            UidValidity = folder.UidValidity,
            Total = folder.Count,
            Page = page,
            PageSize = pageSize
        };

        var (start, end) = ComputePageWindow(folder.Count, page, pageSize);
        if (start < 0) return Result.Success(result);

        var items = await folder.FetchAsync(start, end,
            MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
            MessageSummaryItems.Size | MessageSummaryItems.BodyStructure | MessageSummaryItems.InternalDate |
            MessageSummaryItems.PreviewText,
            cancellationToken);

        foreach (var item in items.Reverse())   // fetch is oldest-first; the list is newest-first
        {
            result.Messages.Add(ToSummary(item));
        }

        return Result.Success(result);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to list messages in {Folder}", folderPath);
        return Result.Failure<MailFolderPage>("Unable to read the messages");
    }
}

private static MailMessageSummary ToSummary(IMessageSummary item)
{
    var sender = item.Envelope?.From?.Mailboxes?.FirstOrDefault();

    return new MailMessageSummary
    {
        Uid = item.UniqueId.Id,
        Subject = item.Envelope?.Subject ?? string.Empty,
        FromName = sender?.Name is { Length: > 0 } name ? name : sender?.Address ?? string.Empty,
        FromAddress = sender?.Address ?? string.Empty,
        Date = item.Envelope?.Date ?? item.InternalDate ?? DateTimeOffset.MinValue,
        Seen = item.Flags?.HasFlag(MessageFlags.Seen) ?? false,
        Flagged = item.Flags?.HasFlag(MessageFlags.Flagged) ?? false,
        Answered = item.Flags?.HasFlag(MessageFlags.Answered) ?? false,
        HasAttachments = item.Attachments?.Any() ?? false,
        Size = item.Size ?? 0,
        Preview = item.PreviewText ?? string.Empty
    };
}
```

Add `using MailKit.Search;` only if the compiler asks for it. `MessageSummaryItems.PreviewText` requires the server to support `PREVIEW`; MailKit degrades to an empty value otherwise — confirm that against the installed version and, if it throws instead, drop the flag and leave `Preview` empty, noting it in your report.

- [ ] **Step 4: Implement the repository, controller action and DI**

`MailMessageRepository` follows Task 4's shape exactly. The controller action:

```csharp
/// <summary>
/// One page of a folder, newest first. The folder path travels in the query string, not a
/// route segment, because the hierarchy separator may be '/'.
/// </summary>
[HttpGet("Messages")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status502BadGateway)]
public async Task<ActionResult<MailFolderPage>> GetMessages(
    [FromQuery] string folder, [FromQuery] int page = 0, [FromQuery] int pageSize = 50,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(folder)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));
    if (page < 0) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Page must not be negative"));
    if (pageSize is < 1 or > 200) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Page size must be between 1 and 200"));

    var password = _credentials.Retrieve(Request);
    if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

    var result = await _messages.ListAsync(AuthenticatedUser, password.Value, folder, page, pageSize, cancellationToken);
    return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
}
```

The 200 upper bound on `pageSize` is a guard: an unbounded value lets one request fetch an entire mailbox.

Register: `builder.Services.AddScoped<IMailMessageRepository, MailMessageRepository>();`

- [ ] **Step 5: Add repository and controller tests**

Repository: delegation with the right arguments, connection-failure propagation, disposal, `ArgumentNullException` on a null user — the same four as Task 4.
Controller: success, blank folder → 400, `page = -1` → 400, `pageSize = 500` → 400, credentials failure → 401, IMAP failure → 502. In each 400 case assert the repository was never called.

- [ ] **Step 6: Verify and commit**

Run: `dotnet test` → all green.

```bash
git add -A
git commit -m "Serve a paginated message list"
```

---

### Task 8: Message detail and attachment download

**Files:**
- Create: `Models/Mail/MailMessageDetail.cs`, `Models/Mail/MailAttachmentInfo.cs`, `Models/Mail/MailAttachmentContent.cs`
- Modify: `Services/IImapSession.cs`, `Services/ImapSession.cs`
- Modify: `Repositories/IMailMessageRepository.cs`, `Repositories/MailMessageRepository.cs`, `Controllers/MailController.cs`
- Test: extend `MailMessageRepositoryTests.cs`, `MailControllerTests.cs`

**Interfaces:**
- Produces on `IImapSession`:
  ```csharp
  Task<Result<MailMessageDetail>> GetMessageAsync(string folderPath, uint uid, CancellationToken ct);
  Task<Result<MailAttachmentContent>> GetAttachmentAsync(string folderPath, uint uid, string partSpecifier, CancellationToken ct);
  ```
  and endpoints `GET /api/Mail/Messages/Detail?folder=&uid=` and `GET /api/Mail/Messages/Attachment?folder=&uid=&part=`.

**Attachments are addressed by MIME part specifier, not by a numeric index.** An index is positional and drifts; the specifier is what IMAP itself uses to fetch a part, so a stale client link fails cleanly instead of downloading the wrong file.

- [ ] **Step 1: Define the models**

```csharp
// Models/Mail/MailAttachmentInfo.cs
namespace weesky.Snoopy.Microservice.Models.Mail
{
    public class MailAttachmentInfo
    {
        /// <summary>MIME part specifier — the download handle. Opaque to the client.</summary>
        public string Part { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = "application/octet-stream";

        /// <summary>Size in octets, from the body structure — no download required.</summary>
        public uint Size { get; set; }

        /// <summary>True for a part referenced from the HTML body by cid:, not a real attachment.</summary>
        public bool IsInline { get; set; }
    }
}
```

```csharp
// Models/Mail/MailMessageDetail.cs
namespace weesky.Snoopy.Microservice.Models.Mail
{
    /// <summary>A single message, ready to render.</summary>
    public class MailMessageDetail
    {
        public uint Uid { get; set; }
        public string FolderPath { get; set; } = string.Empty;
        public uint UidValidity { get; set; }

        public string Subject { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;
        public string FromAddress { get; set; } = string.Empty;
        public List<string> To { get; set; } = new();
        public List<string> Cc { get; set; } = new();
        public DateTimeOffset Date { get; set; }

        /// <summary>Sanitised HTML body. Empty when the message is text-only.</summary>
        public string HtmlBody { get; set; } = string.Empty;

        /// <summary>Plain-text body. Empty when the message is HTML-only.</summary>
        public string TextBody { get; set; } = string.Empty;

        /// <summary>Remote images withheld by the sanitiser, for the "show images" prompt.</summary>
        public int BlockedImageCount { get; set; }

        public List<MailAttachmentInfo> Attachments { get; set; } = new();
    }
}
```

```csharp
// Models/Mail/MailAttachmentContent.cs
namespace weesky.Snoopy.Microservice.Models.Mail
{
    /// <summary>A decoded attachment, ready to stream to the client.</summary>
    public class MailAttachmentContent
    {
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public string FileName { get; set; } = "attachment";
        public string ContentType { get; set; } = "application/octet-stream";
    }
}
```

- [ ] **Step 2: Implement the session verbs**

```csharp
// add to ImapSession
public async Task<Result<MailMessageDetail>> GetMessageAsync(string folderPath, uint uid, CancellationToken cancellationToken)
{
    ThrowIfDisposed();

    try
    {
        var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var uniqueId = new UniqueId(folder.UidValidity, uid);

        var summaries = await folder.FetchAsync(new[] { uniqueId },
            MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.BodyStructure,
            cancellationToken);

        var summary = summaries.FirstOrDefault();
        if (summary == null) return Result.Failure<MailMessageDetail>("Message not found");

        var message = await folder.GetMessageAsync(uniqueId, cancellationToken);

        var sanitized = _sanitizer.Sanitize(message.HtmlBody ?? string.Empty);
        var sender = message.From?.Mailboxes?.FirstOrDefault();

        var detail = new MailMessageDetail
        {
            Uid = uid,
            FolderPath = folder.FullName,
            UidValidity = folder.UidValidity,
            Subject = message.Subject ?? string.Empty,
            FromName = sender?.Name is { Length: > 0 } name ? name : sender?.Address ?? string.Empty,
            FromAddress = sender?.Address ?? string.Empty,
            To = message.To?.Mailboxes?.Select(m => m.Address).ToList() ?? new List<string>(),
            Cc = message.Cc?.Mailboxes?.Select(m => m.Address).ToList() ?? new List<string>(),
            Date = message.Date,
            HtmlBody = sanitized.Html,
            TextBody = message.TextBody ?? string.Empty,
            BlockedImageCount = sanitized.BlockedImageCount
        };

        foreach (var part in summary.BodyParts.OfType<BodyPartBasic>())
        {
            if (!part.IsAttachment && string.IsNullOrEmpty(part.FileName)) continue;

            detail.Attachments.Add(new MailAttachmentInfo
            {
                Part = part.PartSpecifier,
                FileName = string.IsNullOrEmpty(part.FileName) ? "attachment" : part.FileName,
                ContentType = part.ContentType?.MimeType ?? "application/octet-stream",
                Size = part.Octets,
                IsInline = !part.IsAttachment
            });
        }

        return Result.Success(detail);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to read message {Uid} in {Folder}", uid, folderPath);
        return Result.Failure<MailMessageDetail>("Unable to read the message");
    }
}

public async Task<Result<MailAttachmentContent>> GetAttachmentAsync(string folderPath, uint uid, string partSpecifier, CancellationToken cancellationToken)
{
    ThrowIfDisposed();

    try
    {
        var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
        await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var uniqueId = new UniqueId(folder.UidValidity, uid);

        var summaries = await folder.FetchAsync(new[] { uniqueId }, MessageSummaryItems.BodyStructure, cancellationToken);
        var summary = summaries.FirstOrDefault();
        if (summary == null) return Result.Failure<MailAttachmentContent>("Message not found");

        var part = summary.BodyParts.OfType<BodyPartBasic>()
            .FirstOrDefault(p => string.Equals(p.PartSpecifier, partSpecifier, StringComparison.Ordinal));
        if (part == null) return Result.Failure<MailAttachmentContent>("Attachment not found");

        var entity = await folder.GetBodyPartAsync(uniqueId, part, cancellationToken);
        if (entity is not MimePart mimePart) return Result.Failure<MailAttachmentContent>("Attachment not found");

        using var buffer = new MemoryStream();
        await mimePart.Content.DecodeToAsync(buffer, cancellationToken);

        return Result.Success(new MailAttachmentContent
        {
            Content = buffer.ToArray(),
            FileName = string.IsNullOrEmpty(part.FileName) ? "attachment" : part.FileName,
            ContentType = part.ContentType?.MimeType ?? "application/octet-stream"
        });
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to read attachment {Part} of message {Uid}", partSpecifier, uid);
        return Result.Failure<MailAttachmentContent>("Unable to read the attachment");
    }
}
```

`ImapSession` now needs `IMailHtmlSanitizer _sanitizer` injected — add it to the constructor and update `ImapConnectionFactory` to pass it through (the factory takes `IMailHtmlSanitizer` in its own constructor and hands it to each session it builds). Add `using MimeKit;` and `using MailKit;`.

- [ ] **Step 3: Repository and controller**

Repository methods follow Task 4's shape. The two controller actions:

```csharp
[HttpGet("Messages/Detail")]
public async Task<ActionResult<MailMessageDetail>> GetMessage(
    [FromQuery] string folder, [FromQuery] uint uid, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(folder)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));

    var password = _credentials.Retrieve(Request);
    if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

    var result = await _messages.GetAsync(AuthenticatedUser, password.Value, folder, uid, cancellationToken);

    // "Message not found" is a 404; anything else from IMAP is a 502.
    if (result.IsFailure && result.Error == "Message not found")
        return NotFound(ResultEnveloppe.CreateErrorEnveloppe(result.Error));

    return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
}

[HttpGet("Messages/Attachment")]
public async Task<ActionResult> GetAttachment(
    [FromQuery] string folder, [FromQuery] uint uid, [FromQuery] string part, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(folder)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));
    if (string.IsNullOrWhiteSpace(part)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A part is required"));

    var password = _credentials.Retrieve(Request);
    if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

    var result = await _messages.GetAttachmentAsync(AuthenticatedUser, password.Value, folder, uid, part, cancellationToken);

    if (result.IsFailure)
    {
        var status = result.Error is "Message not found" or "Attachment not found"
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status502BadGateway;
        return StatusCode(status, ResultEnveloppe.CreateErrorEnveloppe(result.Error));
    }

    // Always an attachment disposition: never let the browser render message content inline.
    return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
}
```

Add the full XML docs and `[ProducesResponseType]` set on both, matching the style of the earlier actions.

- [ ] **Step 4: Tests**

Repository: for each of the two methods, delegation, connection-failure propagation, disposal, null-user throw.
Controller: detail success; `"Message not found"` → 404; other failure → 502; blank folder → 400; credentials failure → 401. Attachment: success returns `FileContentResult` with the right content type and file name; blank part → 400; not found → 404; other failure → 502.

```csharp
[Fact]
public async Task GetAttachment_ReturnsTheFileWithAnAttachmentDisposition()
{
    _messages.Setup(m => m.GetAttachmentAsync(It.IsAny<User>(), It.IsAny<string>(), "INBOX", 42u, "2", It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Success(new MailAttachmentContent
             {
                 Content = new byte[] { 1, 2, 3 },
                 FileName = "report.pdf",
                 ContentType = "application/pdf"
             }));

    var result = await CreateController().GetAttachment("INBOX", 42, "2", CancellationToken.None);

    var file = Assert.IsType<FileContentResult>(result);
    Assert.Equal("application/pdf", file.ContentType);
    Assert.Equal("report.pdf", file.FileDownloadName);
}
```

- [ ] **Step 5: Verify and commit**

Run: `dotnet test` → all green.

```bash
git add -A
git commit -m "Serve message detail and attachment download"
```

---

### Task 9: Sliding session renewal and user-existence cache

**Files:**
- Create: `Authentication/Middleware/SlidingSessionMiddleware.cs`
- Modify: `Authentication/Extensions/AuthorizationExtension.cs` (cache the per-request lookup)
- Modify: `Program.cs` (middleware registration, memory cache)
- Test: `snoopy.microservice.Tests/Authentication/SlidingSessionMiddlewareTests.cs`

**Interfaces:**
- Consumes: `ITokenManager` (to reissue a JWT), `IMailCredentialStore` (Task 2), `IOptions<TokenConstants>`.
- Produces: middleware that, on an authenticated request whose token is past **half** its lifetime, reissues both cookies with a fresh expiry.

**Why:** a 30-minute JWT with no renewal logs the user out mid-reading. A refresh token would need an endpoint and a store; sliding renewal of the cookies achieves the same with neither. The credentials cookie must be renewed in the **same** step, or it would expire while the JWT lives on.

- [ ] **Step 1: Write the failing test**

```csharp
// snoopy.microservice.Tests/Authentication/SlidingSessionMiddlewareTests.cs
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Claims;
using weesky.Snoopy.Microservice.Authentication.Middleware;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Services;

namespace snoopy.microservice.Tests.Authentication
{
    public class SlidingSessionMiddlewareTests
    {
        private readonly Mock<ITokenManager> _tokens = new();
        private readonly Mock<IMailCredentialStore> _credentials = new();
        private readonly TokenConstants _constants = new() { ExpiryInMinutes = 30, AuthCookieName = "BearerAuth" };

        private HttpContext CreateContext(bool authenticated, DateTimeOffset issuedAt)
        {
            var context = new DefaultHttpContext();

            if (authenticated)
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.Upn, "alice"),
                    new Claim(ClaimTypes.Dns, "weesky.be"),
                    new Claim("iat", issuedAt.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
                };
                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            }

            return context;
        }

        private SlidingSessionMiddleware CreateSut(RequestDelegate? next = null)
            => new(next ?? (_ => Task.CompletedTask), Options.Create(_constants));

        [Fact]
        public async Task Invoke_RenewsBothCookiesPastHalfLife()
        {
            _tokens.Setup(t => t.Generate(It.IsAny<string>(), It.IsAny<string>()))
                   .Returns(new AuthToken { Token = "fresh" });
            _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>())).Returns(Result.Success("hunter2"));

            var context = CreateContext(authenticated: true, issuedAt: DateTimeOffset.UtcNow.AddMinutes(-20));

            await CreateSut().InvokeAsync(context, _tokens.Object, _credentials.Object);

            Assert.Contains("BearerAuth=fresh", context.Response.Headers.SetCookie.ToString());
            _credentials.Verify(c => c.Store(It.IsAny<HttpResponse>(), "hunter2", It.IsAny<TimeSpan>()), Times.Once);
        }

        [Fact]
        public async Task Invoke_DoesNotRenewBeforeHalfLife()
        {
            var context = CreateContext(authenticated: true, issuedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

            await CreateSut().InvokeAsync(context, _tokens.Object, _credentials.Object);

            _tokens.Verify(t => t.Generate(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
            _credentials.Verify(c => c.Store(It.IsAny<HttpResponse>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
        }

        [Fact]
        public async Task Invoke_DoesNothingForAnUnauthenticatedRequest()
        {
            var context = CreateContext(authenticated: false, issuedAt: DateTimeOffset.UtcNow.AddMinutes(-20));

            await CreateSut().InvokeAsync(context, _tokens.Object, _credentials.Object);

            _tokens.Verify(t => t.Generate(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Invoke_DoesNotRenewWhenCredentialsAreGone()
        {
            _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>())).Returns(Result.Failure<string>("credentials_unavailable"));
            var context = CreateContext(authenticated: true, issuedAt: DateTimeOffset.UtcNow.AddMinutes(-20));

            await CreateSut().InvokeAsync(context, _tokens.Object, _credentials.Object);

            // Renewing the JWT alone would produce a session that looks alive but cannot open IMAP.
            _tokens.Verify(t => t.Generate(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Invoke_AlwaysCallsTheNextMiddleware()
        {
            var called = false;
            var sut = CreateSut(_ => { called = true; return Task.CompletedTask; });

            await sut.InvokeAsync(CreateContext(false, DateTimeOffset.UtcNow), _tokens.Object, _credentials.Object);

            Assert.True(called);
        }
    }
}
```

Read `ITokenManager` and `AuthToken` first: if `Generate` has a different name or signature, adapt the test and the implementation to the real one and note it in your report.

- [ ] **Step 2: Run to verify it fails, then implement**

```csharp
// Authentication/Middleware/SlidingSessionMiddleware.cs
using Microsoft.Extensions.Options;
using System.Security.Claims;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Authentication.Middleware
{
    /// <summary>
    /// Extends a live session in place rather than letting it expire mid-use. A webmail is
    /// open for hours; a 30-minute token with no renewal would sign the user out while they
    /// read. Both cookies are renewed together — renewing the JWT alone would leave a session
    /// that looks alive but can no longer open IMAP.
    /// </summary>
    public class SlidingSessionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IOptions<TokenConstants> _tokenConstants;

        public SlidingSessionMiddleware(RequestDelegate next, IOptions<TokenConstants> tokenConstants)
        {
            _next = next;
            _tokenConstants = tokenConstants;
        }

        public async Task InvokeAsync(HttpContext context, ITokenManager tokens, IMailCredentialStore credentials)
        {
            TryRenew(context, tokens, credentials);
            await _next(context);
        }

        private void TryRenew(HttpContext context, ITokenManager tokens, IMailCredentialStore credentials)
        {
            if (context.User?.Identity?.IsAuthenticated != true) return;

            var name = context.User.FindFirst(ClaimTypes.Upn)?.Value;
            var domain = context.User.FindFirst(ClaimTypes.Dns)?.Value;
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(domain)) return;

            var issuedAtClaim = context.User.FindFirst("iat")?.Value;
            if (!long.TryParse(issuedAtClaim, out var issuedAtUnix)) return;

            var lifetime = TimeSpan.FromMinutes(_tokenConstants.Value.ExpiryInMinutes);
            var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(issuedAtUnix);
            if (age < lifetime / 2) return;

            // Renew both or neither.
            var password = credentials.Retrieve(context.Request);
            if (password.IsFailure) return;

            var token = tokens.Generate(name, domain);

            context.Response.Cookies.Append(_tokenConstants.Value.AuthCookieName, token.Token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.Add(lifetime)
            });

            credentials.Store(context.Response, password.Value, lifetime);
        }
    }
}
```

Register in `Program.cs` **after** `app.UseAuthentication()` and **before** `app.UseAuthorization()` (line 195-196), so the principal exists but authorization has not short-circuited:

```csharp
app.UseMiddleware<SlidingSessionMiddleware>();
```

- [ ] **Step 3: Cache the per-request user lookup**

In `AuthorizationExtension.OnTokenValidated`, the `FindByEmailAsync` call runs on **every** request. A mail client is far chattier than the alias panel this was written for. Wrap it in a short-lived cache:

```csharp
// replace the repo lookup block
var email = $"{name}@{domain}";
var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();

var exists = await cache.GetOrCreateAsync($"user-exists:{email}", async entry =>
{
    // Short TTL: a deleted or disabled account keeps working for at most this long.
    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60);
    var repo = context.HttpContext.RequestServices.GetRequiredService<IUsersRepository>();
    return await repo.FindByEmailAsync(email) != null;
});

if (!exists)
{
    context.Fail("User no longer exists");
}
```

Add `using Microsoft.Extensions.Caching.Memory;` and register `builder.Services.AddMemoryCache();` in `Program.cs` if it is not already registered — grep first.

- [ ] **Step 4: Verify and commit**

Run: `dotnet test` → all green. `dotnet build` → succeeds.

```bash
git add -A
git commit -m "Renew sessions in place and cache the user-existence check"
```

---

### Task 10: Extend the API client

**Files:**
- Modify: `src/frontend/src/api.js`
- Test: `src/frontend/src/api.test.js` (extend)

**Interfaces:**
- Produces: `ApiError extends Error` with `.status` and `.code`; `request(method, path, body, options)` accepting `{ signal }`; `requestBlob(path, options)` returning `{ blob, fileName }`. All existing `api` methods keep their signatures.

**Why:** the current helper throws a bare `Error`, so the HTTP status is lost and a deleted message (404) is indistinguishable from a server fault (500). It has no `AbortSignal`, so switching folders quickly races stale responses into the UI. And it always parses JSON, so an attachment cannot be fetched.

- [ ] **Step 1: Write the failing tests**

```js
// add to src/frontend/src/api.test.js
import { api, ApiError, requestBlob } from './api.js'

describe('ApiError', () => {
  it('carries the HTTP status', async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: false, status: 502, statusText: 'Bad Gateway',
      text: async () => JSON.stringify({ message: 'Unable to connect to the mail service' }),
    })

    await expect(api.getMailFolders()).rejects.toMatchObject({
      name: 'ApiError',
      status: 502,
      message: 'Unable to connect to the mail service',
    })
  })

  it('exposes the backend error string as a code', async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: false, status: 401, statusText: 'Unauthorized',
      text: async () => JSON.stringify({ message: 'credentials_unavailable' }),
    })

    await expect(api.getMailFolders()).rejects.toMatchObject({
      status: 401,
      code: 'credentials_unavailable',
    })
  })

  it('is still an Error, so existing catch blocks keep working', async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: false, status: 400, statusText: 'Bad Request',
      text: async () => 'A folder name is required',
    })

    await expect(api.getMailFolders()).rejects.toBeInstanceOf(Error)
  })
})

describe('abort support', () => {
  it('passes the signal through to fetch', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 200, json: async () => [] })
    global.fetch = fetchMock
    const controller = new AbortController()

    await api.getMailFolders({ signal: controller.signal })

    expect(fetchMock.mock.calls[0][1].signal).toBe(controller.signal)
  })
})

describe('requestBlob', () => {
  it('returns the blob and the file name from Content-Disposition', async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: { get: (h) => (h.toLowerCase() === 'content-disposition' ? 'attachment; filename="report.pdf"' : null) },
      blob: async () => new Blob(['pdf']),
    })

    const result = await requestBlob('/api/Mail/Messages/Attachment?folder=INBOX&uid=1&part=2')

    expect(result.fileName).toBe('report.pdf')
    expect(result.blob).toBeInstanceOf(Blob)
  })

  it('falls back to a default file name', async () => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true, status: 200,
      headers: { get: () => null },
      blob: async () => new Blob(['x']),
    })

    expect((await requestBlob('/x')).fileName).toBe('attachment')
  })
})
```

- [ ] **Step 2: Run to verify it fails**

Run: `npx vitest run src/api.test.js` → the new blocks fail (`ApiError` and `requestBlob` are not exported, `getMailFolders` does not exist).

- [ ] **Step 3: Implement**

Replace the `request` helper in `src/api.js` and add the new exports:

```js
/**
 * An HTTP failure that keeps its status. The backend puts a stable string in the
 * ResultEnveloppe message (for example "credentials_unavailable"), which is surfaced as
 * `code` so callers can branch on it without matching prose.
 */
export class ApiError extends Error {
  constructor(message, status, code) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
  }
}

async function readError(res) {
  const text = await res.text().catch(() => '')
  if (!text) return { message: res.statusText, code: null }
  try {
    const parsed = JSON.parse(text)
    const message = parsed?.message ?? parsed?.Message ?? text
    return { message, code: typeof message === 'string' ? message : null }
  } catch {
    return { message: text, code: null }
  }
}

async function request(method, path, body, options = {}) {
  const headers = {}
  if (body) headers['Content-Type'] = 'application/json'

  const res = await fetch(`${BASE}${path}`, {
    method,
    headers,
    credentials: 'include',
    body: body ? JSON.stringify(body) : undefined,
    signal: options.signal,
  })

  if (res.status === 401) {
    const { code } = await readError(res)
    clearSession()
    unauthorizedHandler?.()
    throw new ApiError('Unauthorized', 401, code)
  }

  if (res.status === 204) return null

  if (!res.ok) {
    const { message, code } = await readError(res)
    throw new ApiError(message || res.statusText, res.status, code)
  }

  return res.json()
}

/**
 * Fetches a binary response — attachments. Kept separate from request() because that helper
 * always parses JSON.
 */
export async function requestBlob(path, options = {}) {
  const res = await fetch(`${BASE}${path}`, {
    method: 'GET',
    credentials: 'include',
    signal: options.signal,
  })

  if (res.status === 401) {
    clearSession()
    unauthorizedHandler?.()
    throw new ApiError('Unauthorized', 401, null)
  }

  if (!res.ok) {
    const { message, code } = await readError(res)
    throw new ApiError(message || res.statusText, res.status, code)
  }

  const disposition = res.headers.get('content-disposition') ?? ''
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)

  return { blob: await res.blob(), fileName: match ? decodeURIComponent(match[1]) : 'attachment' }
}
```

Then add the mail methods to the `api` object, each forwarding `options`:

```js
  getMailFolders: (options) =>
    request('GET', '/api/Mail/Folders', undefined, options),

  createMailFolder: (parentPath, name) =>
    request('POST', '/api/Mail/Folders', { parentPath, name }),

  renameMailFolder: (path, newParentPath, newName) =>
    request('PUT', '/api/Mail/Folders', { path, newParentPath, newName }),

  deleteMailFolder: (path) =>
    request('DELETE', '/api/Mail/Folders', { path }),

  setMailFolderSubscription: (path, subscribed) =>
    request('PUT', '/api/Mail/Folders/Subscription', { path, subscribed }),

  getMailMessages: (folder, page, pageSize, options) =>
    request('GET', `/api/Mail/Messages?folder=${encodeURIComponent(folder)}&page=${page}&pageSize=${pageSize}`, undefined, options),

  getMailMessage: (folder, uid, options) =>
    request('GET', `/api/Mail/Messages/Detail?folder=${encodeURIComponent(folder)}&uid=${uid}`, undefined, options),
```

`encodeURIComponent` on the folder path is mandatory — the path may contain `/`, `&` or `#`.

- [ ] **Step 4: Verify**

Run: `npx vitest run src/api.test.js` → all pass, including the pre-existing 42. `npm run test` → 304 + new. `npm run lint` → clean.

If any pre-existing test asserted the exact `Error` constructor rather than the message, adapt it — `ApiError` is a subclass, so `toThrow('message')` and `toBeInstanceOf(Error)` both still hold.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "Give the API client status codes, abort support and binary responses"
```

---

### Task 11: TanStack Query and the mail query layer

**Files:**
- Modify: `src/frontend/package.json` (dependency), `src/frontend/src/App.tsx`
- Create: `src/frontend/src/modules/mail/api/mailTypes.ts`, `src/frontend/src/modules/mail/queries.ts`
- Test: `src/frontend/src/modules/mail/queries.test.tsx`

**Interfaces:**
- Consumes: `api.getMailFolders`, `api.getMailMessages`, `api.getMailMessage`, and the four folder mutations (Task 10); `useAuth().activeAccount` (from sub-project 1) — `{ id: 'primary', email, displayName, isPrimary }`.
- Produces: `mailKeys`, and the hooks `useFolders`, `useMessages`, `useMessage`, `useCreateFolder`, `useRenameFolder`, `useDeleteFolder`, `useSetFolderSubscription`.

**Query keys carry the active account id from day one** so slice 2d's account switching needs no rewrite.

- [ ] **Step 1: Install and wire the provider**

```bash
npm install @tanstack/react-query
```

In `src/App.tsx`, wrap the existing tree — outside `AuthProvider` so the client survives an auth state change:

```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // A mailbox changes on the server without telling us; refetch when the user comes back.
      refetchOnWindowFocus: true,
      staleTime: 30_000,
      retry: (failureCount, error) =>
        // Never retry an auth failure: it will not succeed, and it delays the redirect to /login.
        (error as { status?: number })?.status === 401 ? false : failureCount < 2,
    },
  },
})

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <AuthProvider>
          <RouterProvider router={router} />
        </AuthProvider>
      </ThemeProvider>
    </QueryClientProvider>
  )
}
```

- [ ] **Step 2: Define the types**

```ts
// src/modules/mail/api/mailTypes.ts
export interface MailFolderNode {
  path: string
  name: string
  specialUse: 'inbox' | 'sent' | 'drafts' | 'trash' | 'junk' | 'archive' | null
  selectable: boolean
  subscribed: boolean
  total: number | null
  unread: number | null
  uidValidity: number
  children: MailFolderNode[]
}

export interface MailMessageSummary {
  uid: number
  subject: string
  fromName: string
  fromAddress: string
  date: string
  seen: boolean
  flagged: boolean
  answered: boolean
  hasAttachments: boolean
  size: number
  preview: string
}

export interface MailFolderPage {
  folderPath: string
  uidValidity: number
  total: number
  page: number
  pageSize: number
  messages: MailMessageSummary[]
}

export interface MailAttachmentInfo {
  part: string
  fileName: string
  contentType: string
  size: number
  isInline: boolean
}

export interface MailMessageDetail {
  uid: number
  folderPath: string
  uidValidity: number
  subject: string
  fromName: string
  fromAddress: string
  to: string[]
  cc: string[]
  date: string
  htmlBody: string
  textBody: string
  blockedImageCount: number
  attachments: MailAttachmentInfo[]
}
```

- [ ] **Step 3: Write the failing test**

```tsx
// src/modules/mail/queries.test.tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { mailKeys, useFolders, useMessages } from './queries'

const mocks = vi.hoisted(() => ({ getMailFolders: vi.fn(), getMailMessages: vi.fn() }))

vi.mock('../../api.js', () => ({
  api: { getMailFolders: mocks.getMailFolders, getMailMessages: mocks.getMailMessages },
}))

vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary', email: 'alice@weesky.be' } }),
}))

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

describe('mailKeys', () => {
  it('scopes every key by account id', () => {
    expect(mailKeys.folders('primary')).toEqual(['mail', 'primary', 'folders'])
    expect(mailKeys.messages('primary', 'INBOX', 0)).toEqual(['mail', 'primary', 'messages', 'INBOX', 0])
    expect(mailKeys.message('primary', 'INBOX', 42)).toEqual(['mail', 'primary', 'message', 'INBOX', 42])
  })
})

describe('useFolders', () => {
  beforeEach(() => vi.clearAllMocks())

  it('loads the folder tree', async () => {
    mocks.getMailFolders.mockResolvedValue([{ path: 'INBOX', name: 'INBOX', children: [] }])

    const { result } = renderHook(() => useFolders(), { wrapper })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(result.current.data?.[0].path).toBe('INBOX')
  })
})

describe('useMessages', () => {
  beforeEach(() => vi.clearAllMocks())

  it('does not fetch until a folder is selected', () => {
    renderHook(() => useMessages(null, 0), { wrapper })

    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  it('requests the selected folder and page', async () => {
    mocks.getMailMessages.mockResolvedValue({ folderPath: 'INBOX', messages: [], total: 0, page: 1, pageSize: 50 })

    const { result } = renderHook(() => useMessages('INBOX', 1), { wrapper })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))
    expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 1, 50, expect.anything())
  })
})
```

- [ ] **Step 4: Run to verify it fails, then implement**

```ts
// src/modules/mail/queries.ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api.js'
import { useAuth } from '../../contexts/AuthContext'
import type { MailFolderNode, MailFolderPage, MailMessageDetail } from './api/mailTypes'

export const PAGE_SIZE = 50

/**
 * Every key is scoped by the active account, so switching accounts in slice 2d isolates
 * caches instead of mixing two mailboxes together.
 */
export const mailKeys = {
  all: (accountId: string) => ['mail', accountId] as const,
  folders: (accountId: string) => ['mail', accountId, 'folders'] as const,
  messages: (accountId: string, folder: string, page: number) =>
    ['mail', accountId, 'messages', folder, page] as const,
  message: (accountId: string, folder: string, uid: number) =>
    ['mail', accountId, 'message', folder, uid] as const,
}

function useAccountId(): string {
  return useAuth().activeAccount?.id ?? 'primary'
}

export function useFolders() {
  const accountId = useAccountId()

  return useQuery<MailFolderNode[]>({
    queryKey: mailKeys.folders(accountId),
    queryFn: ({ signal }) => api.getMailFolders({ signal }),
  })
}

export function useMessages(folderPath: string | null, page: number) {
  const accountId = useAccountId()

  return useQuery<MailFolderPage>({
    queryKey: mailKeys.messages(accountId, folderPath ?? '', page),
    queryFn: ({ signal }) => api.getMailMessages(folderPath, page, PAGE_SIZE, { signal }),
    enabled: folderPath !== null,
    placeholderData: (previous) => previous,   // keeps the list on screen while paging
  })
}

export function useMessage(folderPath: string | null, uid: number | null) {
  const accountId = useAccountId()

  return useQuery<MailMessageDetail>({
    queryKey: mailKeys.message(accountId, folderPath ?? '', uid ?? 0),
    queryFn: ({ signal }) => api.getMailMessage(folderPath, uid, { signal }),
    enabled: folderPath !== null && uid !== null,
  })
}

/** Folder mutations all invalidate the tree: counts and hierarchy both change. */
function useFolderMutation<TArgs>(mutationFn: (args: TArgs) => Promise<unknown>) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) }),
  })
}

export const useCreateFolder = () =>
  useFolderMutation<{ parentPath: string; name: string }>(
    ({ parentPath, name }) => api.createMailFolder(parentPath, name))

export const useRenameFolder = () =>
  useFolderMutation<{ path: string; newParentPath: string; newName: string }>(
    ({ path, newParentPath, newName }) => api.renameMailFolder(path, newParentPath, newName))

export const useDeleteFolder = () =>
  useFolderMutation<{ path: string }>(({ path }) => api.deleteMailFolder(path))

export const useSetFolderSubscription = () =>
  useFolderMutation<{ path: string; subscribed: boolean }>(
    ({ path, subscribed }) => api.setMailFolderSubscription(path, subscribed))
```

- [ ] **Step 5: Verify and commit**

Run: `npx vitest run src/modules/mail/queries.test.tsx` → pass. `npm run test`, `npm run typecheck`, `npm run lint`, `npm run build` → clean.

Existing tests that mount the router now need a `QueryClientProvider` if they render `App`; `App.test.tsx` mounts `routes` directly with its own providers, so check whether it needs one and add it there rather than changing `App.tsx`.

```bash
git add -A
git commit -m "Add TanStack Query and the mail query layer"
```

---

### Task 12: Mail design tokens, stylesheet and icon sizing

**Files:**
- Modify: `src/frontend/src/styles/theme-night.css`, `src/frontend/src/styles/theme-classic.css` (4 blocks total)
- Create: `src/frontend/src/styles/mail.css`
- Modify: `src/frontend/src/main.tsx` (import)
- Modify: the 9 files in `src/frontend/src/icons/` (add a `size` prop)
- Create: `src/frontend/src/icons/FolderIcon.tsx`, `ChevronRightIcon.tsx`, `PaperclipIcon.tsx`, `RefreshIcon.tsx`
- Test: `src/frontend/src/icons/icons.test.tsx`

**Interfaces:**
- Produces the tokens below, and every icon accepting `{ size?: number }` defaulting to its current hard-coded value.

- [ ] **Step 1: Add the tokens**

Ten new role tokens, in **all four** blocks. `--accent-unread` already exists in both palettes and is finally consumed by this slice.

`theme-night.css`, light block:
```css
  --list-row-hover: #f4f1ee;
  --list-row-selected-bg: #fdf3f0;
  --list-row-selected-fg: #182238;
  --list-row-unread-bg: #ffffff;
  --list-separator: #ece7e1;
  --badge-count-bg: #e2674a;
  --badge-count-fg: #ffffff;
  --reader-header-border: #ece7e1;
  --quote-text: #8a827a;
  --attachment-chip-bg: #f4f1ee;
```

`theme-night.css`, dark block:
```css
  --list-row-hover: #262a31;
  --list-row-selected-bg: #2a2420;
  --list-row-selected-fg: #f3efeb;
  --list-row-unread-bg: #212429;
  --list-separator: #2c3038;
  --badge-count-bg: #f0785c;
  --badge-count-fg: #1a0f0b;
  --reader-header-border: #2c3038;
  --quote-text: #8b857e;
  --attachment-chip-bg: #262a31;
```

`theme-classic.css`, light block:
```css
  --list-row-hover: #f0f2f5;
  --list-row-selected-bg: #dbe3f5;
  --list-row-selected-fg: #25397a;
  --list-row-unread-bg: #ffffff;
  --list-separator: #e6e9ee;
  --badge-count-bg: #3450a3;
  --badge-count-fg: #ffffff;
  --reader-header-border: #dde1e7;
  --quote-text: #7c8593;
  --attachment-chip-bg: #f0f2f5;
```

`theme-classic.css`, dark block:
```css
  --list-row-hover: #2f3849;
  --list-row-selected-bg: #33405c;
  --list-row-selected-fg: #bcd2ec;
  --list-row-unread-bg: #272d38;
  --list-separator: #333b49;
  --badge-count-bg: #84aad8;
  --badge-count-fg: #12161c;
  --reader-header-border: #333b49;
  --quote-text: #828d9e;
  --attachment-chip-bg: #2f3849;
```

- [ ] **Step 2: Create the stylesheet**

`src/styles/mail.css` — layout for the three columns, the folder tree, the list rows and the reader. Every colour goes through a token; no literal hex anywhere in this file.

```css
/* Mail module. Three independently scrolling columns inside the shell's single outlet. */

.mail-layout {
  display: flex;
  height: 100%;
  overflow: hidden;   /* .app-content sets overflow:auto; the columns scroll, not the page */
}

.mail-folders {
  width: 240px;
  flex: none;
  overflow-y: auto;
  border-right: 1px solid var(--border);
  padding: 12px 8px;
}

.mail-list {
  width: 380px;
  flex: none;
  overflow-y: auto;
  border-right: 1px solid var(--border);
  background: var(--surface);
}

.mail-reader {
  flex: 1;
  min-width: 0;
  overflow-y: auto;
  background: var(--surface);
}

/* Folder tree */
.folder-row {
  display: flex;
  align-items: center;
  gap: 6px;
  width: 100%;
  padding: 6px 8px;
  border: none;
  border-radius: var(--radius-sm);
  background: none;
  color: var(--text);
  font: inherit;
  text-align: left;
  cursor: pointer;
}

.folder-row:hover { background: var(--pane-item-hover); }

.folder-row.is-active {
  background: var(--pane-item-active-bg);
  color: var(--pane-item-active-fg);
  font-weight: 600;
}

.folder-row-name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

.folder-row-count {
  min-width: 20px;
  padding: 1px 6px;
  border-radius: 9px;
  background: var(--badge-count-bg);
  color: var(--badge-count-fg);
  font-size: 11px;
  font-weight: 600;
  text-align: center;
}

.folder-children { margin-left: 14px; }

.folder-toggle {
  border: none;
  background: none;
  padding: 0;
  color: var(--text-muted);
  cursor: pointer;
  display: flex;
}

.folder-toggle.is-open { transform: rotate(90deg); }

/* Message list */
.message-row {
  display: block;
  width: 100%;
  padding: 10px 12px;
  border: none;
  border-bottom: 1px solid var(--list-separator);
  background: var(--surface);
  color: var(--text);
  font: inherit;
  text-align: left;
  cursor: pointer;
}

.message-row:hover { background: var(--list-row-hover); }

.message-row.is-selected {
  background: var(--list-row-selected-bg);
  color: var(--list-row-selected-fg);
  box-shadow: inset 3px 0 0 var(--accent-unread);
}

.message-row.is-unread { background: var(--list-row-unread-bg); }
.message-row.is-unread .message-row-from,
.message-row.is-unread .message-row-subject { font-weight: 700; }

.message-row-top { display: flex; align-items: baseline; gap: 8px; }
.message-row-from { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.message-row-date { color: var(--text-muted); font-size: 11px; flex: none; }
.message-row-subject { margin-top: 2px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.message-row-preview { margin-top: 2px; color: var(--text-muted); font-size: 12px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.message-row-unread-dot { width: 7px; height: 7px; border-radius: 50%; background: var(--accent-unread); flex: none; }

/* Reader */
.reader-header { padding: 18px 22px; border-bottom: 1px solid var(--reader-header-border); }
.reader-subject { font-size: 18px; font-weight: 650; margin-bottom: 8px; }
.reader-meta { color: var(--text-muted); font-size: 12px; display: flex; gap: 10px; flex-wrap: wrap; }

.reader-blocked-images {
  display: flex;
  align-items: center;
  gap: 10px;
  margin: 12px 22px 0;
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface-sunken);
  font-size: 13px;
}

.reader-body { width: 100%; border: none; }
.reader-text { padding: 18px 22px; white-space: pre-wrap; font-family: var(--font); }

.reader-attachments { display: flex; flex-wrap: wrap; gap: 8px; padding: 12px 22px; border-top: 1px solid var(--reader-header-border); }

.attachment-chip {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--attachment-chip-bg);
  color: var(--text);
  font: inherit;
  font-size: 12px;
  cursor: pointer;
}

.attachment-chip-size { color: var(--text-muted); }

.mail-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: var(--text-muted);
}
```

Import it in `main.tsx` after `shell.css`.

- [ ] **Step 3: Give the icons a size prop**

Every icon in `src/icons/` currently hard-codes its dimensions and takes no props, which forced `AccountPage` to define its own `CheckIcon`/`XIcon` inline. Add to each:

```tsx
export default function MailIcon({ size = 20 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6">
      {/* unchanged paths */}
    </svg>
  )
}
```

Keep each file's existing default dimension as the default value — `TrashIcon` already has a `size` prop from sub-project 1; leave its default at 15. Add four new icons in the same style: `FolderIcon`, `ChevronRightIcon`, `PaperclipIcon`, `RefreshIcon`.

- [ ] **Step 4: Test the icon contract**

```tsx
// src/icons/icons.test.tsx
import { describe, it, expect } from 'vitest'
import { render } from '@testing-library/react'
import MailIcon from './MailIcon'
import FolderIcon from './FolderIcon'

describe('icons', () => {
  it('render at their default size', () => {
    const { container } = render(<MailIcon />)
    expect(container.querySelector('svg')).toHaveAttribute('width', '20')
  })

  it('accept a size override', () => {
    const { container } = render(<FolderIcon size={14} />)
    const svg = container.querySelector('svg')
    expect(svg).toHaveAttribute('width', '14')
    expect(svg).toHaveAttribute('height', '14')
  })

  it('inherit colour from the surrounding text', () => {
    const { container } = render(<MailIcon />)
    expect(container.querySelector('svg')).toHaveAttribute('stroke', 'currentColor')
  })
})
```

- [ ] **Step 5: Verify and commit**

Run: `npm run test`, `npm run typecheck`, `npm run lint`, `npm run build` → clean.

```bash
git add -A
git commit -m "Add mail tokens, stylesheet and sizable icons"
```

---

### Task 13: Mail layout, route and folder tree

**Files:**
- Create: `src/frontend/src/modules/mail/MailLayout.tsx`, `src/frontend/src/modules/mail/folders/FolderTree.tsx`, `src/frontend/src/modules/mail/folders/FolderDialogs.tsx`
- Modify: `src/frontend/src/routes.tsx`, `src/frontend/src/components/ComingSoon.tsx`, `src/frontend/src/App.test.tsx`
- Test: `src/frontend/src/modules/mail/folders/FolderTree.test.tsx`, `src/frontend/src/modules/mail/MailLayout.test.tsx`

**Interfaces:**
- Consumes: `useFolders`, `useCreateFolder`, `useRenameFolder`, `useDeleteFolder`, `useSetFolderSubscription` (Task 11); `DeleteConfirmModal` and `useToasts`/`Toasts` from sub-project 1.
- Produces: `MailLayout` owning the three columns and the selection state, `FolderTree` with `{ folders, selectedPath, onSelect }`.

**Deviation from the spec, deliberate.** Spec § 6.1 proposes `/mail/:folderPath?`. That cannot work: the hierarchy separator may be `/`, which is exactly why the API keeps folder paths out of route segments. **State lives in search params instead — `/mail?folder=INBOX&uid=42`** — which keeps deep links and the back button while surviving any separator. Record this deviation in your report.

- [ ] **Step 1: Write the failing folder-tree test**

```tsx
// src/frontend/src/modules/mail/folders/FolderTree.test.tsx
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import FolderTree from './FolderTree'
import type { MailFolderNode } from '../api/mailTypes'

function node(partial: Partial<MailFolderNode>): MailFolderNode {
  return {
    path: 'X', name: 'X', specialUse: null, selectable: true, subscribed: true,
    total: 0, unread: 0, uidValidity: 1, children: [], ...partial,
  }
}

const tree: MailFolderNode[] = [
  node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox', unread: 4 }),
  node({
    path: 'Projects', name: 'Projects',
    children: [node({ path: 'Projects/Alpha', name: 'Alpha', unread: 2 })],
  }),
  node({ path: 'Hidden', name: 'Hidden', subscribed: false }),
]

describe('FolderTree', () => {
  it('renders subscribed folders and hides unsubscribed ones', () => {
    render(<FolderTree folders={tree} selectedPath="INBOX" onSelect={vi.fn()} />)

    expect(screen.getByText('INBOX')).toBeInTheDocument()
    expect(screen.getByText('Projects')).toBeInTheDocument()
    expect(screen.queryByText('Hidden')).not.toBeInTheDocument()
  })

  it('shows unread counts only when non-zero', () => {
    render(<FolderTree folders={tree} selectedPath="INBOX" onSelect={vi.fn()} />)

    expect(screen.getByText('4')).toBeInTheDocument()
    expect(screen.queryByText('0')).not.toBeInTheDocument()
  })

  it('marks the selected folder', () => {
    render(<FolderTree folders={tree} selectedPath="INBOX" onSelect={vi.fn()} />)

    expect(screen.getByRole('button', { name: /INBOX/ })).toHaveClass('is-active')
  })

  it('calls onSelect with the folder path', () => {
    const onSelect = vi.fn()
    render(<FolderTree folders={tree} selectedPath="INBOX" onSelect={onSelect} />)

    fireEvent.click(screen.getByRole('button', { name: /Projects/ }))

    expect(onSelect).toHaveBeenCalledWith('Projects')
  })

  it('expands a parent to reveal its children', () => {
    render(<FolderTree folders={tree} selectedPath="INBOX" onSelect={vi.fn()} />)

    expect(screen.queryByText('Alpha')).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /expand Projects/i }))
    expect(screen.getByText('Alpha')).toBeInTheDocument()
  })

  it('does not select an unselectable container folder', () => {
    const onSelect = vi.fn()
    render(
      <FolderTree
        folders={[node({ path: 'Container', name: 'Container', selectable: false })]}
        selectedPath={null}
        onSelect={onSelect}
      />)

    fireEvent.click(screen.getByRole('button', { name: /Container/ }))

    expect(onSelect).not.toHaveBeenCalled()
  })
})
```

- [ ] **Step 2: Run to verify it fails, then implement the tree**

```tsx
// src/modules/mail/folders/FolderTree.tsx
import { useState } from 'react'
import ChevronRightIcon from '../../../icons/ChevronRightIcon'
import type { MailFolderNode } from '../api/mailTypes'

interface Props {
  folders: MailFolderNode[]
  selectedPath: string | null
  onSelect: (path: string) => void
}

/** Well-known folders first, in reading order, then the rest alphabetically. */
const SPECIAL_ORDER = ['inbox', 'drafts', 'sent', 'archive', 'junk', 'trash']

function sortFolders(folders: MailFolderNode[]): MailFolderNode[] {
  return [...folders].sort((a, b) => {
    const rankA = a.specialUse ? SPECIAL_ORDER.indexOf(a.specialUse) : SPECIAL_ORDER.length
    const rankB = b.specialUse ? SPECIAL_ORDER.indexOf(b.specialUse) : SPECIAL_ORDER.length
    return rankA !== rankB ? rankA - rankB : a.name.localeCompare(b.name)
  })
}

function FolderRow({ folder, depth, selectedPath, onSelect }: Props & { folder: MailFolderNode; depth: number }) {
  const [open, setOpen] = useState(folder.specialUse === 'inbox')
  const visibleChildren = sortFolders(folder.children.filter(c => c.subscribed))

  return (
    <>
      <div style={{ display: 'flex', alignItems: 'center' }}>
        {visibleChildren.length > 0 ? (
          <button
            type="button"
            className={open ? 'folder-toggle is-open' : 'folder-toggle'}
            aria-label={`${open ? 'Collapse' : 'Expand'} ${folder.name}`}
            onClick={() => setOpen(o => !o)}
          >
            <ChevronRightIcon size={14} />
          </button>
        ) : (
          <span style={{ width: 14, flex: 'none' }} />
        )}

        <button
          type="button"
          className={folder.path === selectedPath ? 'folder-row is-active' : 'folder-row'}
          onClick={() => { if (folder.selectable) onSelect(folder.path) }}
          aria-current={folder.path === selectedPath ? 'true' : undefined}
        >
          <span className="folder-row-name">{folder.name}</span>
          {folder.unread ? <span className="folder-row-count">{folder.unread}</span> : null}
        </button>
      </div>

      {open && visibleChildren.length > 0 && (
        <div className="folder-children">
          {visibleChildren.map(child => (
            <FolderRow
              key={child.path}
              folder={child}
              depth={depth + 1}
              folders={[]}
              selectedPath={selectedPath}
              onSelect={onSelect}
            />
          ))}
        </div>
      )}
    </>
  )
}

export default function FolderTree({ folders, selectedPath, onSelect }: Props) {
  return (
    <nav aria-label="Folders">
      {sortFolders(folders.filter(f => f.subscribed)).map(folder => (
        <FolderRow
          key={folder.path}
          folder={folder}
          depth={0}
          folders={[]}
          selectedPath={selectedPath}
          onSelect={onSelect}
        />
      ))}
    </nav>
  )
}
```

- [ ] **Step 3: Implement the layout and folder management**

`MailLayout` owns selection via search params, renders the three columns, and hosts the folder-management controls:

```tsx
// src/modules/mail/MailLayout.tsx
import { useSearchParams } from 'react-router-dom'
import { useFolders } from './queries'
import FolderTree from './folders/FolderTree'
import MessageList from './list/MessageList'      // Task 14
import MessageReader from './reader/MessageReader' // Task 15

export default function MailLayout() {
  const [params, setParams] = useSearchParams()
  const { data: folders, isLoading, isError } = useFolders()

  // Folder paths may contain '/', so selection lives in search params rather than a route
  // segment — deep links and the back button still work.
  const folder = params.get('folder')
  const uidParam = params.get('uid')
  const uid = uidParam ? Number(uidParam) : null

  function selectFolder(path: string) {
    setParams({ folder: path })   // clears uid: a message id is meaningless in another folder
  }

  function selectMessage(nextUid: number) {
    if (!folder) return
    setParams({ folder, uid: String(nextUid) })
  }

  return (
    <div className="mail-layout">
      <div className="mail-folders">
        {isLoading && <p className="mail-empty">Loading folders…</p>}
        {isError && <p className="mail-empty">Could not load folders.</p>}
        {folders && <FolderTree folders={folders} selectedPath={folder} onSelect={selectFolder} />}
      </div>

      <div className="mail-list">
        <MessageList folderPath={folder} selectedUid={uid} onSelect={selectMessage} />
      </div>

      <div className="mail-reader">
        <MessageReader folderPath={folder} uid={uid} />
      </div>
    </div>
  )
}
```

`FolderDialogs.tsx` holds folder management. It is mounted from `MailLayout` above the tree and owns its own dialog state:

```tsx
// src/modules/mail/folders/FolderDialogs.tsx
import { useState } from 'react'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import { useCreateFolder, useDeleteFolder, useRenameFolder, useSetFolderSubscription } from '../queries'
import type { MailFolderNode } from '../api/mailTypes'

interface Props {
  folders: MailFolderNode[]
  selectedPath: string | null
  onNotify: (message: string, type?: 'success' | 'error') => void
}

/** Flattens the tree so the parent picker and the manage list can show every folder. */
function flatten(nodes: MailFolderNode[], depth = 0): Array<{ node: MailFolderNode; depth: number }> {
  return nodes.flatMap(node => [{ node, depth }, ...flatten(node.children, depth + 1)])
}

export default function FolderDialogs({ folders, selectedPath, onNotify }: Props) {
  const [creating, setCreating] = useState(false)
  const [newName, setNewName] = useState('')
  const [newParent, setNewParent] = useState('')
  const [renaming, setRenaming] = useState<MailFolderNode | null>(null)
  const [renameValue, setRenameValue] = useState('')
  const [pendingDelete, setPendingDelete] = useState<MailFolderNode | null>(null)
  const [managing, setManaging] = useState(false)

  const createFolder = useCreateFolder()
  const renameFolder = useRenameFolder()
  const deleteFolder = useDeleteFolder()
  const setSubscription = useSetFolderSubscription()

  const all = flatten(folders)

  async function run(action: () => Promise<unknown>, success: string, failure: string) {
    try {
      await action()
      onNotify(success)
      return true
    } catch (error) {
      onNotify(error instanceof Error ? error.message : failure, 'error')
      return false
    }
  }

  return (
    <div className="folder-actions">
      <button type="button" className="btn" onClick={() => { setCreating(true); setNewParent(selectedPath ?? '') }}>
        New folder
      </button>
      <button type="button" className="btn" onClick={() => setManaging(m => !m)}>
        {managing ? 'Done' : 'Manage'}
      </button>

      {creating && (
        <div className="folder-form">
          <label>
            Name
            <input value={newName} onChange={e => setNewName(e.target.value)} autoFocus />
          </label>
          <label>
            Parent
            <select value={newParent} onChange={e => setNewParent(e.target.value)}>
              <option value="">(top level)</option>
              {all.map(({ node, depth }) => (
                <option key={node.path} value={node.path}>{' '.repeat(depth * 2)}{node.name}</option>
              ))}
            </select>
          </label>
          <button
            type="button"
            className="btn btn-primary"
            disabled={!newName.trim() || createFolder.isPending}
            onClick={async () => {
              const ok = await run(
                () => createFolder.mutateAsync({ parentPath: newParent, name: newName.trim() }),
                `Folder "${newName.trim()}" created`, 'Could not create the folder')
              if (ok) { setCreating(false); setNewName('') }
            }}
          >
            Create
          </button>
          <button type="button" className="btn" onClick={() => { setCreating(false); setNewName('') }}>Cancel</button>
        </div>
      )}

      {managing && (
        <ul className="folder-manage-list">
          {all.map(({ node, depth }) => (
            <li key={node.path} style={{ paddingLeft: depth * 12 }}>
              <label>
                <input
                  type="checkbox"
                  checked={node.subscribed}
                  aria-label={`Show ${node.name}`}
                  onChange={e => run(
                    () => setSubscription.mutateAsync({ path: node.path, subscribed: e.target.checked }),
                    e.target.checked ? `"${node.name}" is now visible` : `"${node.name}" is now hidden`,
                    'Could not change the folder visibility')}
                />
                {node.name}
              </label>
              <button
                type="button"
                className="btn"
                aria-label={`Rename ${node.name}`}
                onClick={() => { setRenaming(node); setRenameValue(node.name) }}
              >
                Rename
              </button>
              {node.specialUse !== 'inbox' && (
                <button
                  type="button"
                  className="btn btn-danger"
                  aria-label={`Delete ${node.name}`}
                  onClick={() => setPendingDelete(node)}
                >
                  Delete
                </button>
              )}
            </li>
          ))}
        </ul>
      )}

      {renaming && (
        <div className="folder-form">
          <label>
            New name
            <input value={renameValue} onChange={e => setRenameValue(e.target.value)} autoFocus />
          </label>
          <button
            type="button"
            className="btn btn-primary"
            disabled={!renameValue.trim() || renameFolder.isPending}
            onClick={async () => {
              const parent = renaming.path.slice(0, Math.max(0, renaming.path.length - renaming.name.length - 1))
              const ok = await run(
                () => renameFolder.mutateAsync({ path: renaming.path, newParentPath: parent, newName: renameValue.trim() }),
                'Folder renamed', 'Could not rename the folder')
              if (ok) setRenaming(null)
            }}
          >
            Rename
          </button>
          <button type="button" className="btn" onClick={() => setRenaming(null)}>Cancel</button>
        </div>
      )}

      {pendingDelete && (
        <DeleteConfirmModal
          entityLabel={pendingDelete.name}
          loading={deleteFolder.isPending}
          onClose={() => setPendingDelete(null)}
          onConfirm={async () => {
            const ok = await run(
              () => deleteFolder.mutateAsync({ path: pendingDelete.path }),
              `Folder "${pendingDelete.name}" deleted`, 'Could not delete the folder')
            if (ok) setPendingDelete(null)
          }}
        />
      )}
    </div>
  )
}
```

Deriving the parent path by slicing off the leaf name works for any separator, since the leaf name is known and cannot contain it (enforced in Task 5).

`DeleteConfirmModal` is reused rather than reimplemented: deleting a folder is destructive and interruptive, which is exactly the case that component exists for. Read its props before wiring — `{ entityLabel, onConfirm, onClose, loading, cancelClassName }`.

Add to `mail.css`:

```css
.folder-actions { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 10px; }
.folder-form { display: flex; flex-direction: column; gap: 6px; padding: 8px; margin-bottom: 10px; border: 1px solid var(--border); border-radius: var(--radius-sm); background: var(--surface-sunken); }
.folder-form label { display: flex; flex-direction: column; gap: 3px; font-size: 12px; color: var(--text-muted); }
.folder-manage-list { list-style: none; margin-bottom: 10px; }
.folder-manage-list li { display: flex; align-items: center; gap: 6px; padding: 3px 0; font-size: 13px; }
.folder-manage-list label { display: flex; align-items: center; gap: 6px; flex: 1; }
```

Mount it in `MailLayout` above `FolderTree`, passing `folders`, `selectedPath` and `addToast` from a layout-level `useToasts()`, and render `<Toasts toasts={toasts} onRemove={removeToast} />` at the end of the layout — the same pattern `AccountPage` uses.

**Tests for `FolderDialogs`** (`FolderDialogs.test.tsx`), mocking the four query hooks: creating with a blank name leaves the button disabled; a successful create calls `createFolder` with the chosen parent and notifies; a failed create surfaces the error message; the visibility checkbox calls `setSubscription` with the inverted value; the inbox offers no Delete button; deleting opens the confirm modal and only calls `deleteFolder` after confirmation.

- [ ] **Step 4: Wire the route**

In `routes.tsx`, replace `{ path: 'mail', element: <ComingSoon module="Mail" /> }` with a lazy-loaded layout, matching the pattern used for Rules and Admin:

```tsx
const MailLayout = lazy(() => import('./modules/mail/MailLayout'))
// ...
{ path: 'mail', element: <Suspense fallback={null}><MailLayout /></Suspense> },
```

Then remove the `module === 'Mail'` special case from `ComingSoon.tsx` — its links to Aliases and Rules existed only while Mail was a placeholder.

- [ ] **Step 5: Update the shell tests**

`App.test.tsx` asserts `/coming soon/i` on `/mail`. Its subject is gone, so replace — do not delete — those assertions with ones that prove the mail layout renders: the folder navigation landmark is present. Mock `api.getMailFolders` in that file's factory and wrap in `QueryClientProvider`.

- [ ] **Step 6: Verify and commit**

Run: `npm run test`, `npm run typecheck`, `npm run lint`, `npm run build` → clean.

```bash
git add -A
git commit -m "Add the mail layout and folder tree"
```

---

### Task 14: Message list panel

**Files:**
- Create: `src/frontend/src/modules/mail/list/MessageList.tsx`, `src/frontend/src/modules/mail/list/formatDate.ts`
- Test: `src/frontend/src/modules/mail/list/MessageList.test.tsx`, `formatDate.test.ts`

**Interfaces:**
- Consumes: `useMessages(folderPath, page)` and `PAGE_SIZE` (Task 11).
- Produces: `MessageList` with `{ folderPath: string | null, selectedUid: number | null, onSelect: (uid: number) => void }`.

- [ ] **Step 1: Write the failing date-format test**

```ts
// src/modules/mail/list/formatDate.test.ts
import { describe, it, expect } from 'vitest'
import { formatListDate } from './formatDate'

const now = new Date('2026-07-18T15:00:00Z')

describe('formatListDate', () => {
  it('shows a time for today', () => {
    expect(formatListDate('2026-07-18T09:30:00Z', now)).toMatch(/\d{1,2}:\d{2}/)
  })

  it('shows a day and month within the year', () => {
    expect(formatListDate('2026-03-04T09:30:00Z', now)).toMatch(/Mar/)
  })

  it('shows the year for an older message', () => {
    expect(formatListDate('2024-03-04T09:30:00Z', now)).toMatch(/2024/)
  })

  it('returns an empty string for an unparseable date', () => {
    expect(formatListDate('not-a-date', now)).toBe('')
  })
})
```

Implement:

```ts
// src/modules/mail/list/formatDate.ts
/**
 * List rows have one line for the date, so the precision shrinks as the message ages:
 * a time today, a day and month this year, a year beyond that.
 */
export function formatListDate(iso: string, now: Date = new Date()): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return ''

  const sameDay = date.toDateString() === now.toDateString()
  if (sameDay) return date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' })

  if (date.getFullYear() === now.getFullYear()) {
    return date.toLocaleDateString(undefined, { day: 'numeric', month: 'short' })
  }

  return date.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
}
```

- [ ] **Step 2: Write the failing list test**

```tsx
// src/modules/mail/list/MessageList.test.tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import MessageList from './MessageList'

const mocks = vi.hoisted(() => ({ getMailMessages: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: { getMailMessages: mocks.getMailMessages } }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const page = {
  folderPath: 'INBOX', uidValidity: 1, total: 2, page: 0, pageSize: 50,
  messages: [
    { uid: 2, subject: 'Re: facture', fromName: 'Alice Martin', fromAddress: 'alice@x.be',
      date: '2026-07-18T09:00:00Z', seen: false, flagged: false, answered: false,
      hasAttachments: true, size: 100, preview: 'Merci pour…' },
    { uid: 1, subject: 'Réunion', fromName: 'Bob', fromAddress: 'bob@x.be',
      date: '2026-07-17T09:00:00Z', seen: true, flagged: false, answered: false,
      hasAttachments: false, size: 90, preview: 'Mardi ?' },
  ],
}

describe('MessageList', () => {
  beforeEach(() => vi.clearAllMocks())

  it('prompts when no folder is selected', () => {
    render(<MessageList folderPath={null} selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    expect(screen.getByText(/select a folder/i)).toBeInTheDocument()
    expect(mocks.getMailMessages).not.toHaveBeenCalled()
  })

  it('renders sender, subject and preview', async () => {
    mocks.getMailMessages.mockResolvedValue(page)

    render(<MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    expect(await screen.findByText('Alice Martin')).toBeInTheDocument()
    expect(screen.getByText('Re: facture')).toBeInTheDocument()
    expect(screen.getByText('Merci pour…')).toBeInTheDocument()
  })

  it('marks unread rows', async () => {
    mocks.getMailMessages.mockResolvedValue(page)

    render(<MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    const unread = (await screen.findByText('Alice Martin')).closest('button')
    const read = screen.getByText('Bob').closest('button')
    expect(unread).toHaveClass('is-unread')
    expect(read).not.toHaveClass('is-unread')
  })

  it('marks the selected row', async () => {
    mocks.getMailMessages.mockResolvedValue(page)

    render(<MessageList folderPath="INBOX" selectedUid={1} onSelect={vi.fn()} />, { wrapper })

    expect((await screen.findByText('Bob')).closest('button')).toHaveClass('is-selected')
  })

  it('calls onSelect with the uid', async () => {
    mocks.getMailMessages.mockResolvedValue(page)
    const onSelect = vi.fn()

    render(<MessageList folderPath="INBOX" selectedUid={null} onSelect={onSelect} />, { wrapper })

    fireEvent.click(await screen.findByText('Alice Martin'))

    expect(onSelect).toHaveBeenCalledWith(2)
  })

  it('shows an empty state for an empty folder', async () => {
    mocks.getMailMessages.mockResolvedValue({ ...page, total: 0, messages: [] })

    render(<MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    expect(await screen.findByText(/no messages/i)).toBeInTheDocument()
  })

  it('pages forward and resets when the folder changes', async () => {
    mocks.getMailMessages.mockResolvedValue({ ...page, total: 120 })

    const { rerender } = render(
      <MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    fireEvent.click(await screen.findByRole('button', { name: /next page/i }))
    await waitFor(() => expect(mocks.getMailMessages).toHaveBeenCalledWith('INBOX', 1, 50, expect.anything()))

    rerender(<MessageList folderPath="Sent" selectedUid={null} onSelect={vi.fn()} />)
    await waitFor(() => expect(mocks.getMailMessages).toHaveBeenCalledWith('Sent', 0, 50, expect.anything()))
  })

  it('surfaces a load failure', async () => {
    mocks.getMailMessages.mockRejectedValue(new Error('boom'))

    render(<MessageList folderPath="INBOX" selectedUid={null} onSelect={vi.fn()} />, { wrapper })

    expect(await screen.findByText(/could not load/i)).toBeInTheDocument()
  })
})
```

- [ ] **Step 3: Implement**

```tsx
// src/modules/mail/list/MessageList.tsx
import { useEffect, useState } from 'react'
import PaperclipIcon from '../../../icons/PaperclipIcon'
import { PAGE_SIZE, useMessages } from '../queries'
import { formatListDate } from './formatDate'

interface Props {
  folderPath: string | null
  selectedUid: number | null
  onSelect: (uid: number) => void
}

export default function MessageList({ folderPath, selectedUid, onSelect }: Props) {
  const [page, setPage] = useState(0)

  // A page index means nothing in a different folder.
  useEffect(() => { setPage(0) }, [folderPath])

  const { data, isLoading, isError } = useMessages(folderPath, page)

  if (!folderPath) return <p className="mail-empty">Select a folder</p>
  if (isLoading && !data) return <p className="mail-empty">Loading messages…</p>
  if (isError) return <p className="mail-empty">Could not load messages.</p>
  if (!data || data.messages.length === 0) return <p className="mail-empty">No messages</p>

  const lastPage = Math.max(0, Math.ceil(data.total / PAGE_SIZE) - 1)

  return (
    <div>
      <ul style={{ listStyle: 'none' }}>
        {data.messages.map(message => {
          const classes = ['message-row']
          if (!message.seen) classes.push('is-unread')
          if (message.uid === selectedUid) classes.push('is-selected')

          return (
            <li key={message.uid}>
              <button type="button" className={classes.join(' ')} onClick={() => onSelect(message.uid)}>
                <div className="message-row-top">
                  {!message.seen && <span className="message-row-unread-dot" />}
                  <span className="message-row-from">{message.fromName || message.fromAddress}</span>
                  {message.hasAttachments && <PaperclipIcon size={13} />}
                  <span className="message-row-date">{formatListDate(message.date)}</span>
                </div>
                <div className="message-row-subject">{message.subject || '(no subject)'}</div>
                {message.preview && <div className="message-row-preview">{message.preview}</div>}
              </button>
            </li>
          )
        })}
      </ul>

      {lastPage > 0 && (
        <div style={{ display: 'flex', justifyContent: 'space-between', padding: '10px 12px' }}>
          <button type="button" className="btn" disabled={page === 0} onClick={() => setPage(p => p - 1)}>
            Previous page
          </button>
          <span style={{ color: 'var(--text-muted)', fontSize: 12 }}>{page + 1} / {lastPage + 1}</span>
          <button type="button" className="btn" disabled={page >= lastPage} onClick={() => setPage(p => p + 1)}>
            Next page
          </button>
        </div>
      )}
    </div>
  )
}
```

- [ ] **Step 4: Verify and commit**

Run: `npm run test`, `npm run typecheck`, `npm run lint` → clean.

```bash
git add -A
git commit -m "Add the message list panel"
```

---

### Task 15: Message reader panel

**Files:**
- Create: `src/frontend/src/modules/mail/reader/MessageReader.tsx`, `src/frontend/src/modules/mail/reader/formatSize.ts`
- Modify: `src/frontend/src/api.js` (attachment download helper)
- Test: `src/frontend/src/modules/mail/reader/MessageReader.test.tsx`, `formatSize.test.ts`

**Interfaces:**
- Consumes: `useMessage(folderPath, uid)` (Task 11), `requestBlob` (Task 10).
- Produces: `MessageReader` with `{ folderPath: string | null, uid: number | null }`.

**The HTML body is rendered in a sandboxed iframe** — `sandbox` with neither `allow-scripts` nor `allow-same-origin` — never with `dangerouslySetInnerHTML`. This is the second of the two independent barriers; the backend sanitiser (Task 6) is the first.

**Also add DOMPurify** (`npm install dompurify`) and run the body through it before setting
`srcDoc`. Decided during Task 6: the backend sanitiser and the client one then use different
parsers in different engines, so a parse divergence in one — the class of bug that
GHSA-pgww-w46g-26qg is — cannot propagate through the other. Test that a body containing
`<script>` and an `onerror` handler comes out inert after the client pass alone, so the test
proves the client barrier independently of the server's.

- [ ] **Step 1: Write the failing tests**

```tsx
// src/modules/mail/reader/MessageReader.test.tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import MessageReader from './MessageReader'

const mocks = vi.hoisted(() => ({ getMailMessage: vi.fn(), requestBlob: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: { getMailMessage: mocks.getMailMessage }, requestBlob: mocks.requestBlob }))
vi.mock('../../../contexts/AuthContext', () => ({ useAuth: () => ({ activeAccount: { id: 'primary' } }) }))

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const detail = {
  uid: 2, folderPath: 'INBOX', uidValidity: 1,
  subject: 'Re: facture', fromName: 'Alice Martin', fromAddress: 'alice@x.be',
  to: ['mick@weesky.be'], cc: [], date: '2026-07-18T09:00:00Z',
  htmlBody: '<p>Bonjour</p>', textBody: 'Bonjour', blockedImageCount: 0,
  attachments: [{ part: '2', fileName: 'report.pdf', contentType: 'application/pdf', size: 2048, isInline: false }],
}

describe('MessageReader', () => {
  beforeEach(() => vi.clearAllMocks())

  it('prompts when nothing is selected', () => {
    render(<MessageReader folderPath="INBOX" uid={null} />, { wrapper })

    expect(screen.getByText(/select a message/i)).toBeInTheDocument()
    expect(mocks.getMailMessage).not.toHaveBeenCalled()
  })

  it('renders the headers', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText('Re: facture')).toBeInTheDocument()
    expect(screen.getByText(/Alice Martin/)).toBeInTheDocument()
  })

  it('renders the body in a sandboxed iframe with no scripts and no same-origin', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    const iframe = container.querySelector('iframe')
    expect(iframe).toBeTruthy()
    const sandbox = iframe!.getAttribute('sandbox') ?? ''
    expect(sandbox).not.toContain('allow-scripts')
    expect(sandbox).not.toContain('allow-same-origin')
    expect(iframe!.getAttribute('srcdoc')).toContain('Bonjour')
  })

  it('offers to show blocked images and reveals them on demand', async () => {
    mocks.getMailMessage.mockResolvedValue({
      ...detail,
      blockedImageCount: 2,
      htmlBody: '<img data-blocked-src="https://t.example/p.gif">',
    })

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    fireEvent.click(await screen.findByRole('button', { name: /show images/i }))

    expect(container.querySelector('iframe')!.getAttribute('srcdoc')).toContain('src="https://t.example/p.gif"')
  })

  it('falls back to the text body when there is no HTML', async () => {
    mocks.getMailMessage.mockResolvedValue({ ...detail, htmlBody: '', textBody: 'plain only' })

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText('plain only')).toBeInTheDocument()
  })

  it('lists attachments with their size and downloads on click', async () => {
    mocks.getMailMessage.mockResolvedValue(detail)
    mocks.requestBlob.mockResolvedValue({ blob: new Blob(['x']), fileName: 'report.pdf' })
    global.URL.createObjectURL = vi.fn(() => 'blob:x')
    global.URL.revokeObjectURL = vi.fn()

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    fireEvent.click(await screen.findByRole('button', { name: /report\.pdf/ }))

    expect(mocks.requestBlob).toHaveBeenCalledWith(expect.stringContaining('part=2'))
  })

  it('hides inline parts from the attachment list', async () => {
    mocks.getMailMessage.mockResolvedValue({
      ...detail,
      attachments: [{ part: '3', fileName: 'logo.png', contentType: 'image/png', size: 10, isInline: true }],
    })

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    expect(screen.queryByRole('button', { name: /logo\.png/ })).not.toBeInTheDocument()
  })
})
```

Plus `formatSize.test.ts`: `0 → "0 B"`, `2048 → "2 KB"`, `1_500_000 → "1.4 MB"`.

- [ ] **Step 2: Implement**

```tsx
// src/modules/mail/reader/MessageReader.tsx
import { useEffect, useState } from 'react'
import { requestBlob } from '../../../api.js'
import PaperclipIcon from '../../../icons/PaperclipIcon'
import { useMessage } from '../queries'
import { formatSize } from './formatSize'

interface Props {
  folderPath: string | null
  uid: number | null
}

/** Swaps the sanitiser's data-blocked-src attributes back to src, on user consent only. */
function revealImages(html: string): string {
  return html.replace(/data-blocked-src=/g, 'src=')
}

export default function MessageReader({ folderPath, uid }: Props) {
  const { data, isLoading, isError } = useMessage(folderPath, uid)
  const [imagesShown, setImagesShown] = useState(false)

  // Consent is per message, never carried to the next one.
  useEffect(() => { setImagesShown(false) }, [folderPath, uid])

  if (uid === null) return <p className="mail-empty">Select a message</p>
  if (isLoading) return <p className="mail-empty">Loading message…</p>
  if (isError || !data) return <p className="mail-empty">Could not load this message.</p>

  const attachments = data.attachments.filter(a => !a.isInline)
  const body = imagesShown ? revealImages(data.htmlBody) : data.htmlBody

  async function download(part: string, fileName: string) {
    const result = await requestBlob(
      `/api/Mail/Messages/Attachment?folder=${encodeURIComponent(folderPath!)}&uid=${uid}&part=${encodeURIComponent(part)}`)

    const url = URL.createObjectURL(result.blob)
    const link = document.createElement('a')
    link.href = url
    link.download = result.fileName || fileName
    link.click()
    URL.revokeObjectURL(url)
  }

  return (
    <article>
      <header className="reader-header">
        <h1 className="reader-subject">{data.subject || '(no subject)'}</h1>
        <div className="reader-meta">
          <span>{data.fromName ? `${data.fromName} <${data.fromAddress}>` : data.fromAddress}</span>
          <span>{new Date(data.date).toLocaleString()}</span>
          {data.to.length > 0 && <span>To: {data.to.join(', ')}</span>}
          {data.cc.length > 0 && <span>Cc: {data.cc.join(', ')}</span>}
        </div>
      </header>

      {data.blockedImageCount > 0 && !imagesShown && (
        <div className="reader-blocked-images">
          <span>
            {data.blockedImageCount} remote image{data.blockedImageCount > 1 ? 's were' : ' was'} blocked.
            Loading them tells the sender you opened this message.
          </span>
          <button type="button" className="btn" onClick={() => setImagesShown(true)}>Show images</button>
        </div>
      )}

      {data.htmlBody ? (
        // Two independent barriers: the backend sanitised this, and the iframe can neither
        // run scripts nor reach our origin. Never render message HTML into the page itself.
        <iframe
          className="reader-body"
          sandbox=""
          title="Message body"
          srcDoc={body}
          style={{ height: '60vh' }}
        />
      ) : (
        <div className="reader-text">{data.textBody}</div>
      )}

      {attachments.length > 0 && (
        <div className="reader-attachments">
          {attachments.map(attachment => (
            <button
              key={attachment.part}
              type="button"
              className="attachment-chip"
              onClick={() => download(attachment.part, attachment.fileName)}
            >
              <PaperclipIcon size={13} />
              {attachment.fileName}
              <span className="attachment-chip-size">{formatSize(attachment.size)}</span>
            </button>
          ))}
        </div>
      )}
    </article>
  )
}
```

```ts
// src/modules/mail/reader/formatSize.ts
export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}
```

- [ ] **Step 3: Verify and commit**

Run: `npm run test`, `npm run typecheck`, `npm run lint`, `npm run build` → clean.

```bash
git add -A
git commit -m "Add the message reader panel"
```

---

### Task 16: Documentation, server prerequisite and final verification

**Files:**
- Modify: `src/frontend/CLAUDE.md`, `src/snoopy.microservice/CLAUDE.md`
- Create: `docs/superpowers/mail-2a-server-prerequisite.md`

**Interfaces:** none — documentation and verification.

- [ ] **Step 1: Write the server prerequisite note**

The systemd unit is not versioned (spec decision), so the change must be written down somewhere the next person will find it. Create `docs/superpowers/mail-2a-server-prerequisite.md` containing: the two units to edit (`snoopy.microservice` and `snoopy.microservice-dev`), the exact lines to add to `[Service]`:

```ini
StateDirectory=snoopy.microservice
StateDirectoryMode=0700
```

(with `snoopy.microservice-dev` for the dev unit), then `systemctl daemon-reload && systemctl restart <unit>`, and the verification: `/var/lib/<name>` exists at mode 0700, and the service log line `Data Protection key ring: /var/lib/<name>/keys` appears at startup. State plainly that **without this, the service refuses to start outside Development** — that is the intended loud failure, not a bug, and `Restart=always` will make it crash-loop every 60 seconds until fixed.

- [ ] **Step 2: Update the frontend CLAUDE.md**

Written against the final code, not from memory. Additions: the mail module directory layout; TanStack Query as the data layer, its query-key convention including the account-id scoping and why; that mail selection lives in **search params** rather than a route segment, with the separator reason; the new tokens; that `request()` now throws `ApiError` carrying `.status`/`.code` and accepts `{ signal }`, with `requestBlob` for binaries; and the rule that message HTML is only ever rendered in a sandboxed iframe. Correct the route table: `/mail` is no longer a placeholder.

- [ ] **Step 3: Update the backend CLAUDE.md**

Add `MailController` to the controller list with its eight endpoints; `MailFolderRepository` / `MailMessageRepository` to the repositories; `ImapConnectionFactory`, `ImapSession`, `MailCredentialStore` and `MailHtmlSanitizer` to the services. Record three conventions a newcomer would otherwise breach: folder paths never go in a route segment; IMAP failures are 502 and `credentials_unavailable` is 401; and the Data Protection key ring depends on `StateDirectory=` with a pointer to the prerequisite note.

- [ ] **Step 4: Full automated verification**

Backend, from `src/snoopy.microservice`:
```bash
dotnet build && dotnet test
```
Frontend, from `src/frontend`:
```bash
npm run lint && npm run typecheck && npm run test && npm run test:coverage && npm run build
```
Report the final test count and the coverage figure. Coverage must not regress against the 91.4 % recorded at the end of sub-project 1.

- [ ] **Step 5: Manual verification checklist for the human**

These cannot be run headless. Write them into your report as a checklist, in this order:

1. Apply the server prerequisite (Step 1), then confirm the key ring log line.
2. Sign in, then `systemctl restart snoopy.microservice-dev` — **the mail session must survive**. This is the single test that proves the key ring design works.
3. Folder tree matches Thunderbird's view of the same account, including hierarchy and special folders.
4. Create a folder in the webmail → appears in Thunderbird. Create one in Thunderbird → appears after a refresh. Rename and delete both ways.
5. Unsubscribe a folder → it disappears from the tree; resubscribe → it returns.
6. Open a message with an attachment; download it and check the bytes match.
7. Open an HTML message with remote images: nothing loads until "Show images" is clicked.
8. Send yourself a message containing `<script>alert(1)</script>` and an `onerror` handler: neither fires.
9. All four theme combinations (night/classic × light/dark) on the mail view — this is where hard-coded colours show.
10. Deep link `/mail?folder=<some folder>&uid=<some uid>` in a fresh tab; back button behaves.
11. Leave the session idle past 30 minutes with periodic activity — sliding renewal keeps it alive; with no activity at all, the next action lands cleanly on `/login`.
12. At 1024 px wide: three columns still usable, nothing overlaps.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Document the mail module and its server prerequisite"
```

---

## Post-plan notes for the executor

- **Read before you cut.** Tasks 5, 7 and 8 extend `ImapSession` and `MailController`, which earlier tasks created — read the current file before appending, and keep the established order of members.
- **MailKit signatures are the most likely source of friction.** When one differs from this plan, adapt it, keep the behaviour, and record the difference in your report. Do not change the *contract* (the interface and the models) to suit an API surprise without flagging it.
- The `PreviewText` fetch item (Task 7) is the single most likely thing to be unsupported by the server. Degrading to an empty preview is acceptable; failing the whole list fetch is not.
- Tasks 1-9 are backend-only and 10-15 frontend-only, so a frontend failure never means a backend regression and vice versa. Run only the relevant suite while iterating; run both before committing.
- The spec's `/mail/:folderPath?` route shape is wrong and Task 13 deviates from it deliberately — see that task's note.
