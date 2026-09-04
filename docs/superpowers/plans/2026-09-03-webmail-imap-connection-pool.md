# Pool de connexions IMAP — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Réutiliser les connexions IMAP authentifiées entre requêtes HTTP, sans qu'aucun secret ne soit jamais détenu côté serveur entre deux requêtes.

**Architecture:** Un singleton `ImapConnectionPool` détient des `ImapClient` connectés, indexés par `(hôte, port, sécurité, username, empreinte HMAC du credential)`, prêtés en exclusivité et rendus à la fin du scope de requête. `ScopedImapSessionProvider` est le seul appelant du pool ; les chemins d'authentification (`UserAuthenticator`, sonde de compte connecté) gardent `IImapConnectionFactory` et ne sont jamais poolés. `ImapSession` reste construit par requête et reçoit *comment* relâcher son client ; un balayeur ferme les sockets inactives, et `LoginController` purge à la déconnexion.

**Tech Stack:** .NET 10, C# 14, MailKit 4.17.0, ASP.NET Core DI / `IOptionsMonitor`, xUnit 2.9 + Moq 4.20, serveur IMAP scripté sur `TcpListener` (patron `FakeImapServer`).

**Spec:** `docs/superpowers/specs/2026-08-20-webmail-imap-connection-pool-design.md` — le plan argumente depuis la spec ; l'exécutant lit les deux.

## Global Constraints

- Branche de travail : `socket-pool` (déjà créée). Ne jamais pousser sans demander.
- Projet produit : `src/snoopy.microservice/snoopy.microservice.core.csproj` (namespace `weesky.Snoopy.Microservice`), tests : `src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj` (namespace `weesky.Snoopy.Microservice.Tests`). `InternalsVisibleTo` est en place : les tests voient les types `internal`.
- Commande de test : `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~<Classe>"`. **Jamais `--no-build`** quand un fichier de test a été ajouté.
- `dotnet test` régénère `ApiDocumentation.xml` (artefact versionné). Si `git status` le montre modifié, le révertrer avant de committer.
- Commentaires de code en anglais, 3 lignes max, seulement quand le code ne suffit pas. Pas de duplication.
- Messages de commit : concis, deux lignes max, ne commencent ni ne finissent par `@`. Les trailers d'attribution de la session s'ajoutent à la fin.
- Valeurs de la spec, verbatim : `PoolEnabled=true`, `PoolIdleSeconds=70`, `PoolMaxLifetimeMinutes=15`, `PoolMaxPerIdentity=4`, `PoolMaxTotal=200`, `PoolHealthTimeoutSeconds=3`, fenêtre de confiance sans `NOOP` = 5 s, balayeur = 15 s, ligne d'agrégat = une passe sur 20.
- Invariants de sécurité (spec, § Les décisions) : aucun `MailCredential` ni `MailAccountConnection` conservé dans une entrée du pool ; l'empreinte n'est jamais journalisée ; **aucun `CLOSE` de dossier** sur le chemin de retour ; une socket en échec de santé ou marquée *tainted* est jetée **sans `LOGOUT`** ; toute saturation dégrade vers une connexion à usage unique, jamais vers une attente.

---

### Task 1 : Ligne de base chronométrée et scission `OpenClientAsync`

