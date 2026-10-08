# Image Docker, compose et workflow GHCR — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publier sur GHCR une image unique du webmail (plateforme `generic`) où l'API sert aussi les pages, avec son compose, son guide, et un workflow candidate → release.

**Architecture:** Un fichier `Configuration/FrontendHosting.cs` (lecture/validation de `Frontend:*`, service des pages, `config.js`, séparation par hôte) branché dans `Program.cs`, inactif sans `Frontend:Path`. Une commande `healthcheck` sur le modèle de `migrate`. Un Dockerfile en trois étapes (Node, SDK, `aspnet` chiseled-extra), un script de test de démarrage partagé entre le poste et la CI, et `.github/workflows/image.yml` (build/test, candidate, release par promotion).

**Tech Stack:** ASP.NET Core .NET 10 (StaticFiles, TestHost), xUnit 2.9, Moq ; React/Vitest ; Docker buildx, GitHub Actions (`docker/build-push-action@v6`), GHCR, Dependabot.

**Spec:** `docs/superpowers/2026-10-08-docker-image-design.md`

## Global Constraints

- Rien ne s'active sans `Frontend:Path` : le serveur déployé par `deploy.yml` garde exactement son comportement (CORS obligatoire, `default-src 'none'` partout, `/` en 404).
- Préfixes de l'API : `/api`, `/dav`, `/.well-known`, `/swagger` ; `/health` à part (servi sur tout hôte). Comparaisons de chemin par `PathString.StartsWithSegments` (insensible à la casse).
- `Frontend:ApiBase` : vide, ou une origine `http(s)://hôte[:port]` sans chemin, requête ni fragment ; sinon refus de démarrer en citant la valeur.
- `config.js` : exactement `window.SCOTTY_CONFIG = {"apiBase":"…"};\n`, valeur sérialisée par `System.Text.Json` (encodeur par défaut), `Content-Type: text/javascript; charset=utf-8`, `Cache-Control: no-cache`.
- Cache : `/assets/*` → `public, max-age=31536000, immutable` ; tout le reste des pages → `no-cache`.
- Image : `ghcr.io/darthmaul0181/scotty-webmail` ; uid `1654` ; port `8080` ; état `/var/lib/scotty` ; `SCOTTY_IMAGE_VERSION` ; `docker/VERSION` = `1.0.0`.
- Étiquettes : candidate `X.Y.Z-rc.N` ; release `X.Y.Z`, `X.Y`, `X`, `latest` ; tag Git `image-vX.Y.Z`.
- Messages, logs, textes de l'interface et guide public en anglais ; marche à suivre du propriétaire (`docs/operations/`) en français. Le guide public ne mentionne aucun serveur du propriétaire.
- Commentaires seulement quand le code ne s'explique pas, 3 lignes max. Pas de duplication.
- Tests backend : `dotnet test` depuis `src/` (jamais `--no-build` quand des fichiers de test sont ajoutés). Avant chaque commit : `git checkout -- src/scotty.microservice/ApiDocumentation.xml` si `git status` le montre modifié.
- Tests front : `npm test -- --run <fichier>` depuis `src/frontend`, puis `npm run lint`.
- Dépôt en `autocrlf=true` : remplacements multi-lignes dans un fichier existant par script python (chaînes brutes `r"..."`, `\r\n` respecté) ; vérifier `git diff`.
- Docker sur le poste : `export PATH="$PATH:/c/Users/mick0/AppData/Local/Programs/DockerDesktop/resources/bin"` dans chaque commande Bash qui appelle `docker`.
- Commits : deux lignes max, anglais, heredoc `git commit -F - <<'EOF'`, terminés par `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`, jamais `@` en début ou fin. Ne jamais pousser.

## Review Focus

1. Un fichier du bundle dont l'extension n'est pas connue de `UseStaticFiles` : il retomberait sur `index.html` et le navigateur recevrait du HTML à la place. Couvert par le test de démarrage (tâche 6), qui retélécharge chaque fichier de `/app/frontend` et le compare octet à octet.
2. Un proxy qui ne transmet pas l'en-tête `Host` en montage deux sous-domaines : tout répond 404. Comportement voulu (visible tout de suite), documenté dans le tableau de dépannage (tâche 8) ; la règle de séparation est figée par les tests de la tâche 2.
3. `Frontend:ApiBase` avec une barre finale, une majuscule ou un port (`https://API.example.net:8443/`) : accepté, normalisé, l'hôte comparé sans casse ni port. Tests tâches 1 et 2.
4. Une requête `POST` ou `PUT` vers une adresse de page (`/mail/inbox`) : 404, jamais `index.html` ni une exécution de l'API. Test tâche 2.
5. Le conteneur en `read_only` : l'API ne doit rien écrire hors de `/tmp` et `/var/lib/scotty` (logs console, clé et trousseau dans le volume). Couvert par le test de démarrage (tâche 6), lancé avec `--read-only`.

---

### Task 1: `FrontendSettings` — lire et valider `Frontend:*`

**Files:**
- Create: `src/scotty.microservice/Configuration/FrontendHosting.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Configuration/FrontendSettingsTests.cs`

**Interfaces:**
- Produces: `public sealed record FrontendSettings(string Root, string ApiBase, string? ApiHost)` avec `public static FrontendSettings? Read(IConfiguration configuration)` — `null` sans `Frontend:Path` ; `ApiBase` = `""` ou l'origine sans barre finale ; `ApiHost` = l'hôte de `ApiBase`, `null` quand `ApiBase` est vide. Lève `InvalidOperationException`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.Extensions.Configuration;
using weesky.Scotty.Microservice.Configuration;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

public sealed class FrontendSettingsTests : IDisposable
{
    private readonly DirectoryInfo dist = Directory.CreateTempSubdirectory("scotty-dist-");

    public FrontendSettingsTests() => File.WriteAllText(Path.Combine(dist.FullName, "index.html"), "<!doctype html>");

    public void Dispose() => dist.Delete(recursive: true);

    private static FrontendSettings? Read(string? path, string? apiBase = null) =>
        FrontendSettings.Read(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:Path"] = path,
                ["Frontend:ApiBase"] = apiBase,
            })
            .Build());

    [Fact]
    public void WithoutAPath_ThereIsNoFrontend() => Assert.Null(Read(null, "https://api.example.net"));

    [Fact]
    public void APathWithoutIndex_RefusesToStart()
    {
        var empty = Directory.CreateTempSubdirectory("scotty-empty-");
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => Read(empty.FullName));
            Assert.Contains(empty.FullName, error.Message);
        }
        finally
        {
            empty.Delete();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyApiBase_MeansOneAddress(string? apiBase)
    {
        var settings = Read(dist.FullName, apiBase)!;

        Assert.Equal(dist.FullName, settings.Root);
        Assert.Equal("", settings.ApiBase);
        Assert.Null(settings.ApiHost);
    }

    [Theory]
    [InlineData("https://api.example.net", "https://api.example.net", "api.example.net")]
    [InlineData("https://API.Example.net/", "https://api.example.net", "api.example.net")]
    [InlineData("http://api.example.net:8443", "http://api.example.net:8443", "api.example.net")]
    public void AnOrigin_IsKeptWithoutItsTrailingSlash(string apiBase, string expected, string host)
    {
        var settings = Read(dist.FullName, apiBase)!;

        Assert.Equal(expected, settings.ApiBase);
        Assert.Equal(host, settings.ApiHost);
    }

    [Theory]
    [InlineData("api.example.net")]
    [InlineData("ftp://api.example.net")]
    [InlineData("https://example.net/scotty")]
    [InlineData("https://example.net/?x=1")]
    [InlineData("https://user:secret@api.example.net")]
    public void AnythingElse_RefusesToStartNamingTheValue(string apiBase)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Read(dist.FullName, apiBase));

        Assert.Contains($"'{apiBase}'", error.Message);
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `cd src && dotnet test --filter FullyQualifiedName~FrontendSettingsTests`
Expected: build FAIL, `The type or namespace name 'FrontendSettings' could not be found`.

- [ ] **Step 3: Write the implementation**

`src/scotty.microservice/Configuration/FrontendHosting.cs` :

```csharp
using Microsoft.Extensions.Configuration;

namespace weesky.Scotty.Microservice.Configuration;

/// <summary>
/// The web pages, served by the API itself only where <c>Frontend:Path</c> names them (the Docker
/// image). <paramref name="ApiHost"/> is the API's own host when the pages call it on another
/// address, and null when one address serves both.
/// </summary>
public sealed record FrontendSettings(string Root, string ApiBase, string? ApiHost)
{
    public static FrontendSettings? Read(IConfiguration configuration)
    {
        var root = configuration["Frontend:Path"];
        if (string.IsNullOrWhiteSpace(root)) return null;
        if (!File.Exists(Path.Combine(root, "index.html")))
            throw new InvalidOperationException($"Frontend:Path is '{root}', which holds no index.html.");

        var apiBase = configuration["Frontend:ApiBase"]?.Trim() ?? "";
        if (apiBase.Length == 0) return new FrontendSettings(root, "", null);

        if (!Uri.TryCreate(apiBase, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
        {
            throw new InvalidOperationException(
                $"Frontend:ApiBase holds '{apiBase}'. Write the API's address alone, such as " +
                "https://api.example.net, or leave it empty when one address serves both the pages and the API.");
        }

        return new FrontendSettings(root, uri.GetLeftPart(UriPartial.Authority), uri.Host);
    }
}
```

- [ ] **Step 4: Run them to verify they pass**

Run: `cd src && dotnet test --filter FullyQualifiedName~FrontendSettingsTests`
Expected: PASS, 13 tests.

- [ ] **Step 5: Commit**

