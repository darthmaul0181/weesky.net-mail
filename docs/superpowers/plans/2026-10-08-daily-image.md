# Image du jour — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** l'image du jour de Bing en fond de la page de connexion et, au choix de chaque utilisateur, du panneau de lecture vide.

**Architecture:** un service singleton de l'API lit l'archive Bing, vérifie l'entrée, télécharge le JPEG
1920×1080 et le garde en mémoire par marché jusqu'au changement d'image ; un contrôleur anonyme le sert
sous une adresse versionnée, seulement si `app.dailyImage` vaut `true`. Côté client, un hook
`useDailyImage()` lit les métadonnées, précharge la photo et ne la rend qu'une fois chargée ; la page de
connexion et un composant `ReaderEmpty` l'affichent, ce dernier selon la préférence `ui.dailyImage`.

**Tech Stack:** ASP.NET Core 10, `IHttpClientFactory`, `TimeProvider`, xUnit + Moq ; React 19, TanStack
Query, Vitest + Testing Library, i18next.

**Spec:** `docs/superpowers/2026-10-08-daily-image-design.md` · Maquettes : https://claude.ai/artifact/WiiRPaCXMqEQPGzXfVRHUH

## Global Constraints

- Clés : instance `app.dailyImage` (`true`/`false`, défaut `false`) ; compte `ui.dailyImage`
  (`none`/`fullBleed`/`postcard`/`watermark`, défaut `none`).
- Hôte Bing fixe `https://www.bing.com` ; `urlbase` accepté seulement s'il correspond à `^/th\?id=[A-Za-z0-9._-]+$` ;
  image = `https://www.bing.com` + `urlbase` + `_1920x1080.jpg`. Aucune URL reçue n'est suivie telle quelle.