La spec exige que la mesure `connect` / `AUTHENTICATE` **précède** le pool (§ Ce qu'on mesure). Cette tâche pose la mesure et sépare, dans la fabrique, l'ouverture du client de la construction de la session — le pool a besoin du client nu.

**Files:**
- Modify: `src/snoopy.microservice/Services/MailConnectionFactory.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailConnectionFactoryTimingTests.cs`

**Interfaces:**
- Produces: `public Task<Result<TClient>> OpenClientAsync(MailAccountConnection, CancellationToken)` sur `MailConnectionFactory<TClient,TSession>` — le client connecté et authentifié, propriété transférée à l'appelant. `OpenAsync` inchangé en signature, devient `OpenClientAsync` + `CreateSession`.

- [ ] **Step 1 : Écrire le test rouge**

```csharp
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The baseline the pool will be judged against: every open logs how long connect+TLS and
/// AUTHENTICATE took, before any pooling exists to muddy the comparison.
/// </summary>
public sealed class MailConnectionFactoryTimingTests
{
    [Fact]
    public async Task OpenClientAsync_LogsConnectAndAuthenticateDurations()
    {
        using var server = new FakeImapServer();
        server.Start();
        var logger = new Mock<ILogger<ImapConnectionFactory>>();
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new MailOptions { TimeoutSeconds = 10, AllowCleartext = true });
        var factory = new ImapConnectionFactory(monitor.Object, Mock.Of<IMailHtmlSanitizer>(), logger.Object);
        var connection = TestConnections.Primary("alice@weesky.be", "hunter2") with
        {
            ImapHost = "127.0.0.1", ImapPort = server.Port, ImapSecurity = SecureSocketOptions.None
        };

        var opened = await factory.OpenClientAsync(connection, CancellationToken.None);

        Assert.True(opened.IsSuccess);
        Assert.True(opened.Value.IsAuthenticated);
        logger.Verify(l => l.Log(
                LogLevel.Debug, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("authenticate")),
                null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        opened.Value.Dispose();
    }
}
```

`FakeImapServer` vit dans `Tests/Services/ImapSessionListFoldersTests.cs` (classe `internal`, même namespace de tests) : il est visible sans `using` supplémentaire.

- [ ] **Step 2 : Vérifier qu'il échoue**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~MailConnectionFactoryTimingTests"`
Expected: échec de compilation — `OpenClientAsync` n'existe pas.

- [ ] **Step 3 : Scinder `OpenAsync` et chronométrer**

Dans `MailConnectionFactory.cs`, remplacer la méthode `OpenAsync` entière par les deux méthodes suivantes (le corps est celui d'aujourd'hui, plus le chronomètre, et il rend le client au lieu de la session) :

```csharp
    public async Task<Result<TSession>> OpenAsync(MailAccountConnection connection, CancellationToken cancellationToken)
    {
        var client = await OpenClientAsync(connection, cancellationToken);
        return client.IsFailure
            ? Result.Failure<TSession>(client.Error)
            : Result.Success(CreateSession(client.Value));
    }

    /// <summary>
    /// The connected, authenticated client; the caller owns it on success. The pool builds on
    /// this rather than on <see cref="OpenAsync"/> because it wraps the client itself.
    /// </summary>
    public async Task<Result<TClient>> OpenClientAsync(MailAccountConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrWhiteSpace(connection.Username))
            throw new ArgumentException("Username is required", nameof(connection));

        var endpoint = Endpoint(connection);

        if (!endpoint.IsConfigured)
        {
            Logger.LogError("{Protocol} is not configured ({ConfigurationKey} missing)",
                endpoint.Protocol, endpoint.ConfigurationKey);
            return Result.Failure<TClient>("Mail service is not configured");
        }

        if (endpoint.Security is SecureSocketOptions.None)
            Logger.LogWarning(
                "{Protocol} endpoint {Host}:{Port} is configured without transport security",
                endpoint.Protocol, endpoint.Host, endpoint.Port);

        TClient? client = null;

        try
        {
            client = CreateClient();
            client.ServerCertificateValidationCallback =
                (_, _, _, errors) => ValidateCertificate(endpoint.Protocol, errors);
            client.Timeout = options.CurrentValue.TimeoutSeconds * 1000;

            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                connectCts.CancelAfter(TimeSpan.FromSeconds(options.CurrentValue.TimeoutSeconds));
                var stopwatch = Stopwatch.StartNew();
                await client.ConnectAsync(endpoint.Host, endpoint.Port, endpoint.Security, connectCts.Token);
                var connectMs = stopwatch.ElapsedMilliseconds;

                if (!client.IsSecure)
                {
                    if (!options.CurrentValue.AllowCleartext)
                    {
                        Logger.LogError(
                            "Refusing to authenticate over an unencrypted {Protocol} connection to {Host}:{Port}; " +
                            "set Mail:AllowCleartext if the link is genuinely trusted",
                            endpoint.Protocol, endpoint.Host, endpoint.Port);
                        return Result.Failure<TClient>("Unable to connect to the mail service");
                    }

                    Logger.LogWarning(
                        "Authenticating over an unencrypted {Protocol} connection to {Host}:{Port} — " +
                        "the mail password crosses this link in the clear",
                        endpoint.Protocol, endpoint.Host, endpoint.Port);
                }

                await (connection.Credential switch
                {
                    OAuthCredential oauth => client.AuthenticateAsync(
                        new SaslMechanismOAuth2(connection.Username, oauth.AccessToken), connectCts.Token),
                    PasswordCredential password => client.AuthenticateAsync(
                        connection.Username, password.Password, connectCts.Token),
                    _ => throw new UnreachableException()
                });

                // TLS sits inside ConnectAsync for StartTls and SslOnConnect alike: MailKit has no seam between them.
                Logger.LogDebug("{Protocol} opened {Host}:{Port}: connect+tls {ConnectMs} ms, authenticate {AuthMs} ms",
                    endpoint.Protocol, endpoint.Host, endpoint.Port, connectMs, stopwatch.ElapsedMilliseconds - connectMs);
            }

            var opened = client;
            client = null; // ownership transferred to the caller
            return Result.Success(opened);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AuthenticationException)
        {
            Logger.LogWarning("{Protocol} authentication failed for {Username}", endpoint.Protocol, connection.Username);
            return Result.Failure<TClient>("Mail authentication failed");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Unable to connect to {Protocol} at {Host}:{Port}",
                endpoint.Protocol, endpoint.Host, endpoint.Port);
            return Result.Failure<TClient>("Unable to connect to the mail service");
        }
        finally
        {
            client?.Dispose();
        }
    }
```

Conserver les commentaires existants du bloc `if (!client.IsSecure)` (« Only the connected client knows… ») et de l'échec d'authentification (« Never echo the server's message… ») — ils sont omis ci-dessus pour la lisibilité, pas pour être supprimés. `System.Diagnostics` est déjà importé.

Mettre à jour la première phrase du résumé de classe : « Opens one connection per request — no pooling, the Rainloop model: » devient « Opens one connection per call; reuse, when any, sits above it in ImapConnectionPool: ».

- [ ] **Step 4 : Vérifier vert, y compris les tests existants de la fabrique**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~MailConnectionFactory|FullyQualifiedName~SmtpConnectionFactory"`
Expected: PASS.

- [ ] **Step 5 : Commit**

```bash
git add src/snoopy.microservice/Services/MailConnectionFactory.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/MailConnectionFactoryTimingTests.cs
git commit -m "feat(mail): chronométrage de l'ouverture IMAP/SMTP et OpenClientAsync" -m "Ligne de base connect+tls / authenticate, avant tout pooling."
```

---

### Task 2 : `CredentialFingerprint` et `PoolKey`

**Files:**
- Create: `src/snoopy.microservice/Services/CredentialFingerprint.cs`
- Create: `src/snoopy.microservice/Services/PoolKey.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/CredentialFingerprintTests.cs`

**Interfaces:**
- Produces: `internal sealed class CredentialFingerprint { string Of(MailCredential) }` ; `internal readonly record struct PoolKey(string Host, int Port, SecureSocketOptions Security, string Username, string Fingerprint) { static PoolKey From(MailAccountConnection, CredentialFingerprint) }`.

- [ ] **Step 1 : Écrire les tests rouges**

```csharp
using MailKit.Security;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The pool indexes by what authenticated, never by the secret itself. These pin the three
/// properties that make that safe: same credential same key, any difference a different key, and
/// nothing that prints the secret.
/// </summary>
public sealed class CredentialFingerprintTests
{
    private readonly CredentialFingerprint _fingerprint = new();

    [Fact]
    public void Of_IsStableForTheSameCredential()
    {
        Assert.Equal(
            _fingerprint.Of(new PasswordCredential("hunter2")),
            _fingerprint.Of(new PasswordCredential("hunter2")));
    }

    [Fact]
    public void Of_DiffersBetweenTwoPasswords()
    {
        Assert.NotEqual(
            _fingerprint.Of(new PasswordCredential("hunter2")),
            _fingerprint.Of(new PasswordCredential("hunter3")));
    }

    // A password and an OAuth token of the same text are not the same credential.
    [Fact]
    public void Of_DiffersBetweenCredentialKindsOfTheSameText()
    {
        Assert.NotEqual(
            _fingerprint.Of(new PasswordCredential("token")),
            _fingerprint.Of(new OAuthCredential("token")));
    }

    // Another process draws another key: its fingerprints mean nothing here.
    [Fact]
    public void Of_DiffersBetweenTwoProcesses()
    {
        Assert.NotEqual(
            new CredentialFingerprint().Of(new PasswordCredential("hunter2")),
            new CredentialFingerprint().Of(new PasswordCredential("hunter2")));
    }

    [Fact]
    public void Of_NeverContainsTheSecret()
    {
        var value = _fingerprint.Of(new PasswordCredential("hunter2"));

        Assert.DoesNotContain("hunter2", value);
        Assert.Equal(44, value.Length); // Base64 of 32 bytes, fixed whatever the input
    }

    [Fact]
    public void PoolKey_ToString_NamesTheEndpointButNotTheFingerprint()
    {
        var connection = TestConnections.Primary("alice@weesky.be", "hunter2");
        var key = PoolKey.From(connection, _fingerprint);

        Assert.Contains("alice@weesky.be", key.ToString());
        Assert.DoesNotContain(key.Fingerprint, key.ToString());
    }

    [Fact]
    public void PoolKey_From_DiffersWhenTransportSecurityDiffers()
    {
        var connection = TestConnections.Primary("alice@weesky.be", "hunter2");

        Assert.NotEqual(
            PoolKey.From(connection, _fingerprint),
            PoolKey.From(connection with { ImapSecurity = SecureSocketOptions.SslOnConnect }, _fingerprint));
    }
}
```

- [ ] **Step 2 : Vérifier qu'ils échouent**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~CredentialFingerprintTests"`
Expected: échec de compilation.

- [ ] **Step 3 : Implémenter**

`CredentialFingerprint.cs` :

```csharp
using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// What the pool indexes a credential by, so that no table ever holds a password. HMAC-SHA256
/// under a key drawn at startup and never persisted: an old process's fingerprints mean nothing
/// to the new one. The kind and the secret are length-delimited so two distinct pairs cannot
/// concatenate to the same bytes.
/// </summary>
internal sealed class CredentialFingerprint
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public string Of(MailCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        var (kind, secret) = credential switch
        {
            PasswordCredential password => ("password", password.Password),
            OAuthCredential oauth => ("oauth", oauth.AccessToken),
            _ => throw new UnreachableException()
        };

        var kindBytes = Encoding.UTF8.GetBytes(kind);
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var input = new byte[8 + kindBytes.Length + secretBytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(input, kindBytes.Length);
        kindBytes.CopyTo(input, 4);
        BinaryPrimitives.WriteInt32BigEndian(input.AsSpan(4 + kindBytes.Length), secretBytes.Length);
        secretBytes.CopyTo(input, 8 + kindBytes.Length);

        try
        {
            return Convert.ToBase64String(HMACSHA256.HashData(_key, input));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
            CryptographicOperations.ZeroMemory(secretBytes);
        }
    }
}
```

`PoolKey.cs` :

```csharp
using MailKit.Security;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// What authenticated a pooled connection — never the account id the URL named. Transport
/// security is part of it so a domain an admin tightened never reuses a socket opened under the
/// old policy.
/// </summary>
internal readonly record struct PoolKey(
    string Host, int Port, SecureSocketOptions Security, string Username, string Fingerprint)
{
    public static PoolKey From(MailAccountConnection connection, CredentialFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(fingerprint);
        return new PoolKey(
            connection.ImapHost, connection.ImapPort, connection.ImapSecurity,
            connection.Username, fingerprint.Of(connection.Credential));
    }

    /// <summary>Log-safe: the fingerprint is derived from a password and never printed.</summary>
    public override string ToString() => $"{Username}@{Host}:{Port} ({Security})";
}
```

- [ ] **Step 4 : Vérifier vert**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~CredentialFingerprintTests"`
Expected: 7 PASS.

- [ ] **Step 5 : Commit**

```bash
git add src/snoopy.microservice/Services/CredentialFingerprint.cs src/snoopy.microservice/Services/PoolKey.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/CredentialFingerprintTests.cs
git commit -m "feat(mail): empreinte HMAC des credentials et clé du pool IMAP"
```

---

### Task 3 : `ImapSession` — relâchement injecté et taint

**Files:**
- Modify: `src/snoopy.microservice/Services/ImapSession.cs` (constructeur, `ExecuteAsync` ×2, `DisposeAsync`)
- Modify: `src/snoopy.microservice/Services/IImapSession.cs` (résumé)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapSessionTaintTests.cs`

**Interfaces:**
- Produces: `internal delegate ValueTask ImapClientRelease(ImapClient client, bool healthy);` — `ImapSession(ImapClient, IMailHtmlSanitizer, ILogger, ImapClientRelease? release = null)` ; `internal bool Tainted { get; }` ; `internal static ValueTask ImapSession.CloseAsync(ImapClient client, bool healthy)` (le relâchement par défaut : `LOGOUT` sous 2 s si sain, puis `Dispose`).

- [ ] **Step 1 : Écrire les tests rouges**

```csharp
using CSharpFunctionalExtensions;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// A session that met an exception or a cancellation mid-command may have left the protocol out
/// of sync; the pool must never reuse that socket. The session records it, and hands the verdict
/// to whoever releases the client.
/// </summary>
public sealed class ImapSessionTaintTests
{
    private static ImapSession CreateSession(ImapClientRelease? release = null) =>
        new(new ImapClient(), Mock.Of<IMailHtmlSanitizer>(), Mock.Of<ILogger>(), release);

    [Fact]
    public async Task ExecuteAsync_OnAnUnrecognisedException_Taints()
    {
        var session = CreateSession();

        await session.ExecuteAsync<string>(CancellationToken.None,
            () => throw new IOException("stream torn"), "opaque", _ => { });

        Assert.True(session.Tainted);
    }

    // A tagged NO after a clean exchange: the socket is fine, and the caller handles the sentinel.
    [Fact]
    public async Task ExecuteAsync_OnASentinel_DoesNotTaint()
    {
        var session = CreateSession();

        await session.ExecuteAsync<string>(CancellationToken.None,
            () => throw new FolderNotFoundException("Archive"), "opaque", _ => { }, ImapSession.FolderSentinel);

        Assert.False(session.Tainted);
    }

    [Fact]
    public async Task ExecuteAsync_OnCancellation_TaintsThenRethrows()
    {
        var session = CreateSession();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.ExecuteAsync<string>(cts.Token, () => throw new OperationCanceledException(cts.Token), "opaque", _ => { }));

        Assert.True(session.Tainted);
    }

    [Fact]
    public async Task ExecuteAsync_OnSuccess_StaysClean()
    {
        var session = CreateSession();

        await session.ExecuteAsync(CancellationToken.None, () => Task.FromResult(Result.Success("ok")), "opaque", _ => { });

        Assert.False(session.Tainted);
    }

    [Fact]
    public async Task DisposeAsync_HandsTheClientAndTheVerdictToTheRelease()
    {
        ImapClient? released = null;
        bool? healthy = null;
        var session = CreateSession((client, ok) => { released = client; healthy = ok; return ValueTask.CompletedTask; });
        await session.ExecuteAsync<string>(CancellationToken.None, () => throw new IOException(), "opaque", _ => { });

        await session.DisposeAsync();

        Assert.NotNull(released);
        Assert.False(healthy);
    }

    [Fact]
    public async Task DisposeAsync_ReleasesOnlyOnce()
    {
        var releases = 0;
        var session = CreateSession((_, _) => { releases++; return ValueTask.CompletedTask; });

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(1, releases);
    }
}
```

- [ ] **Step 2 : Vérifier qu'ils échouent**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~ImapSessionTaintTests"`
Expected: échec de compilation (`ImapClientRelease`, `Tainted`).

- [ ] **Step 3 : Implémenter**

Dans `ImapSession.cs`, au-dessus de la classe :

```csharp
/// <summary>
/// How a session lets go of its client on disposal — close it, or hand it back to a pool.
/// <c>healthy</c> is false when a command left the protocol in doubt; a pool must not reuse it.
/// </summary>
internal delegate ValueTask ImapClientRelease(ImapClient client, bool healthy);
```

Champs et constructeur :

```csharp
    private readonly ImapClient _client;
    private readonly ImapClientRelease _release;
    private readonly ImapFolderCommands _folders;
    private readonly ImapMessageCommands _messages;
    private bool _disposed;

    public ImapSession(ImapClient client, IMailHtmlSanitizer sanitizer, ILogger logger, ImapClientRelease? release = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        ArgumentNullException.ThrowIfNull(sanitizer);
        ArgumentNullException.ThrowIfNull(logger);
        _release = release ?? new ImapClientRelease(CloseAsync);

        DirectorySeparator = client.PersonalNamespaces.Count > 0
            ? client.PersonalNamespaces[0].DirectorySeparator
            : '/';

        _folders = new ImapFolderCommands(this, client, logger);
        _messages = new ImapMessageCommands(this, client, sanitizer, logger);
    }

    /// <summary>True once a command ended in an unrecognised exception or a cancellation: the
    /// socket may be out of sync and must be closed, never pooled.</summary>
    internal bool Tainted { get; private set; }
```

Dans **les deux** surcharges d'`ExecuteAsync`, les blocs `catch` deviennent :

```csharp
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Tainted = true;
            throw;
        }
        catch (Exception ex)
        {
            if (sentinel?.Invoke(ex) is { } known) return Result.Failure<T>(known);

            Tainted = true;
            logFailure(ex);
            return Result.Failure<T>(failureMessage);
        }
```

(la surcharge non générique utilise `Result.Failure(known)` / `Result.Failure(failureMessage)` comme aujourd'hui).

`DisposeAsync` et le relâchement par défaut, en remplacement du `DisposeAsync` actuel :

```csharp
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _release(_client, healthy: !Tainted);
    }

    /// <summary>
    /// The release when nobody pools: a polite LOGOUT under its own 2 s cap when the socket is
    /// believed alive, then Dispose. Teardown runs after the response went out and must not
    /// inherit the protocol timeout. A tainted socket gets no LOGOUT — nothing is in sync to say it to.
    /// </summary>
    internal static async ValueTask CloseAsync(ImapClient client, bool healthy)
    {
        try
        {
            if (healthy && client.IsConnected)
            {
                using var cap = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await client.DisconnectAsync(quit: true, cap.Token);
            }
        }
        catch
        {
            // Best effort — the connection is being torn down anyway.
        }

        client.Dispose();
    }
```

Dans `IImapSession.cs`, remplacer le résumé « An open, authenticated IMAP session. One session per repository method, disposed at the end of it — there is no pooling, which is also how Rainloop operates. » par : « An open, authenticated IMAP session, one per request. Disposing it releases the client — to a pool when the request borrowed one, closing it otherwise. »

- [ ] **Step 4 : Vérifier vert, y compris les tests existants de la session**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~ImapSession"`
Expected: PASS (dont `ImapSessionDisposeTests`, qui passe par le relâchement par défaut).

- [ ] **Step 5 : Commit**

```bash
git add src/snoopy.microservice/Services/ImapSession.cs src/snoopy.microservice/Services/IImapSession.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapSessionTaintTests.cs
git commit -m "feat(mail): ImapSession reçoit son relâchement et marque les sessions douteuses"
```

---

### Task 4 : `IImapClientSource` sur la fabrique

**Files:**
- Create: `src/snoopy.microservice/Services/IImapClientSource.cs`
- Modify: `src/snoopy.microservice/Services/ImapConnectionFactory.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapConnectionFactoryClientSourceTests.cs`

**Interfaces:**
- Consumes: `OpenClientAsync` (Task 1), `ImapClientRelease` (Task 3).
- Produces: `internal interface IImapClientSource { Task<Result<ImapClient>> OpenClientAsync(MailAccountConnection, CancellationToken); IImapSession CreateSession(ImapClient client, ImapClientRelease release); }` — implémentée par `ImapConnectionFactory`.

- [ ] **Step 1 : Écrire le test rouge**

```csharp
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ImapConnectionFactoryClientSourceTests
{
    [Fact]
    public async Task CreateSession_WrapsAClientAndCallsTheGivenReleaseOnDispose()
    {
        using var server = new FakeImapServer();
        server.Start();
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(new MailOptions { TimeoutSeconds = 10, AllowCleartext = true });
        IImapClientSource source = new ImapConnectionFactory(
            monitor.Object, Mock.Of<IMailHtmlSanitizer>(), NullLogger<ImapConnectionFactory>.Instance);
        var connection = TestConnections.Primary("alice@weesky.be", "hunter2") with
        {
            ImapHost = "127.0.0.1", ImapPort = server.Port, ImapSecurity = SecureSocketOptions.None
        };
        var opened = await source.OpenClientAsync(connection, CancellationToken.None);
        ImapClient? released = null;

        var session = source.CreateSession(opened.Value, (client, _) => { released = client; return ValueTask.CompletedTask; });
        await session.DisposeAsync();

        Assert.Same(opened.Value, released);
        Assert.True(opened.Value.IsConnected); // the release decides; this one closed nothing
        opened.Value.Dispose();
    }
}
```

- [ ] **Step 2 : Vérifier qu'il échoue**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~ImapConnectionFactoryClientSourceTests"`
Expected: échec de compilation.

- [ ] **Step 3 : Implémenter**

`IImapClientSource.cs` :

```csharp
using CSharpFunctionalExtensions;
using MailKit.Net.Imap;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The pool's view of the factory: a bare connected client, and a session over a client it did
/// not open. Separate from <see cref="IImapConnectionFactory"/> on purpose — the login probe and
/// the connected-account probe must keep authenticating for real, and only ever see that one.
/// </summary>
internal interface IImapClientSource
{
    /// <summary>A connected, authenticated client the caller owns. Same failures as OpenAsync.</summary>
    Task<Result<ImapClient>> OpenClientAsync(MailAccountConnection connection, CancellationToken cancellationToken);

    /// <summary>Wraps a client; the session calls <paramref name="release"/> exactly once, on disposal.</summary>
    IImapSession CreateSession(ImapClient client, ImapClientRelease release);
}
```

`ImapConnectionFactory.cs` — la déclaration et les implémentations explicites :

```csharp
internal sealed class ImapConnectionFactory(
    IOptionsMonitor<MailOptions> options,
    IMailHtmlSanitizer sanitizer,
    ILogger<ImapConnectionFactory> logger)
    : MailConnectionFactory<ImapClient, IImapSession>(options, logger), IImapConnectionFactory, IImapClientSource
{
    protected override MailEndpoint Endpoint(MailAccountConnection connection) => new(
        Protocol: "IMAP",
        ConfigurationKey: "Mail:ImapHost",
        Host: connection.ImapHost,
        Port: connection.ImapPort,
        Security: connection.ImapSecurity,
        IsConfigured: !string.IsNullOrWhiteSpace(connection.ImapHost));

    protected override ImapClient CreateClient() => new();

    protected override IImapSession CreateSession(ImapClient client) => new ImapSession(client, sanitizer, Logger);

    Task<Result<IImapSession>> IImapConnectionFactory.OpenAsync(
        MailAccountConnection connection, CancellationToken cancellationToken) =>
        OpenAsync(connection, cancellationToken);

    Task<Result<ImapClient>> IImapClientSource.OpenClientAsync(
        MailAccountConnection connection, CancellationToken cancellationToken) =>
        OpenClientAsync(connection, cancellationToken);

    IImapSession IImapClientSource.CreateSession(ImapClient client, ImapClientRelease release) =>
        new ImapSession(client, sanitizer, Logger, release);
}
```

- [ ] **Step 4 : Vérifier vert**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~ImapConnectionFactory"`
Expected: PASS.

- [ ] **Step 5 : Commit**

```bash
git add src/snoopy.microservice/Services/IImapClientSource.cs src/snoopy.microservice/Services/ImapConnectionFactory.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapConnectionFactoryClientSourceTests.cs
git commit -m "feat(mail): IImapClientSource, la vue du pool sur la fabrique IMAP"
```

---

### Task 5 : Infrastructure de test — `PoolImapServer` et `PoolTestHost`

Un serveur IMAP scripté **multi-connexions** qui compte ce qu'il reçoit ; `FakeImapServer` n'accepte qu'un client et ne compte rien. Aucun test dans cette tâche : elle est validée par la compilation et par la première utilisation en Task 6.

**Files:**
- Create: `src/snoopy.microservice/snoopy.microservice.Tests/Infrastructure/PoolImapServer.cs`
- Create: `src/snoopy.microservice/snoopy.microservice.Tests/Infrastructure/PoolTestHost.cs`

**Interfaces:**
- Produces: `PoolImapServer { int Port; int Logins, Logouts, NoOps, Closes, Expunges, Open; IReadOnlyList<string> Commands; void Start(); void SilenceOpenConnections(); Task<bool> WaitUntilAsync(Func<bool>, TimeSpan? = null) }` ; `PoolTestHost.Create(server, configure?) → PoolHost { ImapConnectionPool Pool; MutableTimeProvider Clock; MailOptions Options }` (`IAsyncDisposable`, dispose le pool) ; `PoolTestHost.Connection(server, email, password)` ; `PoolTestHost.Shared(server, accountId, email, password)`.

- [ ] **Step 1 : Écrire `PoolImapServer.cs`**

```csharp
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// A scripted IMAP server that accepts any number of connections and counts what each one sends,
/// so pool tests assert on the wire — how many LOGINs, whether a CLOSE ever went out — rather
/// than on mocks. <see cref="SilenceOpenConnections"/> turns every connection open at that moment
/// into a black hole: commands are read and never answered, the socket stays up.
/// </summary>
internal sealed class PoolImapServer : IDisposable
{
    private const string Caps = "IMAP4rev1 NAMESPACE";

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly object _gate = new();
    private readonly List<string> _commands = [];
    private readonly List<StrongBox<bool>> _silence = [];
    private int _logins, _logouts, _noops, _closes, _expunges, _open;

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public int Logins => Volatile.Read(ref _logins);
    public int Logouts => Volatile.Read(ref _logouts);
    public int NoOps => Volatile.Read(ref _noops);
    public int Closes => Volatile.Read(ref _closes);
    public int Expunges => Volatile.Read(ref _expunges);

    /// <summary>Connections accepted and not yet closed by either side.</summary>
    public int Open => Volatile.Read(ref _open);

    public IReadOnlyList<string> Commands
    {
        get { lock (_gate) return _commands.ToArray(); }
    }

    public void Start()
    {
        _listener.Start();
        _ = AcceptLoopAsync();
    }

    public void SilenceOpenConnections()
    {
        lock (_gate) foreach (var box in _silence) box.Value = true;
    }

    /// <summary>Polls until <paramref name="predicate"/> holds or the timeout passes; true when it held.</summary>
    public async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            if (DateTime.UtcNow > deadline) return false;
            await Task.Delay(20);
        }
        return true;
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (true) _ = ServeAsync(await _listener.AcceptTcpClientAsync());
        }
        catch (Exception)
        {
            // Listener stopped: the test is over.
        }
    }

    private async Task ServeAsync(TcpClient tcpClient)
    {
        var silent = new StrongBox<bool>(false);
        lock (_gate) _silence.Add(silent);
        Interlocked.Increment(ref _open);

        try
        {
            using (tcpClient)
            using (var stream = tcpClient.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII))
            await using (var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true })
            {
                await writer.WriteLineAsync($"* OK [CAPABILITY {Caps}] Pool fake ready");

                while (true)
                {
                    var line = await reader.ReadLineAsync();
                    if (line is null) return;
                    lock (_gate) _commands.Add(line);

                    var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length < 2) continue;
                    var tag = words[0];
                    var command = words[1].ToUpperInvariant();
                    if (command == "UID" && words.Length > 2) command = "UID " + words[2].ToUpperInvariant();

                    if (silent.Value) continue;

                    switch (command)
                    {
                        case "LOGIN":
                            Interlocked.Increment(ref _logins);
                            await writer.WriteLineAsync($"{tag} OK [CAPABILITY {Caps}] LOGIN completed");
                            break;

                        case "CAPABILITY":
                            await writer.WriteLineAsync($"* CAPABILITY {Caps}");
                            await writer.WriteLineAsync($"{tag} OK CAPABILITY completed");
                            break;

                        case "NAMESPACE":
                            await writer.WriteLineAsync("* NAMESPACE ((\"\" \"/\")) NIL NIL");
                            await writer.WriteLineAsync($"{tag} OK NAMESPACE completed");
                            break;

                        case "NOOP":
                            Interlocked.Increment(ref _noops);
                            await writer.WriteLineAsync($"{tag} OK NOOP completed");
                            break;

                        case "LIST":
                            await writer.WriteLineAsync("* LIST (\\HasNoChildren) \"/\" \"INBOX\"");
                            await writer.WriteLineAsync($"{tag} OK LIST completed");
                            break;

                        case "SELECT":
                            await writer.WriteLineAsync("* 1 EXISTS");
                            await writer.WriteLineAsync("* 0 RECENT");
                            await writer.WriteLineAsync("* FLAGS (\\Seen \\Flagged \\Deleted)");
                            await writer.WriteLineAsync("* OK [PERMANENTFLAGS (\\Seen \\Flagged \\Deleted)] Flags");
                            await writer.WriteLineAsync("* OK [UIDVALIDITY 1] UIDs valid");
                            await writer.WriteLineAsync("* OK [UIDNEXT 2] Predicted next UID");
                            await writer.WriteLineAsync($"{tag} OK [READ-WRITE] SELECT completed");
                            break;

                        case "UID STORE":
                            await writer.WriteLineAsync($"{tag} OK STORE completed");
                            break;

                        case "CLOSE":
                            Interlocked.Increment(ref _closes);
                            await writer.WriteLineAsync($"{tag} OK CLOSE completed");
                            break;

                        case "EXPUNGE":
                            Interlocked.Increment(ref _expunges);
                            await writer.WriteLineAsync($"{tag} OK EXPUNGE completed");
                            break;

                        case "LOGOUT":
                            Interlocked.Increment(ref _logouts);
                            await writer.WriteLineAsync("* BYE logging out");
                            await writer.WriteLineAsync($"{tag} OK LOGOUT completed");
                            return;

                        default:
                            await writer.WriteLineAsync($"{tag} BAD unhandled command in fake server: {command}");
                            break;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Torn down by the client or by the test: the assertions are the source of truth.
        }
        finally
        {
            Interlocked.Decrement(ref _open);
        }
    }

    public void Dispose() => _listener.Stop();
}
```

- [ ] **Step 2 : Écrire `PoolTestHost.cs`**

```csharp
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

/// <summary>
/// A pool over a real factory over <see cref="PoolImapServer"/>, with a clock the test moves by
/// hand. The options object is shared by reference: mutate it to change the pool's behaviour
/// mid-test, exactly as a hot reload would. Disposing the host disposes the pool.
/// </summary>
internal sealed class PoolHost(ImapConnectionPool pool, MutableTimeProvider clock, MailOptions options) : IAsyncDisposable
{
    public ImapConnectionPool Pool { get; } = pool;
    public MutableTimeProvider Clock { get; } = clock;
    public MailOptions Options { get; } = options;

    public ValueTask DisposeAsync() => Pool.DisposeAsync();
}

internal static class PoolTestHost
{
    public static PoolHost Create(PoolImapServer server, Action<MailOptions>? configure = null)
    {
        var options = new MailOptions { TimeoutSeconds = 10, AllowCleartext = true, PoolHealthTimeoutSeconds = 2 };
        configure?.Invoke(options);
        var monitor = new Mock<IOptionsMonitor<MailOptions>>();
        monitor.Setup(m => m.CurrentValue).Returns(options);

        var factory = new ImapConnectionFactory(
            monitor.Object, Mock.Of<IMailHtmlSanitizer>(), NullLogger<ImapConnectionFactory>.Instance);
        var clock = new MutableTimeProvider();
        var pool = new ImapConnectionPool(
            factory, new CredentialFingerprint(), monitor.Object, clock, NullLogger<ImapConnectionPool>.Instance);
        return new PoolHost(pool, clock, options);
    }

    public static MailAccountConnection Connection(PoolImapServer server, string email, string password) =>
        TestConnections.Primary(email, password) with
        {
            ImapHost = "127.0.0.1", ImapPort = server.Port, ImapSecurity = SecureSocketOptions.None
        };

    /// <summary>A local shared mailbox: two users, one (host, user, secret) — one pool entry.</summary>
    public static MailAccountConnection Shared(PoolImapServer server, string accountId, string email, string password) =>
        TestConnections.ConnectedLocal(accountId, email, password) with
        {
            ImapHost = "127.0.0.1", ImapPort = server.Port, ImapSecurity = SecureSocketOptions.None
        };
}
```

`PoolTestHost` ne compile qu'avec `ImapConnectionPool` (Task 6). Committer les deux fichiers avec la Task 6.

---

### Task 6 : `ImapConnectionPool` — cœur : emprunt, retour, clé, plafonds

**Files:**
- Modify: `src/snoopy.microservice/Models/Mail/MailOptions.cs` (six réglages)
- Create: `src/snoopy.microservice/Services/IImapConnectionPool.cs`
- Create: `src/snoopy.microservice/Services/ImapConnectionPool.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapConnectionPoolTests.cs`

**Interfaces:**
- Consumes: `IImapClientSource` (Task 4), `CredentialFingerprint`/`PoolKey` (Task 2), `ImapSession.CloseAsync` (Task 3), `TimeProvider` (déjà enregistré dans `Program.cs:30`).
- Produces: `internal interface IImapConnectionPool { Task<Result<IImapSession>> BorrowAsync(MailAccountConnection, Guid userUid, CancellationToken); void Close(Guid userUid); void Revoke(Guid userUid); Task<int> SweepAsync(CancellationToken); PoolStatistics Snapshot(); }` et `internal readonly record struct PoolStatistics(int Idle, int Borrowed, long Borrows, long Reused, long Opened, long SingleUse, long HealthFailures, long ClosedIdle, long ClosedLifetime, long Evicted)`. La classe complète est écrite ici ; les Tasks 7 à 9 en vérifient les autres comportements test par test.

- [ ] **Step 1 : Écrire les tests rouges**

```csharp
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The pool on the wire: what the server sees is the truth. Test 1 of the spec — two identities
/// never share a socket — comes first because it guards the only grave fault this work can have.
/// </summary>
public sealed class ImapConnectionPoolTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    [Fact]
    public async Task Borrow_NeverHandsAnotherCredentialsSocketOver()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;

        await using (var session = (await pool.BorrowAsync(PoolTestHost.Connection(server, "alice@weesky.be", "hunter2"), Alice, CancellationToken.None)).Value) { }
        await using (var session = (await pool.BorrowAsync(PoolTestHost.Connection(server, "bob@weesky.be", "swordfish"), Bob, CancellationToken.None)).Value) { }
        await using (var session = (await pool.BorrowAsync(PoolTestHost.Connection(server, "alice@weesky.be", "changed"), Alice, CancellationToken.None)).Value) { }

        Assert.Equal(3, server.Logins);
    }

    [Fact]
    public async Task Borrow_ReusesTheSocketReturnedByThePreviousRequest()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        for (var i = 0; i < 3; i++)
            await using (var session = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }

        Assert.Equal(1, server.Logins);
        Assert.Equal(2, pool.Snapshot().Reused);
    }

    [Fact]
    public async Task ParallelBorrows_GetDistinctSocketsAndTheOverflowIsSingleUse()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server, o => o.PoolMaxPerIdentity = 2);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        var sessions = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            pool.BorrowAsync(alice, Alice, CancellationToken.None)));

        Assert.All(sessions, s => Assert.True(s.IsSuccess));
        Assert.Equal(3, server.Logins);
        Assert.Equal(1, pool.Snapshot().SingleUse);

        foreach (var session in sessions) await session.Value.DisposeAsync();

        Assert.Equal(2, pool.Snapshot().Idle);
        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 1), "the single-use socket must LOGOUT");
    }

    [Fact]
    public async Task Borrow_WhenTheTotalCapIsReached_EvictsTheOldestIdleSocket()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server, o => o.PoolMaxTotal = 2);
        var (pool, clock) = (host.Pool, host.Clock);

        await using (var s = (await pool.BorrowAsync(PoolTestHost.Connection(server, "a@weesky.be", "a"), Alice, CancellationToken.None)).Value) { }
        clock.Now += TimeSpan.FromSeconds(1);
        await using (var s = (await pool.BorrowAsync(PoolTestHost.Connection(server, "b@weesky.be", "b"), Bob, CancellationToken.None)).Value) { }
        clock.Now += TimeSpan.FromSeconds(1);
        await using (var s = (await pool.BorrowAsync(PoolTestHost.Connection(server, "c@weesky.be", "c"), Alice, CancellationToken.None)).Value) { }

        Assert.Equal(3, server.Logins);
        Assert.Equal(1, pool.Snapshot().Evicted);
        Assert.Equal(2, pool.Snapshot().Idle);
        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 1));
    }

    [Fact]
    public async Task Borrow_WithThePoolDisabled_AuthenticatesEveryTime()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server, o => o.PoolEnabled = false);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }

        Assert.Equal(2, server.Logins);
        Assert.Equal(0, pool.Snapshot().Idle);
    }

    [Fact]
    public async Task Borrow_WhenAuthenticationFails_ReturnsTheFailureAndHoldsNoPlace()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server, o => o.PoolMaxPerIdentity = 1);
        var pool = host.Pool;
        var unreachable = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2") with { ImapPort = 1 };

        var failed = await pool.BorrowAsync(unreachable, Alice, CancellationToken.None);
        var opened = await pool.BorrowAsync(PoolTestHost.Connection(server, "alice@weesky.be", "hunter2"), Alice, CancellationToken.None);

        Assert.True(failed.IsFailure);
        Assert.True(opened.IsSuccess);
        Assert.Equal(0, pool.Snapshot().SingleUse); // the failed reservation was given back
        await opened.Value.DisposeAsync();
    }
}
```

- [ ] **Step 2 : Vérifier qu'ils échouent**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~ImapConnectionPoolTests"`
Expected: échec de compilation.

- [ ] **Step 3 : Les réglages dans `MailOptions.cs`**

Après la propriété `AllowCleartext` :

```csharp
    /// <summary>Whether authenticated IMAP connections are kept between requests. Read on every
    /// borrow and every sweep, so switching it off takes effect without a restart.</summary>
    public bool PoolEnabled { get; set; } = true;

    /// <summary>Idle time before a pooled connection is closed. Above the frontend's 60 s poll on purpose.</summary>
    public int PoolIdleSeconds { get; set; } = 70;

    /// <summary>Absolute lifetime of a pooled connection: the bound on how long a revoked credential keeps working.</summary>
    public int PoolMaxLifetimeMinutes { get; set; } = 15;

    /// <summary>Connections per (host, port, security, user, credential). Keep well under Dovecot's
    /// mail_max_userip_connections (10): this service is one IP.</summary>
    public int PoolMaxPerIdentity { get; set; } = 4;

    /// <summary>Pooled connections in this process, all identities together.</summary>
    public int PoolMaxTotal { get; set; } = 200;

    /// <summary>Bound on the NOOP that checks a pooled connection before reuse, and on a polite LOGOUT.</summary>
    public int PoolHealthTimeoutSeconds { get; set; } = 3;
```

- [ ] **Step 4 : `IImapConnectionPool.cs`**

```csharp
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Authenticated IMAP connections kept between requests. Only <see cref="ScopedImapSessionProvider"/>
/// borrows; the login and connected-account probes never do. Saturation degrades to a single-use
/// connection — never a wait, never an error.
/// </summary>
internal interface IImapConnectionPool
{
    /// <summary>A session over a pooled socket when one is fit, over a fresh one otherwise.
    /// Disposing the session returns the socket. <paramref name="userUid"/> is the borrower —
    /// what <see cref="Close"/> and <see cref="Revoke"/> index by.</summary>
    Task<Result<IImapSession>> BorrowAsync(MailAccountConnection connection, Guid userUid, CancellationToken cancellationToken);

    /// <summary>DELETE /Login: closes the user's idle sockets. Housekeeping, not revocation.</summary>
    void Close(Guid userUid);

    /// <summary>DELETE /Login/All: <see cref="Close"/>, and sockets the user has out right now
    /// are closed on return instead of pooled.</summary>
    void Revoke(Guid userUid);

    /// <summary>One pass over idle sockets: closes what is past its idle or absolute lifetime.
    /// Returns how many.</summary>
    Task<int> SweepAsync(CancellationToken cancellationToken);

    PoolStatistics Snapshot();
}

/// <summary>Counters, not events: the aggregate line the sweeper logs.</summary>
internal readonly record struct PoolStatistics(
    int Idle, int Borrowed,
    long Borrows, long Reused, long Opened, long SingleUse, long HealthFailures,
    long ClosedIdle, long ClosedLifetime, long Evicted);
```

- [ ] **Step 5 : `ImapConnectionPool.cs` — la classe complète**

```csharp
using CSharpFunctionalExtensions;
using MailKit.Net.Imap;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Exclusive leases over authenticated clients, keyed by what authenticated them (<see cref="PoolKey"/>),
/// a few per identity, two clocks (idle since return, absolute since authentication) evaluated
/// only at borrow and return, and one rule above the others: saturation degrades to a single-use
/// connection. An entry holds the client, the key, the clocks and a generation — never a
/// credential, never a connection record. Spec: docs/superpowers/specs/2026-08-20-webmail-imap-connection-pool-design.md.
/// </summary>
internal sealed class ImapConnectionPool(
    IImapClientSource source,
    CredentialFingerprint fingerprint,
    IOptionsMonitor<MailOptions> options,
    TimeProvider clock,
    ILogger<ImapConnectionPool> logger) : IImapConnectionPool, IAsyncDisposable
{
    /// <summary>Returned this recently, a socket is trusted without a NOOP: the parallel burst.</summary>
    internal static readonly TimeSpan TrustWindow = TimeSpan.FromSeconds(5);

    private sealed class Entry(PoolKey key, ImapClient client, DateTimeOffset openedAt)
    {
        public PoolKey Key { get; } = key;
        public ImapClient Client { get; } = client;
        public DateTimeOffset OpenedAt { get; } = openedAt;
        public DateTimeOffset ReturnedAt { get; set; } = openedAt;
        public DateTimeOffset BorrowedAt { get; set; } = openedAt;
        public bool Borrowed { get; set; }
        /// <summary>Holds a place under the caps; false once the borrow horizon gave it back.</summary>
        public bool Counted { get; set; } = true;
        public Guid Borrower { get; set; }
        public long BorrowGeneration { get; set; }
        public HashSet<Guid> Users { get; } = [];
    }

    private readonly object _gate = new();
    private readonly Dictionary<PoolKey, List<Entry>> _idle = [];
    private readonly Dictionary<PoolKey, int> _counted = [];
    private readonly Dictionary<Guid, HashSet<Entry>> _byUser = [];
    private readonly Dictionary<Guid, long> _generation = [];
    private readonly HashSet<Entry> _borrowed = [];
    private int _countedTotal;
    private bool _disposed;
    private long _borrows, _reused, _opened, _singleUse, _healthFailures, _closedIdle, _closedLifetime, _evicted;

    public async Task<Result<IImapSession>> BorrowAsync(
        MailAccountConnection connection, Guid userUid, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var settings = options.CurrentValue;
        if (!settings.PoolEnabled || _disposed) return await SingleUseAsync(connection, cancellationToken);

        Interlocked.Increment(ref _borrows);
        var key = PoolKey.From(connection, fingerprint);

        while (TakeIdle(key, userUid, settings) is { } entry)
        {
            bool healthy;
            try
            {
                healthy = await IsHealthyAsync(entry, settings, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Discard(entry);
                throw;
            }

            if (healthy)
            {
                Interlocked.Increment(ref _reused);
                return Result.Success(Lease(entry));
            }

            Interlocked.Increment(ref _healthFailures);
            Discard(entry);
        }

        if (!TryReserve(key, settings))
        {
            Interlocked.Increment(ref _singleUse);
            return await SingleUseAsync(connection, cancellationToken);
        }

        var opened = await source.OpenClientAsync(connection, cancellationToken);
        if (opened.IsFailure)
        {
            Unreserve(key);
            return Result.Failure<IImapSession>(opened.Error);
        }

        Interlocked.Increment(ref _opened);
        var fresh = new Entry(key, opened.Value, clock.GetUtcNow());
        lock (_gate) Lend(fresh, userUid);
        return Result.Success(Lease(fresh));
    }

    public void Close(Guid userUid)
    {
        List<Entry> closing;
        lock (_gate)
        {
            if (!_byUser.TryGetValue(userUid, out var set)) return;
            closing = set.Where(e => !e.Borrowed).ToList();
            foreach (var entry in closing) RemoveLocked(entry);
        }
        CloseInBackground(closing);
    }

    public void Revoke(Guid userUid)
    {
        lock (_gate) _generation[userUid] = GenerationOf(userUid) + 1;
        Close(userUid);
    }

    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;
        var now = clock.GetUtcNow();
        var expired = new List<Entry>();
        lock (_gate)
        {
            foreach (var entry in _idle.Values.SelectMany(list => list).ToArray())
            {
                var idle = now - entry.ReturnedAt >= IdleTtl(settings);
                var old = now - entry.OpenedAt >= Lifetime(settings);
                if (!idle && !old && settings.PoolEnabled) continue;

                RemoveLocked(entry);
                expired.Add(entry);
                Interlocked.Increment(ref old ? ref _closedLifetime : ref _closedIdle);
            }

            // A lease past the horizon gives its place back; the socket stays with its request.
            foreach (var entry in _borrowed)
                if (entry.Counted && now - entry.BorrowedAt >= Lifetime(settings)) ReleasePlaceLocked(entry);
        }

        await CloseGracefullyAsync(expired.Select(e => e.Client), HealthTimeout(settings), cancellationToken);
        return expired.Count;
    }

    public PoolStatistics Snapshot()
    {
        lock (_gate)
            return new PoolStatistics(
                Idle: _idle.Values.Sum(list => list.Count), Borrowed: _borrowed.Count,
                Borrows: Interlocked.Read(ref _borrows), Reused: Interlocked.Read(ref _reused),
                Opened: Interlocked.Read(ref _opened), SingleUse: Interlocked.Read(ref _singleUse),
                HealthFailures: Interlocked.Read(ref _healthFailures),
                ClosedIdle: Interlocked.Read(ref _closedIdle), ClosedLifetime: Interlocked.Read(ref _closedLifetime),
                Evicted: Interlocked.Read(ref _evicted));
    }

    public async ValueTask DisposeAsync()
    {
        List<Entry> all;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            all = _idle.Values.SelectMany(list => list).ToList();
            foreach (var entry in all) RemoveLocked(entry);
        }
        await CloseGracefullyAsync(all.Select(e => e.Client), HealthTimeout(options.CurrentValue));
    }

    private IImapSession Lease(Entry entry) =>
        source.CreateSession(entry.Client, (_, healthy) => ReturnAsync(entry, healthy));

    private async Task<Result<IImapSession>> SingleUseAsync(MailAccountConnection connection, CancellationToken cancellationToken)
    {
        var opened = await source.OpenClientAsync(connection, cancellationToken);
        return opened.IsFailure
            ? Result.Failure<IImapSession>(opened.Error)
            : Result.Success(source.CreateSession(opened.Value, ImapSession.CloseAsync));
    }

    /// <summary>The most recently returned idle socket of the key, lent to the user; sockets past
    /// their absolute lifetime are closed on the way. Null when none is idle.</summary>
    private Entry? TakeIdle(PoolKey key, Guid userUid, MailOptions settings)
    {
        List<Entry>? stale = null;
        Entry? taken = null;
        lock (_gate)
        {
            if (_idle.TryGetValue(key, out var list))
                while (list.Count > 0)
                {
                    var candidate = list[^1];
                    list.RemoveAt(list.Count - 1);
                    if (clock.GetUtcNow() - candidate.OpenedAt >= Lifetime(settings))
                    {
                        RemoveLocked(candidate);
                        (stale ??= []).Add(candidate);
                        Interlocked.Increment(ref _closedLifetime);
                        continue;
                    }

                    Lend(candidate, userUid);
                    taken = candidate;
                    break;
                }
        }

        if (stale is not null) CloseInBackground(stale);
        return taken;
    }

    private async Task<bool> IsHealthyAsync(Entry entry, MailOptions settings, CancellationToken cancellationToken)
    {
        var client = entry.Client;
        if (!client.IsConnected || !client.IsAuthenticated) return false;
        client.Timeout = settings.TimeoutSeconds * 1000;
        if (clock.GetUtcNow() - entry.ReturnedAt < TrustWindow) return true;

        using var cap = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cap.CancelAfter(HealthTimeout(settings));
        try
        {
            await client.NoOpAsync(cap.Token);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return false; // timed out, refused, or gone: the caller discards it
        }
    }

    private ValueTask ReturnAsync(Entry entry, bool healthy)
    {
        bool pooled;
        lock (_gate)
        {
            var settings = options.CurrentValue;
            var now = clock.GetUtcNow();
            _borrowed.Remove(entry);
            entry.Borrowed = false;
            pooled = healthy && entry.Counted && settings.PoolEnabled && !_disposed
                     && entry.Client.IsConnected
                     && entry.BorrowGeneration == GenerationOf(entry.Borrower)
                     && now - entry.OpenedAt < Lifetime(settings);
            if (pooled)
            {
                entry.ReturnedAt = now;
                if (!_idle.TryGetValue(entry.Key, out var list)) _idle[entry.Key] = list = [];
                list.Add(entry);
            }
            else RemoveLocked(entry);
        }

        if (pooled) return ValueTask.CompletedTask;
        if (healthy) CloseInBackground([entry]);
        else entry.Client.Dispose(); // dead or out of sync: nothing is there to say LOGOUT to
        return ValueTask.CompletedTask;
    }

    /// <summary>Under the lock: marks the entry lent to the user and stamps their generation.</summary>
    private void Lend(Entry entry, Guid userUid)
    {
        entry.Borrowed = true;
        entry.BorrowedAt = clock.GetUtcNow();
        entry.Borrower = userUid;
        entry.BorrowGeneration = GenerationOf(userUid);
        entry.Users.Add(userUid);
        if (!_byUser.TryGetValue(userUid, out var set)) _byUser[userUid] = set = [];
        set.Add(entry);
        _borrowed.Add(entry);
    }

    private bool TryReserve(PoolKey key, MailOptions settings)
    {
        Entry? evicted = null;
        lock (_gate)
        {
            _counted.TryGetValue(key, out var perIdentity);
            if (perIdentity >= settings.PoolMaxPerIdentity) return false;
            if (_countedTotal >= settings.PoolMaxTotal && (evicted = EvictOldestIdleLocked()) is null) return false;
            _counted[key] = perIdentity + 1;
            _countedTotal++;
        }

        if (evicted is not null)
        {
            Interlocked.Increment(ref _evicted);
            CloseInBackground([evicted]);
        }
        return true;
    }

    private void Unreserve(PoolKey key)
    {
        lock (_gate)
        {
            _counted[key]--;
            _countedTotal--;
        }
    }

    private Entry? EvictOldestIdleLocked()
    {
        var oldest = _idle.Values.SelectMany(list => list).MinBy(e => e.ReturnedAt);
        if (oldest is null) return null;
        RemoveLocked(oldest);
        return oldest;
    }

    private void Discard(Entry entry)
    {
        lock (_gate) RemoveLocked(entry);
        entry.Client.Dispose();
    }

    private void RemoveLocked(Entry entry)
    {
        if (_idle.TryGetValue(entry.Key, out var list)) list.Remove(entry);
        _borrowed.Remove(entry);
        foreach (var uid in entry.Users)
            if (_byUser.TryGetValue(uid, out var set) && set.Remove(entry) && set.Count == 0) _byUser.Remove(uid);
        ReleasePlaceLocked(entry);
    }

    private void ReleasePlaceLocked(Entry entry)
    {
        if (!entry.Counted) return;
        entry.Counted = false;
        _counted[entry.Key]--;
        _countedTotal--;
    }

    private long GenerationOf(Guid userUid) => _generation.TryGetValue(userUid, out var generation) ? generation : 0;

    private void CloseInBackground(IEnumerable<Entry> entries) =>
        _ = CloseGracefullyAsync(entries.Select(e => e.Client).ToArray(), HealthTimeout(options.CurrentValue));

    /// <summary>Polite LOGOUTs in parallel under one budget; whatever is still pending past it is cut.</summary>
    private async Task CloseGracefullyAsync(
        IEnumerable<ImapClient> clients, TimeSpan budget, CancellationToken cancellationToken = default)
    {
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cap.CancelAfter(budget);
        await Task.WhenAll(clients.Select(async client =>
        {
            try
            {
                if (client.IsConnected) await client.DisconnectAsync(quit: true, cap.Token);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "A pooled IMAP connection did not close politely");
            }
            finally
            {
                client.Dispose();
            }
        }));
    }

    private static TimeSpan IdleTtl(MailOptions settings) => TimeSpan.FromSeconds(settings.PoolIdleSeconds);
    private static TimeSpan Lifetime(MailOptions settings) => TimeSpan.FromMinutes(settings.PoolMaxLifetimeMinutes);
    private static TimeSpan HealthTimeout(MailOptions settings) => TimeSpan.FromSeconds(settings.PoolHealthTimeoutSeconds);
}
```

- [ ] **Step 6 : Vérifier vert**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~ImapConnectionPoolTests"`
Expected: 6 PASS.

Si `ParallelBorrows_…` échoue sur `Logouts == 1` : le `LOGOUT` de la connexion à usage unique passe par `ImapSession.CloseAsync` (2 s de cap) ; augmenter le `timeout` de `WaitUntilAsync` à 10 s avant de suspecter le pool.

- [ ] **Step 7 : Commit**

```bash
git add src/snoopy.microservice/Models/Mail/MailOptions.cs src/snoopy.microservice/Services/IImapConnectionPool.cs src/snoopy.microservice/Services/ImapConnectionPool.cs src/snoopy.microservice/snoopy.microservice.Tests/Infrastructure/PoolImapServer.cs src/snoopy.microservice/snoopy.microservice.Tests/Infrastructure/PoolTestHost.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapConnectionPoolTests.cs
git commit -m "feat(mail): pool de connexions IMAP — emprunt exclusif, clé par credential, plafonds" -m "Serveur IMAP scripté multi-connexions pour les tests sur le fil."
```

---

### Task 7 : Santé à l'emprunt, taint, fermeture sans `LOGOUT` (tests 5, 6, 14)

**Files:**
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapConnectionPoolHealthTests.cs`
- Modify (seulement si un test est rouge) : `src/snoopy.microservice/Services/ImapConnectionPool.cs`

**Interfaces:**
- Consumes: `ImapConnectionPool` (Task 6), `PoolImapServer.SilenceOpenConnections` (Task 5), `IImapSession.SetFlagsAsync(string folderPath, IReadOnlyList<uint> uids, MailFlag flag, bool value, CancellationToken)`.

- [ ] **Step 1 : Écrire les tests**

```csharp
using System.Diagnostics;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The only recovery path in the design sits before any business command: a NOOP under its own
/// short bound. A socket that fails it, or a session that ended in doubt, is dropped without a
/// LOGOUT — nothing in sync is there to say it to, and a second bound would double the wait.
/// </summary>
public sealed class ImapConnectionPoolHealthTests
{
    private static readonly Guid Alice = Guid.NewGuid();

    [Fact]
    public async Task Borrow_WithinTheTrustWindow_SkipsTheNoop()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, clock) = (host.Pool, host.Clock);
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
        clock.Now += TimeSpan.FromSeconds(1);
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }

        Assert.Equal(0, server.NoOps);
    }

    [Fact]
    public async Task Borrow_PastTheTrustWindow_SendsOneNoop()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, clock) = (host.Pool, host.Clock);
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
        clock.Now += TimeSpan.FromSeconds(60);
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }

        Assert.Equal(1, server.NoOps);
        Assert.Equal(1, server.Logins);
    }

    // Test 5 + 14 of the spec: the black hole. The server reads and never answers; the socket
    // stays open, so only the health bound can end the wait — and no LOGOUT may follow it.
    [Fact]
    public async Task Borrow_OnABlackHoleSocket_FailsOverWithinTheHealthBoundAndWithoutLogout()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, clock) = (host.Pool, host.Clock);
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
        clock.Now += TimeSpan.FromSeconds(60);
        server.SilenceOpenConnections();

        var stopwatch = Stopwatch.StartNew();
        var borrowed = await pool.BorrowAsync(alice, Alice, CancellationToken.None);
        stopwatch.Stop();

        Assert.True(borrowed.IsSuccess);
        Assert.Equal(2, server.Logins);
        Assert.Equal(1, pool.Snapshot().HealthFailures);
        Assert.Equal(0, server.Logouts);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(6),
            $"failover took {stopwatch.Elapsed.TotalSeconds:F1}s — must sit under the 2 s health bound plus a fresh open, not under the 10 s client timeout");

        var flagged = await borrowed.Value.SetFlagsAsync("INBOX", [1u], MailFlag.Flagged, true, CancellationToken.None);
        Assert.True(flagged.IsSuccess); // the business command ran once, on the fresh socket
        await borrowed.Value.DisposeAsync();
    }

    // Test 6 of the spec: a cancelled command leaves the protocol in doubt.
    [Fact]
    public async Task Return_OfATaintedSession_DropsTheSocketWithoutLogout()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        var session = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value;
        server.SilenceOpenConnections();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.SetFlagsAsync("INBOX", [1u], MailFlag.Flagged, true, cts.Token));
        await session.DisposeAsync();

        Assert.Equal(0, pool.Snapshot().Idle);
        Assert.Equal(0, server.Logouts);

        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
        Assert.Equal(2, server.Logins);
    }

    // The counterpart: a clean sentinel — the server answered, the socket is fine — is reused.
    [Fact]
    public async Task Return_AfterASentinel_KeepsTheSocket()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        await using (var session = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value)
        {
            var missing = await session.SetFlagsAsync("Nope", [1u], MailFlag.Flagged, true, CancellationToken.None);
            Assert.Equal(ImapSession.FolderNotFound, missing.Error);
        }
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }

        Assert.Equal(1, server.Logins);
    }
}
```

- [ ] **Step 2 : Exécuter**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~ImapConnectionPoolHealthTests"`
Expected: 5 PASS.