```bash
git checkout -- src/scotty.microservice/ApiDocumentation.xml 2>/dev/null
git add src/scotty.microservice/Configuration/FrontendHosting.cs src/scotty.microservice/scotty.microservice.Tests/Configuration/FrontendSettingsTests.cs
git commit -F - <<'EOF'
Read and validate the Frontend settings the Docker image will set

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: Servir les pages, `config.js`, séparer par hôte, en-têtes de sécurité

**Files:**
- Modify: `src/scotty.microservice/Configuration/FrontendHosting.cs` (ajout de la classe `FrontendHosting`)
- Modify: `src/scotty.microservice/Configuration/SecurityConfiguration.cs:268-284` (`UseSecurityHeaders`)
- Modify: `src/scotty.microservice.host/Program.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Configuration/FrontendHostingTests.cs`

**Interfaces:**
- Consumes: `FrontendSettings` (tâche 1).
- Produces:
  - `public static class FrontendHosting` avec `public static bool IsPagePath(PathString path)`, `internal static bool Misrouted(HttpRequest request, FrontendSettings frontend)`, `public static IApplicationBuilder UseFrontendHosting(this IApplicationBuilder app, FrontendSettings? frontend)` ;
  - `SecurityConfiguration.UseSecurityHeaders(this IApplicationBuilder app, FrontendSettings? frontend = null)` ;
  - dans `Program.cs`, une variable `frontend` (`FrontendSettings?`) lue juste après `UsesWeeskyPlatform()`, que la tâche 3 passera à `AddFrontendCors`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using weesky.Scotty.Microservice.Configuration;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

/// <summary>The pipeline order of Program.cs, with three stand-in endpoints for the API.</summary>
public sealed class FrontendHostingTests : IAsyncLifetime
{
    private const string Index = "<!doctype html><div id=\"root\"></div>";
    private readonly DirectoryInfo dist = Directory.CreateTempSubdirectory("scotty-dist-");
    private WebApplication? app;

    public Task InitializeAsync()
    {
        File.WriteAllText(Path.Combine(dist.FullName, "index.html"), Index);
        File.WriteAllBytes(Path.Combine(dist.FullName, "icon-192.png"), [0x89, 0x50, 0x4E, 0x47]);
        Directory.CreateDirectory(Path.Combine(dist.FullName, "assets"));
        File.WriteAllText(Path.Combine(dist.FullName, "assets", "app-abc123.js"), "console.log(1)");
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
        dist.Delete(recursive: true);
    }

    private FrontendSettings OneAddress => new(dist.FullName, "", null);

    private FrontendSettings TwoAddresses => new(dist.FullName, "https://api.monmail.net", "api.monmail.net");

    private async Task<HttpClient> StartAsync(FrontendSettings? frontend)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        app = builder.Build();
        app.UseSecurityHeaders(frontend);
        app.UseFrontendHosting(frontend);
        app.MapGet("/api/ping", () => "api");
        app.MapGet("/dav/ping", () => "dav");
        app.MapGet("/health", () => "Healthy");
        await app.StartAsync();
        return app.GetTestClient();
    }

    [Fact]
    public async Task WithoutFrontend_NothingIsServedAndTheApiKeepsItsPolicy()
    {
        var client = await StartAsync(null);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/")).StatusCode);
        var api = await client.GetAsync("/api/ping");
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", api.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task ADeepLink_AnswersTheIndexUncached()
    {
        var client = await StartAsync(OneAddress);

        var response = await client.GetAsync("/mail/inbox");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Index, await response.Content.ReadAsStringAsync());
        Assert.Equal("no-cache", response.Headers.CacheControl!.ToString());
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task TheRoot_AnswersTheIndex()
    {
        var client = await StartAsync(OneAddress);

        Assert.Equal(Index, await client.GetStringAsync("/"));
    }

    [Fact]
    public async Task AnAsset_IsCachedForAYear()
    {
        var client = await StartAsync(OneAddress);

        var response = await client.GetAsync("/assets/app-abc123.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=31536000, immutable", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task AMissingAsset_IsNotFound()
    {
        var client = await StartAsync(OneAddress);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/assets/absent.js")).StatusCode);
    }

    [Fact]
    public async Task AFileBesideTheIndex_IsServedUncached()
    {
        var client = await StartAsync(OneAddress);

        var response = await client.GetAsync("/icon-192.png");

        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-cache", response.Headers.CacheControl!.ToString());
    }

    [Theory]
    [InlineData("/api/unknown")]
    [InlineData("/dav/unknown")]
    [InlineData("/.well-known/unknown")]
    [InlineData("/API/unknown")]
    public async Task AnApiAddress_NeverAnswersTheIndex(string path)
    {
        var client = await StartAsync(OneAddress);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("root", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheApi_KeepsItsStrictPolicyBesideThePages()
    {
        var client = await StartAsync(OneAddress);

        var response = await client.GetAsync("/api/ping");

        Assert.Equal("api", await response.Content.ReadAsStringAsync());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task AWriteToAPageAddress_IsNotFound()
    {
        var client = await StartAsync(OneAddress);

        var response = await client.PostAsync("/mail/inbox", new StringContent("x"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConfigJs_CarriesTheApiBaseUncached()
    {
        var client = await StartAsync(TwoAddresses);

        var response = await client.GetAsync("http://box.monmail.net/config.js");

        Assert.Equal("window.SCOTTY_CONFIG = {\"apiBase\":\"https://api.monmail.net\"};\n", await response.Content.ReadAsStringAsync());
        Assert.Equal("text/javascript", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-cache", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task ConfigJs_IsEmptyForOneAddress()
    {
        var client = await StartAsync(OneAddress);

        Assert.Equal("window.SCOTTY_CONFIG = {\"apiBase\":\"\"};\n", await client.GetStringAsync("/config.js"));
    }

    [Fact]
    public async Task ConfigJs_CannotBeBrokenOutOfByItsValue()
    {
        var client = await StartAsync(new FrontendSettings(dist.FullName, "https://a.example\"</script><script>alert(1)//", null));

        var body = await client.GetStringAsync("/config.js");

        Assert.DoesNotContain("</script>", body);
        Assert.DoesNotContain("a.example\"", body);
    }

    [Theory]
    [InlineData("api.monmail.net", "/api/ping", HttpStatusCode.OK)]
    [InlineData("api.monmail.net", "/", HttpStatusCode.NotFound)]
    [InlineData("api.monmail.net", "/config.js", HttpStatusCode.NotFound)]
    [InlineData("box.monmail.net", "/", HttpStatusCode.OK)]
    [InlineData("box.monmail.net", "/api/ping", HttpStatusCode.NotFound)]
    [InlineData("box.monmail.net", "/dav/ping", HttpStatusCode.NotFound)]
    [InlineData("api.monmail.net", "/health", HttpStatusCode.OK)]
    [InlineData("box.monmail.net", "/health", HttpStatusCode.OK)]
    [InlineData("127.0.0.1", "/health", HttpStatusCode.OK)]
    public async Task TwoAddresses_EachServeOnlyTheirOwnRole(string host, string path, HttpStatusCode expected)
    {
        var client = await StartAsync(TwoAddresses);

        Assert.Equal(expected, (await client.GetAsync($"http://{host}{path}")).StatusCode);
    }

    [Fact]
    public async Task OneAddress_ServesBothRolesOnAnyHost()
    {
        var client = await StartAsync(OneAddress);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("http://anything.example/api/ping")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("http://anything.example/")).StatusCode);
    }

    [Theory]
    [InlineData("API.MonMail.net:8443", "/", true)]
    [InlineData("API.MonMail.net:8443", "/api/ping", false)]
    [InlineData("Box.MonMail.net", "/api/ping", true)]
    [InlineData("Box.MonMail.net", "/mail/inbox", false)]
    public void TheHost_IsComparedWithoutCaseOrPort(string host, string path, bool misrouted)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Request.Path = path;

        Assert.Equal(misrouted, FrontendHosting.Misrouted(context.Request, TwoAddresses));
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `cd src && dotnet test --filter FullyQualifiedName~FrontendHostingTests`
Expected: build FAIL, `'FrontendHosting' does not exist` and no overload of `UseSecurityHeaders` taking one argument.

- [ ] **Step 3: Write the implementation**

Ajouter à `FrontendHosting.cs` (usings en tête du fichier : `System.Text`, `System.Text.Json`, `Microsoft.AspNetCore.Builder`, `Microsoft.AspNetCore.Http`, `Microsoft.Extensions.FileProviders`) :

```csharp
public static class FrontendHosting
{
    private static readonly PathString[] ApiPrefixes = ["/api", "/dav", "/.well-known", "/swagger"];
    private static readonly PathString Health = "/health";
    private static readonly PathString Assets = "/assets";
    private const string NoCache = "no-cache";
    private const string Immutable = "public, max-age=31536000, immutable";

    /// <summary>True for an address the pages answer: neither the API's nor <c>/health</c>.</summary>
    public static bool IsPagePath(PathString path) =>
        !path.StartsWithSegments(Health) && !ApiPrefixes.Any(prefix => path.StartsWithSegments(prefix));

    /// <summary>With two addresses, a page asked of the API's host or the API asked of another.</summary>
    internal static bool Misrouted(HttpRequest request, FrontendSettings frontend) =>
        frontend.ApiHost is not null
        && !request.Path.StartsWithSegments(Health)
        && IsPagePath(request.Path) == string.Equals(request.Host.Host, frontend.ApiHost, StringComparison.OrdinalIgnoreCase);

