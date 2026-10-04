# Logo personnalisé — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** l'administrateur remplace le logo Scotty par celui de son organisation, affiché partout où le logo apparaît.

**Architecture:** le navigateur de l'admin rend trois PNG carrés (32, 192, 512) ; l'API les vérifie octet par
octet (signature, en-tête IHDR, poids) sans jamais les décoder, les range dans `app_logo` et les sert
anonymement avec un cache d'un an. Le client lit une version (`app.logo`) dans `GET /api/AppSettings`,
construit des adresses versionnées, et un seul hook (`useAppLogo`) alimente la barre du haut, le favicon,
les notifications, le manifeste et l'icône iOS.

**Tech Stack:** ASP.NET Core 10, EF Core (Pomelo / InMemory pour les tests), xUnit + Moq ; React 19,
TanStack Query, Vitest + Testing Library, i18next.

**Spec:** `docs/superpowers/2026-10-04-webmail-custom-logo-design.md`

## Global Constraints

- Tailles : exactement **32, 192, 512** px, carrés, PNG.
- Plafonds de poids : **32 px : 16 Ko (16 384 o) ; 192 px : 256 Ko (262 144 o) ; 512 px : 1 Mo (1 048 576 o)** ; requête PUT : 2 Mo.
- Le serveur ne décode **jamais** l'image : il lit la signature PNG et l'en-tête IHDR, rien d'autre.
- Écriture (`PUT`, `DELETE`) réservée à `AdminRequirement.PolicyName` ; lecture du logo et des réglages anonyme.
- `GET` du logo : `Content-Type: image/png`, `Cache-Control: public, max-age=31536000, immutable` (`nosniff` est déjà posé globalement par `UseSecurityHeaders`).
- Clé de version : `app.logo` ; valeur `yyyyMMdd'T'HHmmssfff` (UTC, culture invariante) ou chaîne vide.
- Logo affiché tel qu'envoyé : **aucun** `border-radius` sur `.topbar-logo`.
- Libellés UI en anglais, traduction française dans `locales/fr` (apostrophe typographique `’`, insécable U+00A0 avant `:` `?` `!` — voir la mémoire « Insécable et outil Edit »).
- Pas de nouvelle dépendance NuGet ni npm.
- Commentaires : seulement s'ils disent un pourquoi non évident, 3 lignes max.
- Commits : deux lignes max, terminés par `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`, via heredoc `git commit -F -` (jamais de here-string PowerShell). Ne pas pousser.
- Avant chaque commit côté API : `git checkout -- src/scotty.microservice/ApiDocumentation.xml` puis ne réintégrer que les blocs des nouvelles méthodes (mémoire « Dérive d'ApiDocumentation.xml »).

## Review Focus

1. **SVG sans `width`/`height`** (seulement un `viewBox`) : il doit sortir net et non déformé dans les trois tailles — Firefox lui donne une taille naturelle nulle. → test dans la tâche 6.
2. **Logo remplacé alors qu'une pastille « non lu » est affichée ou en cours de dessin** : la pastille doit se redessiner sur le nouveau logo, jamais sur l'ancien. → test dans la tâche 5.
3. **Image très détaillée** dont le PNG 512 dépasse 1 Mo : refus lisible côté client avant tout envoi, pas une erreur 400 brute. → test dans la tâche 6.
4. **Logo rectangulaire très allongé** (ex. 1000×100) : centré sans déformation, marges transparentes. → test dans la tâche 6.
5. **Fichier PNG valide mais pas carré, ou dimension menteuse** envoyé directement à l'API (client contourné) : 400. → test dans la tâche 1.

---

### Task 1: Validateur de logo côté API

**Files:**
- Create: `src/scotty.microservice/Models/AppLogo.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Models/AppLogoTests.cs`

**Interfaces:**
- Produces: `AppLogo.Sizes` (`IReadOnlyList<int>` = 32, 192, 512), `AppLogo.MaxBytes(int size) : int`,
  `AppLogo.MaxRequestBytes` (`const int`), `AppLogo.VersionKey` (`"app.logo"`),
  `AppLogo.Check(int size, byte[] png) : CSharpFunctionalExtensions.Result`, `AppLogo.Version(DateTime) : string`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Buffers.Binary;
using weesky.Scotty.Microservice.Models;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Models;

public sealed class AppLogoTests
{
    /// <summary>A PNG signature and an IHDR chunk header: all the validator ever reads.</summary>
    internal static byte[] Png(int width, int height, int length = 64)
    {
        var bytes = new byte[length];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
        return bytes;
    }

    [Theory]
    [InlineData(32)]
    [InlineData(192)]
    [InlineData(512)]
    public void Check_AcceptsASquarePngOfTheAnnouncedSize(int size) =>
        Assert.True(AppLogo.Check(size, Png(size, size)).IsSuccess);

    [Fact]
    public void Check_RefusesAnUnknownSize() => Assert.True(AppLogo.Check(64, Png(64, 64)).IsFailure);

    [Fact]
    public void Check_RefusesAJpeg()
    {
        var jpeg = Png(192, 192);
        jpeg[0] = 0xFF; jpeg[1] = 0xD8;
        Assert.True(AppLogo.Check(192, jpeg).IsFailure);
    }

    [Fact]
    public void Check_RefusesATruncatedFile() => Assert.True(AppLogo.Check(32, Png(32, 32)[..20]).IsFailure);

    [Fact]
    public void Check_RefusesAPngWhoseFirstChunkIsNotIhdr()
    {
        var png = Png(32, 32);
        "tEXt"u8.CopyTo(png.AsSpan(12));
        Assert.True(AppLogo.Check(32, png).IsFailure);
    }

    [Theory]
    [InlineData(192, 191)]
    [InlineData(512, 192)]
    [InlineData(1000, 1000)]
    public void Check_RefusesAWrongOrNonSquareDimension(int width, int height) =>
        Assert.True(AppLogo.Check(192, Png(width, height)).IsFailure);

    [Theory]
    [InlineData(32, 16 * 1024)]
    [InlineData(192, 256 * 1024)]
    [InlineData(512, 1024 * 1024)]
    public void Check_AcceptsTheCapAndRefusesOneByteMore(int size, int cap)
    {
        Assert.True(AppLogo.Check(size, Png(size, size, cap)).IsSuccess);
        Assert.True(AppLogo.Check(size, Png(size, size, cap + 1)).IsFailure);
    }

    [Fact]
    public void Version_IsCompactUtcToTheMillisecond() =>
        Assert.Equal("20261004T101500123",
            AppLogo.Version(new DateTime(2026, 10, 4, 10, 15, 0, 123, DateTimeKind.Utc)));
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~AppLogoTests"`
Expected: build error, `AppLogo` does not exist.

- [ ] **Step 3: Implement**

```csharp
using System.Buffers.Binary;
using System.Globalization;
using CSharpFunctionalExtensions;

namespace weesky.Scotty.Microservice.Models;

/// <summary>
/// The instance logo's three renditions. The admin's browser draws them; the server reads the
/// PNG signature and the IHDR header and never decodes a pixel.
/// </summary>
public static class AppLogo
{
    public const string VersionKey = "app.logo";
    public const int MaxRequestBytes = 2 * 1024 * 1024;

    public static IReadOnlyList<int> Sizes { get; } = [32, 192, 512];

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static int MaxBytes(int size) => size switch
    {
        32 => 16 * 1024,
        192 => 256 * 1024,
        512 => 1024 * 1024,
        _ => 0,
    };

    public static Result Check(int size, byte[] png)
    {
        if (!Sizes.Contains(size)) return Result.Failure($"{size} px is not a logo size");
        if (png.Length > MaxBytes(size)) return Result.Failure($"The {size} px logo is over {MaxBytes(size) / 1024} KB");
        if (png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(Signature) || !png.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            return Result.Failure($"The {size} px logo is not a PNG");

        var width = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4));
        return width == size && height == size
            ? Result.Success()
            : Result.Failure($"The {size} px logo is {width}x{height}");
    }

    public static string Version(DateTime updatedAt) =>
        updatedAt.ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~AppLogoTests"`
Expected: PASS (13 tests).

- [ ] **Step 5: Commit**

```bash
git add src/scotty.microservice/Models/AppLogo.cs src/scotty.microservice/scotty.microservice.Tests/Models/AppLogoTests.cs
git commit -F - <<'EOF'
Check an uploaded logo's PNG header without decoding it

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: Table `app_logo` et son store

**Files:**
- Create: `src/scotty.microservice/Data/Preferences/AppLogoImage.cs`
- Create: `src/scotty.microservice/Repositories/IAppLogoStore.cs`
- Create: `src/scotty.microservice/Repositories/AppLogoStore.cs`
- Modify: `src/scotty.microservice/Data/Preferences/PreferencesDbContext.cs` (clé ~ligne 56, `DbSet` ~ligne 231)
- Modify: `src/scotty.microservice/Configuration/ApplicationServicesConfiguration.cs:158`
- Modify: `install/install.sql` (table après `app_settings` ~ligne 72, clé après la clé de `app_settings` ~ligne 390)
- Create: `docs/operations/app-logo-migration.md`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Repositories/AppLogoStoreTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public interface IAppLogoStore
  {
      Task<byte[]?> GetImageAsync(int size, CancellationToken cancellationToken);
      Task<DateTime?> GetUpdatedAtAsync(CancellationToken cancellationToken);
      Task ReplaceAsync(IReadOnlyDictionary<int, byte[]> images, CancellationToken cancellationToken);
      Task ClearAsync(CancellationToken cancellationToken);
  }
  ```

- [ ] **Step 1: Write the failing tests**

```csharp
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Repositories;

public sealed class AppLogoStoreTests
{
    private static AppLogoStore CreateStore(string dbName) => new(new PreferencesTestDbContext(dbName));

    private static Dictionary<int, byte[]> Images(byte mark) =>
        new() { [32] = [mark, 32], [192] = [mark, 192], [512] = [mark, 2] };

    [Fact]
    public async Task Empty_HasNoImageAndNoDate()
    {
        var store = CreateStore(nameof(Empty_HasNoImageAndNoDate));

        Assert.Null(await store.GetImageAsync(192, CancellationToken.None));
        Assert.Null(await store.GetUpdatedAtAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Replace_StoresEverySize()
    {
        var store = CreateStore(nameof(Replace_StoresEverySize));

        await store.ReplaceAsync(Images(1), CancellationToken.None);

        Assert.Equal([1, 32], await store.GetImageAsync(32, CancellationToken.None));
        Assert.Equal([1, 192], await store.GetImageAsync(192, CancellationToken.None));
        Assert.Equal([1, 2], await store.GetImageAsync(512, CancellationToken.None));
    }

    [Fact]
    public async Task Replace_OverwritesThePreviousLogoAndMovesTheDate()
    {
        var store = CreateStore(nameof(Replace_OverwritesThePreviousLogoAndMovesTheDate));
        await store.ReplaceAsync(Images(1), CancellationToken.None);
        var first = await store.GetUpdatedAtAsync(CancellationToken.None);
        await Task.Delay(5);

        await store.ReplaceAsync(Images(2), CancellationToken.None);

        Assert.Equal([2, 192], await store.GetImageAsync(192, CancellationToken.None));
        Assert.True(await store.GetUpdatedAtAsync(CancellationToken.None) > first);
    }

    [Fact]
    public async Task Clear_RemovesEverySize()
    {
        var store = CreateStore(nameof(Clear_RemovesEverySize));
        await store.ReplaceAsync(Images(1), CancellationToken.None);

        await store.ClearAsync(CancellationToken.None);

        Assert.Null(await store.GetImageAsync(32, CancellationToken.None));
        Assert.Null(await store.GetUpdatedAtAsync(CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~AppLogoStoreTests"`
Expected: build error, `AppLogoStore` does not exist.

- [ ] **Step 3: Implement the entity, the store and the context entries**

`Data/Preferences/AppLogoImage.cs` :

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Scotty.Microservice.Data.Preferences;

/// <summary>One rendition of the instance logo; no row at all means the bundled Scotty logo.</summary>
[Table("app_logo")]
public sealed class AppLogoImage
{
    [Column("size")]
    public short Size { get; set; }

    [Column("image")]
    public byte[] Image { get; set; } = [];

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
```

`Repositories/IAppLogoStore.cs` : l'interface ci-dessus, avec ce résumé :

```csharp
/// <summary>The instance logo's renditions. It checks nothing: the caller validates every image first.</summary>
```

`Repositories/AppLogoStore.cs` :

```csharp
using Microsoft.EntityFrameworkCore;
using weesky.Scotty.Microservice.Data.Preferences;

namespace weesky.Scotty.Microservice.Repositories;

internal sealed class AppLogoStore(PreferencesDbContext context)
    : ScopedStore<AppLogoImage>(context), IAppLogoStore
{
    public async Task<byte[]?> GetImageAsync(int size, CancellationToken cancellationToken)
        => await Untracked.Where(l => l.Size == size).Select(l => l.Image).FirstOrDefaultAsync(cancellationToken);

    public async Task<DateTime?> GetUpdatedAtAsync(CancellationToken cancellationToken)
        => await Untracked.MaxAsync(l => (DateTime?)l.UpdatedAt, cancellationToken);

    // Updated in place rather than deleted and re-added: one SaveChanges is one transaction, and
    // EF refuses to track a new row under the key of one it is deleting.
    public async Task ReplaceAsync(IReadOnlyDictionary<int, byte[]> images, CancellationToken cancellationToken)
    {
        var rows = await Set.ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var (size, image) in images)
        {
            var row = rows.FirstOrDefault(r => r.Size == size);
            if (row is null) Set.Add(new AppLogoImage { Size = (short)size, Image = image, UpdatedAt = now });
            else { row.Image = image; row.UpdatedAt = now; }
        }
        await Context.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await RemoveWhereAsync(Set, _ => true, cancellationToken);
        await Context.SaveChangesAsync(cancellationToken);
    }
}
```

`PreferencesDbContext.cs`, à côté de `modelBuilder.Entity<AppSetting>().HasKey(...)` :

```csharp
        modelBuilder.Entity<AppLogoImage>().HasKey(l => l.Size);
        modelBuilder.Entity<AppLogoImage>().Property(l => l.Size).ValueGeneratedNever();
```

et à côté de `public DbSet<AppSetting> AppSettings { get; set; }` (même style de résumé XML que les voisins) :

```csharp
    public DbSet<AppLogoImage> AppLogoImages { get; set; }
```

`ApplicationServicesConfiguration.cs`, sous `services.AddScoped<IAppSettingStore, AppSettingStore>();` :

```csharp
        services.AddScoped<IAppLogoStore, AppLogoStore>();
```

- [ ] **Step 4: Schéma SQL**

`install/install.sql`, après le `CREATE TABLE` de `app_settings` :

```sql
CREATE TABLE IF NOT EXISTS `app_logo` (
  `size` smallint NOT NULL COMMENT '32, 192 or 512',
  `image` mediumblob NOT NULL COMMENT 'PNG, checked by the API, never decoded',
  `updated_at` datetime(3) NOT NULL COMMENT 'UTC; set by the code, never by the schema'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

et après `ALTER TABLE \`app_settings\` ADD PRIMARY KEY (\`setting_key\`);` :

```sql
ALTER TABLE `app_logo`
  ADD PRIMARY KEY (`size`);
```

`datetime(3)` : la version porte les millisecondes ; un `datetime` nu les tronquerait et deux envois
dans la même seconde garderaient la même adresse, donc le même cache.

`docs/operations/app-logo-migration.md` :

````markdown
# Logo personnalisé — migration du schéma

```sql
CREATE TABLE IF NOT EXISTS app_logo (
  size smallint NOT NULL,
  image mediumblob NOT NULL,
  updated_at datetime(3) NOT NULL,
  PRIMARY KEY (size)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

À rejouer à la main sur chaque base — prod et dev — avant de déployer le code qui la lit : sans
elle, `GET /api/AppSettings` échoue, et avec lui la page de login.

Vérifier ensuite :

```sql
SHOW CREATE TABLE app_logo;
```
````

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~AppLogoStoreTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git checkout -- src/scotty.microservice/ApiDocumentation.xml
git add src/scotty.microservice/Data/Preferences/AppLogoImage.cs src/scotty.microservice/Repositories/IAppLogoStore.cs src/scotty.microservice/Repositories/AppLogoStore.cs src/scotty.microservice/Data/Preferences/PreferencesDbContext.cs src/scotty.microservice/Configuration/ApplicationServicesConfiguration.cs install/install.sql docs/operations/app-logo-migration.md src/scotty.microservice/scotty.microservice.Tests/Repositories/AppLogoStoreTests.cs
git commit -F - <<'EOF'
Store the instance logo's three renditions in app_logo

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 3: Routes du logo et clé `app.logo`

**Files:**
- Modify: `src/scotty.microservice/Controllers/AppSettingsController.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Controllers/AppSettingsControllerTests.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Controllers/AppSettingsLogoTests.cs` (nouveau)

**Interfaces:**
- Consumes: `AppLogo.*` (tâche 1), `IAppLogoStore` (tâche 2).
- Produces (HTTP) :
  - `GET /api/AppSettings` → dictionnaire contenant en plus `"app.logo"` (`""` ou `AppLogo.Version(...)`).
  - `PUT /api/AppSettings/logo`, multipart, champs fichiers `logo32`, `logo192`, `logo512` → 204 / 400.
  - `DELETE /api/AppSettings/logo` → 204.
  - `GET /api/AppSettings/logo/{size:int}` → 200 `image/png` / 404.

- [ ] **Step 1: Adapter le constructeur dans les tests existants**

Dans `AppSettingsControllerTests.cs`, ajouter le champ et construire avec les deux stores :

```csharp
    private readonly Mock<IAppLogoStore> _logos = new();
    // ...
        return new AppSettingsController(_store.Object, _logos.Object);
```

et ajouter au même fichier :

```csharp
    [Fact]
    public async Task Get_AnswersAnEmptyLogoVersionWhenNoLogoIsStored()
    {
        var result = await CreateController().GetAppSettings(CancellationToken.None);

        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(string.Empty, values[AppLogo.VersionKey]);
    }

    [Fact]
    public async Task Get_AnswersTheLogoVersion()
    {
        var controller = CreateController();
        _logos.Setup(l => l.GetUpdatedAtAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new DateTime(2026, 10, 4, 10, 15, 0, 123, DateTimeKind.Utc));

        var result = await controller.GetAppSettings(CancellationToken.None);

        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(
            Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("20261004T101500123", values[AppLogo.VersionKey]);
    }

    // The version is computed: the generic PUT must not let anyone forge it.
    [Fact]
    public async Task Set_RefusesTheLogoVersionKey()
    {
        var result = await CreateController().SetAppSetting(
            new SetAppSettingRequest { Key = AppLogo.VersionKey, Value = "20261004T101500123" },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }
```

(Vérifier le type exact renvoyé par `BadRequestEnveloppe` dans `ApiBaseController.cs:41` et l'aligner sur les assertions 400 déjà présentes dans ce fichier — mémoire « Assert.IsType exact type check ».)

- [ ] **Step 2: Écrire les tests des routes**

`AppSettingsLogoTests.cs` :

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Scotty.Microservice.Authentication.Authorization;
using weesky.Scotty.Microservice.Controllers;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Tests.Models;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Controllers;

public sealed class AppSettingsLogoTests
{
    private readonly Mock<IAppSettingStore> _store = new();
    private readonly Mock<IAppLogoStore> _logos = new();

    private AppSettingsController CreateController() => new(_store.Object, _logos.Object)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private static FormFile File(byte[] bytes, string name) =>
        new(new MemoryStream(bytes), 0, bytes.Length, name, $"{name}.png");

    private static (IFormFile, IFormFile, IFormFile) Valid() => (
        File(AppLogoTests.Png(32, 32), "logo32"),
        File(AppLogoTests.Png(192, 192), "logo192"),
        File(AppLogoTests.Png(512, 512), "logo512"));

    [Fact]
    public async Task Set_StoresTheThreeRenditionsTogether()
    {
        var (a, b, c) = Valid();

        var result = await CreateController().SetLogo(a, b, c, CancellationToken.None);

        Assert.IsType<StatusCodeResult>(result);
        _logos.Verify(l => l.ReplaceAsync(
            It.Is<IReadOnlyDictionary<int, byte[]>>(d => d.Count == 3 && d[512].Length == 64),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Set_RefusesAMissingRenditionAndStoresNothing()
    {
        var (a, b, _) = Valid();

        var result = await CreateController().SetLogo(a, b, null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _logos.Verify(l => l.ReplaceAsync(It.IsAny<IReadOnlyDictionary<int, byte[]>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Set_RefusesARenditionOfTheWrongSizeAndStoresNothing()
    {
        var (a, _, c) = Valid();

        var result = await CreateController().SetLogo(a, File(AppLogoTests.Png(512, 512), "logo192"), c, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _logos.Verify(l => l.ReplaceAsync(It.IsAny<IReadOnlyDictionary<int, byte[]>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Refused on the declared length, before a byte is copied into memory.
    [Fact]
    public async Task Set_RefusesAnOversizedRendition()
    {
        var (_, b, c) = Valid();

        var result = await CreateController().SetLogo(
            File(AppLogoTests.Png(32, 32, 16 * 1024 + 1), "logo32"), b, c, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Delete_ClearsTheLogo()
    {
        var result = await CreateController().DeleteLogo(CancellationToken.None);

        Assert.IsType<StatusCodeResult>(result);
        _logos.Verify(l => l.ClearAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_ServesThePngWithALongCache()
    {
        var png = AppLogoTests.Png(192, 192);
        _logos.Setup(l => l.GetImageAsync(192, It.IsAny<CancellationToken>())).ReturnsAsync(png);
        var controller = CreateController();

        var result = await controller.GetLogo(192, CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(png, file.FileContents);
        Assert.Equal("public, max-age=31536000, immutable", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Get_Answers404WithoutALogo()
    {
        var result = await CreateController().GetLogo(192, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // Never reaches the store with a size it does not hold.
    [Fact]
    public async Task Get_Answers404ForAnUnknownSize()
    {
        var result = await CreateController().GetLogo(64, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        _logos.Verify(l => l.GetImageAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(nameof(AppSettingsController.SetLogo))]
    [InlineData(nameof(AppSettingsController.DeleteLogo))]
    public void Writes_AreReservedToAdministrators(string action)
    {
        var authorize = Assert.Single(typeof(AppSettingsController).GetMethod(action)!
            .GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>());
        Assert.Equal(AdminRequirement.PolicyName, authorize.Policy);
    }

    // The login page and the manifest load it with no session.
    [Fact]
    public void Get_IsAnonymous() =>
        Assert.NotEmpty(typeof(AppSettingsController).GetMethod(nameof(AppSettingsController.GetLogo))!
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), false));
}
```

(Ajuster `StatusCodeResult`/`NotFoundObjectResult` aux types réels produits par `StatusCode(204)` et `NotFoundEnveloppe` dans `ApiBaseController.cs`, comme ci-dessus.)

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~AppSettings"`
Expected: build error (constructeur à un argument, `SetLogo`/`DeleteLogo`/`GetLogo` absents).

- [ ] **Step 4: Implement**

Dans `AppSettingsController.cs` : constructeur `AppSettingsController(IAppSettingStore store, IAppLogoStore logos)`, résumé de la classe complété (« …and under what name and logo »), puis :

```csharp
    private const string NoLogo = "No logo of that size";
```

`GetAppSettings` devient :

```csharp
        var effective = new Dictionary<string, string>(AppSettings.Effective(await store.GetAsync(cancellationToken)))
        {
            [AppLogo.VersionKey] = await logos.GetUpdatedAtAsync(cancellationToken) is { } at
                ? AppLogo.Version(at) : string.Empty,
        };

        return Ok(effective);
```

(mettre à jour son `<response code="200">` : « …plus `app.logo`, the logo's version or empty ».)

Nouvelles actions :

```csharp
    /// <summary>One rendition of the instance logo. Its URL carries the version, so it may be cached for good.</summary>
    /// <param name="size">32, 192 or 512</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The PNG</response>
    /// <response code="404">An unknown size, or no logo stored (the client then uses the bundled one)</response>
    [HttpGet("logo/{size:int}")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetLogo(int size, CancellationToken cancellationToken)
    {
        var image = AppLogo.Sizes.Contains(size) ? await logos.GetImageAsync(size, cancellationToken) : null;
        if (image is null) return NotFoundEnveloppe(NoLogo);

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return File(image, "image/png");
    }

    /// <summary>Replaces the instance logo: the three renditions together, or none of them.</summary>
    /// <param name="logo32">the 32×32 PNG</param>
    /// <param name="logo192">the 192×192 PNG</param>
    /// <param name="logo512">the 512×512 PNG</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Logo stored</response>
    /// <response code="400">A rendition missing, not a PNG, of the wrong size or too heavy</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    [HttpPut("logo")]
    [Authorize(Policy = AdminRequirement.PolicyName)]
    [RequestSizeLimit(AppLogo.MaxRequestBytes)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> SetLogo(
        IFormFile? logo32, IFormFile? logo192, IFormFile? logo512, CancellationToken cancellationToken)
    {
        var images = new Dictionary<int, byte[]>();
        foreach (var (size, file) in new[] { (32, logo32), (192, logo192), (512, logo512) })
        {
            if (file is null) return BadRequestEnveloppe($"The {size} px logo is missing");
            if (file.Length > AppLogo.MaxBytes(size))
                return BadRequestEnveloppe($"The {size} px logo is over {AppLogo.MaxBytes(size) / 1024} KB");

            using var buffer = new MemoryStream((int)file.Length);
            await file.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();

            var check = AppLogo.Check(size, bytes);
            if (check.IsFailure) return BadRequestEnveloppe(check.Error);
            images[size] = bytes;
        }

        await logos.ReplaceAsync(images, cancellationToken);
        return StatusCode(StatusCodes.Status204NoContent);
    }

    /// <summary>Back to the bundled Scotty logo.</summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Logo removed</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    [HttpDelete("logo")]
    [Authorize(Policy = AdminRequirement.PolicyName)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> DeleteLogo(CancellationToken cancellationToken)
    {
        await logos.ClearAsync(cancellationToken);
        return StatusCode(StatusCodes.Status204NoContent);
    }
```

- [ ] **Step 5: Run the whole API suite**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests`
Expected: PASS, aucun test existant cassé (le constructeur a changé : d'autres fichiers de test qui
construisent `AppSettingsController` doivent être adaptés de la même façon — `grep -rn "new AppSettingsController"`).

- [ ] **Step 6: Commit**

```bash
git checkout -- src/scotty.microservice/ApiDocumentation.xml
dotnet build src/scotty.microservice   # régénère le XML
git diff src/scotty.microservice/ApiDocumentation.xml   # ne garder que les blocs GetLogo/SetLogo/DeleteLogo/GetAppSettings
git add -p src/scotty.microservice/ApiDocumentation.xml
git add src/scotty.microservice/Controllers/AppSettingsController.cs src/scotty.microservice/scotty.microservice.Tests/Controllers/AppSettingsControllerTests.cs src/scotty.microservice/scotty.microservice.Tests/Controllers/AppSettingsLogoTests.cs
git checkout -- src/scotty.microservice/ApiDocumentation.xml
git commit -F - <<'EOF'
Serve, replace and remove the instance logo; announce its version

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 4: Source unique des adresses du logo (`appLogo.ts`, `useAppLogo`)

**Files:**
- Create: `src/frontend/src/lib/appLogo.ts`
- Create: `src/frontend/src/hooks/useAppLogo.ts`
- Modify: `src/frontend/src/hooks/useAppSettings.ts:6-10` (clé `logo`)
- Test: `src/frontend/src/lib/appLogo.test.ts`
- Test: `src/frontend/src/hooks/useAppLogo.test.tsx`

**Interfaces:**
- Produces:
  ```ts
  export const LOGO_SIZES = [32, 192, 512] as const
  export type LogoSize = typeof LOGO_SIZES[number]
  export type LogoUrls = Record<LogoSize, string>
  export const LOGO_MAX_BYTES: Record<LogoSize, number>
  export function logoUrls(version: string): LogoUrls
  export function rememberedLogoVersion(): string
  export function rememberLogoVersion(version: string): void
  export function currentLogo(): LogoUrls
  export function setCurrentLogo(urls: LogoUrls): void
  export function useAppLogo(): LogoUrls            // hooks/useAppLogo.ts
  APP_SETTING_KEYS.logo === 'app.logo'
  ```

L'URL est construite ici, pas dans `api.ts` : une dizaine de tests mockent `../api.js` en entier
(`vi.mock('../api.js', () => ({ api: mocks }))`) ; un export de plus dans `api.ts` y serait `undefined`.

- [ ] **Step 1: Write the failing tests**

`lib/appLogo.test.ts` :

```ts
import { afterEach, describe, expect, it, vi } from 'vitest'
import { currentLogo, logoUrls, rememberLogoVersion, rememberedLogoVersion, setCurrentLogo } from './appLogo'

afterEach(() => { localStorage.clear(); vi.restoreAllMocks() })

describe('logoUrls', () => {
  it('answers the bundled Scotty images without a version', () => {
    const urls = logoUrls('')
    expect(urls[32]).toMatch(/favicon-32/)
    expect(urls[192]).toMatch(/logo-192/)
    expect(urls[512]).toBe('/icon-512.png')
  })

  it('puts the version in every API address, so a new logo is a new URL', () => {
    const urls = logoUrls('20261004T101500123')
    for (const size of [32, 192, 512] as const)
      expect(urls[size]).toMatch(new RegExp(`/api/AppSettings/logo/${size}\\?v=20261004T101500123$`))
  })
})

describe('remembered version', () => {
  it('reads back what was remembered, and nothing at first', () => {
    expect(rememberedLogoVersion()).toBe('')
    rememberLogoVersion('v1')
    expect(rememberedLogoVersion()).toBe('v1')
  })

  it('survives a storage that throws', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('denied') })
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('denied') })
    expect(() => rememberLogoVersion('v1')).not.toThrow()
    expect(rememberedLogoVersion()).toBe('')
  })
})

describe('current logo', () => {
  it('starts on Scotty and follows what it is given', () => {
    expect(currentLogo()).toEqual(logoUrls(''))
    setCurrentLogo(logoUrls('v2'))
    expect(currentLogo()[192]).toMatch(/v=v2$/)
  })
})
```

(Si `safeStorage` n'avale pas déjà les exceptions, le test « survives a storage that throws » le
révèle : l'aligner, ne pas réécrire un try/catch à côté.)

`hooks/useAppLogo.test.tsx` :

```tsx
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { useAppLogo } from './useAppLogo'
import { createTestQueryClient, withQueryClient } from '../test-utils'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))

beforeEach(() => { localStorage.clear(); vi.clearAllMocks() })

const render = () => renderHook(() => useAppLogo(), { wrapper: withQueryClient(createTestQueryClient()) })

describe('useAppLogo', () => {
  it('uses the API logo once the settings name a version, and remembers it', async () => {
    mocks.getAppSettings.mockResolvedValue({ 'app.logo': 'v1' })
    const { result } = render()

    await waitFor(() => expect(result.current[192]).toMatch(/v=v1$/))
    expect(localStorage.getItem('app.logoVersion')).toBe('v1')
  })

  // No Scotty-then-organisation flash on every page load.
  it('starts from the remembered version while the settings are on their way', () => {
    localStorage.setItem('app.logoVersion', 'v1')
    mocks.getAppSettings.mockReturnValue(new Promise(() => {}))

    expect(render().result.current[192]).toMatch(/v=v1$/)
  })

  it('goes back to Scotty, and forgets, when the admin restored the default', async () => {
    localStorage.setItem('app.logoVersion', 'v1')
    mocks.getAppSettings.mockResolvedValue({ 'app.logo': '' })
    const { result } = render()

    await waitFor(() => expect(result.current[192]).toMatch(/logo-192/))
    expect(localStorage.getItem('app.logoVersion')).toBe('')
  })
})
```

(`withQueryClient` et `createTestQueryClient` existent dans `src/frontend/src/test-utils` — voir
`useWebAppManifest.test.tsx`.)

- [ ] **Step 2: Run to verify they fail**

Run (dans `src/frontend`) : `npx vitest run src/lib/appLogo.test.ts src/hooks/useAppLogo.test.tsx`
Expected: FAIL, modules introuvables.

- [ ] **Step 3: Implement**

`hooks/useAppSettings.ts` — ajouter à `APP_SETTING_KEYS` :

```ts
  logo: 'app.logo',
```

`lib/appLogo.ts` :

```ts
import favicon32 from '../assets/favicon-32.png'
import logo192 from '../assets/logo-192.png'
import { readStored, writeStored } from './safeStorage'

export const LOGO_SIZES = [32, 192, 512] as const
export type LogoSize = typeof LOGO_SIZES[number]
export type LogoUrls = Record<LogoSize, string>

/** `AppLogo.MaxBytes` on the API, mirrored so a too-detailed image is refused before upload. */
export const LOGO_MAX_BYTES: Record<LogoSize, number> = { 32: 16 * 1024, 192: 256 * 1024, 512: 1024 * 1024 }

const SCOTTY: LogoUrls = { 32: favicon32, 192: logo192, 512: '/icon-512.png' }
const VERSION_KEY = 'app.logoVersion'
const BASE: string = import.meta.env.VITE_API_BASE

export function logoUrls(version: string): LogoUrls {
  if (!version) return SCOTTY
  const at = (size: LogoSize) => `${BASE}/api/AppSettings/logo/${size}?v=${encodeURIComponent(version)}`
  return { 32: at(32), 192: at(192), 512: at(512) }
}

export function rememberedLogoVersion(): string {
  return readStored(VERSION_KEY) ?? ''
}

export function rememberLogoVersion(version: string): void {
  writeStored(VERSION_KEY, version)
}

let current = SCOTTY

/** For a plain module such as the desktop notifications, which cannot call a hook. */
export function currentLogo(): LogoUrls {
  return current
}

export function setCurrentLogo(urls: LogoUrls): void {
  current = urls
}
```

`hooks/useAppLogo.ts` :

```ts
import { useEffect, useMemo } from 'react'
import { logoUrls, rememberLogoVersion, rememberedLogoVersion, type LogoUrls } from '../lib/appLogo'
import { APP_SETTING_KEYS, useAppSettings } from './useAppSettings'

/** The logo's addresses. The last version seen stands in until the settings answer: no Scotty flash. */
export function useAppLogo(): LogoUrls {
  const { data } = useAppSettings()
  const answered = data?.[APP_SETTING_KEYS.logo]

  useEffect(() => {
    if (answered !== undefined) rememberLogoVersion(answered)
  }, [answered])

  return useMemo(() => logoUrls(answered ?? rememberedLogoVersion()), [answered])
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `npx vitest run src/lib/appLogo.test.ts src/hooks/useAppLogo.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/lib/appLogo.ts src/frontend/src/lib/appLogo.test.ts src/frontend/src/hooks/useAppLogo.ts src/frontend/src/hooks/useAppLogo.test.tsx src/frontend/src/hooks/useAppSettings.ts
git commit -F - <<'EOF'
Derive the logo's addresses from its version, remembered across loads

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 5: Brancher les quatre usages

**Files:**
- Modify: `src/frontend/src/layouts/TopBar.tsx`, `src/frontend/src/layouts/TopBar.test.tsx`
- Modify: `src/frontend/src/styles/shell.css:124`
- Modify: `src/frontend/src/lib/favicon.ts`
- Create: `src/frontend/src/lib/favicon.test.ts`
- Modify: `src/frontend/src/modules/mail/notify/channels.ts:2,36`
- Modify: `src/frontend/src/lib/webAppManifest.ts`, `src/frontend/src/lib/webAppManifest.test.ts`
- Modify: `src/frontend/src/hooks/useWebAppManifest.ts`, `src/frontend/src/hooks/useWebAppManifest.test.tsx`
- Create: `src/frontend/src/hooks/useBrandIcons.ts`, `src/frontend/src/hooks/useBrandIcons.test.tsx`
- Modify: `src/frontend/src/App.tsx:22-27`

**Interfaces:**
- Consumes: `useAppLogo`, `LogoUrls`, `setCurrentLogo`, `currentLogo` (tâche 4).
- Produces: `setFaviconBase(href: string): void` (favicon.ts) ; `buildManifest(settings, origin, t, logo: LogoUrls)` ; `useBrandIcons(): void`.

La spec cite « la tête du tiroir sur téléphone » : vérifié, le tiroir n'affiche aucun logo (`ContextDrawer.tsx`) et la barre du haut est masquée sur téléphone. Rien à brancher là.

- [ ] **Step 1: Tests du favicon (Review Focus 2)**

`lib/favicon.test.ts` — jsdom n'a ni canvas ni chargement d'image ; on simule les deux :

```ts
import { beforeEach, describe, expect, it, vi } from 'vitest'

let loads: { src: string; crossOrigin: string | null; fire: () => void }[]

beforeEach(async () => {
  vi.resetModules()
  loads = []
  document.head.innerHTML = '<link rel="icon" href="/scotty-32.png">'
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({
    drawImage: vi.fn(), beginPath: vi.fn(), arc: vi.fn(), fill: vi.fn(), stroke: vi.fn(),
  } as unknown as CanvasRenderingContext2D)
  let n = 0
  vi.spyOn(HTMLCanvasElement.prototype, 'toDataURL').mockImplementation(() => `data:badge-${++n}`)
  vi.stubGlobal('Image', class {
    onload: (() => void) | null = null
    onerror: (() => void) | null = null
    crossOrigin: string | null = null
    set src(value: string) { loads.push({ src: value, crossOrigin: this.crossOrigin, fire: () => this.onload?.() }) }
  })
})

const link = () => document.querySelector<HTMLLinkElement>('link[rel="icon"]')!
const flush = () => new Promise(resolve => setTimeout(resolve))

describe('favicon base', () => {
  it('shows the new base when no badge is wanted', async () => {
    const { setFaviconBase } = await import('./favicon')
    setFaviconBase('/org-32.png')
    expect(link().getAttribute('href')).toBe('/org-32.png')
  })

  it('redraws a wanted badge over the new base, never the old one', async () => {
    const { setFaviconBadge, setFaviconBase } = await import('./favicon')
    setFaviconBadge(true)
    setFaviconBase('/org-32.png')
    loads[0]!.fire()            // the drawing over Scotty lands late
    loads[1]!.fire()
    await flush()

    expect(loads.map(l => l.src)).toEqual([expect.stringMatching(/scotty-32/), '/org-32.png'])
    expect(link().getAttribute('href')).toBe('data:badge-2')
  })

  it('turning the badge off shows the new base', async () => {
    const { setFaviconBadge, setFaviconBase } = await import('./favicon')
    setFaviconBase('/org-32.png')
    setFaviconBadge(true)
    setFaviconBadge(false)
    expect(link().getAttribute('href')).toBe('/org-32.png')
  })

  // An API on another origin would taint the canvas and the badge would silently vanish.
  it('loads the base with CORS', async () => {
    const { setFaviconBadge } = await import('./favicon')
    setFaviconBadge(true)
    expect(loads[0]!.crossOrigin).toBe('anonymous')
  })
})
```

- [ ] **Step 2: Run to verify they fail**

Run: `npx vitest run src/lib/favicon.test.ts`
Expected: FAIL (`setFaviconBase` n'existe pas).

- [ ] **Step 3: Implement favicon.ts**

Dans `paint`, avant `image.src = href` :

```ts
    image.crossOrigin = 'anonymous'
```

Dans `setFaviconBadge`, le `.then` ignore une peinture devenue périmée :

```ts
  const base = originalHref
  void paint(base, colour).then(url => {
    // Dropped if the logo changed while it was drawing: its dot would sit on the old one.
    if (!url || base !== originalHref) return
    drawn.set(colour, url)
    if (wanted) link.href = url
  })
```

Nouvelle fonction exportée :

```ts
/** The admin may replace the logo under an open tab: every drawing made over the old one is stale. */
export function setFaviconBase(href: string): void {
  const link = iconLink()
  if (!link) return
  originalHref = href
  drawn.clear()
  if (wanted) setFaviconBadge(true)
  else link.href = href
}
```

Run: `npx vitest run src/lib/favicon.test.ts` → PASS.

- [ ] **Step 4: Tests du manifeste et de useBrandIcons**

Dans `lib/webAppManifest.test.ts`, chaque appel `buildManifest(settings, origin, t)` reçoit un 4ᵉ
argument `logoUrls('')`, et ajouter :

```ts
  it('names the custom logo as the installed icon, absolute', () => {
    const manifest = buildManifest(enabled, 'https://mail.example.net', t, {
      32: 'https://api.example.net/api/AppSettings/logo/32?v=v1',
      192: 'https://api.example.net/api/AppSettings/logo/192?v=v1',
      512: 'https://api.example.net/api/AppSettings/logo/512?v=v1',
    })
    expect(manifest!.icons).toEqual([
      { src: 'https://api.example.net/api/AppSettings/logo/192?v=v1', sizes: '192x192', type: 'image/png' },
      { src: 'https://api.example.net/api/AppSettings/logo/512?v=v1', sizes: '512x512', type: 'image/png' },
    ])
  })

  it('makes a relative icon address absolute on the page origin', () => {
    const manifest = buildManifest(enabled, 'https://mail.example.net', t, logoUrls(''))
    expect(manifest!.icons[1]!.src).toBe('https://mail.example.net/icon-512.png')
  })
```

(Réutiliser les noms de fixtures déjà présents dans ce fichier pour `enabled` et `t`.)

`hooks/useBrandIcons.test.tsx` :

```tsx
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { useBrandIcons } from './useBrandIcons'
import { currentLogo } from '../lib/appLogo'
import { createTestQueryClient, withQueryClient } from '../test-utils'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))
const favicon = vi.hoisted(() => ({ setFaviconBase: vi.fn() }))
vi.mock('../lib/favicon', () => favicon)

beforeEach(() => {
  localStorage.clear()
  document.head.innerHTML = '<link rel="apple-touch-icon" href="/scotty-192.png">'
})

describe('useBrandIcons', () => {
  it('puts the custom logo on the tab, the iOS icon and the notifications', async () => {
    mocks.getAppSettings.mockResolvedValue({ 'app.logo': 'v1' })
    renderHook(() => useBrandIcons(), { wrapper: withQueryClient(createTestQueryClient()) })

    await waitFor(() => expect(favicon.setFaviconBase).toHaveBeenLastCalledWith(expect.stringMatching(/logo\/32\?v=v1$/)))
    expect(document.querySelector('link[rel="apple-touch-icon"]')!.getAttribute('href')).toMatch(/logo\/192\?v=v1$/)
    expect(currentLogo()[192]).toMatch(/logo\/192\?v=v1$/)
  })
})
```

Dans `hooks/useWebAppManifest.test.tsx`, ajouter un cas : avec `'app.logo': 'v1'` dans les réglages,
le manifeste posté (lu depuis le blob du `<link rel="manifest">`, comme les tests voisins) a pour
icône 512 une adresse finissant par `/api/AppSettings/logo/512?v=v1`.

- [ ] **Step 5: Run to verify they fail**

Run: `npx vitest run src/lib/webAppManifest.test.ts src/hooks/useWebAppManifest.test.tsx src/hooks/useBrandIcons.test.tsx`
Expected: FAIL.

- [ ] **Step 6: Implement manifest, brand icons, App, TopBar, notifications, CSS**

`lib/webAppManifest.ts` : importer `type LogoUrls` de `./appLogo`, ajouter le paramètre
`logo: LogoUrls` à `buildManifest`, et :

```ts
    icons: [
      { src: new URL(logo[192], origin).href, sizes: '192x192', type: 'image/png' },
      { src: new URL(logo[512], origin).href, sizes: '512x512', type: 'image/png' },
    ],
```

`hooks/useWebAppManifest.ts` : `const logo = useAppLogo()`, passer `logo` à `buildManifest`, et
l'ajouter aux dépendances de l'effet (`[data, t, logo]`).

`hooks/useBrandIcons.ts` :

```ts
import { useEffect } from 'react'
import { setCurrentLogo } from '../lib/appLogo'
import { setFaviconBase } from '../lib/favicon'
import { useAppLogo } from './useAppLogo'

/** The icons that live outside the React tree: the tab, the iOS home screen, the notifications. */
export function useBrandIcons(): void {
  const logo = useAppLogo()

  useEffect(() => {
    setCurrentLogo(logo)
    setFaviconBase(logo[32])
    document.querySelector('link[rel="apple-touch-icon"]')?.setAttribute('href', logo[192])
  }, [logo])
}
```

`App.tsx` : `InstallManifest` appelle aussi `useBrandIcons()` ; mettre à jour son commentaire
(« Renders nothing: it posts the manifest and the brand icons. Outside the router so it covers /login… »).

`layouts/TopBar.tsx` :

```tsx
import { useAppLogo } from '../hooks/useAppLogo'
import { PRODUCT } from '../lib/product'

export default function TopBar() {
  const logo = useAppLogo()
  return (
    <header className="app-topbar">
      <div className="topbar-brand">
        <img src={logo[192]} alt="" className="topbar-logo" />
```

(le reste inchangé ; garder le commentaire de tête, en remplaçant « Brand only » par « Brand only; the logo is the administrator's ».)

`layouts/TopBar.test.tsx` : envelopper le rendu dans `withQueryClient(createTestQueryClient())`,
mocker `../api.js` avec `getAppSettings`, et ajouter :

```tsx
  it('shows the administrator’s logo once the settings name one', async () => {
    mocks.getAppSettings.mockResolvedValue({ 'app.logo': 'v1' })
    const { container } = render(<TopBar />, { wrapper: withQueryClient(createTestQueryClient()) })

    await waitFor(() => expect(container.querySelector('.topbar-logo')!.getAttribute('src')).toMatch(/logo\/192\?v=v1$/))
  })
```

`modules/mail/notify/channels.ts` : retirer `import logo from '../../../assets/logo-192.png'`,
importer `currentLogo` de `../../../lib/appLogo`, et `icon: currentLogo()[192]`.

`styles/shell.css:124` :

```css
.topbar-logo { height: 29px; width: 29px; object-fit: contain; }
```

- [ ] **Step 7: Run the whole frontend suite, typecheck and lint**

Run (dans `src/frontend`) : `npm run typecheck && npm run lint && npm test`
Expected: tout vert. Les tests qui rendent `AppShell` ou `TopBar` sans `getAppSettings` dans leur mock
d'API doivent l'ajouter (`mockResolvedValue({})`) plutôt que de laisser une requête rejetée.

- [ ] **Step 8: Commit**

```bash
git add src/frontend/src/layouts/TopBar.tsx src/frontend/src/layouts/TopBar.test.tsx src/frontend/src/styles/shell.css src/frontend/src/lib/favicon.ts src/frontend/src/lib/favicon.test.ts src/frontend/src/modules/mail/notify/channels.ts src/frontend/src/lib/webAppManifest.ts src/frontend/src/lib/webAppManifest.test.ts src/frontend/src/hooks/useWebAppManifest.ts src/frontend/src/hooks/useWebAppManifest.test.tsx src/frontend/src/hooks/useBrandIcons.ts src/frontend/src/hooks/useBrandIcons.test.tsx src/frontend/src/App.tsx
git commit -F - <<'EOF'
Show the instance logo on the top bar, tab, notifications and installed app

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 6: Préparation de l'image dans le navigateur (`logoImage.ts`)

**Files:**
- Create: `src/frontend/src/modules/settings/admin/logoImage.ts`
- Test: `src/frontend/src/modules/settings/admin/logoImage.test.ts`

**Interfaces:**
- Consumes: `LOGO_SIZES`, `LOGO_MAX_BYTES`, `LogoSize` (tâche 4).
- Produces:
  ```ts
  export const LOGO_UNREADABLE = 'application.logoUnreadable'
  export const LOGO_TOO_DETAILED = 'application.logoTooDetailed'
  export type LogoErrorKey = typeof LOGO_UNREADABLE | typeof LOGO_TOO_DETAILED
  export interface PreparedLogo { images: Record<LogoSize, Blob>; lowResolution: boolean }
  export function prepareLogo(file: File): Promise<PreparedLogo>   // throws Error(LogoErrorKey)
  ```

Décodage par `HTMLImageElement` et non `createImageBitmap` (que `contactPhoto.ts` utilise) : Chrome
refuse un SVG dans `createImageBitmap`. Pas de fond blanc : PNG, transparence conservée.

- [ ] **Step 1: Write the failing tests**

```ts
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LOGO_TOO_DETAILED, LOGO_UNREADABLE, prepareLogo } from './logoImage'

let natural = { width: 400, height: 200 }
let decodeFails = false
let drawn: number[][]
let blobBytes: Record<number, number>
let decodedSources: string[]

beforeEach(() => {
  natural = { width: 400, height: 200 }
  decodeFails = false
  drawn = []
  blobBytes = {}
  decodedSources = []
  let n = 0
  vi.stubGlobal('URL', Object.assign(URL, {
    createObjectURL: vi.fn((blob: Blob) => { decodedSources.push(blob.type); return `blob:${++n}` }),
    revokeObjectURL: vi.fn(),
  }))
  Object.defineProperty(HTMLImageElement.prototype, 'naturalWidth', { configurable: true, get: () => natural.width })
  Object.defineProperty(HTMLImageElement.prototype, 'naturalHeight', { configurable: true, get: () => natural.height })
  HTMLImageElement.prototype.decode = vi.fn(() => decodeFails ? Promise.reject(new Error('bad')) : Promise.resolve())
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({
    imageSmoothingQuality: 'low',
    drawImage: (_image: unknown, ...args: number[]) => { drawn.push(args) },
  } as unknown as CanvasRenderingContext2D)
  vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(function (this: HTMLCanvasElement, callback: BlobCallback) {
    callback(new Blob([new Uint8Array(blobBytes[this.width] ?? 10)], { type: 'image/png' }))
  })
})

afterEach(() => vi.restoreAllMocks())

const png = () => new File([], 'logo.png', { type: 'image/png' })

describe('prepareLogo', () => {
  it('renders three PNGs, 32, 192 and 512', async () => {
    const { images } = await prepareLogo(png())
    expect(Object.keys(images)).toEqual(['32', '192', '512'])
    expect(images[512].type).toBe('image/png')
  })

  // Review Focus 4: centred, never stretched, transparent margins.
  it('fits a long logo whole in the square, centred', async () => {
    natural = { width: 1000, height: 100 }
    await prepareLogo(png())
    expect(drawn[2]).toEqual([0, 230.4, 512, 51.2])
  })

  it('flags an image under 512 px as low resolution', async () => {
    natural = { width: 300, height: 120 }
    expect((await prepareLogo(png())).lowResolution).toBe(true)
    natural = { width: 600, height: 120 }
    expect((await prepareLogo(png())).lowResolution).toBe(false)
  })

  it('refuses a file the browser cannot decode', async () => {
    decodeFails = true
    await expect(prepareLogo(png())).rejects.toThrow(LOGO_UNREADABLE)
  })

  it('refuses an image with no size', async () => {
    natural = { width: 0, height: 0 }
    await expect(prepareLogo(png())).rejects.toThrow(LOGO_UNREADABLE)
  })

  // Review Focus 3.
  it('refuses an image too detailed for the 512 px cap, before any upload', async () => {
    blobBytes = { 512: 1024 * 1024 + 1 }
    await expect(prepareLogo(png())).rejects.toThrow(LOGO_TOO_DETAILED)
  })

  // Review Focus 1: Firefox gives an SVG with only a viewBox a zero natural size.
  it('lends a viewBox-only SVG its size, and never calls it low resolution', async () => {
    natural = { width: 100, height: 50 }
    const svg = new File(['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 50"><rect width="100" height="50"/></svg>'],
      'logo.svg', { type: 'image/svg+xml' })
    const read = vi.spyOn(XMLSerializer.prototype, 'serializeToString')

    const prepared = await prepareLogo(svg)

    expect(read.mock.results[0]!.value).toMatch(/width="100" height="50"|height="50" width="100"/)
    expect(prepared.lowResolution).toBe(false)
  })
})
```

- [ ] **Step 2: Run to verify they fail**

Run: `npx vitest run src/modules/settings/admin/logoImage.test.ts`
Expected: FAIL, module introuvable.

- [ ] **Step 3: Implement**

```ts
// The admin's logo, drawn into the three PNGs the API stores. Pure: no query, React or i18n; it
// throws a translation key.
import { LOGO_MAX_BYTES, LOGO_SIZES, type LogoSize } from '../../../lib/appLogo'

export const LOGO_UNREADABLE = 'application.logoUnreadable'
export const LOGO_TOO_DETAILED = 'application.logoTooDetailed'
export type LogoErrorKey = typeof LOGO_UNREADABLE | typeof LOGO_TOO_DETAILED

export interface PreparedLogo {
  images: Record<LogoSize, Blob>
  /** Under 512 px on its longest side: the installed app's icon will be blurry. */
  lowResolution: boolean
}

export async function prepareLogo(file: File): Promise<PreparedLogo> {
  const svg = isSvg(file)
  const image = await decode(svg ? await sized(file) : file)
  try {
    const images = {} as Record<LogoSize, Blob>
    for (const size of LOGO_SIZES) {
      images[size] = await render(image, size)
      if (images[size].size > LOGO_MAX_BYTES[size]) throw new Error(LOGO_TOO_DETAILED)
    }
    return { images, lowResolution: !svg && Math.max(image.naturalWidth, image.naturalHeight) < 512 }
  } finally {
    URL.revokeObjectURL(image.src)
  }
}

function isSvg(file: File): boolean {
  return file.type === 'image/svg+xml' || /\.svg$/i.test(file.name)
}

/** Firefox gives an SVG with no width/height a zero natural size: the viewBox lends it one. */
async function sized(file: File): Promise<Blob> {
  const doc = new DOMParser().parseFromString(await file.text(), 'image/svg+xml')
  const root = doc.documentElement
  const box = root.getAttribute('viewBox')?.trim().split(/[\s,]+/).map(Number)
  if (root.localName !== 'svg' || box?.length !== 4 || !box.every(Number.isFinite)) return file
  root.setAttribute('width', String(box[2]))
  root.setAttribute('height', String(box[3]))
  return new Blob([new XMLSerializer().serializeToString(doc)], { type: 'image/svg+xml' })
}

async function decode(blob: Blob): Promise<HTMLImageElement> {
  const image = new Image()
  image.src = URL.createObjectURL(blob)
  try {
    await image.decode()
  } catch {
    URL.revokeObjectURL(image.src)
    throw new Error(LOGO_UNREADABLE)
  }
  if (!image.naturalWidth || !image.naturalHeight) {
    URL.revokeObjectURL(image.src)
    throw new Error(LOGO_UNREADABLE)
  }
  return image
}

/** Whole and centred in a transparent square: a logo is never cropped nor stretched. */
function render(image: HTMLImageElement, size: LogoSize): Promise<Blob> {
  const canvas = document.createElement('canvas')
  canvas.width = size
  canvas.height = size
  const context = canvas.getContext('2d')
  if (!context) throw new Error(LOGO_UNREADABLE)

  const scale = size / Math.max(image.naturalWidth, image.naturalHeight)
  const width = image.naturalWidth * scale
  const height = image.naturalHeight * scale
  context.imageSmoothingQuality = 'high'
  context.drawImage(image, (size - width) / 2, (size - height) / 2, width, height)

  return new Promise((resolve, reject) => canvas.toBlob(
    blob => blob ? resolve(blob) : reject(new Error(LOGO_UNREADABLE)), 'image/png'))
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `npx vitest run src/modules/settings/admin/logoImage.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/settings/admin/logoImage.ts src/frontend/src/modules/settings/admin/logoImage.test.ts
git commit -F - <<'EOF'
Draw an uploaded logo into three transparent square PNGs

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 7: Rubrique Logo dans Administration › Application

**Files:**
- Modify: `src/frontend/src/api.ts` (bloc « App settings », ~ligne 627)
- Modify: `src/frontend/src/hooks/useAppSettings.ts` (deux mutations)
- Create: `src/frontend/src/modules/settings/admin/LogoSection.tsx`
- Create: `src/frontend/src/modules/settings/admin/LogoSection.test.tsx`
- Modify: `src/frontend/src/modules/settings/admin/ApplicationTab.tsx` (monter la rubrique avant `SchedulingAccountSection`)
- Modify: `src/frontend/src/modules/settings/admin/ApplicationTab.test.tsx` (mocks `setAppLogo`, `deleteAppLogo`)
- Modify: `src/frontend/src/locales/en/admin.json`, `src/frontend/src/locales/fr/admin.json`
- Modify: `src/frontend/src/index.css` (styles de l'aperçu, près de `.svc-account-section` ~ligne 1049)

**Interfaces:**
- Consumes: `prepareLogo`, `LOGO_UNREADABLE`, `LOGO_TOO_DETAILED`, `LogoErrorKey` (tâche 6) ; `useAppLogo` (tâche 4) ; `LOGO_SIZES`, `LogoSize` (tâche 4).
- Produces:
  ```ts
  api.setAppLogo(images: Record<LogoSize, Blob>): Promise<null>
  api.deleteAppLogo(): Promise<null>
  useSetAppLogo(), useDeleteAppLogo()   // useMutation, invalidate ['appSettings'] onSettled
  ```

- [ ] **Step 1: Write the failing tests**

`LogoSection.test.tsx` :

```tsx
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import LogoSection from './LogoSection'
import { createTestQueryClient, withQueryClient } from '../../../test-utils'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn(), setAppLogo: vi.fn(), deleteAppLogo: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))
const prepare = vi.hoisted(() => ({ prepareLogo: vi.fn() }))
vi.mock('./logoImage', async importOriginal => ({ ...(await importOriginal<object>()), ...prepare }))

const addToast = vi.fn()
const images = { 32: new Blob(['a']), 192: new Blob(['b']), 512: new Blob(['c']) }

function renderSection(settings: Record<string, string> = { 'app.logo': '' }) {
  mocks.getAppSettings.mockResolvedValue(settings)
  return render(<LogoSection addToast={addToast} />, { wrapper: withQueryClient(createTestQueryClient()) })
}

async function choose(file = new File(['x'], 'logo.png', { type: 'image/png' })) {
  await userEvent.upload(screen.getByLabelText('Logo file'), file)
}

beforeEach(() => {
  vi.clearAllMocks()
  URL.createObjectURL = vi.fn(() => 'blob:preview')
  URL.revokeObjectURL = vi.fn()
  prepare.prepareLogo.mockResolvedValue({ images, lowResolution: false })
  mocks.setAppLogo.mockResolvedValue(null)
  mocks.deleteAppLogo.mockResolvedValue(null)
})

describe('LogoSection', () => {
  it('offers no restore while Scotty is the logo', async () => {
    renderSection()
    expect(await screen.findByRole('button', { name: 'Change logo…' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Restore default' })).not.toBeInTheDocument()
  })

  it('previews a chosen file without sending it', async () => {
    renderSection()
    await screen.findByRole('button', { name: 'Change logo…' })
    await choose()

    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
    expect(screen.getAllByRole('img', { name: /preview/i })[0]).toHaveAttribute('src', 'blob:preview')
    expect(mocks.setAppLogo).not.toHaveBeenCalled()
  })

  it('saves the three renditions and says so', async () => {
    renderSection()
    await screen.findByRole('button', { name: 'Change logo…' })
    await choose()
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(mocks.setAppLogo).toHaveBeenCalledWith(images))
    expect(addToast).toHaveBeenCalledWith('The logo was saved')
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
  })

  it('cancel drops the preview and sends nothing', async () => {
    renderSection()
    await screen.findByRole('button', { name: 'Change logo…' })
    await choose()
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview')
    expect(mocks.setAppLogo).not.toHaveBeenCalled()
  })

  it('warns that a small image will be blurry once installed', async () => {
    prepare.prepareLogo.mockResolvedValue({ images, lowResolution: true })
    renderSection()
    await screen.findByRole('button', { name: 'Change logo…' })
    await choose()

    expect(screen.getByText(/blurry/)).toBeInTheDocument()
  })

  it('names an unreadable file and offers no save', async () => {
    prepare.prepareLogo.mockRejectedValue(new Error('application.logoUnreadable'))
    renderSection()
    await screen.findByRole('button', { name: 'Change logo…' })
    await choose()

    expect(await screen.findByText('This file isn’t a readable image')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
  })

  it('reports a refused save in a toast and keeps the preview', async () => {
    mocks.setAppLogo.mockRejectedValue(new Error('The 512 px logo is over 1024 KB'))
    renderSection()
    await screen.findByRole('button', { name: 'Change logo…' })
    await choose()
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(addToast).toHaveBeenCalledWith(expect.any(String), 'error'))
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
  })

  it('restores Scotty after confirmation', async () => {
    renderSection({ 'app.logo': 'v1' })
    await userEvent.click(await screen.findByRole('button', { name: 'Restore default' }))
    await userEvent.click(await screen.findByRole('button', { name: 'Restore' }))

    await waitFor(() => expect(mocks.deleteAppLogo).toHaveBeenCalled())
    expect(addToast).toHaveBeenCalledWith('The default logo is back')
  })
})
```

(Le texte exact de l'erreur `apiErrorMessage` dépend de la forme d'erreur de `request` : vérifier dans
`lib/apiErrorMessage.ts` et, si besoin, rejeter avec l'objet d'erreur qu'utilisent déjà les tests de
`ApplicationTab.test.tsx`.)

- [ ] **Step 2: Run to verify they fail**

Run: `npx vitest run src/modules/settings/admin/LogoSection.test.tsx`
Expected: FAIL, module introuvable.

- [ ] **Step 3: API client et mutations**

`api.ts`, dans le bloc « App settings » (importer `LOGO_SIZES`, `type LogoSize` de `./lib/appLogo`) :

```ts
  setAppLogo: (images: Record<LogoSize, Blob>) => {
    const form = new FormData()
    for (const size of LOGO_SIZES) form.append(`logo${size}`, images[size], `logo-${size}.png`)
    return request<null>('PUT', '/api/AppSettings/logo', form)
  },

  deleteAppLogo: () => request<null>('DELETE', '/api/AppSettings/logo'),
```

`hooks/useAppSettings.ts` :

```ts
export function useSetAppLogo() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (images: Record<LogoSize, Blob>) => api.setAppLogo(images),
    onSettled: () => client.invalidateQueries({ queryKey }),
  })
}

export function useDeleteAppLogo() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => api.deleteAppLogo(),
    onSettled: () => client.invalidateQueries({ queryKey }),
  })
}
```

- [ ] **Step 4: Traductions**

`locales/en/admin.json`, dans `application` :

```json
    "logo": "Logo",
    "logoIntro": "Shown in the top bar, the browser tab, notifications and the installed app. PNG, JPEG, WebP or SVG; square works best.",
    "logoFile": "Logo file",
    "logoChange": "Change logo…",
    "logoRestore": "Restore default",
    "logoRestoreTitle": "Restore the Scotty logo?",
    "logoRestoreBody": "Everyone will see the Scotty logo again.",
    "logoRestoreConfirm": "Restore",
    "logoPreviewBar": "Top bar preview",
    "logoPreviewTab": "Browser tab preview",
    "logoLowResolution": "This image is under 512 px: the installed app’s icon will look blurry.",
    "logoUnreadable": "This file isn’t a readable image",
    "logoTooDetailed": "This image is too detailed to use as a logo. Try a simpler or smaller one.",
    "logoSaved": "The logo was saved",
    "logoSaveFailed": "Could not save the logo",
    "logoRestored": "The default logo is back",
    "logoRestoreFailed": "Could not restore the default logo"
```

`locales/fr/admin.json`, mêmes clés (U+00A0 avant `?` et `:` — écrire puis vérifier en PowerShell) :

```json
    "logo": "Logo",
    "logoIntro": "Affiché dans la barre du haut, l’onglet du navigateur, les notifications et l’application installée. PNG, JPEG, WebP ou SVG ; un logo carré rend le mieux.",
    "logoFile": "Fichier du logo",
    "logoChange": "Changer le logo…",
    "logoRestore": "Rétablir le logo par défaut",
    "logoRestoreTitle": "Rétablir le logo Scotty ?",
    "logoRestoreBody": "Tout le monde reverra le logo Scotty.",
    "logoRestoreConfirm": "Rétablir",
    "logoPreviewBar": "Aperçu dans la barre du haut",
    "logoPreviewTab": "Aperçu dans l’onglet du navigateur",
    "logoLowResolution": "Cette image fait moins de 512 px : l’icône de l’application installée sera floue.",
    "logoUnreadable": "Ce fichier n’est pas une image lisible",
    "logoTooDetailed": "Cette image est trop détaillée pour servir de logo. Essayez-en une plus simple ou plus petite.",
    "logoSaved": "Le logo a été enregistré",
    "logoSaveFailed": "Impossible d’enregistrer le logo",
    "logoRestored": "Le logo par défaut est rétabli",
    "logoRestoreFailed": "Impossible de rétablir le logo par défaut"
```

- [ ] **Step 5: Composant**

`LogoSection.tsx` :

```tsx
import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal'
import { useAppLogo } from '../../../hooks/useAppLogo'
import { APP_SETTING_KEYS, useAppSettings, useDeleteAppLogo, useSetAppLogo } from '../../../hooks/useAppSettings'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { PRODUCT } from '../../../lib/product'
import type { AddToast } from '../../../hooks/useToasts'
import { LOGO_TOO_DETAILED, LOGO_UNREADABLE, prepareLogo, type LogoErrorKey, type PreparedLogo } from './logoImage'

interface Pending extends PreparedLogo { previewBar: string; previewTab: string }

/** The instance logo: chosen, previewed, then sent as three PNGs drawn here, never decoded server side. */
export default function LogoSection({ addToast }: { addToast: AddToast }) {
  const { t } = useTranslation('admin')
  const { data: settings } = useAppSettings()
  const logo = useAppLogo()
  const save = useSetAppLogo()
  const restore = useDeleteAppLogo()
  const input = useRef<HTMLInputElement>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [error, setError] = useState<LogoErrorKey | null>(null)
  const [confirming, setConfirming] = useState(false)

  useEffect(() => () => {
    if (pending) { URL.revokeObjectURL(pending.previewBar); URL.revokeObjectURL(pending.previewTab) }
  }, [pending])

  async function choose(file: File | undefined) {
    if (input.current) input.current.value = ''
    if (!file) return
    setError(null)
    try {
      const prepared = await prepareLogo(file)
      setPending({ ...prepared,
        previewBar: URL.createObjectURL(prepared.images[192]),
        previewTab: URL.createObjectURL(prepared.images[32]) })
    } catch (caught) {
      setPending(null)
      setError(caught instanceof Error && caught.message === LOGO_TOO_DETAILED ? LOGO_TOO_DETAILED : LOGO_UNREADABLE)
    }
  }

  async function send() {
    if (!pending) return
    try {
      await save.mutateAsync(pending.images)
      setPending(null)
      addToast(t('application.logoSaved'))
    } catch (caught) {
      addToast(apiErrorMessage(caught, t('application.logoSaveFailed')), 'error')
    }
  }

  async function backToScotty() {
    try {
      await restore.mutateAsync()
      addToast(t('application.logoRestored'))
    } catch (caught) {
      addToast(apiErrorMessage(caught, t('application.logoRestoreFailed')), 'error')
    }
  }

  const custom = Boolean(settings?.[APP_SETTING_KEYS.logo])
  const busy = save.isPending || restore.isPending

  return (
    <div className="svc-account-section">
      <h2 className="svc-account-section-title">{t('application.logo')}</h2>
      <p className="svc-account-section-intro">{t('application.logoIntro')}</p>

      <div className="logo-preview">
        <div className="logo-preview-bar">
          <img src={pending?.previewBar ?? logo[192]} alt={t('application.logoPreviewBar')} className="topbar-logo" />
          <span className="topbar-name">{PRODUCT.name} <span className="topbar-name-kind">{PRODUCT.kind}</span></span>
        </div>
        <img src={pending?.previewTab ?? logo[32]} alt={t('application.logoPreviewTab')} className="logo-preview-tab" width={32} height={32} />
      </div>

      {pending?.lowResolution && <p className="field-hint">{t('application.logoLowResolution')}</p>}
      {error && <p className="field-error" role="alert">{t(error)}</p>}

      <input ref={input} type="file" hidden aria-label={t('application.logoFile')}
        accept="image/png,image/jpeg,image/webp,image/svg+xml"
        onChange={event => void choose(event.target.files?.[0])} />

      <div className="logo-actions">
        {pending ? (
          <>
            <button type="button" className="btn btn-primary btn-auto" disabled={busy} onClick={() => void send()}>
              {save.isPending ? <span className="spinner" /> : t('actions.save', { ns: 'common' })}
            </button>
            <button type="button" className="btn btn-auto" disabled={busy} onClick={() => setPending(null)}>
              {t('actions.cancel', { ns: 'common' })}
            </button>
          </>
        ) : (
          <>
            <button type="button" className="btn btn-auto" disabled={busy} onClick={() => input.current?.click()}>
              {t('application.logoChange')}
            </button>
            {custom && (
              <button type="button" className="btn btn-auto" disabled={busy} onClick={() => setConfirming(true)}>
                {t('application.logoRestore')}
              </button>
            )}
          </>
        )}
      </div>

      {confirming && (
        <DeleteConfirmModal title={t('application.logoRestoreTitle')} message={t('application.logoRestoreBody')}
          confirmLabel={t('application.logoRestoreConfirm')} tone="primary"
          onConfirm={backToScotty} onClose={() => setConfirming(false)} loading={restore.isPending} />
      )}
    </div>
  )
}
```

Vérifier avant d'écrire : les classes `field-hint` / `field-error` / `btn` (secondaire) réellement utilisées ailleurs dans
`modules/settings` — réutiliser les noms existants plutôt que d'en créer.

`index.css`, sous `.svc-account-section-intro` :

```css
/* The top bar's own colours, so the admin judges the logo against the palette it will sit on. */
.logo-preview { display: flex; align-items: center; gap: 16px; margin-bottom: 12px; }
.logo-preview-bar { display: flex; align-items: center; gap: 10px; height: 44px; padding: 0 16px;
  border-radius: 8px; background: var(--topbar-bg); color: var(--topbar-fg); }
.logo-preview-tab { width: 32px; height: 32px; object-fit: contain; }
.logo-actions { display: flex; gap: 8px; }
```

(Les règles `.topbar-name` de `shell.css` portent un `top: -3px` d'alignement optique : le vérifier
dans l'aperçu et, s'il décale le texte hors de la barre réduite, le neutraliser par
`.logo-preview-bar .topbar-name { top: 0; }`.)

`ApplicationTab.tsx` : importer `LogoSection` et le monter juste avant `<SchedulingAccountSection …/>` :

```tsx
      <LogoSection addToast={addToast} />
```

`ApplicationTab.test.tsx` : ajouter `setAppLogo: vi.fn()` et `deleteAppLogo: vi.fn()` aux mocks.

- [ ] **Step 6: Run the whole frontend suite, typecheck and lint**

Run (dans `src/frontend`) : `npm run typecheck && npm run lint && npm test`
Expected: tout vert, y compris `locales/parity.test.ts` et `locales/keys.test.ts`.

Vérifier l'insécable française :

```powershell
Select-String -Path src/frontend/src/locales/fr/admin.json -Pattern 'logo' | Where-Object { $_.Line -match ' [?:!]' }
```
Expected: aucune ligne (chaque `?`, `:` est précédé d'U+00A0).

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/api.ts src/frontend/src/hooks/useAppSettings.ts src/frontend/src/modules/settings/admin/LogoSection.tsx src/frontend/src/modules/settings/admin/LogoSection.test.tsx src/frontend/src/modules/settings/admin/ApplicationTab.tsx src/frontend/src/modules/settings/admin/ApplicationTab.test.tsx src/frontend/src/locales/en/admin.json src/frontend/src/locales/fr/admin.json src/frontend/src/index.css
git commit -F - <<'EOF'
Let the administrator preview, save and restore the instance logo

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 8: Documentation et vérification dans un vrai navigateur

**Files:**
- Modify: `src/frontend/docs/architecture-shell.md` (paragraphe « Shell layout », ligne 15)
- Modify: `src/frontend/docs/architecture-settings.md` (section de l'onglet Application)

- [ ] **Step 1: Docs**

`architecture-shell.md` : remplacer « `TopBar` … carries the logo/wordmark and nothing else » par une
phrase disant que le logo vient de `useAppLogo()` (version `app.logo` des réglages, mémorisée en
`localStorage` contre le flash), et que `useBrandIcons()` (monté avec le manifeste, hors routeur)
pose le même logo sur le favicon, l'icône iOS et les notifications.

`architecture-settings.md` : ajouter à l'onglet Application la rubrique Logo — trois PNG dessinés par
le navigateur (`logoImage.ts`), vérifiés sans décodage par l'API (`AppLogo.Check`), servis
anonymement avec une adresse versionnée et un cache d'un an ; `DELETE` ramène Scotty.

- [ ] **Step 2: Suites complètes**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests` puis (dans `src/frontend`)
`npm run typecheck && npm run lint && npm test`.
Expected: tout vert. Révertrer `ApiDocumentation.xml` si `dotnet test` l'a régénéré.

- [ ] **Step 3: Vérification navigateur (tout en local : API + front locaux, mémoire « CORS de l'API dev »)**

Créer la table `app_logo` dans la base locale (`docs/operations/app-logo-migration.md`), lancer
l'API et le front, se connecter en admin, puis, avec Chrome **et** Firefox :

1. Envoyer un PNG carré : barre du haut, onglet et aperçu à 32 px changent ; recharger la page : pas
   de flash Scotty.
2. Envoyer un SVG ne portant qu'un `viewBox` (ex. `tools/brand` ou un fichier écrit à la main) :
   net et non déformé dans les trois tailles (ouvrir `…/logo/512?v=…` dans un onglet).
3. Envoyer un logo très allongé (1000×100) : centré, marges transparentes, rien de rogné.
4. Avec un message non lu : la pastille se dessine sur le nouveau logo (onglet). Changer le logo
   pendant que la pastille est affichée : elle se redessine sur le nouveau.
5. Notification de bureau (s'envoyer un message) : elle porte le nouveau logo.
6. Rétablir le défaut : Scotty revient partout ; recharger : toujours Scotty.

Mesurer, ne pas raisonner (mémoire « Géométrie UI ») : capture de chaque point. Signaler à
l'utilisateur tout point non vérifiable en local (ex. installation PWA) pour la recette sur l'hôte
de dev après déploiement.

- [ ] **Step 4: Commit**

```bash
git add src/frontend/docs/architecture-shell.md src/frontend/docs/architecture-settings.md
git commit -F - <<'EOF'
Document where the instance logo comes from and how it is checked

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```