Si le test trou noir dépasse la borne : MailKit ne coupe pas la lecture en attente à l'annulation du jeton sur cette version. Remplacer alors, dans `IsHealthyAsync`, l'attente du `NOOP` par une course contre le délai, et jeter le client sur dépassement :

```csharp
        var noop = client.NoOpAsync(cancellationToken);
        var finished = await Task.WhenAny(noop, Task.Delay(HealthTimeout(settings), cancellationToken));
        if (finished != noop) { client.Dispose(); return false; } // Dispose aborts the pending read
        try { await noop; return true; } catch (Exception) { return false; }
```

(la vérification d'annulation de l'appelant reste au-dessus, via le `Task.Delay` lié au jeton). Ne garder qu'une des deux formes.

Si `Return_AfterASentinel_…` échoue avec un autre message que `FolderNotFound` : MailKit résout `Nope` par `LIST "" Nope`, et le serveur ne renvoie qu'`INBOX` ; vérifier que la ligne `LIST` du `PoolImapServer` est bien celle de la Task 5.

- [ ] **Step 3 : Commit**

```bash
git add src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapConnectionPoolHealthTests.cs src/snoopy.microservice/Services/ImapConnectionPool.cs
git commit -m "test(mail): santé à l'emprunt, taint et fermeture sans LOGOUT du pool IMAP"
```