    public static IApplicationBuilder UseFrontendHosting(this IApplicationBuilder app, FrontendSettings? frontend)
    {
        if (frontend is null) return app;

        var files = new PhysicalFileProvider(frontend.Root);
        var index = files.GetFileInfo("index.html");
        var configJs = Encoding.UTF8.GetBytes(
            $"window.SCOTTY_CONFIG = {JsonSerializer.Serialize(new { apiBase = frontend.ApiBase })};\n");

        app.Use((context, next) =>
        {
            if (!Misrouted(context.Request, frontend)) return next(context);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });

        return app.MapWhen(context => IsPagePath(context.Request.Path), pages => pages
            .Use((context, next) => context.Request.Path == "/config.js" && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                ? Send(context, "text/javascript; charset=utf-8", configJs)
                : next(context))
            .UseStaticFiles(new StaticFileOptions
            {
                FileProvider = files,
                OnPrepareResponse = file => file.Context.Response.Headers.CacheControl =
                    file.Context.Request.Path.StartsWithSegments(Assets) ? Immutable : NoCache,
            })
            .Run(context =>
            {
                var request = context.Request;
                if (request.Path.StartsWithSegments(Assets) || !(HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
                }

                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.Headers.CacheControl = NoCache;
                return context.Response.SendFileAsync(index);
            }));
    }

    private static Task Send(HttpContext context, string contentType, byte[] body)
    {
        context.Response.ContentType = contentType;
        context.Response.Headers.CacheControl = NoCache;
        return context.Response.Body.WriteAsync(body).AsTask();
    }
}
```

Dans `SecurityConfiguration.cs`, remplacer `UseSecurityHeaders` (python, `\r\n`) par :

```csharp
    /// <summary>
    /// This is an API: nothing it returns is meant to be rendered, framed or referred from. The
    /// Swagger UI is one exception, with the only policy that allows its own assets; the web pages
    /// the Docker image serves are the other, and carry no policy, as under Apache.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, FrontendSettings? frontend = null) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            if (context.Request.Path.StartsWithSegments("/swagger"))
                headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'";
            else if (frontend is null || !FrontendHosting.IsPagePath(context.Request.Path))
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
            await next();
        });
```

Dans `Program.cs` (python, `\r\n`) :
- après `var isWeesky = builder.Configuration.UsesWeeskyPlatform();`, ajouter une ligne vide puis `var frontend = FrontendSettings.Read(builder.Configuration);` ;
- remplacer `app.UseSecurityHeaders();` par :

```csharp
app.UseSecurityHeaders(frontend);
app.UseFrontendHosting(frontend);
```

- [ ] **Step 4: Run them to verify they pass**

Run: `cd src && dotnet test --filter "FullyQualifiedName~FrontendHostingTests|FullyQualifiedName~SecurityConfiguration"`
Expected: PASS (29 tests in `FrontendHostingTests`, existing security tests unchanged).

- [ ] **Step 5: Run the whole backend suite**

Run: `cd src && dotnet test > ../.superpowers/task2-tests.log 2>&1; tail -n 15 ../.superpowers/task2-tests.log`
Expected: all tests pass, none failed.

- [ ] **Step 6: Commit**

```bash
git checkout -- src/scotty.microservice/ApiDocumentation.xml 2>/dev/null
git add src/scotty.microservice/Configuration/FrontendHosting.cs src/scotty.microservice/Configuration/SecurityConfiguration.cs src/scotty.microservice.host/Program.cs src/scotty.microservice/scotty.microservice.Tests/Configuration/FrontendHostingTests.cs
git commit -F - <<'EOF'
Serve the web pages and config.js from the API where Frontend:Path is set

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 3: CORS facultatif quand une seule adresse sert tout

**Files:**
- Modify: `src/scotty.microservice/Configuration/SecurityConfiguration.cs` (`AddFrontendCors`)
- Modify: `src/scotty.microservice.host/Program.cs` (`.AddFrontendCors(builder.Configuration)`)
- Test: `src/scotty.microservice/scotty.microservice.Tests/Configuration/SecurityConfigurationTests.cs`

**Interfaces:**
- Consumes: `FrontendSettings`, variable `frontend` de `Program.cs` (tâche 2).
- Produces: `AddFrontendCors(this IServiceCollection services, IConfiguration configuration, FrontendSettings? frontend = null)`.

- [ ] **Step 1: Write the failing tests**

Ajouter à `SecurityConfigurationTests` :

```csharp
    private static IServiceCollection Cors(FrontendSettings? frontend, params string[] origins) =>
        new ServiceCollection().AddFrontendCors(
            new ConfigurationBuilder()
                .AddInMemoryCollection(origins.Select((origin, i) =>
                    new KeyValuePair<string, string?>($"Cors:AllowedOrigins:{i}", origin)))
                .Build(),
            frontend);

    [Fact]
    public void AddFrontendCors_StillRequiresAnOriginWithoutTheImagePages()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Cors(null));

        Assert.StartsWith("No CORS origin is configured.", error.Message);
    }

    [Fact]
    public void AddFrontendCors_NeedsNoOriginWhenOneAddressServesBoth()
    {
        using var provider = Cors(new FrontendSettings("/app/frontend", "", null)).BuildServiceProvider();

        var policy = provider.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy(SecurityConfiguration.CorsPolicy)!;
        Assert.Empty(policy.Origins);
    }

    [Fact]
    public void AddFrontendCors_RequiresThePagesOriginWhenTheApiHasItsOwnAddress()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Cors(new FrontendSettings("/app/frontend", "https://api.example.net", "api.example.net")));

        Assert.Contains("Frontend:ApiBase", error.Message);
        Assert.Contains("Cors__AllowedOrigins__0", error.Message);
    }

    [Fact]
    public void AddFrontendCors_AcceptsThePagesOriginWithTwoAddresses()
    {
        using var provider = Cors(new FrontendSettings("/app/frontend", "https://api.example.net", "api.example.net"),
            "https://mail.example.net").BuildServiceProvider();

        var policy = provider.GetRequiredService<IOptions<CorsOptions>>().Value.GetPolicy(SecurityConfiguration.CorsPolicy)!;
        Assert.Equal(new[] { "https://mail.example.net" }, policy.Origins);
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `cd src && dotnet test --filter FullyQualifiedName~SecurityConfigurationTests`
Expected: build FAIL, no overload of `AddFrontendCors` takes 3 arguments.

- [ ] **Step 3: Write the implementation**

Signature `AddFrontendCors(this IServiceCollection services, IConfiguration configuration, FrontendSettings? frontend = null)`. Remplacer le bloc `if (allowedOrigins.Length == 0) { throw … }` par :

```csharp
        // With one address the browser never makes a cross-origin call, so no list is needed.
        if (allowedOrigins.Length == 0 && frontend is not { ApiHost: null })
        {
            throw new InvalidOperationException(frontend is null
                ? "No CORS origin is configured. Set Cors__AllowedOrigins__0 in the service's " +
                  "EnvironmentFile — for example Cors__AllowedOrigins__0=https://mail.example.net. " +
                  "Additional origins are Cors__AllowedOrigins__1, __2, and so on."
                : "No CORS origin is configured. Frontend:ApiBase sends the pages' calls to " +
                  $"{frontend.ApiBase}, so name the pages' own address in Cors__AllowedOrigins__0 — " +
                  "for example Cors__AllowedOrigins__0=https://mail.example.net.");
        }
```

Mettre à jour le commentaire au-dessus (« There is no valid configuration of this API with no origin… ») pour qu'il dise « no valid configuration without an origin, except the Docker image on one address ». Dans `Program.cs` : `.AddFrontendCors(builder.Configuration, frontend)`.

- [ ] **Step 4: Run them to verify they pass**

Run: `cd src && dotnet test --filter FullyQualifiedName~SecurityConfigurationTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Commit**

```bash
git checkout -- src/scotty.microservice/ApiDocumentation.xml 2>/dev/null
git add src/scotty.microservice/Configuration/SecurityConfiguration.cs src/scotty.microservice.host/Program.cs src/scotty.microservice/scotty.microservice.Tests/Configuration/SecurityConfigurationTests.cs
git commit -F - <<'EOF'
Make the CORS origin optional when one address serves the pages and the API

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 4: La commande `healthcheck`

**Files:**
- Create: `src/scotty.microservice/Configuration/HealthProbe.cs`
- Modify: `src/scotty.microservice.host/Program.cs` (en tête, après le bloc `migrate`)
- Test: `src/scotty.microservice/scotty.microservice.Tests/Configuration/HealthProbeTests.cs`

**Interfaces:**
- Produces: `internal static class HealthProbe` avec `internal static Task<int> RunAsync(string? httpPorts, TextWriter error, HttpMessageHandler? handler = null, TimeSpan? timeout = null)` — 0 sur 200, 1 sinon.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Net;
using weesky.Scotty.Microservice.Configuration;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Configuration;

public sealed class HealthProbeTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public Uri? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request.RequestUri;
            return send(request, cancellationToken);
        }
    }

    private static Handler Answering(HttpStatusCode status) => new((_, _) => Task.FromResult(new HttpResponseMessage(status)));

    [Fact]
    public async Task Healthy_ExitsZeroAndSaysNothing()
    {
        var error = new StringWriter();

        Assert.Equal(0, await HealthProbe.RunAsync("8080", error, Answering(HttpStatusCode.OK)));
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public async Task TheFirstConfiguredPort_IsTheOneAsked()
    {
        var handler = Answering(HttpStatusCode.OK);

        await HealthProbe.RunAsync("8081;8082", TextWriter.Null, handler);

        Assert.Equal(new Uri("http://127.0.0.1:8081/health"), handler.Asked);
    }

    [Fact]
    public async Task WithoutAPort_8080IsAsked()
    {
        var handler = Answering(HttpStatusCode.OK);

        await HealthProbe.RunAsync(null, TextWriter.Null, handler);

        Assert.Equal(new Uri("http://127.0.0.1:8080/health"), handler.Asked);
    }

    [Fact]
    public async Task Unhealthy_ExitsOneNamingTheStatus()
    {
        var error = new StringWriter();

        Assert.Equal(1, await HealthProbe.RunAsync("8080", error, Answering(HttpStatusCode.ServiceUnavailable)));
        Assert.Contains("503", error.ToString());
    }

    [Fact]
    public async Task ARefusedConnection_ExitsOne()
    {
        var error = new StringWriter();
        var handler = new Handler((_, _) => throw new HttpRequestException("Connection refused"));

        Assert.Equal(1, await HealthProbe.RunAsync("8080", error, handler));
        Assert.Contains("Connection refused", error.ToString());
    }

    [Fact]
    public async Task NoAnswerInTime_ExitsOne()
    {
        var error = new StringWriter();
        var handler = new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Assert.Equal(1, await HealthProbe.RunAsync("8080", error, handler, TimeSpan.FromMilliseconds(50)));
        Assert.Contains("no answer", error.ToString());
    }

    [Fact]
    public async Task AnUnreadablePort_ExitsOneNamingIt()
    {
        var error = new StringWriter();

        Assert.Equal(1, await HealthProbe.RunAsync("http", error, Answering(HttpStatusCode.OK)));
        Assert.Contains("'http'", error.ToString());
    }
}
```