- `HttpClient` nommé `bing` : `AllowAutoRedirect = false`, `Timeout` 5 s.
- Plafonds : métadonnées 64 Ko (65 536 o), image 5 Mo (5 242 880 o) ; `Content-Type` de l'image `image/jpeg` exigé.
- Expiration = `fullstartdate` (UTC, `yyyyMMddHHmm`) + 24 h, jamais moins de maintenant + 1 h. Nouvel essai après échec : 15 min.
- Marché : `fr` → `fr-FR`, toute autre valeur → `en-US`.
- Routes anonymes : `GET /api/AppSettings/daily-image?lang=` (`Cache-Control: no-cache`) et
  `GET /api/AppSettings/daily-image/{version}?lang=` (`public, max-age=31536000, immutable` + `Vary: Origin`).
  Contrôleur dédié `DailyImageController` (écart assumé avec la spec, qui disait « contrôleur `AppSettings` » :
  l'adresse est la même, le contrôleur existant et ses tests restent intacts — la tâche 4 corrige la spec).
- Couleurs : uniquement des tokens de rôle (`.claude/rules/frontend-theming.md`). Pastilles et crédit sur
  `color-mix(in oklab, var(--surface) …%, transparent)` avec `var(--text)` : ils suivent le mode sombre.
  (La maquette montrait un crédit sombre fixe ; un fond de surface translucide est l'équivalent en tokens.)
- Fondu d'apparition 200 ms, aucun si `prefers-reduced-motion: reduce`.
- Libellés UI en anglais, traduction dans `locales/fr` (apostrophe `’`, insécable U+00A0 avant `:` `?` `!` —
  mémoire « Insécable et outil Edit ») ; `src/locales/parity.test.ts` exige les mêmes clés dans les deux.
- Pas de nouvelle dépendance NuGet ni npm.
- Commentaires : seulement un pourquoi non évident, 3 lignes max.
- Commits : deux lignes max, terminés par `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`, via
  heredoc `git commit -F -`. Ne pas pousser.
- Avant chaque commit côté API : `git checkout -- src/scotty.microservice/ApiDocumentation.xml`, puis ne
  réintégrer que les blocs des nouveaux membres documentés (mémoire « Dérive d'ApiDocumentation.xml »).
- Tests API : `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter <Classe>` (jamais `--no-build`
  après un nouveau fichier). Tests web : depuis `src/frontend`, `npx vitest run <chemin>` ; puis `npm run typecheck` et `npm run lint`.

## Review Focus

1. **Archive Bing périmée** (`fullstartdate` vieux de plusieurs jours) : sans plancher, l'entrée naîtrait
   expirée et chaque visite rappellerait Bing. → test `Parse_FloorsAStaleExpiry` (tâche 2) et
   `Get_DoesNotRefetchAStaleArchiveOnEveryVisit` (tâche 3).
2. **Visiteur qui ferme l'onglet pendant le téléchargement** : son annulation ne doit ni bloquer les autres
   (verrou relâché) ni déclencher les 15 min d'attente pour tout le monde. → `Get_AClientCancellationDelaysNobody` (tâche 3).
3. **Version périmée** (onglet ouvert la veille, l'API a changé d'image) : la photo répond 404 ; le client
   doit rester sur le texte seul, jamais une image cassée. → `stays null when the photo fails to load` (tâche 5)
   et `File_RefusesAVersionThatIsNoLongerCurrent` (tâche 4).
4. **Admin qui coupe la fonction** pendant qu'un utilisateur a choisi `watermark` : le panneau revient au
   texte dès que les réglages sont relus, et le choix stocké n'est pas effacé. → `falls back to the text when
   the admin switches the feature off` (tâche 7) et `hides the section…` (tâche 8).
5. **Changement de langue** sur la page de connexion : la légende suit (fr → légende française). →
   `follows the interface language` (tâche 5).

---

### Task 1: Les deux clés dans les registres

**Files:**
- Modify: `src/scotty.microservice/Models/AppSettings.cs`
- Modify: `src/scotty.microservice/Models/UserPreferences.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Models/AppSettingsTests.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Models/UserPreferencesTests.cs`

**Interfaces:**
- Produces: `AppSettings.DailyImage` (`"app.dailyImage"`), `UserPreferences.UiDailyImage` (`"ui.dailyImage"`).

- [ ] **Step 1: Write the failing tests**

Dans `AppSettingsTests.Effective_AnswersEveryKeyWithItsDefault`, ajouter :

```csharp
        Assert.Equal("false", values[AppSettings.DailyImage]);
```

Dans `UserPreferencesTests`, ajouter à `All_CarriesTheKeysTheClientOffers` :

```csharp
        Assert.Contains(UserPreferences.All, p => p.Key == UserPreferences.UiDailyImage);
```

et à la fin de la classe :

```csharp
    [Theory]
    [InlineData("none")]
    [InlineData("fullBleed")]
    [InlineData("postcard")]
    [InlineData("watermark")]
    public void IsValid_AcceptsEveryDailyImageStyle(string value)
        => Assert.True(UserPreferences.IsValid(UserPreferences.UiDailyImage, value));

    [Theory]
    [InlineData("full-bleed")]
    [InlineData("")]
    [InlineData("Watermark")]
    public void IsValid_RefusesAnyOtherDailyImageStyle(string value)
        => Assert.False(UserPreferences.IsValid(UserPreferences.UiDailyImage, value));
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~AppSettingsTests|FullyQualifiedName~UserPreferencesTests"`
Expected: FAIL à la compilation (`DailyImage`, `UiDailyImage` inconnus).

- [ ] **Step 3: Implement**

`AppSettings.cs` — à côté des autres constantes, puis dans `All` :

```csharp
    public const string DailyImage = "app.dailyImage";
```

```csharp
        // Off by default: switched on, the server calls Bing every day, which an admin decides.
        new(DailyImage, "false", 5, Booleans),
```

`UserPreferences.cs` — à côté de `UiCustomPalette`, puis dans `All` après `UiCustomPalette` :

```csharp
    public const string UiDailyImage = "ui.dailyImage";
```

```csharp
        new(UiDailyImage, "none", ["none", "fullBleed", "postcard", "watermark"]),
```

- [ ] **Step 4: Run to verify they pass**

Même commande. Expected: PASS (dont `Default_IsItselfAValueTheRegistryAccepts` pour la nouvelle clé).

- [ ] **Step 5: Commit**

```bash
git add src/scotty.microservice/Models/AppSettings.cs src/scotty.microservice/Models/UserPreferences.cs src/scotty.microservice/scotty.microservice.Tests/Models/AppSettingsTests.cs src/scotty.microservice/scotty.microservice.Tests/Models/UserPreferencesTests.cs
git commit -q -F - <<'EOF'
Register app.dailyImage and ui.dailyImage

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: Lecture de l'archive Bing

**Files:**
- Create: `src/scotty.microservice/Models/DailyImage.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Models/BingArchiveTests.cs`

**Interfaces:**
- Produces:
  - `record DailyImage(string Version, string Title, string Copyright, byte[] Bytes, DateTimeOffset ExpiresAt)`
  - `record DailyImageInfo(string Version, string Title, string Copyright)` (corps JSON de la route d'infos)
  - `record BingImageEntry(string Version, string Title, string Copyright, Uri ImageUrl, DateTimeOffset ExpiresAt)`
  - `static class BingArchive` : `Host`, `MaxMetadataBytes` (65 536), `MaxImageBytes` (5 242 880),
    `MarketOf(string? lang) : string`, `MetadataUrl(string market) : Uri`,
    `Parse(string json, DateTimeOffset now) : CSharpFunctionalExtensions.Result<BingImageEntry>`.

- [ ] **Step 1: Write the failing tests**

```csharp
using weesky.Scotty.Microservice.Models;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Models;

public sealed class BingArchiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    internal static string Archive(
        string urlBase = "/th?id=OHR.MayotteOctopus_FR-FR2063163267",
        string hash = "baeeb03654162a2eaa1439de2eee4b89",
        string start = "202610072200",
        string title = "Poulpe fiction") => $$"""
        {"images":[{"fullstartdate":"{{start}}","urlbase":"{{urlBase}}",
          "copyright":"Poulpe en position défensive, Mayotte (© Gabriel Barathieu/Minden Pictures)",
          "title":"{{title}}","hsh":"{{hash}}"}]}
        """;

    [Fact]
    public void Parse_ReadsTheEntryAndRebuildsTheImageUrl()
    {
        var entry = BingArchive.Parse(Archive(), Now).Value;

        Assert.Equal("baeeb03654162a2eaa1439de2eee4b89", entry.Version);
        Assert.Equal("Poulpe fiction", entry.Title);
        Assert.Equal("Poulpe en position défensive, Mayotte (© Gabriel Barathieu/Minden Pictures)", entry.Copyright);
        Assert.Equal(new Uri("https://www.bing.com/th?id=OHR.MayotteOctopus_FR-FR2063163267_1920x1080.jpg"), entry.ImageUrl);
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 22, 0, 0, TimeSpan.Zero), entry.ExpiresAt);
    }

    [Theory]
    [InlineData("//evil.test/th?id=x")]
    [InlineData("https://evil.test/th?id=x")]
    [InlineData("/th?id=x&url=https://evil.test")]
    [InlineData("/elsewhere?id=x")]
    public void Parse_RefusesAPathOutsideTheThumbnailRoute(string urlBase)
        => Assert.True(BingArchive.Parse(Archive(urlBase: urlBase), Now).IsFailure);

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"images":[]}""")]
    [InlineData("""{"images":["x"]}""")]
    [InlineData("""{"images":[{"urlbase":"/th?id=x","hsh":"abc","fullstartdate":"202610072200","copyright":"c"}]}""")]
    public void Parse_RefusesAnIncompleteArchive(string json)
        => Assert.True(BingArchive.Parse(json, Now).IsFailure);

    [Theory]
    [InlineData("2026-10-07")]
    [InlineData("")]
    public void Parse_RefusesAnUnreadableStartDate(string start)
        => Assert.True(BingArchive.Parse(Archive(start: start), Now).IsFailure);

    // The hash becomes a path segment of our own route.
    [Theory]
    [InlineData("../x")]
    [InlineData("")]
    public void Parse_RefusesAHashThatIsNotPlainAlphanumeric(string hash)
        => Assert.True(BingArchive.Parse(Archive(hash: hash), Now).IsFailure);

    [Fact]
    public void Parse_FloorsAStaleExpiry()
    {
        var entry = BingArchive.Parse(Archive(start: "202610042200"), Now).Value;

        Assert.Equal(Now.AddHours(1), entry.ExpiresAt);
    }

    [Theory]
    [InlineData("fr", "fr-FR")]
    [InlineData("en", "en-US")]
    [InlineData(null, "en-US")]
    [InlineData("de", "en-US")]
    public void MarketOf_FollowsTheInterfaceLanguage(string? lang, string market)
        => Assert.Equal(market, BingArchive.MarketOf(lang));
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~BingArchiveTests"`
Expected: FAIL à la compilation (`BingArchive` inconnu).

- [ ] **Step 3: Implement `Models/DailyImage.cs`**

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;

namespace weesky.Scotty.Microservice.Models;

/// <summary>The image of the day as the API holds it: the photo and the credit it must carry.</summary>
public sealed record DailyImage(
    string Version, string Title, string Copyright, byte[] Bytes, DateTimeOffset ExpiresAt);

/// <summary>What the client reads to show the credit and build the versioned image address.</summary>
public sealed record DailyImageInfo(string Version, string Title, string Copyright);

/// <summary>One checked entry of Bing's archive.</summary>
public sealed record BingImageEntry(
    string Version, string Title, string Copyright, Uri ImageUrl, DateTimeOffset ExpiresAt);

/// <summary>
/// Bing's image archive. It has no official API, so every field is checked, and the image is
/// fetched from an address rebuilt on a fixed host, never from one the answer supplies.
/// </summary>
public static class BingArchive
{
    public const string Host = "https://www.bing.com";
    public const int MaxMetadataBytes = 64 * 1024;
    public const int MaxImageBytes = 5 * 1024 * 1024;

    // An archive already days old would otherwise be born expired and refetched on every visit.
    private static readonly TimeSpan MinFreshness = TimeSpan.FromHours(1);

    private static readonly Regex UrlBase = new(
        @"^/th\?id=[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Hash = new(
        "^[A-Za-z0-9]{1,64}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string MarketOf(string? lang) => lang == "fr" ? "fr-FR" : "en-US";

    public static Uri MetadataUrl(string market) =>
        new($"{Host}/HPImageArchive.aspx?format=js&idx=0&n=1&mkt={market}");

    public static Result<BingImageEntry> Parse(string json, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("images", out var images)
                || images.ValueKind != JsonValueKind.Array
                || images.GetArrayLength() == 0
                || images[0].ValueKind != JsonValueKind.Object)
                return Result.Failure<BingImageEntry>("No image in the archive");

            var image = images[0];
            var urlBase = Text(image, "urlbase");
            var hash = Text(image, "hsh");
            var title = Text(image, "title");
            var copyright = Text(image, "copyright");

            if (!UrlBase.IsMatch(urlBase)) return Result.Failure<BingImageEntry>("Unexpected image path");
            if (!Hash.IsMatch(hash)) return Result.Failure<BingImageEntry>("Unexpected image hash");
            if (title.Length is 0 or > 200 || copyright.Length is 0 or > 400)
                return Result.Failure<BingImageEntry>("Missing credit");
            if (!DateTimeOffset.TryParseExact(Text(image, "fullstartdate"), "yyyyMMddHHmm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var start))
                return Result.Failure<BingImageEntry>("Unreadable start date");

            var expires = start.AddDays(1);
            if (expires < now + MinFreshness) expires = now + MinFreshness;

            return new BingImageEntry(hash, title, copyright, new Uri($"{Host}{urlBase}_1920x1080.jpg"), expires);
        }
        catch (JsonException)
        {
            return Result.Failure<BingImageEntry>("Unreadable archive");
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!.Trim()
            : string.Empty;
}
```

- [ ] **Step 4: Run to verify they pass**

Même commande. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/scotty.microservice/Models/DailyImage.cs src/scotty.microservice/scotty.microservice.Tests/Models/BingArchiveTests.cs
git commit -q -F - <<'EOF'
Read and check Bing's image archive

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 3: Service de l'image du jour (téléchargement, cache, attente après échec)

**Files:**
- Create: `src/scotty.microservice/Services/IDailyImageService.cs`
- Create: `src/scotty.microservice/Services/DailyImageService.cs`
- Modify: `src/scotty.microservice/Configuration/ApplicationServicesConfiguration.cs` (après l'`AddHttpClient<IOAuthTokenService…>`)
- Modify: `src/scotty.microservice/scotty.microservice.Tests/Infrastructure/StubHttpMessageHandler.cs` (enregistrer les URI)
- Test: `src/scotty.microservice/scotty.microservice.Tests/Services/DailyImageServiceTests.cs`

**Interfaces:**
- Consumes: `BingArchive`, `DailyImage` (tâche 2).
- Produces: `IDailyImageService.GetAsync(string market, CancellationToken) : Task<DailyImage?>` ;
  `DailyImageService.ClientName` (`"bing"`), `DailyImageService.RetryDelay` (15 min).

- [ ] **Step 1: Let the stub record request URIs**

Dans `StubHttpMessageHandler`, ajouter la propriété et l'enregistrer en tête de `SendAsync` :

```csharp
    public List<Uri?> Uris { get; } = [];
```

```csharp
        Uris.Add(request.RequestUri);
```

- [ ] **Step 2: Write the failing tests**

```csharp
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using weesky.Scotty.Microservice.Tests.Models;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services;

public sealed class DailyImageServiceTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];
    private readonly MutableTimeProvider _clock = new() { Now = new DateTimeOffset(2026, 10, 8, 9, 0, 0, TimeSpan.Zero) };

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class LambdaHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            send(request, ct);
    }

    // A content whose length is unknown up front, the case where only counting the stream protects us.
    private sealed class UnsizedContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private DailyImageService Create(HttpMessageHandler handler) =>
        new(new Factory(handler), _clock, NullLogger<DailyImageService>.Instance);

    private static Func<HttpResponseMessage> ArchiveOk(string hash = "baeeb03654162a2eaa1439de2eee4b89", string start = "202610072200") =>
        StubHttpMessageHandler.Json(HttpStatusCode.OK, BingArchiveTests.Archive(hash: hash, start: start));

    private static Func<HttpResponseMessage> Image(byte[]? bytes = null, string type = "image/jpeg") => () =>
    {
        var content = new ByteArrayContent(bytes ?? Jpeg);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    };

    private static Func<HttpResponseMessage> Status(HttpStatusCode status) => () => new HttpResponseMessage(status);

    [Fact]
    public async Task Get_FetchesTheArchiveThenTheImage()
    {
        var handler = new StubHttpMessageHandler(ArchiveOk(), Image());

        var image = await Create(handler).GetAsync("fr-FR", CancellationToken.None);

        Assert.NotNull(image);
        Assert.Equal("baeeb03654162a2eaa1439de2eee4b89", image.Version);
        Assert.Equal(Jpeg, image.Bytes);
        Assert.Equal(BingArchive.MetadataUrl("fr-FR"), handler.Uris[0]);
        Assert.Equal(new Uri("https://www.bing.com/th?id=OHR.MayotteOctopus_FR-FR2063163267_1920x1080.jpg"), handler.Uris[1]);
    }

    [Fact]
    public async Task Get_ServesTheCacheUntilBingChangesImage()
    {
        var handler = new StubHttpMessageHandler(ArchiveOk(), Image(), ArchiveOk(hash: "next"), Image());
        var service = Create(handler);

        await service.GetAsync("fr-FR", CancellationToken.None);
        await service.GetAsync("fr-FR", CancellationToken.None);
        Assert.Equal(2, handler.Calls);

        _clock.Now = new DateTimeOffset(2026, 10, 8, 22, 0, 0, TimeSpan.Zero);
        var next = await service.GetAsync("fr-FR", CancellationToken.None);

        Assert.Equal(4, handler.Calls);
        Assert.Equal("next", next!.Version);
    }

    [Fact]
    public async Task Get_DoesNotRefetchAStaleArchiveOnEveryVisit()
    {
        var handler = new StubHttpMessageHandler(ArchiveOk(start: "202610042200"), Image());
        var service = Create(handler);

        await service.GetAsync("fr-FR", CancellationToken.None);
        _clock.Now = _clock.Now.AddMinutes(30);
        await service.GetAsync("fr-FR", CancellationToken.None);

        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Get_RefusesAnArchiveThatIsNotAPlainOk(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler(Status(status));

        Assert.Null(await Create(handler).GetAsync("fr-FR", CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Get_RefusesAnImageRedirect()
    {
        var handler = new StubHttpMessageHandler(ArchiveOk(), Status(HttpStatusCode.Found));

        Assert.Null(await Create(handler).GetAsync("fr-FR", CancellationToken.None));
    }

    [Fact]
    public async Task Get_RefusesAnImageThatIsNotAJpeg()
    {
        var handler = new StubHttpMessageHandler(ArchiveOk(), Image(type: "text/html"));

        Assert.Null(await Create(handler).GetAsync("fr-FR", CancellationToken.None));
    }

    [Fact]
    public async Task Get_RefusesAnImageDeclaredOverTheCap()
    {
        var handler = new StubHttpMessageHandler(ArchiveOk(), Image(new byte[BingArchive.MaxImageBytes + 1]));

        Assert.Null(await Create(handler).GetAsync("fr-FR", CancellationToken.None));
    }

    [Fact]
    public async Task Get_RefusesAnUnsizedImageThatRunsOverTheCap()
    {
        var oversized = () =>
        {
            var content = new UnsizedContent(new byte[BingArchive.MaxImageBytes + 1]);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        };
        var handler = new StubHttpMessageHandler(ArchiveOk(), oversized);

        Assert.Null(await Create(handler).GetAsync("fr-FR", CancellationToken.None));
    }

    [Fact]
    public async Task Get_KeepsYesterdaysImageAndWaitsBeforeRetrying()
    {
        var handler = new StubHttpMessageHandler(
            ArchiveOk(), Image(), Status(HttpStatusCode.ServiceUnavailable), ArchiveOk(hash: "next"), Image());
        var service = Create(handler);
        var yesterday = await service.GetAsync("fr-FR", CancellationToken.None);

        _clock.Now = new DateTimeOffset(2026, 10, 8, 22, 0, 0, TimeSpan.Zero);
        Assert.Same(yesterday, await service.GetAsync("fr-FR", CancellationToken.None));

        _clock.Now = _clock.Now.AddMinutes(14);
        Assert.Same(yesterday, await service.GetAsync("fr-FR", CancellationToken.None));
        Assert.Equal(3, handler.Calls);

        _clock.Now = _clock.Now.AddMinutes(1);
        Assert.Equal("next", (await service.GetAsync("fr-FR", CancellationToken.None))!.Version);
    }

    [Fact]
    public async Task Get_TreatsATimeoutAsAFailure()
    {
        var calls = 0;
        var handler = new LambdaHandler((_, _) =>
        {
            calls++;
            throw new TaskCanceledException("timeout");
        });
        var service = Create(handler);

        Assert.Null(await service.GetAsync("fr-FR", CancellationToken.None));
        Assert.Null(await service.GetAsync("fr-FR", CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Get_ConcurrentVisitorsShareOneDownload()
    {
        var release = new TaskCompletionSource();
        var scripted = new StubHttpMessageHandler(ArchiveOk(), Image());
        var handler = new LambdaHandler(async (request, ct) =>
        {
            await release.Task;
            return await new HttpMessageInvoker(scripted, disposeHandler: false).SendAsync(request, ct);
        });
        var service = Create(handler);

        var visits = Enumerable.Range(0, 5).Select(_ => service.GetAsync("fr-FR", CancellationToken.None)).ToArray();
        release.SetResult();
        var images = await Task.WhenAll(visits);

        Assert.Equal(2, scripted.Calls);
        Assert.All(images, image => Assert.Same(images[0], image));
    }

    [Fact]
    public async Task Get_AClientCancellationDelaysNobody()
    {
        var scripted = new StubHttpMessageHandler(ArchiveOk(), Image());
        var first = true;
        var handler = new LambdaHandler(async (request, ct) =>
        {
            if (first)
            {
                first = false;
                await Task.Delay(Timeout.Infinite, ct);
            }
            return await new HttpMessageInvoker(scripted, disposeHandler: false).SendAsync(request, ct);
        });
        var service = Create(handler);
        using var leaving = new CancellationTokenSource();

        var abandoned = service.GetAsync("fr-FR", leaving.Token);
        leaving.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        Assert.NotNull(await service.GetAsync("fr-FR", CancellationToken.None));
    }

    [Fact]
    public async Task Get_KeepsMarketsApart()
    {
        var handler = new StubHttpMessageHandler(ArchiveOk(hash: "fr"), Image(), ArchiveOk(hash: "en"), Image());
        var service = Create(handler);

        Assert.Equal("fr", (await service.GetAsync("fr-FR", CancellationToken.None))!.Version);
        Assert.Equal("en", (await service.GetAsync("en-US", CancellationToken.None))!.Version);
        Assert.Equal(BingArchive.MetadataUrl("en-US"), handler.Uris[2]);
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~DailyImageServiceTests"`
Expected: FAIL à la compilation (`DailyImageService` inconnu).

- [ ] **Step 4: Implement**

`Services/IDailyImageService.cs` :

```csharp
using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Microservice.Services;

/// <summary>Bing's image of the day, fetched once per change and held in memory per market.</summary>
public interface IDailyImageService
{
    /// <summary>Today's image, an earlier one while Bing fails, or null when none was ever fetched.</summary>
    Task<DailyImage?> GetAsync(string market, CancellationToken cancellationToken);
}
```

`Services/DailyImageService.cs` :

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Microservice.Services;

public sealed class DailyImageService(
    IHttpClientFactory clients, TimeProvider clock, ILogger<DailyImageService> logger) : IDailyImageService
{
    public const string ClientName = "bing";
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(15);

    // Bounded: BingArchive.MarketOf only ever answers two markets.
    private readonly ConcurrentDictionary<string, Slot> _slots = new(StringComparer.Ordinal);

    public async Task<DailyImage?> GetAsync(string market, CancellationToken cancellationToken)
    {
        var slot = _slots.GetOrAdd(market, _ => new Slot());
        if (TryServe(slot.State, out var served)) return served;

        await slot.Gate.WaitAsync(cancellationToken);
        try
        {
            if (TryServe(slot.State, out served)) return served;

            var fresh = await FetchAsync(market, cancellationToken);
            slot.State = fresh is null
                ? slot.State with { RetryAfter = clock.GetUtcNow() + RetryDelay }
                : new SlotState(fresh, DateTimeOffset.MinValue);
            return slot.State.Current;
        }
        finally
        {
            slot.Gate.Release();
        }
    }

    // Fresh, or failed too recently to ask again: either way the answer is what the slot holds.
    private bool TryServe(SlotState state, out DailyImage? image)
    {
        var now = clock.GetUtcNow();
        image = state.Current;
        return (image is not null && now < image.ExpiresAt) || now < state.RetryAfter;
    }

    private async Task<DailyImage?> FetchAsync(string market, CancellationToken cancellationToken)
    {
        try
        {
            var client = clients.CreateClient(ClientName);

            using var archive = await client.GetAsync(
                BingArchive.MetadataUrl(market), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (archive.StatusCode != HttpStatusCode.OK) return Refused(market, $"archive answered {(int)archive.StatusCode}");
            var json = await ReadCappedAsync(archive.Content, BingArchive.MaxMetadataBytes, cancellationToken);
            if (json is null) return Refused(market, "archive too large");

            var entry = BingArchive.Parse(Encoding.UTF8.GetString(json), clock.GetUtcNow());
            if (entry.IsFailure) return Refused(market, entry.Error);

            using var image = await client.GetAsync(
                entry.Value.ImageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (image.StatusCode != HttpStatusCode.OK || image.Content.Headers.ContentType?.MediaType != "image/jpeg")
                return Refused(market, $"image answered {(int)image.StatusCode}");
            var bytes = await ReadCappedAsync(image.Content, BingArchive.MaxImageBytes, cancellationToken);
            if (bytes is null) return Refused(market, "image too large");

            var e = entry.Value;
            return new DailyImage(e.Version, e.Title, e.Copyright, bytes, e.ExpiresAt);
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning(ex, "Image of the day unavailable for {Market}", market);
            return null;
        }
    }

    private DailyImage? Refused(string market, string reason)
    {
        logger.LogWarning("Image of the day refused for {Market}: {Reason}", market, reason);
        return null;
    }

    // The declared length is checked first, the stream still counted: a server may omit or lie about it.
    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, int max, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > max) return null;

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > max) return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private sealed record SlotState(DailyImage? Current, DateTimeOffset RetryAfter);

    private sealed class Slot
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public volatile SlotState State = new(null, DateTimeOffset.MinValue);
    }
}
```

Si `ILogger<>` n'est pas visible (pas d'`using` global), ajouter `using Microsoft.Extensions.Logging;`.

`ApplicationServicesConfiguration.cs`, après l'enregistrement d'`IOAuthTokenService` :

```csharp
        services.AddHttpClient(DailyImageService.ClientName, client => client.Timeout = TimeSpan.FromSeconds(5))
            // The image address is rebuilt on Bing's host; a redirect would send us wherever it says.
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        // Singleton is load-bearing: the day's image lives in this instance's memory.
        services.AddSingleton<IDailyImageService, DailyImageService>();
```

- [ ] **Step 5: Run to verify they pass**

Même commande, puis la classe `ApplicationServicesConfigurationTests` (`--filter "FullyQualifiedName~ApplicationServicesConfigurationTests"`). Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git checkout -- src/scotty.microservice/ApiDocumentation.xml
git add src/scotty.microservice/Services/IDailyImageService.cs src/scotty.microservice/Services/DailyImageService.cs src/scotty.microservice/Configuration/ApplicationServicesConfiguration.cs src/scotty.microservice/scotty.microservice.Tests/Infrastructure/StubHttpMessageHandler.cs src/scotty.microservice/scotty.microservice.Tests/Services/DailyImageServiceTests.cs
git commit -q -F - <<'EOF'
Fetch Bing's image of the day once per change, held in memory per market

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 4: Routes anonymes de l'image du jour

**Files:**
- Create: `src/scotty.microservice/Controllers/DailyImageController.cs`
- Modify: `docs/superpowers/2026-10-08-daily-image-design.md` (§ Routes : « contrôleur dédié `DailyImageController`, même adresse »)
- Modify: `src/scotty.microservice/ApiDocumentation.xml` (blocs des deux actions seulement)
- Test: `src/scotty.microservice/scotty.microservice.Tests/Controllers/DailyImageControllerTests.cs`

**Interfaces:**
- Consumes: `IAppSettingStore.GetAsync`, `AppSettings.Effective`, `AppSettings.DailyImage`, `IDailyImageService`, `BingArchive.MarketOf`, `DailyImageInfo`.
- Produces: `GET /api/AppSettings/daily-image?lang=` → `{ version, title, copyright }` ;
  `GET /api/AppSettings/daily-image/{version}?lang=` → `image/jpeg`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Scotty.Microservice.Controllers;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Controllers;

public sealed class DailyImageControllerTests
{
    private static readonly DailyImage Today = new("v1", "Poulpe fiction", "© Gabriel Barathieu", [1, 2, 3], DateTimeOffset.MaxValue);

    private readonly Mock<IAppSettingStore> _settings = new();
    private readonly Mock<IDailyImageService> _images = new();

    private DailyImageController Create(bool enabled = true)
    {
        _settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            [new AppSetting { SettingKey = AppSettings.DailyImage, SettingValue = enabled ? "true" : "false" }]);
        _images.Setup(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Today);

        return new DailyImageController(_settings.Object, _images.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Theory]
    [InlineData(nameof(DailyImageController.GetDailyImage))]
    [InlineData(nameof(DailyImageController.GetDailyImageFile))]
    public void BothRoutesAreAnonymous(string action) =>
        Assert.NotEmpty(typeof(DailyImageController).GetMethod(action)!
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), false));

    [Fact]
    public async Task Info_SwitchedOff_IsNotFoundAndNeverAsksBing()
    {
        var result = await Create(enabled: false).GetDailyImage("fr", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
        _images.Verify(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Info_AnswersTheCreditForTheLanguagesMarket()
    {
        var controller = Create();

        var result = await controller.GetDailyImage("fr", CancellationToken.None);

        var info = Assert.IsType<DailyImageInfo>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new DailyImageInfo("v1", "Poulpe fiction", "© Gabriel Barathieu"), info);
        Assert.Equal("no-cache", controller.Response.Headers.CacheControl.ToString());
        _images.Verify(i => i.GetAsync("fr-FR", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Info_WithoutAnImage_IsNotFound()
    {
        var controller = Create();
        _images.Setup(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((DailyImage?)null);

        var result = await controller.GetDailyImage("en", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task File_ServesTheJpegForGood()
    {
        var controller = Create();

        var result = await controller.GetDailyImageFile("v1", "en", CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal(Today.Bytes, file.FileContents);
        Assert.Equal("public, max-age=31536000, immutable", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal("Origin", controller.Response.Headers.Vary.ToString());
    }

    [Fact]
    public async Task File_RefusesAVersionThatIsNoLongerCurrent()
    {
        var result = await Create().GetDailyImageFile("yesterday", "en", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task File_SwitchedOff_IsNotFound()
    {
        var result = await Create(enabled: false).GetDailyImageFile("v1", "en", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        _images.Verify(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

`NotFoundEnveloppe` renvoie `NotFound(…)`, donc un `NotFoundObjectResult` ; `AppSetting` porte `SettingKey` et `SettingValue`.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~DailyImageControllerTests"`
Expected: FAIL à la compilation (`DailyImageController` inconnu).

- [ ] **Step 3: Implement `Controllers/DailyImageController.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services;

namespace weesky.Scotty.Microservice.Controllers;

/// <summary>
/// Bing's image of the day, served by the API so that browsers load nothing from elsewhere.
/// Anonymous: the login page shows it. Switched off, Bing is never asked.
/// </summary>
[Route("api/AppSettings/daily-image")]
[ApiController]
public sealed class DailyImageController(IAppSettingStore settings, IDailyImageService images) : ApiBaseController
{
    private const string NoImage = "No image of the day";

    /// <summary>The current image's version and credit.</summary>
    /// <param name="lang">interface language, en or fr: it picks Bing's market</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Version, title and copyright</response>
    /// <response code="404">Switched off, or Bing has no image to give</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DailyImageInfo>> GetDailyImage(
        [FromQuery] string? lang, CancellationToken cancellationToken)
    {
        var image = await CurrentAsync(lang, cancellationToken);
        if (image is null) return NotFoundEnveloppe(NoImage);

        Response.Headers.CacheControl = "no-cache";
        return Ok(new DailyImageInfo(image.Version, image.Title, image.Copyright));
    }

    /// <summary>The photo. Its URL carries the version, so it may be cached for good.</summary>
    /// <param name="version">the version the info route answered</param>
    /// <param name="lang">interface language, en or fr</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The JPEG</response>
    /// <response code="404">Switched off, no image, or a version that is no longer current</response>
    [HttpGet("{version}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetDailyImageFile(
        string version, [FromQuery] string? lang, CancellationToken cancellationToken)
    {
        var image = await CurrentAsync(lang, cancellationToken);
        if (image is null || image.Version != version) return NotFoundEnveloppe(NoImage);

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        // Cached for a year: a no-CORS load must not answer a later crossOrigin one.
        Response.Headers.Append(HeaderNames.Vary, "Origin");
        return File(image.Bytes, "image/jpeg");
    }

    private async Task<DailyImage?> CurrentAsync(string? lang, CancellationToken cancellationToken)
    {
        var effective = AppSettings.Effective(await settings.GetAsync(cancellationToken));
        if (effective[AppSettings.DailyImage] != "true") return null;

        return await images.GetAsync(BingArchive.MarketOf(lang), cancellationToken);
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Même commande, puis toute la suite API : `dotnet test src/scotty.microservice/scotty.microservice.Tests`.
Expected: PASS (l'instabilité classée « chunk lazy » concerne le front, pas cette suite).

- [ ] **Step 5: Correct the spec**

Dans `docs/superpowers/2026-10-08-daily-image-design.md`, remplacer le titre `### Routes (contrôleur \`AppSettings\`, anonymes)` par
`### Routes (contrôleur dédié \`DailyImageController\`, anonymes)` et ajouter sous la liste :
« Même préfixe d'adresse que les réglages ; un contrôleur à part laisse `AppSettingsController` et ses tests intacts. »

- [ ] **Step 6: Commit**

```bash
git checkout -- src/scotty.microservice/ApiDocumentation.xml
# réintégrer à la main les seuls blocs <member> de DailyImageController si le build les a générés
git add src/scotty.microservice/Controllers/DailyImageController.cs src/scotty.microservice/scotty.microservice.Tests/Controllers/DailyImageControllerTests.cs docs/superpowers/2026-10-08-daily-image-design.md src/scotty.microservice/ApiDocumentation.xml
git commit -q -F - <<'EOF'
Serve the image of the day anonymously, only when the admin switched it on

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 5: Données côté client (`useDailyImage`)

**Files:**
- Create: `src/frontend/src/lib/dailyImage.ts`
- Create: `src/frontend/src/hooks/useDailyImage.ts`
- Modify: `src/frontend/src/api.ts` (méthode `getDailyImage`, à côté de `getAppSettings`)
- Modify: `src/frontend/src/hooks/useAppSettings.ts` (clé + `dailyImageOf`)
- Modify: `src/frontend/src/hooks/usePreferences.ts` (clé + type + `dailyImageStyleOf`)
- Test: `src/frontend/src/hooks/useDailyImage.test.tsx`

**Interfaces:**
- Produces:
  - `lib/dailyImage.ts` : `interface DailyImageInfo { version: string; title: string; copyright: string }`,
    `dailyImageUrl(version: string, lang: Locale): string`.
  - `api.getDailyImage(lang: Locale, options?: RequestOptions): Promise<DailyImageInfo>`.
  - `APP_SETTING_KEYS.dailyImage = 'app.dailyImage'`, `dailyImageOf(settings: AppSettings): boolean`.
  - `PREFERENCE_KEYS.dailyImage = 'ui.dailyImage'`, `type DailyImageStyle = 'none' | 'fullBleed' | 'postcard' | 'watermark'`,
    `DAILY_IMAGE_STYLES: readonly DailyImageStyle[]`, `dailyImageStyleOf(preferences: Preferences): DailyImageStyle`.
  - `hooks/useDailyImage.ts` : `interface DailyImage { src: string; title: string; copyright: string }`,
    `useDailyImage(wanted?: boolean): DailyImage | null`.

- [ ] **Step 1: Write the failing tests**

```tsx
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import i18next from 'i18next'
import { createTestQueryClient } from '../test-utils'
import { useDailyImage } from './useDailyImage'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn(), getDailyImage: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))

// jsdom never loads an image: this one loads unless its address says "broken".
class LoadingImage {
  onload: (() => void) | null = null
  onerror: (() => void) | null = null
  set src(value: string) {
    queueMicrotask(() => (value.includes('broken') ? this.onerror?.() : this.onload?.()))
  }
}

// One client per test: the wrapper runs again on every rerender, and a fresh client would drop the cache.
let client: QueryClient
function wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

beforeEach(() => {
  client = createTestQueryClient()
  vi.stubGlobal('Image', LoadingImage)
  mocks.getAppSettings.mockResolvedValue({ 'app.dailyImage': 'true' })
  mocks.getDailyImage.mockResolvedValue({ version: 'v1', title: 'Poulpe fiction', copyright: '© G. Barathieu' })
})
afterEach(async () => {
  vi.unstubAllGlobals()
  vi.clearAllMocks()
  await i18next.changeLanguage('en')
})

describe('useDailyImage', () => {
  it('answers the photo and its credit once the photo has loaded', async () => {
    const { result } = renderHook(() => useDailyImage(), { wrapper })

    await waitFor(() => expect(result.current).not.toBeNull())
    expect(result.current!.src).toMatch(/\/api\/AppSettings\/daily-image\/v1\?lang=en$/)
    expect(result.current!.title).toBe('Poulpe fiction')
    expect(result.current!.copyright).toBe('© G. Barathieu')
  })

  it('never asks when the admin left the feature off', async () => {
    mocks.getAppSettings.mockResolvedValue({ 'app.dailyImage': 'false' })
    const { result } = renderHook(() => useDailyImage(), { wrapper })

    await waitFor(() => expect(mocks.getAppSettings).toHaveBeenCalled())
    expect(mocks.getDailyImage).not.toHaveBeenCalled()
    expect(result.current).toBeNull()
  })

  it('never asks when the caller does not want it', async () => {
    const { result } = renderHook(() => useDailyImage(false), { wrapper })

    await waitFor(() => expect(mocks.getAppSettings).toHaveBeenCalled())
    expect(mocks.getDailyImage).not.toHaveBeenCalled()
    expect(result.current).toBeNull()
  })

  it('stays null when the photo fails to load', async () => {
    mocks.getDailyImage.mockResolvedValue({ version: 'broken', title: 't', copyright: 'c' })
    const { result } = renderHook(() => useDailyImage(), { wrapper })

    await waitFor(() => expect(mocks.getDailyImage).toHaveBeenCalled())
    await act(async () => { await Promise.resolve() })
    expect(result.current).toBeNull()
  })

  it('follows the interface language', async () => {
    const { result } = renderHook(() => useDailyImage(), { wrapper })
    await waitFor(() => expect(result.current).not.toBeNull())

    await act(async () => { await i18next.changeLanguage('fr') })

    await waitFor(() => expect(mocks.getDailyImage).toHaveBeenCalledWith('fr', expect.anything()))
    await waitFor(() => expect(result.current!.src).toMatch(/lang=fr$/))
  })
})
```

- [ ] **Step 2: Run to verify they fail**

Run (depuis `src/frontend`) : `npx vitest run src/hooks/useDailyImage.test.tsx`
Expected: FAIL (`./useDailyImage` introuvable).

- [ ] **Step 3: Implement**

`lib/dailyImage.ts` :

```ts
import { configuredApiBase } from './runtimeConfig'
import type { Locale } from './locale'

/** `DailyImageInfo` on the API. */
export interface DailyImageInfo {
  version: string
  title: string
  copyright: string
}

export function dailyImageUrl(version: string, lang: Locale): string {
  return `${configuredApiBase}/api/AppSettings/daily-image/${encodeURIComponent(version)}?lang=${lang}`
}
```

`api.ts` — import en tête (`import type { DailyImageInfo } from './lib/dailyImage'` et `import type { Locale } from './lib/locale'`), puis après `getAppSettings` :

```ts
  getDailyImage: (lang: Locale, options?: RequestOptions) =>
    request<DailyImageInfo>('GET', `/api/AppSettings/daily-image?lang=${lang}`, undefined, options),
```

`useAppSettings.ts` — dans `APP_SETTING_KEYS`, `dailyImage: 'app.dailyImage',` (ordre alphabétique), puis :

```ts
/** Exactly 'true': an absent or malformed value never calls Bing. */
export function dailyImageOf(settings: AppSettings): boolean {
  return settings[APP_SETTING_KEYS.dailyImage] === 'true'
}
```

`usePreferences.ts` — dans `PREFERENCE_KEYS`, `dailyImage: 'ui.dailyImage',`, puis après `customPaletteOf` :

```ts
export type DailyImageStyle = 'none' | 'fullBleed' | 'postcard' | 'watermark'

export const DAILY_IMAGE_STYLES: readonly DailyImageStyle[] = ['none', 'fullBleed', 'postcard', 'watermark']

/** Falls back to 'none' — the plain empty pane — for an absent key or a value this build ignores. */
export function dailyImageStyleOf(preferences: Preferences): DailyImageStyle {
  const stored = preferences[PREFERENCE_KEYS.dailyImage] as DailyImageStyle
  return DAILY_IMAGE_STYLES.includes(stored) ? stored : 'none'
}
```

`hooks/useDailyImage.ts` :

```ts
import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { api } from '../api.js'
import { dailyImageUrl } from '../lib/dailyImage'
import type { Locale } from '../lib/locale'
import { dailyImageOf, useAppSettings } from './useAppSettings'

export interface DailyImage {
  src: string
  title: string
  copyright: string
}

/** Null until the photo itself has loaded: nothing ever shows a broken or half-drawn image. */
export function useDailyImage(wanted = true): DailyImage | null {
  const { i18n } = useTranslation()
  const lang: Locale = i18n.language === 'fr' ? 'fr' : 'en'
  const { data: settings } = useAppSettings()
  const enabled = wanted && !!settings && dailyImageOf(settings)

  const { data: info } = useQuery({
    queryKey: ['dailyImage', lang],
    queryFn: ({ signal }) => api.getDailyImage(lang, { signal }),
    enabled,
    staleTime: 60 * 60 * 1000,
    retry: false,
  })

  const src = enabled && info ? dailyImageUrl(info.version, lang) : null
  const loaded = useLoaded(src)

  return src && info && loaded ? { src, title: info.title, copyright: info.copyright } : null
}

function useLoaded(src: string | null): boolean {
  const [loaded, setLoaded] = useState<string | null>(null)

  useEffect(() => {
    if (!src) return
    let live = true
    const image = new Image()
    image.onload = () => { if (live) setLoaded(src) }
    image.src = src
    return () => { live = false }
  }, [src])

  return loaded === src
}
```

- [ ] **Step 4: Run to verify they pass**

Même commande, puis `npm run typecheck` et `npm run lint`. Expected: PASS, aucune erreur.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/lib/dailyImage.ts src/frontend/src/hooks/useDailyImage.ts src/frontend/src/hooks/useDailyImage.test.tsx src/frontend/src/api.ts src/frontend/src/hooks/useAppSettings.ts src/frontend/src/hooks/usePreferences.ts
git commit -q -F - <<'EOF'
Read the image of the day on the client, shown only once the photo has loaded

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 6: Page de connexion

**Files:**
- Create: `src/frontend/src/components/DailyImageCredit.tsx`
- Modify: `src/frontend/src/pages/LoginPage.tsx`
- Modify: `src/frontend/src/pages/LoginRoute.tsx`
- Modify: `src/frontend/src/index.css` (bloc `.page-center` et nouveau bloc « Image of the day »)
- Test: `src/frontend/src/pages/LoginPage.test.tsx`
- Test: `src/frontend/src/pages/LoginRoute.test.tsx` (neutraliser le hook)

**Interfaces:**
- Consumes: `DailyImage`, `useDailyImage` (tâche 5).
- Produces: `DailyImageCredit({ image, className }: { image: DailyImage; className: string })` ;
  `LoginPage` prend `backdrop?: DailyImage | null`. Classes CSS partagées : `.daily-credit`, `@keyframes daily-fade-in`.

- [ ] **Step 1: Write the failing tests**

Dans `LoginPage.test.tsx`, à la fin du `describe` :

```tsx
  it('keeps the bundled background when there is no image of the day', () => {
    const { container } = render(<LoginPage onLogin={vi.fn()} />)

    expect(container.querySelector('.login-daily')).toBeNull()
    expect(container.querySelector('.daily-credit')).toBeNull()
  })

  it('shows the image of the day behind the card, with its credit', () => {
    const backdrop = { src: '/api/AppSettings/daily-image/v1?lang=en', title: 'Poulpe fiction', copyright: '© G. Barathieu' }
    const { container } = render(<LoginPage onLogin={vi.fn()} backdrop={backdrop} />)

    const photo = container.querySelector('img.login-daily')
    expect(photo).toHaveAttribute('src', backdrop.src)
    expect(photo).toHaveAttribute('alt', '')
    expect(screen.getByText('Poulpe fiction')).toBeInTheDocument()
    expect(screen.getByText('© G. Barathieu')).toBeInTheDocument()
  })
```

Dans `LoginRoute.test.tsx`, à côté des autres `vi.mock` (le test n'a pas de `QueryClientProvider`) :

```tsx
vi.mock('../hooks/useDailyImage', () => ({ useDailyImage: () => null }))
```

- [ ] **Step 2: Run to verify they fail**

Run: `npx vitest run src/pages/LoginPage.test.tsx src/pages/LoginRoute.test.tsx`
Expected: FAIL sur `shows the image of the day…` (prop inconnue, aucune image).

- [ ] **Step 3: Implement**

`components/DailyImageCredit.tsx` :

```tsx
import type { DailyImage } from '../hooks/useDailyImage'

export default function DailyImageCredit({ image, className }: { image: DailyImage; className: string }) {
  return (
    <p className={`daily-credit ${className}`}>
      <strong>{image.title}</strong>
      <span>{image.copyright}</span>
    </p>
  )
}
```

`LoginPage.tsx` — props, imports et rendu :

```tsx
import DailyImageCredit from '../components/DailyImageCredit'
import type { DailyImage } from '../hooks/useDailyImage'

interface LoginPageProps {
  onLogin: () => void
  backdrop?: DailyImage | null
}

export default function LoginPage({ onLogin, backdrop }: LoginPageProps) {
```

```tsx
    <div className="page-center">
      {backdrop && <img className="login-daily" src={backdrop.src} alt="" />}
      <div className="card">
        {/* …inchangé… */}
      </div>
      {backdrop && <DailyImageCredit image={backdrop} className="login-daily-credit" />}
    </div>
```

`LoginRoute.tsx` :

```tsx
import { useDailyImage } from '../hooks/useDailyImage'
```

```tsx
  const backdrop = useDailyImage()
```

(appelé avant le `if (isLoggedIn) return …` : un hook ne se place pas après un retour conditionnel), puis
`<LoginPage backdrop={backdrop} onLogin={…} />`.

`index.css` — dans `.page-center`, ajouter `isolation: isolate;` ; puis après `.page-center::before` :

```css
/* The photo sits under the ::before veil: negative z-index inside .page-center's isolated stack. */
.login-daily {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
  z-index: -1;
  animation: daily-fade-in 200ms ease-out;
}

.login-daily-credit {
  position: absolute;
  right: 16px;
  bottom: 16px;
  z-index: var(--z-raised);
  text-align: right;
}

/* ── Image of the day ────────────────────────────────────── */

.daily-credit {
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
  max-width: min(520px, calc(100% - 32px));
  box-sizing: border-box;
  padding: 8px 12px;
  border-radius: var(--radius-md);
  background: color-mix(in oklab, var(--surface) 85%, transparent);
  color: var(--text);
  font-size: 12px;
  line-height: 1.4;
}

@keyframes daily-fade-in {
  from { opacity: 0; }
  to { opacity: 1; }
}

@media (prefers-reduced-motion: reduce) {
  .login-daily, .reader-daily-photo { animation: none; }
}
```

- [ ] **Step 4: Run to verify they pass**

Même commande, puis `npm run typecheck`. Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/components/DailyImageCredit.tsx src/frontend/src/pages/LoginPage.tsx src/frontend/src/pages/LoginRoute.tsx src/frontend/src/index.css src/frontend/src/pages/LoginPage.test.tsx src/frontend/src/pages/LoginRoute.test.tsx
git commit -q -F - <<'EOF'
Show the image of the day behind the login card, with its credit

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 7: Panneau de lecture vide (trois variantes)

**Files:**
- Create: `src/frontend/src/modules/mail/reader/ReaderEmpty.tsx`
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx` (la ligne `if (uid === null) return <p className="mail-empty">…`)
- Modify: `src/frontend/src/styles/mail.css` (après `.reader-fallback .mail-empty`)
- Test: `src/frontend/src/modules/mail/reader/ReaderEmpty.test.tsx`

**Interfaces:**
- Consumes: `useDailyImage(wanted)` (tâche 5), `dailyImageStyleOf`, `usePreferences`, `DailyImageCredit` (tâche 6).
- Produces: `ReaderEmpty()` — sans props.

- [ ] **Step 1: Write the failing tests**

```tsx
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { act, render, screen, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { createTestQueryClient } from '../../../test-utils'
import ReaderEmpty from './ReaderEmpty'

const mocks = vi.hoisted(() => ({ getPreferences: vi.fn(), getAppSettings: vi.fn(), getDailyImage: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))

class LoadingImage {
  onload: (() => void) | null = null
  set src(_: string) { queueMicrotask(() => this.onload?.()) }
}

function renderPane(style: string) {
  mocks.getPreferences.mockResolvedValue({ 'ui.dailyImage': style })
  const client = createTestQueryClient()
  const view = render(<QueryClientProvider client={client}><ReaderEmpty /></QueryClientProvider>)
  return { client, ...view }
}

beforeEach(() => {
  vi.stubGlobal('Image', LoadingImage)
  mocks.getAppSettings.mockResolvedValue({ 'app.dailyImage': 'true' })
  mocks.getDailyImage.mockResolvedValue({ version: 'v1', title: 'Poulpe fiction', copyright: '© G. Barathieu' })
})
afterEach(() => { vi.unstubAllGlobals(); vi.clearAllMocks() })

describe('ReaderEmpty', () => {
  it('stays the plain text, and asks for nothing, when the user chose none', async () => {
    const { container } = renderPane('none')

    await waitFor(() => expect(mocks.getPreferences).toHaveBeenCalled())
    expect(screen.getByText('Select a message')).toHaveClass('mail-empty')
    expect(container.querySelector('.reader-daily')).toBeNull()
    expect(mocks.getDailyImage).not.toHaveBeenCalled()
  })

  it.each([
    ['fullBleed', 'is-full-bleed'],
    ['postcard', 'is-postcard'],
    ['watermark', 'is-watermark'],
  ])('draws the %s variant with the photo and its credit', async (style, className) => {
    const { container } = renderPane(style)

    await waitFor(() => expect(container.querySelector('.reader-daily')).toHaveClass(className))
    expect(container.querySelector('img')).toHaveAttribute('src', expect.stringMatching(/daily-image\/v1\?lang=en$/))
    expect(screen.getByText('Select a message')).toBeInTheDocument()
    expect(screen.getByText('Poulpe fiction')).toBeInTheDocument()
    expect(screen.getByText('© G. Barathieu')).toBeInTheDocument()
  })

  it('stays the plain text while no image is available', async () => {
    mocks.getDailyImage.mockRejectedValue(new Error('404'))
    const { container } = renderPane('watermark')

    await waitFor(() => expect(mocks.getDailyImage).toHaveBeenCalled())
    expect(screen.getByText('Select a message')).toHaveClass('mail-empty')
    expect(container.querySelector('.reader-daily')).toBeNull()
  })

  it('falls back to the text when the admin switches the feature off', async () => {
    const { client, container } = renderPane('watermark')
    await waitFor(() => expect(container.querySelector('.reader-daily')).not.toBeNull())

    act(() => { client.setQueryData(['appSettings'], { 'app.dailyImage': 'false' }) })

    await waitFor(() => expect(container.querySelector('.reader-daily')).toBeNull())
    expect(screen.getByText('Select a message')).toHaveClass('mail-empty')
  })
})
```

- [ ] **Step 2: Run to verify they fail**

Run: `npx vitest run src/modules/mail/reader/ReaderEmpty.test.tsx`
Expected: FAIL (`./ReaderEmpty` introuvable).

- [ ] **Step 3: Implement**

`ReaderEmpty.tsx` :

```tsx
import { useTranslation } from 'react-i18next'
import DailyImageCredit from '../../../components/DailyImageCredit'
import { useDailyImage } from '../../../hooks/useDailyImage'
import { dailyImageStyleOf, usePreferences, type DailyImageStyle } from '../../../hooks/usePreferences'

const VARIANT_CLASS: Record<Exclude<DailyImageStyle, 'none'>, string> = {
  fullBleed: 'is-full-bleed',
  postcard: 'is-postcard',
  watermark: 'is-watermark',
}

/** The reading pane with no message open: the plain text, or the image of the day as the user chose. */
export default function ReaderEmpty() {
  const { t } = useTranslation('mail')
  const { data: preferences } = usePreferences()
  const style = preferences ? dailyImageStyleOf(preferences) : 'none'
  const image = useDailyImage(style !== 'none')
  const text = t('reader.selectMessage')

  if (style === 'none' || !image) return <p className="mail-empty">{text}</p>

  if (style === 'postcard') return (
    <div className="reader-daily is-postcard">
      <p className="reader-daily-label">{text}</p>
      <figure className="reader-daily-card">
        <img className="reader-daily-photo" src={image.src} alt="" />
        <figcaption>
          <strong>{image.title}</strong>
          <span>{image.copyright}</span>
        </figcaption>
      </figure>
    </div>
  )

  return (
    <div className={`reader-daily ${VARIANT_CLASS[style]}`}>
      <img className="reader-daily-photo" src={image.src} alt="" />
      <p className="reader-daily-label">{text}</p>
      <DailyImageCredit image={image} className="reader-daily-credit" />
    </div>
  )
}
```

`MessageReader.tsx` — `import ReaderEmpty from './ReaderEmpty'` puis :

```tsx
  if (uid === null) return <ReaderEmpty />
```

`mail.css`, après `.reader-fallback .mail-empty { … }` :

```css
/* ── Image of the day (empty reader) ─────────────────────── */

.reader-daily {
  position: relative;
  height: 100%;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 16px;
  padding: 24px;
  box-sizing: border-box;
}

.reader-daily > .reader-daily-photo {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.reader-daily-photo { animation: daily-fade-in 200ms ease-out; }

.reader-daily.is-watermark::after {
  content: '';
  position: absolute;
  inset: 0;
  background: color-mix(in oklab, var(--surface) 60%, transparent);
}

.reader-daily-label {
  position: relative;
  z-index: var(--z-raised);
  margin: 0;
  color: var(--text);
}

.reader-daily.is-full-bleed .reader-daily-label {
  padding: 8px 16px;
  border-radius: 999px;
  background: color-mix(in oklab, var(--surface) 82%, transparent);
}

.reader-daily-credit {
  position: absolute;
  bottom: 16px;
  z-index: var(--z-raised);
}

.reader-daily.is-full-bleed .reader-daily-credit { left: 16px; }
.reader-daily.is-watermark .reader-daily-credit { right: 16px; text-align: right; }

/* Sized on the pane, not the window: the bottom reading pane is short. */
.reader-daily.is-postcard {
  container-type: size;
  background: var(--surface-sunken);
}

.reader-daily.is-postcard .reader-daily-label { color: var(--text-muted); }

.reader-daily-card {
  margin: 0;
  width: 100%;
  max-width: 560px;
  box-sizing: border-box;
  padding: 10px 10px 14px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
}

.reader-daily-card .reader-daily-photo {
  display: block;
  width: 100%;
  aspect-ratio: 16 / 9;
  max-height: 60cqh;
  object-fit: cover;
  border-radius: var(--radius-md);
}

.reader-daily-card figcaption {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding: 0 4px;
}

.reader-daily-card figcaption span {
  color: var(--text-muted);
  font-size: 13px;
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `npx vitest run src/modules/mail/reader/ReaderEmpty.test.tsx src/modules/mail/reader/MessageReader.test.tsx`,
puis `npm run typecheck` et `npm run lint`.
Expected: PASS. `MessageReader.test.tsx` ne mocke pas `getAppSettings` : la requête échoue en silence et le
texte « Select a message » reste celui de `.mail-empty`, ce que ses tests attendent déjà. Si un test y
vérifie le texte par `toHaveClass('mail-empty')` et rougit, ajouter `getAppSettings: vi.fn().mockResolvedValue({})`
à son mock plutôt que de toucher `ReaderEmpty`.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/reader/ReaderEmpty.tsx src/frontend/src/modules/mail/reader/ReaderEmpty.test.tsx src/frontend/src/modules/mail/reader/MessageReader.tsx src/frontend/src/styles/mail.css
git commit -q -F - <<'EOF'
Offer the image of the day in the empty reading pane: full bleed, postcard or watermark

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 8: Réglages — Apparence et Administration

**Files:**
- Create: `src/frontend/src/modules/settings/appearance/DailyImageSection.tsx`
- Create: `src/frontend/src/modules/settings/admin/DailyImageSection.tsx`
- Modify: `src/frontend/src/modules/settings/appearance/AppearancePage.tsx` (après la section Palette)
- Modify: `src/frontend/src/modules/settings/admin/ApplicationTab.tsx` (après `<LogoSection …/>`)
- Modify: `src/frontend/src/locales/en/settings.json`, `src/frontend/src/locales/fr/settings.json` (bloc `appearance`)
- Modify: `src/frontend/src/locales/en/admin.json`, `src/frontend/src/locales/fr/admin.json` (bloc `application`)
- Test: `src/frontend/src/modules/settings/appearance/DailyImageSection.test.tsx`
- Test: `src/frontend/src/modules/settings/admin/DailyImageSection.test.tsx`

**Interfaces:**
- Consumes: `useAppSettings`, `useSetAppSetting`, `dailyImageOf`, `APP_SETTING_KEYS.dailyImage`,
  `usePreferences`, `useSetPreference`, `DAILY_IMAGE_STYLES`, `dailyImageStyleOf`, `PREFERENCE_KEYS.dailyImage` (tâches 5).
- Produces: `DailyImageSection()` (Apparence, sans props) ; `DailyImageSection({ addToast }: { addToast: AddToast })` (admin).

- [ ] **Step 1: Add the strings**

`en/settings.json`, dans `appearance` :

```json
    "dailyImage": {
      "heading": "Image of the day",
      "intro": "Shown in the reading pane when no message is open.",
      "none": "None",
      "fullBleed": "Full bleed",
      "postcard": "Postcard",
      "watermark": "Watermark"
    }
```

`fr/settings.json`, dans `appearance` :

```json
    "dailyImage": {
      "heading": "Image du jour",
      "intro": "Affichée dans le panneau de lecture quand aucun message n’est ouvert.",
      "none": "Aucune",
      "fullBleed": "Plein cadre",
      "postcard": "Carte postale",
      "watermark": "Filigrane"
    }
```

`en/admin.json`, dans `application` :

```json
    "dailyImage": "Image of the day (Bing)",
    "dailyImageIntro": "Shows the photo of the day from Bing behind the login page, and lets each user show it in their reading pane. The server then contacts Bing once a day; switched off, it never does.",
    "dailyImageOn": "The image of the day is on",
    "dailyImageOff": "The image of the day is off"
```

`fr/admin.json`, dans `application` :

```json
    "dailyImage": "Image du jour (Bing)",
    "dailyImageIntro": "Affiche la photo du jour de Bing derrière la page de connexion, et permet à chacun de l’afficher dans son panneau de lecture. Le serveur contacte alors Bing une fois par jour ; désactivée, jamais.",
    "dailyImageOn": "L’image du jour est activée",
    "dailyImageOff": "L’image du jour est désactivée"
```

Dans les deux chaînes françaises, l'espace avant `;` est une insécable U+00A0, comme partout dans `locales/fr`.
L'outil Edit l'écrit en espace ordinaire : la remettre en PowerShell (mémoire « Insécable et outil Edit »).

- [ ] **Step 2: Write the failing tests**

`appearance/DailyImageSection.test.tsx` :

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { createTestQueryClient, setupUser } from '../../../test-utils'
import DailyImageSection from './DailyImageSection'

const mocks = vi.hoisted(() => ({ getPreferences: vi.fn(), setPreference: vi.fn(), getAppSettings: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))

function renderSection(enabled: boolean, style = 'none') {
  mocks.getAppSettings.mockResolvedValue({ 'app.dailyImage': String(enabled) })
  mocks.getPreferences.mockResolvedValue({ 'ui.dailyImage': style })
  mocks.setPreference.mockResolvedValue(null)
  return render(<QueryClientProvider client={createTestQueryClient()}><DailyImageSection /></QueryClientProvider>)
}

beforeEach(() => vi.clearAllMocks())

describe('DailyImageSection (Appearance)', () => {
  it('offers the four choices, the stored one checked', async () => {
    renderSection(true, 'postcard')

    await waitFor(() => expect(screen.getByRole('radio', { name: 'Postcard' })).toBeChecked())
    expect(screen.getAllByRole('radio').map(r => r.getAttribute('value')))
      .toEqual(['none', 'fullBleed', 'postcard', 'watermark'])
    expect(screen.getByText('Shown in the reading pane when no message is open.')).toBeInTheDocument()
  })

  it('stores the choice on the account', async () => {
    const user = setupUser()
    renderSection(true)

    await user.click(await screen.findByRole('radio', { name: 'Watermark' }))

    expect(mocks.setPreference).toHaveBeenCalledWith('ui.dailyImage', 'watermark')
  })

  it('hides the section while the admin left the feature off, keeping the stored choice', async () => {
    renderSection(false, 'watermark')

    await waitFor(() => expect(mocks.getAppSettings).toHaveBeenCalled())
    expect(screen.queryByRole('radiogroup')).toBeNull()
    expect(mocks.setPreference).not.toHaveBeenCalled()
  })
})
```

`admin/DailyImageSection.test.tsx` :

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { createTestQueryClient, setupUser } from '../../../test-utils'
import DailyImageSection from './DailyImageSection'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn(), setAppSetting: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))

beforeEach(() => {
  vi.clearAllMocks()
  mocks.getAppSettings.mockResolvedValue({ 'app.dailyImage': 'false' })
  mocks.setAppSetting.mockResolvedValue(null)
})

describe('DailyImageSection (Administration)', () => {
  it('switches the feature on and says so', async () => {
    const user = setupUser()
    const addToast = vi.fn()
    render(<QueryClientProvider client={createTestQueryClient()}><DailyImageSection addToast={addToast} /></QueryClientProvider>)

    const toggle = await screen.findByRole('checkbox', { name: 'Image of the day (Bing)' })
    expect(toggle).not.toBeChecked()
    await user.click(toggle)

    expect(mocks.setAppSetting).toHaveBeenCalledWith('app.dailyImage', 'true')
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('The image of the day is on'))
  })

  it('reports a refused change', async () => {
    const user = setupUser()
    const addToast = vi.fn()
    mocks.setAppSetting.mockRejectedValue(new Error('boom'))
    render(<QueryClientProvider client={createTestQueryClient()}><DailyImageSection addToast={addToast} /></QueryClientProvider>)

    await user.click(await screen.findByRole('checkbox', { name: 'Image of the day (Bing)' }))

    await waitFor(() => expect(addToast).toHaveBeenCalledWith(expect.any(String), 'error'))
  })
})
```

- [ ] **Step 3: Run to verify they fail**

Run: `npx vitest run src/modules/settings/appearance/DailyImageSection.test.tsx src/modules/settings/admin/DailyImageSection.test.tsx`
Expected: FAIL (modules introuvables).

- [ ] **Step 4: Implement**

`appearance/DailyImageSection.tsx` :

```tsx
import { useTranslation } from 'react-i18next'
import { dailyImageOf, useAppSettings } from '../../../hooks/useAppSettings'
import {
  DAILY_IMAGE_STYLES, PREFERENCE_KEYS, dailyImageStyleOf, usePreferences, useSetPreference,
} from '../../../hooks/usePreferences'

/** Shown only while the admin offers the image of the day; a choice stored before stays stored. */
export default function DailyImageSection() {
  const { t } = useTranslation('settings')
  const { data: settings } = useAppSettings()
  const { data: preferences } = usePreferences()
  const setPreference = useSetPreference()

  if (!settings || !dailyImageOf(settings) || !preferences) return null
  const current = dailyImageStyleOf(preferences)

  return (
    <section className="account-section">
      <h2 id="daily-image-heading">{t('appearance.dailyImage.heading')}</h2>
      <p className="svc-account-section-intro">{t('appearance.dailyImage.intro')}</p>
      <div className="seg" role="radiogroup" aria-labelledby="daily-image-heading">
        {DAILY_IMAGE_STYLES.map(style => (
          <label key={style}>
            <input
              type="radio"
              name="daily-image"
              value={style}
              checked={current === style}
              onChange={() => setPreference.mutate({ key: PREFERENCE_KEYS.dailyImage, value: style })}
            />
            {t(`appearance.dailyImage.${style}`)}
          </label>
        ))}
      </div>
    </section>
  )
}
```

`AppearancePage.tsx` — `import DailyImageSection from './DailyImageSection'`, puis juste après la `</section>` de la palette :

```tsx
      <DailyImageSection />
```

`admin/DailyImageSection.tsx` :

```tsx
import { useTranslation } from 'react-i18next'
import { APP_SETTING_KEYS, dailyImageOf, useAppSettings, useSetAppSetting } from '../../../hooks/useAppSettings'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import type { AddToast } from '../../../hooks/useToasts'

export default function DailyImageSection({ addToast }: { addToast: AddToast }) {
  const { t } = useTranslation('admin')
  const { data: settings } = useAppSettings()
  const setSetting = useSetAppSetting()
  if (!settings) return null

  async function toggle(on: boolean) {
    try {
      await setSetting.mutateAsync({ key: APP_SETTING_KEYS.dailyImage, value: String(on) })
      addToast(t(on ? 'application.dailyImageOn' : 'application.dailyImageOff'))
    } catch (error) {
      addToast(apiErrorMessage(error, t('application.saveFailed')), 'error')
    }
  }

  return (
    <div className="svc-account-section">
      <h2 className="svc-account-section-title">{t('application.dailyImage')}</h2>
      <p className="svc-account-section-intro">{t('application.dailyImageIntro')}</p>
      <div className="field-h is-setting">
        <label htmlFor="app-daily-image">{t('application.dailyImage')}</label>
        <label className="toggle-switch">
          <input
            id="app-daily-image"
            type="checkbox"
            checked={dailyImageOf(settings)}
            disabled={setSetting.isPending}
            aria-label={t('application.dailyImage')}
            onChange={event => void toggle(event.target.checked)}
          />
          <span className="toggle-track" />
        </label>
      </div>
    </div>
  )
}
```

`ApplicationTab.tsx` — `import DailyImageSection from './DailyImageSection'`, puis après `<LogoSection addToast={addToast} />` :

```tsx
      <DailyImageSection addToast={addToast} />
```

- [ ] **Step 5: Run to verify they pass**

Run : les deux fichiers de test, puis `npx vitest run src/modules/settings src/locales`, `npm run typecheck`, `npm run lint`.
Expected: PASS (dont `parity.test.ts`). `AppearancePage.test.tsx` ne mocke pas `getAppSettings` : la section
reste masquée et ses tests ne changent pas ; s'il rougit sur une erreur `getAppSettings is not a function`,
ajouter `getAppSettings: vi.fn().mockResolvedValue({})` à son objet `mocks`.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/modules/settings src/frontend/src/locales
git commit -q -F - <<'EOF'
Let the admin switch on the image of the day and each user pick its style

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 9: Documentation et recette dans un vrai navigateur

**Files:**
- Modify: `README.md` (ligne « **The interface** — … », l. 55)
- Modify: `install/README.md` (près de la mention du pare-feu, l. 465 : connexion sortante vers `www.bing.com`)

- [ ] **Step 1: Docs**

`README.md` — compléter la phrase de l'interface :

```markdown
**The interface** — English and French, light and dark, eight colour palettes, an optional image of the
day from Bing behind the login page and the reading pane, installable as an application, and usable on a phone.
```

`install/README.md` — à côté du paragraphe sur le pare-feu, une phrase simple (mémoire « Docs : une seule
installation » : pas de serveur du propriétaire, des mots simples) :

```markdown
If you switch on **Image of the day** (Administration › Application), the server downloads one photo a day
from `https://www.bing.com`: let it reach that address on port 443. Switched off, it never does.
```

- [ ] **Step 2: Full suites**

Run : `dotnet test src/scotty.microservice/scotty.microservice.Tests`, puis depuis `src/frontend` :
`npm test`, `npm run typecheck`, `npm run lint`, `npm run build`.
Expected: tout vert (hors l'instabilité classée « chunk lazy », mémoire « Instabilité de suite classée »).

- [ ] **Step 3: Recette navigateur** (mémoire « Géométrie UI : mesurer, pas raisonner » — jsdom ne voit aucune mise en page)

Lancer l'API et le front en local (tout local : mémoire « CORS de l'API dev »), puis vérifier et mesurer :
1. fonction coupée : page de connexion et panneau vide inchangés, aucune requête vers `/daily-image` dans l'onglet Réseau ;
2. activée dans Administration › Application : page de connexion = planche « Connexion » (photo, voile, carte lisible, crédit en bas à droite) ;
3. Apparence › Image of the day : les quatre choix ; chacun conforme à sa planche A/B/C, en clair **et** en sombre, sur la palette Night et une autre ;
4. volet de lecture en bas (`mail.readingPane = bottom`) : la carte postale tient dans la hauteur, sans débordement ;
5. téléphone (390 px) : page de connexion lisible, crédit sans débordement horizontal ;
6. interface en français : légende française (marché `fr-FR`).

Noter tout écart visible ; un mineur visible se corrige (mémoire « Code state of the art »).

- [ ] **Step 4: Commit**

```bash
git add README.md install/README.md
git commit -q -F - <<'EOF'
Document the image of the day and the outbound call it makes

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```