---

### Task 8 : Horloges, balayage, horizon d'emprunt, arrêt (tests 2, 7, 8)

**Files:**
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapConnectionPoolSweepTests.cs`
- Modify (seulement si rouge) : `src/snoopy.microservice/Services/ImapConnectionPool.cs`

- [ ] **Step 1 : Écrire les tests**

```csharp
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// Two clocks, both read only at borrow and return — never mid-request — and a sweeper that
/// closes what nobody will borrow again. Every LOGOUT here is a polite one: these sockets are
/// healthy, the server deserves to hear it.
/// </summary>
public sealed class ImapConnectionPoolSweepTests
{
    private static readonly Guid Alice = Guid.NewGuid();

    // Test 7: past the idle TTL, the sweep closes it and the server sees a LOGOUT unprompted.
    [Fact]
    public async Task Sweep_ClosesASocketIdlePastTheTtl()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, clock) = (host.Pool, host.Clock);
        await using (var s = (await pool.BorrowAsync(PoolTestHost.Connection(server, "alice@weesky.be", "hunter2"), Alice, CancellationToken.None)).Value) { }

        clock.Now += TimeSpan.FromSeconds(69);
        Assert.Equal(0, await pool.SweepAsync(CancellationToken.None));
        clock.Now += TimeSpan.FromSeconds(2);
        Assert.Equal(1, await pool.SweepAsync(CancellationToken.None));

        Assert.Equal(1, server.Logouts);
        Assert.Equal(1, pool.Snapshot().ClosedIdle);
        Assert.Equal(0, pool.Snapshot().Idle);
    }

    // Test 2, second half: past the absolute lifetime, the next borrow re-authenticates.
    [Fact]
    public async Task Borrow_PastTheAbsoluteLifetime_ReplacesTheSocket()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, clock) = (host.Pool, host.Clock);
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        for (var minute = 0; minute < 14; minute++)
        {
            await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
            clock.Now += TimeSpan.FromMinutes(1);
        }
        Assert.Equal(1, server.Logins);

        clock.Now += TimeSpan.FromMinutes(2);
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }

        Assert.Equal(2, server.Logins);
        Assert.Equal(1, pool.Snapshot().ClosedLifetime);
        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 1));
    }

    // Test 8: the lifetime is never enforced on a socket a request is holding.
    [Fact]
    public async Task Sweep_LeavesABorrowedSocketAloneEvenPastItsLifetime()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, clock) = (host.Pool, host.Clock);
        var session = (await pool.BorrowAsync(PoolTestHost.Connection(server, "alice@weesky.be", "hunter2"), Alice, CancellationToken.None)).Value;

        clock.Now += TimeSpan.FromMinutes(16);
        Assert.Equal(0, await pool.SweepAsync(CancellationToken.None));
        var flagged = await session.SetFlagsAsync("INBOX", [1u], MailFlag.Flagged, true, CancellationToken.None);
        Assert.True(flagged.IsSuccess);

        await session.DisposeAsync();
        Assert.Equal(0, pool.Snapshot().Idle); // refused at return, closed politely
        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 1));
    }

    // The borrow horizon: a lease that never came back stops holding its place under the cap.
    [Fact]
    public async Task Sweep_PastTheHorizon_GivesALostLeasesPlaceBack()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server, o => o.PoolMaxPerIdentity = 1);
        var (pool, clock) = (host.Pool, host.Clock);
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");
        var lost = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value;

        clock.Now += TimeSpan.FromMinutes(16);
        await pool.SweepAsync(CancellationToken.None);
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }

        Assert.Equal(0, pool.Snapshot().SingleUse); // the place was free again: pooled, not single-use
        Assert.Equal(1, pool.Snapshot().Idle);
        await lost.DisposeAsync();
        Assert.Equal(1, pool.Snapshot().Idle); // the late return did not re-enter
    }

    [Fact]
    public async Task Sweep_WithThePoolSwitchedOff_ClosesEverythingIdle()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var (pool, options) = (host.Pool, host.Options);
        await using (var s = (await pool.BorrowAsync(PoolTestHost.Connection(server, "alice@weesky.be", "hunter2"), Alice, CancellationToken.None)).Value) { }

        options.PoolEnabled = false;

        Assert.Equal(1, await pool.SweepAsync(CancellationToken.None));
        Assert.Equal(1, server.Logouts);
    }

    [Fact]
    public async Task DisposeAsync_LogsOutEveryIdleSocket()
    {
        using var server = new PoolImapServer();
        server.Start();
        var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        await using (var s = (await pool.BorrowAsync(PoolTestHost.Connection(server, "a@weesky.be", "a"), Alice, CancellationToken.None)).Value) { }
        await using (var s = (await pool.BorrowAsync(PoolTestHost.Connection(server, "b@weesky.be", "b"), Alice, CancellationToken.None)).Value) { }

        await pool.DisposeAsync();

        Assert.Equal(2, server.Logouts);
        Assert.True(await server.WaitUntilAsync(() => server.Open == 0));
    }
}
```

- [ ] **Step 2 : Exécuter**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~ImapConnectionPoolSweepTests"`
Expected: 6 PASS.