- [ ] **Step 2: Run them to verify they fail**

Run: `cd src && dotnet test --filter FullyQualifiedName~HealthProbeTests`
Expected: build FAIL, `The name 'HealthProbe' does not exist`.

- [ ] **Step 3: Write the implementation**

`src/scotty.microservice/Configuration/HealthProbe.cs` :

```csharp
using System.Net;

namespace weesky.Scotty.Microservice.Configuration;

/// <summary>
/// <c>scotty.microservice healthcheck</c>: the Docker image has no shell and no curl, so the
/// container's health check asks the running service's /health through the binary itself.
/// </summary>
internal static class HealthProbe
{
    internal static async Task<int> RunAsync(string? httpPorts, TextWriter error, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        var port = httpPorts?.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "8080";
        if (!ushort.TryParse(port, out _))
        {
            await error.WriteLineAsync($"Health check failed: ASPNETCORE_HTTP_PORTS starts with '{port}', which is not a port.");
            return 1;
        }

        var wait = timeout ?? TimeSpan.FromSeconds(5);
        using var client = new HttpClient(handler ?? new SocketsHttpHandler()) { Timeout = wait };
        try
        {
            using var response = await client.GetAsync($"http://127.0.0.1:{port}/health");
            if (response.StatusCode == HttpStatusCode.OK) return 0;
            await error.WriteLineAsync($"Health check failed: /health answered {(int)response.StatusCode}.");
        }
        catch (HttpRequestException e)
        {
            await error.WriteLineAsync($"Health check failed: {e.Message}");
        }
        catch (TaskCanceledException)
        {
            await error.WriteLineAsync($"Health check failed: no answer within {wait.TotalSeconds:0.###} s.");
        }

        return 1;
    }
}
```

Dans `Program.cs`, juste après le bloc `if (args is ["migrate"]) { … }` (python, `\r\n`) :

```csharp
if (args is ["healthcheck"])
{
    Environment.ExitCode = await HealthProbe.RunAsync(
        Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS"), Console.Error);
    return;
}
```

- [ ] **Step 4: Run them to verify they pass**

Run: `cd src && dotnet test --filter FullyQualifiedName~HealthProbeTests`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git checkout -- src/scotty.microservice/ApiDocumentation.xml 2>/dev/null
git add src/scotty.microservice/Configuration/HealthProbe.cs src/scotty.microservice.host/Program.cs src/scotty.microservice/scotty.microservice.Tests/Configuration/HealthProbeTests.cs
git commit -F - <<'EOF'
Add a healthcheck command for the shell-less Docker image

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 5: Le numéro de l'image — API, journal de démarrage, page À propos

**Files:**
- Modify: `src/scotty.microservice/Models/ProductVersion.cs`
- Modify: `src/scotty.microservice.host/Program.cs` (ligne de journal au démarrage)
- Modify: `src/frontend/src/modules/settings/about/aboutTypes.ts`, `AboutPage.tsx`
- Modify: `src/frontend/src/locales/en/settings.json`, `src/frontend/src/locales/fr/settings.json` (section `about`)
- Test: `src/scotty.microservice/scotty.microservice.Tests/Models/ProductVersionTests.cs`, `src/frontend/src/modules/settings/about/AboutPage.test.tsx`

**Interfaces:**
- Produces: `public sealed record ProductVersion(string Version, string? Commit, string? Image = null)` ; `public const string ImageVariable = "SCOTTY_IMAGE_VERSION"` ; `public static ProductVersion Parse(string informationalVersion, string? image = null)`. Journal de démarrage : `Version {Version}, commit {Commit}, image {Image}` (`none` pour une valeur absente) — le test de démarrage de la tâche 6 cherche `image <docker/VERSION>` et `commit <court>`. Front : `ServerVersion.image?: string`, clé `about.image`.

- [ ] **Step 1: Write the failing backend tests**

Ajouter à `ProductVersionTests` (usings : `System.Text.Json`, `Microsoft.AspNetCore.Mvc`, `weesky.Scotty.Microservice.Configuration`) :

```csharp
    [Fact]
    public void Parse_KeepsTheImageNumberTheContainerCarries()
        => Assert.Equal(new ProductVersion("1.4.0", "0a1b2c3", "1.0.0"),
            ProductVersion.Parse("1.4.0+0a1b2c3d4e5f", " 1.0.0 "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Parse_LeavesTheImageNullOutsideAContainer(string? image)
        => Assert.Null(ProductVersion.Parse("1.4.0", image).Image);

    [Fact]
    public void Json_OmitsTheImageOutsideAContainer()
    {
        var json = new JsonOptions();
        MvcFormatterConfiguration.ConfigureJson(json);

        Assert.Equal("{\"version\":\"1.4.0\",\"commit\":\"0a1b2c3\"}",
            JsonSerializer.Serialize(new ProductVersion("1.4.0", "0a1b2c3"), json.JsonSerializerOptions));
        Assert.Contains("\"image\":\"1.0.0\"",
            JsonSerializer.Serialize(new ProductVersion("1.4.0", "0a1b2c3", "1.0.0"), json.JsonSerializerOptions));
    }
```

- [ ] **Step 2: Run them to verify they fail**

Run: `cd src && dotnet test --filter FullyQualifiedName~ProductVersionTests`
Expected: build FAIL, no constructor of `ProductVersion` takes 3 arguments.

- [ ] **Step 3: Implement the backend**

`ProductVersion.cs` :

```csharp
/// <summary>
/// The running build: the API's version from src/scotty.microservice/VERSION (suffixed -dev outside a release),
/// the short commit the SDK appends to the informational version after a '+', and the Docker image's
/// own number (docker/VERSION) when the service runs from that image.
/// </summary>
public sealed record ProductVersion(string Version, string? Commit, string? Image = null)
{
    public const string ImageVariable = "SCOTTY_IMAGE_VERSION";
    private const int ShortCommitLength = 7;

    public static ProductVersion Current { get; } = Parse(
        typeof(ProductVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? string.Empty,
        Environment.GetEnvironmentVariable(ImageVariable));

    public static ProductVersion Parse(string informationalVersion, string? image = null)
    {
        var parts = informationalVersion.Split('+', 2);
        var commit = parts.Length == 2 ? parts[1][..Math.Min(ShortCommitLength, parts[1].Length)] : null;

        return new ProductVersion(parts[0], commit, string.IsNullOrWhiteSpace(image) ? null : image.Trim());
    }
}
```

`Program.cs`, juste avant `app.Logger.LogInformation("Data Protection key ring: …")` :

```csharp
var version = ProductVersion.Current;
app.Logger.LogInformation("Version {Version}, commit {Commit}, image {Image}",
    version.Version, version.Commit ?? "none", version.Image ?? "none");
```

(ajouter `using weesky.Scotty.Microservice.Models;` si absent).

- [ ] **Step 4: Run them to verify they pass**

Run: `cd src && dotnet test --filter "FullyQualifiedName~ProductVersionTests|FullyQualifiedName~VersionControllerTests"`
Expected: PASS.

- [ ] **Step 5: Write the failing front test**

Ajouter à `AboutPage.test.tsx` :

```tsx
  it('names the Docker image when the server runs from one', async () => {
    vi.mocked(api.getVersion).mockResolvedValue({ version: '1.4.0', commit: 'f4e5d6c', image: '1.0.0' })
    renderPage()

    expect(await screen.findByText('Docker image 1.0.0')).toBeInTheDocument()
  })

  it('has no image line for an installation without Docker', async () => {
    renderPage()

    await screen.findByText('Server 1.0.0-dev (f4e5d6c)')
    expect(screen.queryByText(/Docker image/)).not.toBeInTheDocument()
  })
```

Run: `cd src/frontend && npm test -- --run src/modules/settings/about/AboutPage.test.tsx`
Expected: FAIL on the first test (`Unable to find an element with the text: Docker image 1.0.0`) — TypeScript accepts the extra field at runtime only after the type change; if `tsc` complains in the editor, the run still fails on the assertion.

- [ ] **Step 6: Implement the front**

`aboutTypes.ts` — ajouter dans `ServerVersion` :

```ts
  /** The Docker image's own number; absent when the API does not run from the image. */
  image?: string
```

`AboutPage.tsx` — dans `<p className="about-builds">`, entre `{serverLine}` et la date :

```tsx
        {server?.image && <span>{`${t('about.image')} ${server.image}`}</span>}
```

`locales/en/settings.json`, section `about`, après `"serverUnavailable"` : `"image": "Docker image",` ; `locales/fr/settings.json`, même place : `"image": "Image Docker",`.

- [ ] **Step 7: Run the front tests and lint**

Run: `cd src/frontend && npm test -- --run src/modules/settings/about src/locales && npm run lint`
Expected: PASS (About tests, locale parity tests), lint clean.

- [ ] **Step 8: Commit**