- [ ] **Step 3 : Commit**

```bash
git add src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapConnectionPoolSweepTests.cs src/snoopy.microservice/Services/ImapConnectionPool.cs
git commit -m "test(mail): horloges, balayage et horizon d'emprunt du pool IMAP"
```

---

### Task 9 : Invalidation, génération, entrée partagée, aucun `CLOSE` (tests 3, 10, 11, 12)

**Files:**
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapConnectionPoolInvalidationTests.cs`
- Modify (seulement si rouge) : `src/snoopy.microservice/Services/ImapConnectionPool.cs`

- [ ] **Step 1 : Écrire les tests**

```csharp
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// Logout closes what is idle; logout-everywhere also refuses what is out. A shared mailbox is
/// one entry for everybody who opens it with the same secret, and the generation is stamped at
/// borrow — by the borrower — so a purge by one user cannot be dodged through a socket another
/// user opened.
/// </summary>
public sealed class ImapConnectionPoolInvalidationTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    // Test 12: DELETE /Login — idle sockets closed, a borrowed one untouched and still poolable.
    [Fact]
    public async Task Close_ClosesTheUsersIdleSocketsAndLeavesTheBorrowedOneAlone()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        await using (var s = (await pool.BorrowAsync(PoolTestHost.Connection(server, "alice@weesky.be", "hunter2"), Alice, CancellationToken.None)).Value) { }
        var held = (await pool.BorrowAsync(PoolTestHost.Shared(server, "acc", "shared@weesky.be", "pw"), Alice, CancellationToken.None)).Value;

        pool.Close(Alice);

        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 1));
        await held.DisposeAsync();
        Assert.Equal(1, pool.Snapshot().Idle); // no generation turned: the lease came back to the pool
    }

    // Test 10: DELETE /Login/All — the in-flight lease closes on return instead of re-entering.
    [Fact]
    public async Task Revoke_RefusesTheLeaseThatWasOutDuringThePurge()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");
        var held = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value;
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { } // a second socket, back idle
        Assert.Equal(2, server.Logins);

        pool.Revoke(Alice);
        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 1)); // the idle one
        await held.DisposeAsync();

        Assert.Equal(0, pool.Snapshot().Idle);
        Assert.True(await server.WaitUntilAsync(() => server.Logouts == 2));
        await using (var s = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value) { }
        Assert.Equal(3, server.Logins);
    }

    // Test 11: a shared entry, opened by Bob, borrowed by Alice, purged by Alice while she holds it.
    [Fact]
    public async Task Revoke_OnASharedEntry_BindsToTheBorrowerNotTheOpener()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        var shared = PoolTestHost.Shared(server, "acc", "shared@weesky.be", "pw");
        await using (var s = (await pool.BorrowAsync(shared, Bob, CancellationToken.None)).Value) { }
        var aliceHolds = (await pool.BorrowAsync(shared, Alice, CancellationToken.None)).Value;
        Assert.Equal(1, server.Logins); // one entry for both

        pool.Revoke(Alice);
        await aliceHolds.DisposeAsync();

        Assert.Equal(0, pool.Snapshot().Idle);
        await using (var s = (await pool.BorrowAsync(shared, Bob, CancellationToken.None)).Value) { }
        Assert.Equal(2, server.Logins);
    }

    [Fact]
    public async Task Revoke_ByOneUser_DoesNotRefuseTheOtherUsersLeaseOnTheSharedEntry()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        var shared = PoolTestHost.Shared(server, "acc", "shared@weesky.be", "pw");
        var bobHolds = (await pool.BorrowAsync(shared, Bob, CancellationToken.None)).Value;

        pool.Revoke(Alice);
        await bobHolds.DisposeAsync();

        Assert.Equal(1, pool.Snapshot().Idle);
    }

    // Test 3: nothing on the return path closes a folder — CLOSE would expunge \Deleted mail.
    [Fact]
    public async Task Return_NeverClosesTheSelectedFolder()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;
        var alice = PoolTestHost.Connection(server, "alice@weesky.be", "hunter2");

        for (var i = 0; i < 2; i++)
            await using (var session = (await pool.BorrowAsync(alice, Alice, CancellationToken.None)).Value)
                Assert.True((await session.SetFlagsAsync("INBOX", [1u], MailFlag.Flagged, true, CancellationToken.None)).IsSuccess);

        Assert.Equal(1, server.Logins);
        Assert.Equal(0, server.Closes);
        Assert.Equal(0, server.Expunges);
        Assert.DoesNotContain(server.Commands, c => c.Contains(" CLOSE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Close_ForAnUnknownUser_DoesNothing()
    {
        using var server = new PoolImapServer();
        server.Start();
        await using var host = PoolTestHost.Create(server);
        var pool = host.Pool;

        pool.Close(Guid.NewGuid());
        pool.Revoke(Guid.NewGuid());
    }
}
```

- [ ] **Step 2 : Exécuter**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~ImapConnectionPoolInvalidationTests"`
Expected: 6 PASS.

- [ ] **Step 3 : Commit**

```bash
git add src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapConnectionPoolInvalidationTests.cs src/snoopy.microservice/Services/ImapConnectionPool.cs
git commit -m "test(mail): invalidation, génération et entrées partagées du pool IMAP"
```

---

### Task 10 : `RequestIdentity`, resolver et `ScopedImapSessionProvider` (test 13)

**Files:**
- Create: `src/snoopy.microservice/Services/RequestIdentity.cs`
- Modify: `src/snoopy.microservice/Services/AccountConnectionResolver.cs` (constructeur, première ligne de `ResolveAsync`)
- Modify: `src/snoopy.microservice/Services/ScopedImapSessionProvider.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ScopedImapSessionProviderTests.cs` (modifier)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/AccountConnectionResolverTests.cs` (modifier `CreateSut`, un test de plus)

**Interfaces:**
- Produces: `internal interface IRequestIdentity { Guid? UserUid { get; } }` ; `internal sealed class RequestIdentity : IRequestIdentity { void Set(Guid uid) }` (scoped). `ScopedImapSessionProvider(IImapConnectionFactory, IImapConnectionPool, IRequestIdentity, ILogger<ScopedImapSessionProvider>)`. `AccountConnectionResolver` gagne un paramètre `RequestIdentity identity` juste avant `logger`.

- [ ] **Step 1 : Adapter et compléter les tests du provider**

Dans `ScopedImapSessionProviderTests.cs`, remplacer les champs et `CreateSut` :

```csharp
    private readonly Mock<IImapConnectionFactory> _factory = new();
    private readonly Mock<IImapConnectionPool> _pool = new();
    private readonly RequestIdentity _identity = new();
    private readonly Mock<IImapSession> _session = new();

    private ScopedImapSessionProvider CreateSut()
    {
        _factory.Setup(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success<IImapSession>(_session.Object));
        _pool.Setup(p => p.BorrowAsync(It.IsAny<MailAccountConnection>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(Result.Success<IImapSession>(_session.Object));
        return new ScopedImapSessionProvider(
            _factory.Object, _pool.Object, _identity, Mock.Of<ILogger<ScopedImapSessionProvider>>());
    }
```

et l'instanciation directe dans `GetAsync_RemembersAFailureInsteadOfReconnecting` :

```csharp
        await using var sut = new ScopedImapSessionProvider(
            _factory.Object, _pool.Object, _identity, Mock.Of<ILogger<ScopedImapSessionProvider>>());
```

Ajouter deux tests :

```csharp
    // Test 13 of the spec: without a request identity the pool is never consulted.
    [Fact]
    public async Task GetAsync_WithoutARequestIdentity_OpensASingleUseConnection()
    {
        await using var sut = CreateSut();

        await sut.GetAsync(Alice, CancellationToken.None);

        _factory.Verify(f => f.OpenAsync(Alice, It.IsAny<CancellationToken>()), Times.Once);
        _pool.Verify(p => p.BorrowAsync(It.IsAny<MailAccountConnection>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_WithARequestIdentity_BorrowsFromThePoolAsThatUser()
    {
        var uid = Guid.NewGuid();
        _identity.Set(uid);
        await using var sut = CreateSut();

        await sut.GetAsync(Alice, CancellationToken.None);

        _pool.Verify(p => p.BorrowAsync(Alice, uid, It.IsAny<CancellationToken>()), Times.Once);
        _factory.Verify(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 2 : Adapter et compléter les tests du resolver**

Dans `AccountConnectionResolverTests.cs`, ajouter le champ `private readonly RequestIdentity _identity = new();` et passer `_identity` dans `CreateSut`, juste avant `NullLogger<AccountConnectionResolver>.Instance`. Ajouter :

```csharp
    // The pool indexes by user, and this is the only place on the mail path that holds the user.
    [Fact]
    public async Task Resolve_RecordsTheUserForThePool()
    {
        await CreateSut().ResolveAsync(_alice, V2Context().Request, CancellationToken.None);

        Assert.Equal(_alice.WebmailUid, _identity.UserUid);
    }
```

- [ ] **Step 3 : Vérifier rouge**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~ScopedImapSessionProviderTests|FullyQualifiedName~AccountConnectionResolverTests"`
Expected: échec de compilation.

- [ ] **Step 4 : Implémenter**

`RequestIdentity.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Services;

/// <summary>The borrower the pool indexes by. Null on a request that never resolved an account.</summary>
internal interface IRequestIdentity
{
    Guid? UserUid { get; }
}

/// <summary>
/// Scoped: set once per request by <see cref="AccountConnectionResolver"/>, the only mail-path
/// service that holds the user, and read by <see cref="ScopedImapSessionProvider"/> when it
/// borrows. Neither the connection record nor the session interface carries the user, on purpose.
/// </summary>
internal sealed class RequestIdentity : IRequestIdentity
{
    public Guid? UserUid { get; private set; }

    public void Set(Guid uid)
    {
        if (uid != Guid.Empty) UserUid = uid;
    }
}
```

`AccountConnectionResolver.cs` — le constructeur gagne `RequestIdentity identity,` avant `ILogger<AccountConnectionResolver> logger`, et `ResolveAsync` commence par :

```csharp
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        identity.Set(user.WebmailUid);
```

`ScopedImapSessionProvider.cs` — remplacer le résumé et la déclaration :

```csharp
/// <summary>
/// Holds one IMAP session for the lifetime of the DI scope — the HTTP request.
///
/// Registered scoped, so the container disposes it at the end of the request; no caller owns the
/// session, which is why no repository disposes what it is handed. The session is per request,
/// the socket under it need not be: when the request has an identity, the client is borrowed
/// from <see cref="IImapConnectionPool"/> and goes back to it at scope teardown. Without one —
/// no account was resolved — a single-use connection is opened, exactly as before pooling.
/// </summary>
internal sealed class ScopedImapSessionProvider(
    IImapConnectionFactory factory,
    IImapConnectionPool pool,
    IRequestIdentity identity,
    ILogger<ScopedImapSessionProvider> logger)
    : IImapSessionProvider, IAsyncDisposable
```

et dans `GetAsync`, remplacer `var opened = await factory.OpenAsync(connection, cancellationToken);` par :

```csharp
            var opened = identity.UserUid is { } uid
                ? await pool.BorrowAsync(connection, uid, cancellationToken)
                : await factory.OpenAsync(connection, cancellationToken);
```

Le commentaire de `CloseAsync` (« The request is over either way… ») et la journalisation restent ; le message `"Closing the IMAP session failed"` devient `"Releasing the IMAP session failed"`.

- [ ] **Step 5 : Vérifier vert**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~ScopedImapSessionProviderTests|FullyQualifiedName~AccountConnectionResolverTests"`
Expected: PASS. Le projet produit ne compile pas encore complètement tant que la DI (Task 13) n'enregistre pas les nouveaux types — mais la compilation, elle, passe : vérifier avec `dotnet build src/snoopy.microservice.sln`.

- [ ] **Step 6 : Commit**

```bash
git add src/snoopy.microservice/Services/RequestIdentity.cs src/snoopy.microservice/Services/AccountConnectionResolver.cs src/snoopy.microservice/Services/ScopedImapSessionProvider.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/ScopedImapSessionProviderTests.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/AccountConnectionResolverTests.cs
git commit -m "feat(mail): l'identité de requête arrive au pool, le provider scoped emprunte" -m "Sans identité résolue : connexion à usage unique, comme avant."
```

---

### Task 11 : `LoginController` — purge à la déconnexion

**Files:**
- Modify: `src/snoopy.microservice/Controllers/LoginController.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/LoginControllerTests.cs` (modifier)

**Interfaces:**
- Consumes: `IImapConnectionPool.Close(Guid)`, `Revoke(Guid)` (Task 6) ; `AuthenticatedUser.WebmailUid` (claim `WebmailClaimTypes.Uid`, `Guid.Empty` quand absent).

- [ ] **Step 1 : Adapter et compléter les tests**

Dans `LoginControllerTests.cs`, ajouter le champ `private readonly Mock<IImapConnectionPool> _pool = new();` et, dans les **trois** instanciations `new LoginController(`, insérer `_pool.Object,` juste avant `NullLogger<LoginController>.Instance`. Ajouter :

```csharp
    // DELETE /Login is housekeeping: the user's idle sockets go, the generation does not turn.
    [Fact]
    public void Logout_ClosesTheUsersPooledSockets()
    {
        var uid = Guid.NewGuid();
        var controller = CreateController();
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", uid);

        controller.Logout();

        _pool.Verify(p => p.Close(uid), Times.Once);
        _pool.Verify(p => p.Revoke(It.IsAny<Guid>()), Times.Never);
    }

    // DELETE /Login/All is the revocation: sockets out right now must not come back either.
    [Fact]
    public async Task LogoutEverywhere_RevokesTheUsersPooledSockets()
    {
        var uid = Guid.NewGuid();
        var controller = CreateController();
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", uid);

        await controller.LogoutEverywhere(CancellationToken.None);

        _pool.Verify(p => p.Revoke(uid), Times.Once);
    }
```

- [ ] **Step 2 : Vérifier rouge**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~LoginControllerTests"`
Expected: échec de compilation.

- [ ] **Step 3 : Implémenter**

Constructeur de `LoginController` : ajouter `IImapConnectionPool pool,` avant `ILogger<LoginController> logger`. Dans `Logout()`, avant `return NoContent();` :

```csharp
        pool.Close(AuthenticatedUser.WebmailUid);
```

Dans `LogoutEverywhere`, juste après `sessions.Forget(email);` :

```csharp
        pool.Revoke(AuthenticatedUser.WebmailUid);
```

Compléter le `<remarks>` de `LogoutEverywhere` d'une phrase : « It also refuses every pooled IMAP socket the account has out, so a session in someone else's hands loses the mailbox at once rather than at the socket's own expiry. »

- [ ] **Step 4 : Vérifier vert**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~LoginControllerTests"`
Expected: PASS.

- [ ] **Step 5 : Commit**

```bash
git add src/snoopy.microservice/Controllers/LoginController.cs src/snoopy.microservice/snoopy.microservice.Tests/Controllers/LoginControllerTests.cs
git commit -m "feat(mail): la déconnexion ferme les sockets IMAP poolées, la révocation les refuse"
```

---

### Task 12 : `ImapPoolSweeper` — balayage à 15 s et ligne d'agrégat

**Files:**
- Create: `src/snoopy.microservice/Services/ImapPoolSweeper.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapPoolSweeperTests.cs`

**Interfaces:**
- Consumes: `IImapConnectionPool.SweepAsync`, `Snapshot` (Task 6).
- Produces: `internal sealed class ImapPoolSweeper(IImapConnectionPool pool, ILogger<ImapPoolSweeper> logger, TimeSpan? period = null) : BackgroundService` ; `internal static readonly TimeSpan DefaultPeriod = 15 s` ; `internal const int PassesPerReport = 20`.

Pas `PeriodicSweeper` comme classe de base (spec, § Balayage) : il journalise à chaque tick, et sa passe de démarrage n'a rien à balayer.

- [ ] **Step 1 : Écrire les tests rouges**

```csharp
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ImapPoolSweeperTests
{
    private readonly Mock<IImapConnectionPool> _pool = new();
    private readonly Mock<ILogger<ImapPoolSweeper>> _logger = new();

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!predicate())
        {
            if (DateTime.UtcNow > deadline) return false;
            await Task.Delay(10);
        }
        return true;
    }

    [Fact]
    public async Task ExecuteAsync_SweepsThePoolEveryPeriod()
    {
        var sweeps = 0;
        _pool.Setup(p => p.SweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => { sweeps++; return 0; });
        using var sweeper = new ImapPoolSweeper(_pool.Object, _logger.Object, TimeSpan.FromMilliseconds(20));

        await sweeper.StartAsync(CancellationToken.None);
        Assert.True(await WaitUntilAsync(() => sweeps >= 3));
        await sweeper.StopAsync(CancellationToken.None);
    }

    // A pass that throws must not end the loop: the next tick retries.
    [Fact]
    public async Task ExecuteAsync_SurvivesAFailingPass()
    {
        var calls = 0;
        _pool.Setup(p => p.SweepAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(() => ++calls == 1 ? throw new InvalidOperationException("boom") : 0);
        using var sweeper = new ImapPoolSweeper(_pool.Object, _logger.Object, TimeSpan.FromMilliseconds(20));

        await sweeper.StartAsync(CancellationToken.None);
        Assert.True(await WaitUntilAsync(() => calls >= 3));
        await sweeper.StopAsync(CancellationToken.None);
    }

    // Counters, not events: one aggregate line per PassesPerReport passes, none in between.
    [Fact]
    public async Task ExecuteAsync_LogsOneAggregateLinePerReportInterval()
    {
        var sweeps = 0;
        _pool.Setup(p => p.SweepAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => { sweeps++; return 0; });
        _pool.Setup(p => p.Snapshot()).Returns(new PoolStatistics(1, 2, 3, 4, 5, 6, 7, 8, 9, 10));
        using var sweeper = new ImapPoolSweeper(_pool.Object, _logger.Object, TimeSpan.FromMilliseconds(5));

        await sweeper.StartAsync(CancellationToken.None);
        Assert.True(await WaitUntilAsync(() => sweeps >= ImapPoolSweeper.PassesPerReport));
        await sweeper.StopAsync(CancellationToken.None);

        _logger.Verify(l => l.Log(
                LogLevel.Information, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("IMAP pool")),
                null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
        _logger.Verify(l => l.Log(
                LogLevel.Information, It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("IMAP pool")),
                null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtMost(sweeps / ImapPoolSweeper.PassesPerReport + 1));
    }
}
```

- [ ] **Step 2 : Vérifier rouge**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~ImapPoolSweeperTests"`
Expected: échec de compilation.

- [ ] **Step 3 : Implémenter**

```csharp
namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Closes pooled IMAP sockets nobody will borrow again — without it, a socket whose tab was closed
/// would live until the next borrow, which is never. Not a PeriodicSweeper: at 15 s a line per
/// tick is 5,760 lines a day, so the heartbeat is one aggregate every <see cref="PassesPerReport"/>
/// passes, and a startup pass has nothing to sweep on an empty pool.
/// </summary>
internal sealed class ImapPoolSweeper(
    IImapConnectionPool pool,
    ILogger<ImapPoolSweeper> logger,
    TimeSpan? period = null) : BackgroundService
{
    internal static readonly TimeSpan DefaultPeriod = TimeSpan.FromSeconds(15);

    /// <summary>Five minutes at the default period.</summary>
    internal const int PassesPerReport = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(period ?? DefaultPeriod);
        var passes = 0;

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await pool.SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The IMAP pool sweep failed");
            }

            if (++passes % PassesPerReport == 0) Report();
        }
    }

    private void Report()
    {
        var s = pool.Snapshot();
        logger.LogInformation(
            "IMAP pool: {Idle} idle, {Borrowed} borrowed; {Borrows} borrows, {Reused} reused, {Opened} opened, " +
            "{SingleUse} single-use, {HealthFailures} health failures, {ClosedIdle} closed idle, " +
            "{ClosedLifetime} closed lifetime, {Evicted} evicted",
            s.Idle, s.Borrowed, s.Borrows, s.Reused, s.Opened, s.SingleUse, s.HealthFailures,
            s.ClosedIdle, s.ClosedLifetime, s.Evicted);
    }
}
```

`WaitForNextTickAsync` lève `OperationCanceledException` à l'arrêt ; `BackgroundService` l'absorbe comme pour `PeriodicSweeper`.

- [ ] **Step 4 : Vérifier vert**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj --filter "FullyQualifiedName~ImapPoolSweeperTests"`
Expected: 3 PASS.

- [ ] **Step 5 : Commit**

```bash
git add src/snoopy.microservice/Services/ImapPoolSweeper.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapPoolSweeperTests.cs
git commit -m "feat(mail): balayeur du pool IMAP à 15 s, une ligne d'agrégat par 5 min"
```

---

### Task 13 : Câblage DI, `appsettings.json`, suite complète

**Files:**
- Modify: `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs` (`AddMailServices`)
- Modify: `src/snoopy.microservice.host/appsettings.json` (section `Mail`)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Configuration/` — vérifier s'il existe un test qui construit le conteneur (`grep -rn "AddMailServices" src/snoopy.microservice/snoopy.microservice.Tests`) ; s'il existe, il doit rester vert ; sinon rien à ajouter.

- [ ] **Step 1 : Enregistrer les services**

Dans `AddMailServices`, remplacer `services.AddSingleton<IImapConnectionFactory, ImapConnectionFactory>();` par :

```csharp
        // One factory instance under two faces: the probes see IImapConnectionFactory and always
        // authenticate for real; only the pool sees IImapClientSource.
        services.AddSingleton<ImapConnectionFactory>();
        services.AddSingleton<IImapConnectionFactory>(sp => sp.GetRequiredService<ImapConnectionFactory>());
        services.AddSingleton<IImapClientSource>(sp => sp.GetRequiredService<ImapConnectionFactory>());

        services.AddSingleton<CredentialFingerprint>();
        services.AddSingleton<ImapConnectionPool>();
        services.AddSingleton<IImapConnectionPool>(sp => sp.GetRequiredService<ImapConnectionPool>());
        services.AddHostedService<ImapPoolSweeper>();
```

et, à côté de `services.AddScoped<IImapSessionProvider, ScopedImapSessionProvider>();` :

```csharp
        services.AddScoped<RequestIdentity>();
        services.AddScoped<IRequestIdentity>(sp => sp.GetRequiredService<RequestIdentity>());
```

Le commentaire au-dessus de l'enregistrement scoped (« Scoped, so the whole request shares one authenticated IMAP connection and the container closes it when the request ends. ») devient : « Scoped, so the whole request shares one IMAP session; the container releases it when the request ends — back to the pool, or closed. »

`TimeProvider.System` est déjà enregistré dans `src/snoopy.microservice.host/Program.cs:30`. `ImapConnectionPool` est `IAsyncDisposable` et singleton : le conteneur le dispose à l'arrêt de l'hôte, ce qui déclenche les `LOGOUT` parallèles sous budget.

- [ ] **Step 2 : `appsettings.json`**

Dans la section `Mail`, après `"AllowCleartext": false,` :

```json
    "PoolEnabled": true,
    "PoolIdleSeconds": 70,
    "PoolMaxLifetimeMinutes": 15,
    "PoolMaxPerIdentity": 4,
    "PoolMaxTotal": 200,
    "PoolHealthTimeoutSeconds": 3,
```

- [ ] **Step 3 : Un test de résolution du conteneur**

Créer `src/snoopy.microservice/snoopy.microservice.Tests/Configuration/MailServicesRegistrationTests.cs` :

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Configuration;

/// <summary>
/// The pool is reachable through exactly one door. Resolving it, its sweeper and the scoped
/// provider proves the registrations line up; the factory being one instance under two
/// interfaces is what keeps the probes and the pool on the same certificate policy.
/// </summary>
public sealed class MailServicesRegistrationTests
{
    private static ServiceCollection Register()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOptionsMonitor<MailOptions>>(new StaticOptionsMonitor(new MailOptions()));
        services.AddMailServices();
        return services;
    }

    // Resolved one by one, never IEnumerable<IHostedService>: that would construct every sweeper,
    // and the others need options this container does not carry.
    [Fact]
    public void ThePoolResolvesAndItsSweeperIsRegistered()
    {
        var services = Register();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IImapConnectionPool>());
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(ImapPoolSweeper));
    }

    [Fact]
    public void TheFactoryIsOneInstanceUnderBothInterfaces()
    {
        using var provider = Register().BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<IImapConnectionFactory>(),
            provider.GetRequiredService<IImapClientSource>());
    }

    [Fact]
    public void TheScopedProviderResolvesWithARequestIdentity()
    {
        using var provider = Register().BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IImapSessionProvider>());
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<RequestIdentity>(),
            scope.ServiceProvider.GetRequiredService<IRequestIdentity>());
    }

    private sealed class StaticOptionsMonitor(MailOptions value) : IOptionsMonitor<MailOptions>
    {
        public MailOptions CurrentValue => value;
        public MailOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<MailOptions, string?> listener) => null;
    }
}
```

Si `AddMailServices` exige d'autres dépendances non enregistrées ici (le conteneur le dira à `GetRequiredService`), les ajouter au `Build()` de ce test avec des `Mock.Of<>()` — ne pas les enregistrer dans le produit.

- [ ] **Step 4 : Suite complète**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests/snoopy.microservice.Tests.csproj`
Expected: tout vert. Puis `git status` : si `ApiDocumentation.xml` apparaît modifié, `git checkout -- <son chemin>` avant de committer.

- [ ] **Step 5 : Relire les invariants de sécurité sur le code final**

Vérifier, `grep` à l'appui, avant de committer :

```bash
grep -n "Fingerprint" src/snoopy.microservice/Services/ImapConnectionPool.cs | grep -i "log"          # doit être vide
grep -n "MailCredential\|MailAccountConnection" src/snoopy.microservice/Services/ImapConnectionPool.cs  # seulement dans BorrowAsync/SingleUseAsync/PoolKey.From, jamais dans Entry
grep -n "CloseAsync\|Close(" src/snoopy.microservice/Services/ImapConnectionPool.cs | grep -i folder     # doit être vide
grep -rn "IImapClientSource\|IImapConnectionPool" src/snoopy.microservice/Authentication src/snoopy.microservice/Controllers/ConnectedAccountsController.cs  # doit être vide
```

- [ ] **Step 6 : Commit**

```bash
git add src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs src/snoopy.microservice.host/appsettings.json src/snoopy.microservice/snoopy.microservice.Tests/Configuration/MailServicesRegistrationTests.cs
git commit -m "feat(mail): câblage du pool de connexions IMAP et de son balayeur" -m "Six réglages Mail:Pool*, PoolEnabled à chaud restaure une connexion par requête."
```

---

## Ce que le plan ne fait pas, et pourquoi

- **Le raffinement du taint sur `ImapCommandException`** (spec, § Préconditions, point 2) reste à l'état « tout marque » : sûr par construction. Il ne s'active qu'après vérification sur MailKit 4.17 que l'exception est toujours levée après lecture complète de la réponse taguée — une tranche à part, une fois la mesure de `HealthFailures` et de `ClosedIdle` en main.
- **La rétention du credential par `ImapClient`** (spec, § Préconditions, point 1) : à vérifier **avant la Task 6** par lecture de la source MailKit 4.17 (`MailKit/Net/Imap/ImapClient.cs`, méthode `AuthenticateAsync(NetworkCredential, …)` — le mécanisme SASL est local à la méthode ; `ImapClient` n'expose aucune propriété de credentials). Si la vérification contredit ceci, ajouter dans `IsHealthyAsync`, juste après le `NOOP`, la remise à null du champ concerné, et noter la version dans un commentaire « Verified on MailKit 4.17 » comme `ImapMessageCommands.cs:654` le fait.
- **`IDLE`, canal push, pooling SMTP** : hors périmètre par la spec.