```bash
git checkout -- src/scotty.microservice/ApiDocumentation.xml 2>/dev/null
git add src/scotty.microservice/Models/ProductVersion.cs src/scotty.microservice.host/Program.cs src/scotty.microservice/scotty.microservice.Tests/Models/ProductVersionTests.cs src/frontend/src/modules/settings/about src/frontend/src/locales/en/settings.json src/frontend/src/locales/fr/settings.json
git commit -F - <<'EOF'
Report and show the Docker image number when the API runs from the image

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 6: Dockerfile, compose, `.env.example` et test de démarrage

**Files:**
- Create: `docker/Dockerfile`, `docker/VERSION`, `docker/docker-compose.yml`, `docker/.env.example`, `docker/smoke-test.sh`, `.dockerignore`
- Modify: `.gitattributes`

**Interfaces:**
- Consumes: `Frontend__Path` (tâche 2), `healthcheck` (tâche 4), journal `Version …, commit …, image …` (tâche 5).
- Produces: `docker/smoke-test.sh IMAGE [COMMIT]` (code 0 si tout passe) ; arguments de build `GIT_COMMIT`, `RELEASE_BUILD`, `IMAGE_VERSION` ; consommés par la tâche 7.

- [ ] **Step 1: `.gitattributes` — the script keeps LF on Windows**

Ajouter à la fin de `.gitattributes` :

```
###############################################################################
# Shell scripts run inside Linux containers and on Linux runners: a CR breaks them.
###############################################################################
*.sh    text eol=lf
```

- [ ] **Step 2: `.dockerignore` — an allow-list, never the dev keys or `.env` files**

```
# The build sends only what the image is made of. Everything else — tests, local keys,
# .env files, build output — stays out of the context.
**
!docker/
!src/Directory.Build.props
!src/scotty.microservice/
!src/scotty.microservice.host/
!src/scotty.providers.weesky/
!src/frontend/
**/bin/
**/obj/
**/keys/
**/TestResults/
**/node_modules/
**/dist/
**/coverage/
**/.env*
src/scotty.microservice/scotty.microservice.Tests/
```

- [ ] **Step 3: `docker/VERSION`**

Contenu : `1.0.0` (une ligne).

- [ ] **Step 4: Pin the three base images**

Run:
```bash
export PATH="$PATH:/c/Users/mick0/AppData/Local/Programs/DockerDesktop/resources/bin"
for i in node:24-bookworm-slim mcr.microsoft.com/dotnet/sdk:10.0 mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra; do
  echo "$i@$(docker buildx imagetools inspect "$i" --format '{{json .Manifest}}' | python -c 'import json,sys; print(json.load(sys.stdin)["digest"])')"
done
```
Expected: three lines `image@sha256:<64 hex>` — the multi-architecture index digests to paste in the Dockerfile below.

- [ ] **Step 5: `docker/Dockerfile`**

Remplacer chaque `@sha256:…` par la ligne correspondante de l'étape 4.

```dockerfile
# syntax=docker/dockerfile:1
# Scotty webmail in one image: the API also serves the web pages. Build from the repository root:
#   docker build -f docker/Dockerfile .
# docker/README.md is the guide; docker/smoke-test.sh checks an image the way CI does.

FROM --platform=$BUILDPLATFORM node:24-bookworm-slim@sha256:… AS web
ARG GIT_COMMIT=""
ARG RELEASE_BUILD=false
WORKDIR /src/frontend
COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci
COPY src/frontend/ ./
RUN GITHUB_SHA="$GIT_COMMIT" RELEASE_BUILD="$RELEASE_BUILD" npm run build -- --mode container

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:… AS api
ARG TARGETARCH
ARG GIT_COMMIT=""
ARG RELEASE_BUILD=false
WORKDIR /src
COPY src/Directory.Build.props src/
COPY src/scotty.microservice/VERSION src/scotty.microservice/scotty.microservice.core.csproj src/scotty.microservice/
COPY src/scotty.microservice.host/scotty.microservice.host.csproj src/scotty.microservice.host/
COPY src/scotty.providers.weesky/scotty.providers.weesky.csproj src/scotty.providers.weesky/
RUN dotnet restore src/scotty.microservice.host/scotty.microservice.host.csproj -a "$TARGETARCH"
COPY src/ src/
# The context carries no .git: the commit comes in as an argument, and SourceLink is told not to look.
RUN dotnet publish src/scotty.microservice.host/scotty.microservice.host.csproj \
      -c Release -a "$TARGETARCH" --self-contained false --no-restore \
      -p:ReleaseBuild="$RELEASE_BUILD" -p:SourceRevisionId="$GIT_COMMIT" \
      -p:EnableSourceControlManagerQueries=false \
      -p:SatelliteResourceLanguages= -p:DebugType=None -p:DebugSymbols=false \
      -o /out/app \
 && rm -rf /out/app/alpine /out/app/arm64 /out/app/CodeCoverage /out/app/macos /out/app/ubuntu /out/app/x64 /out/app/x86 \
 && mkdir /out/state

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:…
ARG IMAGE_VERSION
LABEL org.opencontainers.image.title="Scotty webmail" \
      org.opencontainers.image.source="https://github.com/darthmaul0181/weesky.net-mail" \
      org.opencontainers.image.licenses="AGPL-3.0" \
      org.opencontainers.image.version="$IMAGE_VERSION"
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Logs__Output=console \
    STATE_DIRECTORY=/var/lib/scotty \
    Platform=generic \
    Frontend__Path=/app/frontend \
    SCOTTY_IMAGE_VERSION=$IMAGE_VERSION
WORKDIR /app
COPY --from=api /out/app ./
COPY --from=web /src/frontend/dist ./frontend
COPY --from=api --chown=$APP_UID:$APP_UID /out/state /var/lib/scotty
USER $APP_UID
EXPOSE 8080
VOLUME /var/lib/scotty
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --start-interval=2s --retries=3 \
  CMD ["/app/scotty.microservice", "healthcheck"]
ENTRYPOINT ["/app/scotty.microservice"]
```

- [ ] **Step 6: `docker/docker-compose.yml`**

```yaml
# Scotty webmail: one container. The database and the HTTPS proxy in front are yours —
# docker/README.md walks through both. Settings go in .env, beside this file.
services:
  scotty:
    image: ghcr.io/darthmaul0181/scotty-webmail:1
    restart: unless-stopped
    env_file: .env
    ports:
      # Only this machine reaches it: visitors come through your HTTPS proxy.
      - "127.0.0.1:8080:8080"
    volumes:
      - scotty-state:/var/lib/scotty
    read_only: true
    tmpfs:
      - /tmp
    cap_drop:
      - ALL
    security_opt:
      - no-new-privileges:true

volumes:
  # The session signing key and the keys that encrypt stored secrets. Back it up.
  scotty-state:
```

- [ ] **Step 7: `docker/.env.example`**

```ini
# Settings for the Scotty webmail container.
#
# Copy to .env beside docker-compose.yml, then change the lines marked CHANGE (docker/README.md,
# step 2). It holds database passwords: never commit a filled-in copy.
#
# After any change: docker compose up -d


# --- CHANGE: the two database accounts install/install.sql created --------------------------------
ConnectionStrings__WebmailPreferencesDatabase=Server=db.example.net;Port=3306;Database=scotty_webmail;User=scotty_webmail;Password=CHANGE_ME;
# The account that creates and updates the tables, used once at each start.
ConnectionStrings__WebmailSchema=Server=db.example.net;Port=3306;Database=scotty_webmail;User=scotty_webmail_schema;Password=CHANGE_ME;

# --- CHANGE: your mail service: the IMAP server, the SMTP server, the ManageSieve server ----------
# Ports default to 143 (IMAP) and 587 (SMTP), with STARTTLS.
Mail__ImapHost=imap.example.net
Mail__SmtpHost=smtp.example.net
Sieve__Host=imap.example.net

# --- CHANGE: who administers Scotty: their mail addresses, separated by commas --------------------
Generic__Administrators=you@example.net

# --- CHANGE: where your HTTPS proxy connects from (docker/README.md, step 3) -----------------------
# The container only believes the visitor's address when the proxy is in this range. Docker's own
# networks are in 172.16.0.0/12; a proxy in a Docker network of its own: name that network's range.
ForwardedHeaders__KnownNetworks__0=172.16.0.0/12


# --- Only with two addresses: the pages on mail.example.net, the API on api.example.net ----------
# Frontend__ApiBase=https://api.example.net
# Cors__AllowedOrigins__0=https://mail.example.net

# --- Optional -------------------------------------------------------------------------------------

# Sync contacts and calendars with phones: the address that answers /api (the pages' own address
# with one address, the API's with two).
# Dav__PublicUrl=https://mail.example.net
```

- [ ] **Step 8: `docker/smoke-test.sh`**

```bash
#!/usr/bin/env bash
# Starts IMAGE the way docker-compose.yml runs it, beside a throwaway MariaDB, and checks that it
# migrates, turns healthy and serves the pages. Usage: docker/smoke-test.sh IMAGE [SHORT_COMMIT]
set -euo pipefail

IMAGE=$1
COMMIT=${2:-}
HERE=$(cd "$(dirname "$0")" && pwd)
VERSION=$(tr -d '[:space:]' < "$HERE/VERSION")
RUN=scotty-smoke-$$
PORT=${SMOKE_PORT:-18080}
BASE=http://127.0.0.1:$PORT
WORK=$(mktemp -d "${TMPDIR:-/tmp}/scotty-smoke.XXXXXX")

fail() { echo "::error::$*" >&2; exit 1; }
cleanup() {
  echo "--- container log (last 60 lines) ---"
  docker logs "$RUN-app" 2>&1 | tail -n 60 || true
  docker rm -fv "$RUN-app" "$RUN-db" >/dev/null 2>&1 || true
  docker network rm "$RUN" >/dev/null 2>&1 || true
  rm -rf "$WORK"
}
trap cleanup EXIT

docker network create "$RUN" >/dev/null
docker run -d --name "$RUN-db" --network "$RUN" -e MARIADB_ROOT_PASSWORD=root mariadb:11.4 >/dev/null
for _ in $(seq 60); do
  docker exec "$RUN-db" mariadb-admin -uroot -proot ping >/dev/null 2>&1 && break
  sleep 1
done
sed -e 's/__HOST__/%/g' -e 's/__PASSWORD__/app-secret/g' -e 's/__SCHEMA_PASSWORD__/schema-secret/g' \
  "$HERE/../install/install.sql" | docker exec -i "$RUN-db" mariadb -uroot -proot >/dev/null

DB="Server=$RUN-db;Port=3306;Database=scotty_webmail"
docker run -d --name "$RUN-app" --network "$RUN" -p "127.0.0.1:$PORT:8080" \
  --read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges:true \
  -e "ConnectionStrings__WebmailPreferencesDatabase=$DB;User=scotty_webmail;Password=app-secret" \
  -e "ConnectionStrings__WebmailSchema=$DB;User=scotty_webmail_schema;Password=schema-secret" \
  -e Mail__ImapHost=imap.example.test -e Mail__SmtpHost=smtp.example.test -e Sieve__Host=imap.example.test \
  -e ForwardedHeaders__KnownNetworks__0=172.16.0.0/12 \
  "$IMAGE" >/dev/null

STATUS=
for _ in $(seq 90); do
  STATUS=$(docker inspect -f '{{.State.Status}} {{.State.Health.Status}}' "$RUN-app")
  [ "$STATUS" = 'running healthy' ] && break
  [ "${STATUS%% *}" = running ] || fail "the container stopped: $STATUS"
  sleep 1
done
[ "$STATUS" = 'running healthy' ] || fail "not healthy after 90 s: $STATUS"

# Captured to files first: grep -q stopping early would break the pipe under pipefail.
docker exec "$RUN-db" mariadb -uroot -proot -N -e 'SELECT scriptname FROM scotty_webmail.schema_migrations' > "$WORK/migrations"
grep -qx '0001_initial.sql' "$WORK/migrations" || fail "the database was not migrated"
curl -fsS "$BASE/mail/inbox" -o "$WORK/page" || fail "/mail/inbox did not answer"
grep -q '<div id="root">' "$WORK/page" || fail "/mail/inbox did not answer the index"
[ "$(curl -fsS "$BASE/config.js")" = 'window.SCOTTY_CONFIG = {"apiBase":""};' ] || fail "/config.js is wrong"
[ "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/api/Version")" = 401 ] || fail "/api/Version did not ask to sign in"
docker logs "$RUN-app" > "$WORK/log" 2>&1
grep -q "image $VERSION" "$WORK/log" || fail "the start-up log does not name image $VERSION"

# Every file of the bundle must come back as itself: a type the server does not know would
# fall back to the index, and the browser would get HTML instead.
docker cp "$RUN-app:/app/frontend" "$WORK/frontend"
(cd "$WORK/frontend" && find . -type f ! -name index.html) | while read -r file; do
  curl -fsS "$BASE/${file#./}" -o "$WORK/got" || fail "$file did not download"
  cmp -s "$WORK/frontend/$file" "$WORK/got" || fail "$file came back different"
done

if [ -n "$COMMIT" ]; then
  grep -q "commit $COMMIT" "$WORK/log" || fail "the API does not carry commit $COMMIT"
  grep -rqF "$COMMIT" "$WORK/frontend/assets" || fail "the web app does not carry commit $COMMIT"
fi

echo "Smoke test passed: $IMAGE"
```

Puis `git add --chmod=+x docker/smoke-test.sh` à l'étape de commit.

- [ ] **Step 9: Build and run the smoke test locally**

Run:
```bash
export PATH="$PATH:/c/Users/mick0/AppData/Local/Programs/DockerDesktop/resources/bin"
COMMIT=$(git rev-parse --short=7 HEAD)
docker build -f docker/Dockerfile --build-arg GIT_COMMIT=$COMMIT --build-arg IMAGE_VERSION=$(cat docker/VERSION) -t scotty-webmail:local . > .superpowers/docker-build.log 2>&1; tail -n 5 .superpowers/docker-build.log
MSYS_NO_PATHCONV=1 TMPDIR=.superpowers bash docker/smoke-test.sh scotty-webmail:local "$COMMIT"
```
(Git Bash : `MSYS_NO_PATHCONV=1` empêche MSYS de réécrire `conteneur:/app/frontend`, et un `TMPDIR` relatif reste lisible par `docker.exe`.)
Expected: build ends with `naming to docker.io/library/scotty-webmail:local`, then `Smoke test passed: scotty-webmail:local`. On a failure, the container log printed by the script names the cause; fix it in the Dockerfile (or in the code of tasks 2–5, then rerun their tests).

- [ ] **Step 10: Check the image size and user**

Run: `docker image inspect scotty-webmail:local -f '{{.Size}} {{.Config.User}}'`
Expected: under 250 000 000 bytes, user `1654` (or `$APP_UID` resolved to `1654`).

- [ ] **Step 11: Commit**

```bash
git add .gitattributes .dockerignore docker/Dockerfile docker/VERSION docker/docker-compose.yml docker/.env.example
git add --chmod=+x docker/smoke-test.sh
git commit -F - <<'EOF'
Build the webmail as one Docker image, with its compose file and a start-up test

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 7: Le workflow `image.yml`, Dependabot, le tag ignoré par `deploy.yml`

**Files:**
- Create: `.github/workflows/image.yml`, `.github/dependabot.yml`
- Modify: `.github/workflows/deploy.yml` (ligne `tags-ignore`)
- Modify: `docs/superpowers/2026-10-08-docker-image-design.md` (deux précisions, voir étape 4)

**Interfaces:**
- Consumes: `docker/Dockerfile` et ses trois arguments, `docker/smoke-test.sh IMAGE COMMIT`, `docker/VERSION` (tâche 6).

- [ ] **Step 1: `.github/workflows/image.yml`**

```yaml
name: image

# docker/README.md is the guide for users; docs/operations/docker-image-release.md is how a
# release is made: a candidate from any branch, tested on a server, then promoted from master.
on:
  push:
    branches: ['**']
    paths: ['docker/**', '.dockerignore', '.github/workflows/image.yml']
  workflow_dispatch:
    inputs:
      mode:
        description: 'candidate: build, test and publish X.Y.Z-rc.N — release: promote a candidate'
        type: choice
        options: [candidate, release]
        default: candidate
      candidate:
        description: 'release only: the candidate to promote, such as 1.0.0-rc.2'
        required: false

permissions:
  contents: read

env:
  IMAGE: ghcr.io/darthmaul0181/scotty-webmail

jobs:
  build:
    if: github.event_name == 'push' || inputs.mode == 'candidate'
    runs-on: ubuntu-latest
    # Two candidates started together would otherwise pick the same rc number.
    concurrency: image-${{ github.event_name == 'workflow_dispatch' && 'publish' || github.ref }}
    permissions:
      contents: read
      packages: write
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Describe the build
        id: meta
        run: |
          VERSION=$(tr -d '[:space:]' < docker/VERSION)
          # Only a commit already on master builds as a release: elsewhere the versions read -dev.
          if git merge-base --is-ancestor HEAD origin/master; then RELEASE=true; else RELEASE=false; fi
          {
            echo "version=$VERSION"
            echo "release=$RELEASE"
            echo "commit=${GITHUB_SHA::7}"
          } >> "$GITHUB_OUTPUT"

      - uses: docker/setup-buildx-action@v3

      - name: Build for the start-up test
        uses: docker/build-push-action@v6
        with:
          context: .
          file: docker/Dockerfile
          platforms: linux/amd64
          load: true
          tags: scotty-webmail:test
          build-args: |
            GIT_COMMIT=${{ steps.meta.outputs.commit }}
            RELEASE_BUILD=${{ steps.meta.outputs.release }}
            IMAGE_VERSION=${{ steps.meta.outputs.version }}
          cache-from: type=gha
          cache-to: type=gha,mode=max

      - name: Start-up test
        run: bash docker/smoke-test.sh scotty-webmail:test ${{ steps.meta.outputs.commit }}

      - uses: docker/login-action@v3
        if: github.event_name == 'workflow_dispatch'
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ github.token }}

      - name: Next candidate number
        id: rc
        if: github.event_name == 'workflow_dispatch'
        env:
          VERSION: ${{ steps.meta.outputs.version }}
        run: |
          N=1
          while docker buildx imagetools inspect "$IMAGE:$VERSION-rc.$N" >/dev/null 2>&1; do N=$((N + 1)); done
          echo "tag=$VERSION-rc.$N" >> "$GITHUB_OUTPUT"

      # Both architectures, from the cache the test build filled; pushed only for a candidate.
      - name: Build both architectures
        uses: docker/build-push-action@v6
        with:
          context: .
          file: docker/Dockerfile
          platforms: linux/amd64,linux/arm64
          push: ${{ github.event_name == 'workflow_dispatch' }}
          tags: ${{ env.IMAGE }}:${{ steps.rc.outputs.tag || 'unpublished' }}
          annotations: |
            index:org.opencontainers.image.revision=${{ github.sha }}
            index:org.opencontainers.image.version=${{ steps.meta.outputs.version }}
            index:org.opencontainers.image.source=https://github.com/${{ github.repository }}
          build-args: |
            GIT_COMMIT=${{ steps.meta.outputs.commit }}
            RELEASE_BUILD=${{ steps.meta.outputs.release }}
            IMAGE_VERSION=${{ steps.meta.outputs.version }}
          cache-from: type=gha
          cache-to: type=gha,mode=max

      - name: Candidate published
        if: github.event_name == 'workflow_dispatch'
        run: echo "::notice::Published $IMAGE:${{ steps.rc.outputs.tag }} from ${{ github.ref_name }} (${{ steps.meta.outputs.commit }})"

  release:
    if: github.event_name == 'workflow_dispatch' && inputs.mode == 'release'
    runs-on: ubuntu-latest
    concurrency: image-publish
    permissions:
      contents: write
      packages: write
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ github.token }}

      - name: Check the candidate
        id: check
        env:
          CANDIDATE: ${{ inputs.candidate }}
        run: |
          fail() { echo "::error::$*"; exit 1; }
          [[ "$CANDIDATE" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)-rc\.[0-9]+$ ]] \
            || fail "'$CANDIDATE' is not a candidate tag such as 1.0.0-rc.2"
          VERSION=${CANDIDATE%-rc.*}
          REVISION=$(docker buildx imagetools inspect "$IMAGE:$CANDIDATE" --raw 2>/dev/null \
            | jq -r '.annotations["org.opencontainers.image.revision"] // empty') \
            || true
          [ -n "$REVISION" ] || fail "$IMAGE:$CANDIDATE does not exist"
          git merge-base --is-ancestor "$REVISION" origin/master \
            || fail "$CANDIDATE was built from ${REVISION::7}, which is not on master: merge, then build a new candidate"
          [ "$(git show "$REVISION:docker/VERSION" | tr -d '[:space:]')" = "$VERSION" ] \
            || fail "docker/VERSION at ${REVISION::7} is not $VERSION"
          if git ls-remote --exit-code --tags origin "refs/tags/image-v$VERSION" >/dev/null; then
            fail "image-v$VERSION already exists: raise docker/VERSION and build a new candidate"
          fi
          {
            echo "version=$VERSION"
            echo "revision=$REVISION"
            echo "major=${BASH_REMATCH[1]}"
            echo "minor=${BASH_REMATCH[1]}.${BASH_REMATCH[2]}"
          } >> "$GITHUB_OUTPUT"

      # The same image, byte for byte: new tags on the candidate, nothing rebuilt.
      - name: Promote
        env:
          CANDIDATE: ${{ inputs.candidate }}
          VERSION: ${{ steps.check.outputs.version }}
          REVISION: ${{ steps.check.outputs.revision }}
        run: |
          docker buildx imagetools create \
            --tag "$IMAGE:$VERSION" \
            --tag "$IMAGE:${{ steps.check.outputs.minor }}" \
            --tag "$IMAGE:${{ steps.check.outputs.major }}" \
            --tag "$IMAGE:latest" \
            --annotation "index:org.opencontainers.image.revision=$REVISION" \
            --annotation "index:org.opencontainers.image.version=$VERSION" \
            --annotation "index:org.opencontainers.image.source=https://github.com/${{ github.repository }}" \
            "$IMAGE:$CANDIDATE"

      - name: Tag the release
        env:
          VERSION: ${{ steps.check.outputs.version }}
          REVISION: ${{ steps.check.outputs.revision }}
        run: |
          git tag "image-v$VERSION" "$REVISION"
          git push origin "image-v$VERSION"
          echo "::notice::Published $IMAGE:$VERSION (image-v$VERSION, ${REVISION::7})"
```

- [ ] **Step 2: `.github/dependabot.yml`**

```yaml
# Base-image security fixes arrive as pull requests; nothing published changes until a new
# candidate is built, tested and released (docs/operations/docker-image-release.md).
version: 2
updates:
  - package-ecosystem: docker
    directory: /docker
    schedule:
      interval: weekly
```

- [ ] **Step 3: `deploy.yml` ignores the image tags**

Remplacer `    tags-ignore: ['v*', 'web-v*', 'api-v*']` par `    tags-ignore: ['v*', 'web-v*', 'api-v*', 'image-v*']` (python, `\r\n`). Un tag poussé à la main ne doit pas déployer `dev`, pour la même raison que les trois autres.

- [ ] **Step 4: The spec records the two additions**

Dans `docs/superpowers/2026-10-08-docker-image-design.md` :
- remplacer `\`deploy.yml\` n'est pas modifié.` (décision 8) par `\`deploy.yml\` ne change que d'une entrée : \`image-v*\` rejoint ses \`tags-ignore\`.` ;
- à la fin de la décision 6, ajouter le paragraphe : `Au démarrage, l'API écrit une ligne \`Version …, commit …, image …\` (\`none\` pour une valeur absente) : \`docker compose logs\` dit quelle image tourne, et le test de démarrage vérifie par elle que le numéro et le commit sont bien arrivés dans l'image.`

- [ ] **Step 5: Lint the workflows**

Run:
```bash
export PATH="$PATH:/c/Users/mick0/AppData/Local/Programs/DockerDesktop/resources/bin"
docker run --rm -v "$(pwd -W 2>/dev/null || pwd):/repo" -w /repo rhysd/actionlint:latest -color .github/workflows/image.yml .github/workflows/deploy.yml
```
Expected: no output, exit 0. (Shellcheck warnings inside `run:` blocks are fixed, not silenced.)

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/image.yml .github/dependabot.yml .github/workflows/deploy.yml docs/superpowers/2026-10-08-docker-image-design.md
git commit -F - <<'EOF'
Build, test and publish the image from a workflow: candidates, then promotion from master

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 8: Guides, marche à suivre et versions

**Files:**
- Create: `docker/README.md`, `docs/operations/docker-image-release.md`
- Modify: `README.md` (section « Installing it », tableau Documentation, arbre), `install/README.md` (introduction), `src/scotty.microservice/VERSION` → `1.4.0`, `src/frontend/VERSION` → `2.3.0`, `src/frontend/package.json` (`"version"` → `2.3.0`), `src/frontend/package-lock.json` (les deux `"version"` racine → `2.3.0`)

- [ ] **Step 1: `docker/README.md`**

````markdown
# Running Scotty webmail with Docker

One container serves the whole webmail: the pages and the API. When you are done, your users open
**https://mail.example.net**, sign in with their usual mail address and password, and find their
mail, contacts and calendar.

What the image does not include, on purpose:

- **the database.** Scotty keeps its settings, contacts and calendars in a MySQL or MariaDB database
  you already run, or install next to it;
- **HTTPS.** Your reverse proxy — Caddy, Traefik, nginx — holds the certificate and passes requests
  on. Sign-in only works over HTTPS: the browser keeps the session cookie for secure pages only.

There are six steps. Each one ends with a check: do not move on until it passes.

---

## Before you start

- **Docker** with the compose plugin (`docker compose version` answers).
- **MySQL 8.0 or 8.4, or MariaDB 10.5 to 11.x**, reachable from the container.
- **A mail service** with IMAP and authenticated SMTP, and optionally ManageSieve for mail rules —
  see [What your mail service needs](../install/README.md#what-your-mail-service-needs).
- **A domain name** pointing at this machine, and a reverse proxy that serves it over HTTPS.

## Step 1 — Create the database

Use `install/install.sql` from this repository, exactly as in
[step 2 of the classic guide](../install/README.md#step-2--create-the-database), with one
difference: `__HOST__` is the address **the container** connects from, not `127.0.0.1`. Docker
gives containers addresses in `172.16.0.0/12`, so write `172.%` — or `%` if the database only
accepts connections from your own network.

✅ **Check:** the script's last lines list the rights of `scotty_webmail` and `scotty_webmail_schema`.

## Step 2 — Fill in the settings

```bash
mkdir scotty && cd scotty
curl -fsSLO https://raw.githubusercontent.com/darthmaul0181/weesky.net-mail/master/docker/docker-compose.yml
curl -fsSL -o .env https://raw.githubusercontent.com/darthmaul0181/weesky.net-mail/master/docker/.env.example
chmod 600 .env
```

Open `.env` and change the lines marked **CHANGE**: the two database passwords from step 1 and the
database's address, your mail servers, and the administrators' addresses. The database address is
seen from inside the container: a database on this same machine is `host.docker.internal` on
Docker Desktop, and the machine's own address on a Linux server (not `127.0.0.1`, which is the
container itself).

The image already sets everything else: the port (8080), logs on the console, the `generic`
platform, where its keys are kept.

✅ **Check:** `docker compose config` prints the service without an error.

## Step 3 — Put your HTTPS proxy in front

Choose how your users reach Scotty:

- **one address** — `https://mail.example.net` shows the pages and answers the API under `/api/`.
  Nothing more to set. This is the simplest;
- **two addresses** — the pages on `https://mail.example.net`, the API on `https://api.example.net`.
  Add to `.env`:
  ```ini
  Frontend__ApiBase=https://api.example.net
  Cors__AllowedOrigins__0=https://mail.example.net
  ```
  Each address then serves only its own part: the pages are not found on `api.example.net`, the
  API is not found on `mail.example.net`.
  **Both addresses must be on the same domain**: the browser only sends the sign-in cookie between
  addresses of the same domain.

In both cases every request goes to the container's port 8080, and the proxy must pass on the
original `Host` header, the visitor's address (`X-Forwarded-For`) and `X-Forwarded-Proto`.

`ForwardedHeaders__KnownNetworks__0` in `.env` names where the proxy connects from. A proxy
installed on this machine reaches the container through Docker's network: keep `172.16.0.0/12`. A
proxy in a container of its own: name the range of the network it shares with Scotty
(`docker network inspect <network> -f '{{(index .IPAM.Config 0).Subnet}}'`).

#### Caddy, on this machine

```caddy
mail.example.net {
    encode zstd gzip
    reverse_proxy 127.0.0.1:8080
}
```

With two addresses, write `mail.example.net, api.example.net {` on the first line. Caddy fetches and
renews the certificates itself, and passes on the three headers without being told.

#### nginx, on this machine

```nginx
server {
    listen 443 ssl;
    http2 on;
    server_name mail.example.net;          # with two addresses: mail.example.net api.example.net

    ssl_certificate     /etc/letsencrypt/live/mail.example.net/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/mail.example.net/privkey.pem;

    # nginx refuses anything over 1 MB by default: attachments would fail.
    client_max_body_size 30m;
    gzip on;
    gzip_types text/css text/javascript application/javascript application/json image/svg+xml;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host              $host;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

#### Traefik, in its own container

Remove the `ports:` lines from `docker-compose.yml`, then add to the `scotty` service, with
`traefik` being the network your Traefik container is on:

```yaml
    networks: [traefik]
    labels:
      - traefik.enable=true
      - traefik.http.routers.scotty.rule=Host(`mail.example.net`)
      - traefik.http.routers.scotty.entrypoints=websecure
      - traefik.http.routers.scotty.tls.certresolver=letsencrypt
      - traefik.http.routers.scotty.middlewares=scotty-compress
      - traefik.http.middlewares.scotty-compress.compress=true
      - traefik.http.services.scotty.loadbalancer.server.port=8080
```

and at the end of the file:

```yaml
networks:
  traefik:
    external: true
```

With two addresses: ``Host(`mail.example.net`) || Host(`api.example.net`)``. Set
`ForwardedHeaders__KnownNetworks__0` to the `traefik` network's range.

✅ **Check:** your proxy's configuration test passes (`caddy validate`, `nginx -t`).

## Step 4 — Start

```bash
docker compose up -d
docker compose ps
```

The first start creates the tables, which takes a few seconds.

✅ **Check:** after about a minute, `docker compose ps` shows `healthy`, and
**https://mail.example.net** shows the sign-in page. If it shows `unhealthy` or `restarting`, see
[When it won't start](#when-it-wont-start).

## Step 5 — Sign in

Sign in with any mailbox's full address and its password. There is no account to create: Scotty
checks them with your IMAP server. An address listed in `Generic__Administrators` also sees
Settings › Administration.

✅ **Check:** your inbox appears.

## Step 6 — Back up

Two things hold everything Scotty knows:

- **the database** — back it up as you back up any other;
- **the `scotty-state` volume** — the key that signs sessions and the keys that encrypt the secrets
  kept in the database. Without it, everyone is signed out, and an administrator must enter again
  the external mail services' secrets and the calendar service account's password.

```bash
docker run --rm -v scotty_scotty-state:/state -v "$PWD":/backup alpine \
  tar -czf /backup/scotty-state.tar.gz -C /state .
```

(`scotty_` is the compose project's name, the folder's by default: `docker volume ls` shows it.)

---

## Updating

```bash
docker compose pull
docker compose up -d
```

That is all: the new version updates its tables itself when it starts. Settings › About shows the
running version, with the image's number on its last line.

`docker-compose.yml` follows `:1`, every 1.x release. To stay on one exact version, write it
instead, such as `ghcr.io/darthmaul0181/scotty-webmail:1.0.0`.

**Database installed before version 1.3.0?** Run
[`install/adopt-existing-database.sql`](../install/adopt-existing-database.sql) once, as in
[Installed before version 1.3.0?](../install/README.md#keeping-it-running).

## Optional

**Contacts and calendars on phones.** Add `Dav__PublicUrl` to `.env`: the address that answers the
API — `https://mail.example.net` with one address, `https://api.example.net` with two — then
`docker compose up -d`. A **Sync** tab appears in each user's settings.

**Mail rules, Outlook mailboxes, calendar replies:** as in [Optional](../install/README.md#optional)
of the classic guide; the settings go in `.env` instead of the service's settings file.

## When it won't start

Read why:

```bash
docker compose logs --tail 30 scotty
```

The messages of [When the service won't start](../install/README.md#when-the-service-wont-start)
apply here too, `.env` being the settings file. And these are Docker's own:

| You see | Do this |
|---|---|
| `restarting` in `docker compose ps`, and `Unable to connect to any of the specified MySQL hosts` in the log | The container cannot reach the database. Use an address it can reach (step 2), and let the database accept connections from Docker's network (step 1's `__HOST__`) |
| Every address answers 404, even the home page | The proxy does not pass on the `Host` header (step 3) |
| Signing in seems to work, then every page asks again | The site is not on HTTPS, or the two addresses are on two different domains (step 3) |
| Everyone is signed out after each update | The `scotty-state` volume is missing from `docker-compose.yml` |
| `Access to the path '/var/lib/scotty/…' is denied` | A folder mounted there instead of the volume must belong to user 1654: `chown -R 1654:1654 <folder>` |
| `Frontend:ApiBase holds '…'` | Write the API's address alone, such as `https://api.example.net`, without a path — or remove the line for one address |
| `No CORS origin is configured. Frontend:ApiBase sends…` | With two addresses, add `Cors__AllowedOrigins__0` with the pages' address |
| `No reverse proxy is configured` | Fill in `ForwardedHeaders__KnownNetworks__0` (step 3) |
````

- [ ] **Step 2: `docs/operations/docker-image-release.md`**

```markdown
# Publier une image Docker

Pour le propriétaire du dépôt. Le guide des utilisateurs est `docker/README.md`.

## Le parcours

```
branche ──(candidate rc.1 : essai sur ton serveur)──► corrections ──► fusion dans master
                                                                         │
                                       candidate rc.2 depuis master ◄────┘
                                                  │
                                       test sur ton serveur
                                                  │
                                       release 1.0.0-rc.2 ──► 1.0.0, 1.0, 1, latest
```

## 1. Décider du numéro

`docker/VERSION` porte le numéro de la prochaine image. Le faire monter sur la branche, avant la
fusion : correctif (`1.0.1`) pour un correctif ou une image de base mise à jour, mineur (`1.1.0`)
pour une nouveauté, majeur (`2.0.0`) si un `.env` existant doit changer.

## 2. Construire une candidate

GitHub › Actions › **image** › **Run workflow** : choisir la branche, mode `candidate`. Le workflow
construit les deux architectures, démarre l'image à côté d'une MariaDB jetable, et publie
`1.0.0-rc.N`. Le numéro publié est dans le résumé du run.

Une candidate construite depuis une autre branche que `master` sert à essayer ; elle ne pourra
jamais être publiée. Ses versions web et serveur se terminent par `-dev`.

## 3. La tester

Sur le serveur de test, dans le `docker-compose.yml` :

```yaml
    image: ghcr.io/darthmaul0181/scotty-webmail:1.0.0-rc.2
```

puis `docker compose pull && docker compose up -d`. **Avec une base de test** : l'image applique
ses migrations à la base qu'on lui donne. Vérifier `docker compose ps` (`healthy`), la connexion,
puis Réglages › À propos (« Docker image 1.0.0 »).

## 4. Publier

Actions › **image** › **Run workflow**, mode `release`, candidate `1.0.0-rc.2` (la branche choisie
n'importe pas). Le workflow refuse, en disant pourquoi, si la candidate ne vient pas de `master`, si
`docker/VERSION` ne vaut pas `1.0.0` à son commit, ou si `image-v1.0.0` existe déjà. Sinon, il
ajoute `1.0.0`, `1.0`, `1` et `latest` à la même image, sans la reconstruire, et pose le tag Git
`image-v1.0.0`.

## Une seule fois, après la première publication

GHCR crée tout nouveau paquet en privé. GitHub › ton profil › **Packages** › `scotty-webmail` ›
**Package settings** › **Change visibility** › **Public**. Vérifier depuis une machine sans
connexion à GHCR : `docker pull ghcr.io/darthmaul0181/scotty-webmail:1.0.0`.

## Mises à jour de l'image de base

Dependabot ouvre une PR quand Microsoft ou Node publient une nouvelle image de base. La fusionner
ne publie rien : faire monter `docker/VERSION` d'un correctif, puis suivre les étapes 2 à 4.
```

- [ ] **Step 3: Root `README.md` and `install/README.md`**

Dans `README.md` (python, `\r\n`) :
- après la liste numérotée de « ## Installing it », ajouter le paragraphe :
  `**With Docker**, one container serves the pages and the API: [`docker/README.md`](docker/README.md) takes you from an empty server to signing in, with a database and an HTTPS proxy you provide.`
- dans le tableau « Documentation », après la ligne `install/README.md`, ajouter :
  `| [`docker/README.md`](docker/README.md) | **Or with Docker.** One container, its compose file and settings |`
- dans « Repository layout », avant `├── install/`, ajouter :
```
├── docker/                        # The Docker image: Dockerfile, compose file, settings, guide
```

Dans `install/README.md`, après la première phrase d'introduction (« This guide installs Scotty on a Linux server. When you are done, … calendar. »), ajouter un paragraphe :
`Prefer Docker? [`docker/README.md`](../docker/README.md) does the same in one container.`

- [ ] **Step 4: Versions**

```bash
printf '1.4.0\n' > src/scotty.microservice/VERSION
printf '2.3.0\n' > src/frontend/VERSION
cd src/frontend && npm version 2.3.0 --no-git-tag-version && cd ../..
git diff --stat
```
Expected: `VERSION` files, `package.json` and `package-lock.json` changed (two `"version"` lines in the lock). Check with `git diff src/frontend/package-lock.json` that nothing else moved; the VERSION files keep the repository's line endings (`git diff` shows only the number).

- [ ] **Step 5: Check the links**

Run: `grep -o '](\.\./[^)#]*\|](docker/[^)#]*\|](install/[^)#]*' docker/README.md README.md install/README.md | sed 's/.*](//' | sort -u`
Expected: every listed path exists relative to its file (`../install/README.md`, `../install/adopt-existing-database.sql`, `docker/README.md`, `../docker/README.md`…); open each anchor used (`#step-2--create-the-database`, `#keeping-it-running`, `#optional`, `#when-the-service-wont-start`, `#what-your-mail-service-needs`) and confirm the heading exists in `install/README.md`.

- [ ] **Step 6: Full test run**

Run:
```bash
cd src && dotnet test > ../.superpowers/task8-backend.log 2>&1; tail -n 5 ../.superpowers/task8-backend.log; cd ..
cd src/frontend && npm test -- --run > ../../.superpowers/task8-frontend.log 2>&1; tail -n 6 ../../.superpowers/task8-frontend.log; npm run lint; cd ../..
```
Expected: both suites pass (the known intermittent front-end flake excepted, per memory: rerun once), lint clean. `ProductVersionTests.Current_IsReadFromTheMicroserviceVersionFile` reads 1.4.0.

- [ ] **Step 7: Commit**

```bash
git checkout -- src/scotty.microservice/ApiDocumentation.xml 2>/dev/null
git add docker/README.md docs/operations/docker-image-release.md README.md install/README.md src/scotty.microservice/VERSION src/frontend/VERSION src/frontend/package.json src/frontend/package-lock.json
git commit -F - <<'EOF'
Document the Docker install and the image release, and raise the versions

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```
