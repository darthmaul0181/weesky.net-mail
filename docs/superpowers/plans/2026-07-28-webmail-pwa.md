# Webmail installable (PWA) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rendre le webmail installable comme application (icône d'installation dans Edge/Chrome), avec son nom piloté depuis un nouvel onglet Administration, des raccourcis, et l'enregistrement comme client `mailto:`.

**Architecture:** Le manifest ne peut être ni servi par l'API (règle de même origine sur `start_url`) ni composé par nginx (le déploiement ne pousse que `dist/`) : il est donc construit **côté client** à partir d'un réglage d'instance lu sur `GET /api/AppSettings`, puis injecté en `blob:` dans un `<link rel="manifest">`. Le backend gagne une table clé/valeur sans `user_id` et un contrôleur dont le `GET` est anonyme et le `PUT` réservé aux administrateurs.

**Tech Stack:** React 18 + TypeScript + Vite + TanStack Query côté frontend ; ASP.NET Core + EF Core + MariaDB côté microservice ; Vitest + Testing Library / xUnit + Moq pour les tests.

## Global Constraints

- **Aucun service worker.** Mesuré sur Chromium : `beforeinstallprompt` est déclenché sans. N'en ajoutez pas « pour faire propre » — il ne rendrait aucun service et rouvrirait la classe de bugs du cache périmé.
- **Toutes les URL du manifest sont absolues**, construites depuis `window.location.origin`. Un `blob:` a un chemin opaque ; y résoudre une référence relative n'est pas fiable.
- **Le corps d'un `mailto:` est échappé en HTML** avant d'entrer dans le composeur, et ses adresses passent par `isValidAddress`. Le lien vient du système d'exploitation, donc du monde extérieur.
- **Défauts du registre :** `app.installable` = `'false'`, `app.name` = `Snoopy mail`, `app.shortName` = `Snoopy`. Les deux noms sont élagués avant validation et avant stockage.
- **Bornes :** `app.name` 1 à 60 caractères après élagage, `app.shortName` 1 à 12.
- **`GET /api/AppSettings` est anonyme**, `PUT` est sous `[Authorize(Policy = AdminRequirement.PolicyName)]`.
- **Couleurs du manifest, figées** : `theme_color` `#182238`, `background_color` `#f6f3ef` — la palette `night` en mode clair, celle que reçoit un compte sans préférence.
- **Les commentaires et la documentation dans le code s'écrivent en anglais**, comme tout le dépôt. Les blocs de code de ce plan les portent en français : c'est une erreur de rédaction, il faut les transposer en anglais en gardant le propos — ils expliquent le *pourquoi*, qui est ce qui compte ici.
- Tout code frontend neuf est en TypeScript ; les tests sont posés à côté de ce qu'ils testent (`Foo.tsx` → `Foo.test.tsx`).
- Ce projet n'a **pas de migrations EF** : toute table est créée à la main, documentée dans `docs/superpowers/`.
- Les messages de commit ne commencent ni ne finissent par `@`.

---

### Task 1: Backend — registre, entité, store, table

**Files:**
- Create: `src/snoopy.microservice/Data/Preferences/AppSetting.cs`
- Create: `src/snoopy.microservice/Models/AppSettings.cs`
- Create: `src/snoopy.microservice/Repositories/IAppSettingStore.cs`
- Create: `src/snoopy.microservice/Repositories/AppSettingStore.cs`
- Create: `docs/superpowers/webmail-app-settings-table.md`
- Modify: `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs`
- Modify: `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs` (à côté de la ligne `services.AddScoped<IUserPreferenceStore, UserPreferenceStore>();`)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Models/AppSettingsTests.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/AppSettingStoreTests.cs`

**Interfaces:**
- Consumes: rien (première tâche).
- Produces:
  - `AppSettings.Installable` / `.Name` / `.ShortName` (constantes de clés, `string`)
  - `AppSettings.IsValid(string key, string value) → bool`
  - `AppSettings.Normalize(string key, string value) → string`
  - `AppSettings.Effective(IEnumerable<AppSetting> stored) → IReadOnlyDictionary<string, string>`
  - `IAppSettingStore.GetAsync(CancellationToken) → Task<IReadOnlyList<AppSetting>>`
  - `IAppSettingStore.SetAsync(string key, string value, CancellationToken) → Task`

- [ ] **Step 1: Écrire le test du registre (échoue)**

Créer `snoopy.microservice.Tests/Models/AppSettingsTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models;

public sealed class AppSettingsTests
{
    [Fact]
    public void Effective_AnswersEveryKeyWithItsDefault()
    {
        var values = AppSettings.Effective([]);

        Assert.Equal("false", values[AppSettings.Installable]);
        Assert.Equal("Snoopy mail", values[AppSettings.Name]);
        Assert.Equal("Snoopy", values[AppSettings.ShortName]);
    }

    [Fact]
    public void Effective_LetsAStoredRowWin()
    {
        var values = AppSettings.Effective(
            [new AppSetting { SettingKey = AppSettings.Name, SettingValue = "Weesky Mail" }]);

        Assert.Equal("Weesky Mail", values[AppSettings.Name]);
    }

    // Le registre est le seul garde-fou : la table ne peut vérifier ni la clé ni la valeur, donc
    // une ligne devenue invalide doit s'effacer devant le défaut plutôt que d'atteindre le client.
    [Fact]
    public void Effective_IgnoresAStoredRowTheRegistryNoLongerAccepts()
    {
        var values = AppSettings.Effective(
            [new AppSetting { SettingKey = AppSettings.Installable, SettingValue = "yes" }]);

        Assert.Equal("false", values[AppSettings.Installable]);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void IsValid_AcceptsTheTwoBooleans(string value)
        => Assert.True(AppSettings.IsValid(AppSettings.Installable, value));

    [Theory]
    [InlineData("yes")]
    [InlineData("")]
    public void IsValid_RefusesAnythingElseForABoolean(string value)
        => Assert.False(AppSettings.IsValid(AppSettings.Installable, value));

    [Fact]
    public void IsValid_RefusesAnUnknownKey()
        => Assert.False(AppSettings.IsValid("app.colour", "red"));

    [Fact]
    public void IsValid_RefusesAValueThatIsOnlyWhitespace()
        => Assert.False(AppSettings.IsValid(AppSettings.Name, "   "));

    [Fact]
    public void IsValid_MeasuresLengthAfterTrimming()
    {
        Assert.True(AppSettings.IsValid(AppSettings.ShortName, "  Snoopy  "));
        Assert.False(AppSettings.IsValid(AppSettings.ShortName, "Snoopy webmail"));
        Assert.True(AppSettings.IsValid(AppSettings.Name, new string('x', 60)));
        Assert.False(AppSettings.IsValid(AppSettings.Name, new string('x', 61)));
    }

    // Ce qui est stocké est ce que l'icône affichera : un espace de tête saisi par mégarde ne
    // doit pas se retrouver sous l'icône.
    [Fact]
    public void Normalize_TrimsAName()
        => Assert.Equal("Snoopy", AppSettings.Normalize(AppSettings.ShortName, "  Snoopy  "));

    [Fact]
    public void Normalize_LeavesABooleanAlone()
        => Assert.Equal("true", AppSettings.Normalize(AppSettings.Installable, "true"));
}
```

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.sln --filter FullyQualifiedName~AppSettingsTests`
Expected: échec de compilation — `AppSettings` et `AppSetting` n'existent pas.

- [ ] **Step 3: Créer l'entité**

`src/snoopy.microservice/Data/Preferences/AppSetting.cs` :

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// Un réglage de l'instance, pas d'un compte : la table ne porte pas de user_id, et nommer
/// l'application est une décision d'administrateur, pas une préférence de lecteur.
///
/// Clé/valeur pour la raison qui vaut déjà pour user_preferences : sans migrations EF, une
/// colonne typée voudrait dire un ALTER à la main sur le serveur à chaque nouveau réglage.
/// </summary>
[Table("app_settings")]
public sealed class AppSetting
{
    /// <summary>Pointée et stable, p. ex. "app.name" — jamais traduite, jamais renommée à la légère.</summary>
    [Column("setting_key")]
    public string SettingKey { get; set; } = string.Empty;

    [Column("setting_value")]
    public string SettingValue { get; set; } = string.Empty;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
```

- [ ] **Step 4: Créer le registre**

`src/snoopy.microservice/Models/AppSettings.cs` :

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Models;

/// <summary>
/// Un réglage d'instance. <paramref name="Allowed"/> non nul énumère les valeurs acceptées ;
/// nul, la valeur est du texte libre borné par <paramref name="MaxLength"/>, mesurée élaguée.
/// </summary>
public sealed record AppSettingDefinition(
    string Key, string Default, int MaxLength, IReadOnlyList<string>? Allowed = null);

/// <summary>
/// Le registre des réglages d'instance : l'unique endroit où l'un d'eux est déclaré.
///
/// Même rôle que <see cref="UserPreferences"/> pour les préférences de compte — la base ne sait
/// vérifier ni une clé ni une valeur, donc c'est ici que ça se passe, et une ligne devenue
/// invalide s'efface devant le défaut plutôt que d'atteindre le client.
/// </summary>
public static class AppSettings
{
    public const string Installable = "app.installable";
    public const string Name = "app.name";
    public const string ShortName = "app.shortName";

    private static readonly string[] Booleans = ["true", "false"];

    public static IReadOnlyList<AppSettingDefinition> All { get; } =
    [
        new(Installable, "false", 5, Booleans),
        new(Name, "Snoopy mail", 60),
        new(ShortName, "Snoopy", 12),
    ];

    public static bool IsValid(string key, string value)
    {
        var definition = All.FirstOrDefault(s => s.Key == key);
        if (definition is null) return false;
        if (definition.Allowed is not null) return definition.Allowed.Contains(value);

        var trimmed = value.Trim();
        return trimmed.Length >= 1 && trimmed.Length <= definition.MaxLength;
    }

    /// <summary>Ce qui part en base. Un nom est élagué ; une valeur énumérée est déjà exacte.</summary>
    public static string Normalize(string key, string value) =>
        All.FirstOrDefault(s => s.Key == key)?.Allowed is null ? value.Trim() : value;

    /// <summary>Tous les défauts, une ligne stockée l'emportant là où le registre l'accepte encore.</summary>
    public static IReadOnlyDictionary<string, string> Effective(IEnumerable<AppSetting> stored)
    {
        var effective = All.ToDictionary(s => s.Key, s => s.Default, StringComparer.Ordinal);

        foreach (var row in stored)
        {
            if (IsValid(row.SettingKey, row.SettingValue))
                effective[row.SettingKey] = Normalize(row.SettingKey, row.SettingValue);
        }

        return effective;
    }
}
```

- [ ] **Step 5: Lancer le test du registre**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.sln --filter FullyQualifiedName~AppSettingsTests`
Expected: PASS

- [ ] **Step 6: Écrire le test du store (échoue)**

Créer `snoopy.microservice.Tests/Repositories/AppSettingStoreTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class AppSettingStoreTests
{
    private static AppSettingStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName));

    [Fact]
    public async Task Set_InsertsThenUpdatesTheSameRow()
    {
        var store = CreateStore(nameof(Set_InsertsThenUpdatesTheSameRow));

        await store.SetAsync(AppSettings.Name, "First", CancellationToken.None);
        await store.SetAsync(AppSettings.Name, "Second", CancellationToken.None);

        var row = Assert.Single(await store.GetAsync(CancellationToken.None));
        Assert.Equal("Second", row.SettingValue);
    }

    [Fact]
    public async Task Get_ReturnsEveryStoredRow()
    {
        var store = CreateStore(nameof(Get_ReturnsEveryStoredRow));
        await store.SetAsync(AppSettings.Name, "Snoopy mail", CancellationToken.None);
        await store.SetAsync(AppSettings.Installable, "true", CancellationToken.None);

        var rows = await store.GetAsync(CancellationToken.None);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task Set_StampsUpdatedAt()
    {
        var store = CreateStore(nameof(Set_StampsUpdatedAt));

        await store.SetAsync(AppSettings.ShortName, "Snoopy", CancellationToken.None);

        var row = Assert.Single(await store.GetAsync(CancellationToken.None));
        Assert.NotEqual(default, row.UpdatedAt);
    }
}
```

- [ ] **Step 7: Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.sln --filter FullyQualifiedName~AppSettingStoreTests`
Expected: échec de compilation — `AppSettingStore` n'existe pas.

- [ ] **Step 8: Créer l'interface et le store**

`src/snoopy.microservice/Repositories/IAppSettingStore.cs` :

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// Lit et écrit les réglages d'instance. Ne sait rien des clés qui existent ni de ce qu'elles
/// acceptent — c'est l'affaire du registre, et l'appelant valide avant d'écrire.
/// </summary>
public interface IAppSettingStore
{
    Task<IReadOnlyList<AppSetting>> GetAsync(CancellationToken cancellationToken);

    /// <summary>Pose ou corrige un réglage. La clé est la ligne.</summary>
    Task SetAsync(string key, string value, CancellationToken cancellationToken);
}
```

`src/snoopy.microservice/Repositories/AppSettingStore.cs` :

```csharp
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class AppSettingStore : IAppSettingStore
{
    private readonly PreferencesDbContext _context;

    public AppSettingStore(PreferencesDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<AppSetting>> GetAsync(CancellationToken cancellationToken)
        => await _context.AppSettings.AsNoTracking()
            .OrderBy(s => s.SettingKey)
            .ToListAsync(cancellationToken);

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        var existing = await _context.AppSettings
            .FirstOrDefaultAsync(s => s.SettingKey == key, cancellationToken);

        if (existing is null)
        {
            _context.AppSettings.Add(new AppSetting
            {
                SettingKey = key,
                SettingValue = value,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.SettingValue = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 9: Déclarer l'entité dans le DbContext**

Dans `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs`, ajouter la clé dans `OnModelCreating`, sous la ligne `modelBuilder.Entity<UserPreference>().HasKey(...)` :

```csharp
        // Aucune arête de relation ici, contrairement aux cinq tables par compte plus bas : ce
        // réglage n'appartient à personne, donc rien à ordonner devant users.
        modelBuilder.Entity<AppSetting>().HasKey(s => s.SettingKey);
```

et le `DbSet` à côté des autres :

```csharp
    public DbSet<AppSetting> AppSettings { get; set; }
```

- [ ] **Step 10: Enregistrer le store dans le conteneur**

Dans `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs`, sous `services.AddScoped<IUserPreferenceStore, UserPreferenceStore>();` :

```csharp
        services.AddScoped<IAppSettingStore, AppSettingStore>();
```

- [ ] **Step 11: Lancer les deux suites**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.sln --filter "FullyQualifiedName~AppSetting"`
Expected: PASS

- [ ] **Step 12: Écrire le document de prérequis de la table**

Créer `docs/superpowers/webmail-app-settings-table.md` :

````markdown
# Prérequis serveur — table `app_settings`

**À appliquer sur les deux bases** (`snoopy_webmail` et `snoopy_webmail_dev`) avant de déployer
le backend qui expose `/api/AppSettings`.

Le projet n'utilise pas les migrations EF : la création des tables est manuelle, comme pour
`trusted_senders` (voir `webmail-trusted-senders-table.md`).

## Pourquoi cette table

Les réglages de l'instance, pas d'un compte : elle ne porte donc **pas** de `user_id` et aucune
clé étrangère vers `users`. Aujourd'hui trois lignes au plus — l'activation de l'installation en
application et les deux noms qu'affiche le manifest.

Une clé absente signifie que le défaut du registre (`Models/AppSettings.cs`) s'applique, donc une
instance qui n'a jamais ouvert l'onglet Administration n'a aucune ligne.

## Script

```sql
CREATE TABLE IF NOT EXISTS `snoopy_webmail`.`app_settings` (
  `setting_key`   VARCHAR(64)  NOT NULL COMMENT 'Pointée et stable, p. ex. app.name',
  `setting_value` VARCHAR(255) NOT NULL,
  `updated_at`    DATETIME     NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma',
  PRIMARY KEY (`setting_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS `snoopy_webmail_dev`.`app_settings` (
  `setting_key`   VARCHAR(64)  NOT NULL COMMENT 'Pointée et stable, p. ex. app.name',
  `setting_value` VARCHAR(255) NOT NULL,
  `updated_at`    DATETIME     NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma',
  PRIMARY KEY (`setting_key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```
````

- [ ] **Step 13: Commit**

```bash
git add src/snoopy.microservice/Data/Preferences/AppSetting.cs \
        src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs \
        src/snoopy.microservice/Models/AppSettings.cs \
        src/snoopy.microservice/Repositories/IAppSettingStore.cs \
        src/snoopy.microservice/Repositories/AppSettingStore.cs \
        src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Models/AppSettingsTests.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Repositories/AppSettingStoreTests.cs \
        docs/superpowers/webmail-app-settings-table.md
git commit -m "Reglages d'instance: registre, entite et store app_settings"
```

---

### Task 2: Backend — `AppSettingsController`

**Files:**
- Create: `src/snoopy.microservice/Models/SetAppSettingRequest.cs`
- Create: `src/snoopy.microservice/Controllers/AppSettingsController.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/AppSettingsControllerTests.cs`

**Interfaces:**
- Consumes: `AppSettings.Effective/IsValid/Normalize`, `IAppSettingStore` (Task 1).
- Produces: `GET /api/AppSettings` → `IReadOnlyDictionary<string, string>` (200, anonyme) ; `PUT /api/AppSettings` corps `{ key, value }` → 204, 400 sur clé/valeur refusée, 403 pour un non-administrateur.

- [ ] **Step 1: Écrire le test du contrôleur (échoue)**

Créer `snoopy.microservice.Tests/Controllers/AppSettingsControllerTests.cs` :

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class AppSettingsControllerTests
{
    private readonly Mock<IAppSettingStore> _store = new();

    private AppSettingsController CreateController()
    {
        _store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<AppSetting>());

        return new AppSettingsController(_store.Object);
    }

    [Fact]
    public async Task Get_AnswersEveryKnownKeyEvenWithNoRows()
    {
        var result = await CreateController().GetAppSettings(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(ok.Value);
        Assert.Equal("false", values[AppSettings.Installable]);
        Assert.Equal("Snoopy mail", values[AppSettings.Name]);
        Assert.Equal("Snoopy", values[AppSettings.ShortName]);
    }

    [Fact]
    public async Task Get_LetsAStoredRowWin()
    {
        var controller = CreateController();
        _store.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
              .ReturnsAsync([new AppSetting
              {
                  SettingKey = AppSettings.Name, SettingValue = "Weesky Mail"
              }]);

        var result = await controller.GetAppSettings(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(ok.Value);
        Assert.Equal("Weesky Mail", values[AppSettings.Name]);
    }

    // L'icône d'installation doit vivre sur /login, où il n'y a pas de session.
    [Fact]
    public void Get_IsAnonymous()
    {
        var method = typeof(AppSettingsController).GetMethod(nameof(AppSettingsController.GetAppSettings))!;

        Assert.NotEmpty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), false));
    }

    [Fact]
    public void Set_IsReservedToAdministrators()
    {
        var method = typeof(AppSettingsController).GetMethod(nameof(AppSettingsController.SetAppSetting))!;

        var authorize = Assert.Single(
            method.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>());
        Assert.Equal(AdminRequirement.PolicyName, authorize.Policy);
    }

    [Fact]
    public async Task Set_Returns204AndStoresTheValue()
    {
        var result = await CreateController().SetAppSetting(
            new SetAppSettingRequest { Key = AppSettings.Installable, Value = "true" },
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status204NoContent, Assert.IsType<StatusCodeResult>(result).StatusCode);
        _store.Verify(s => s.SetAsync(AppSettings.Installable, "true", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Ce qui est stocké est ce que l'icône affichera.
    [Fact]
    public async Task Set_StoresANameTrimmed()
    {
        await CreateController().SetAppSetting(
            new SetAppSettingRequest { Key = AppSettings.ShortName, Value = "  Snoopy  " },
            CancellationToken.None);

        _store.Verify(s => s.SetAsync(AppSettings.ShortName, "Snoopy", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("app.colour", "red")]
    [InlineData(AppSettings.Installable, "yes")]
    [InlineData(AppSettings.ShortName, "   ")]
    [InlineData(AppSettings.ShortName, "Snoopy webmail")]
    public async Task Set_Returns400OnAnythingTheRegistryRefuses(string key, string value)
    {
        var result = await CreateController().SetAppSetting(
            new SetAppSettingRequest { Key = key, Value = value }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.Verify(s => s.SetAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Set_Returns400OnAnEmptyBody()
    {
        var result = await CreateController().SetAppSetting(null!, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
```

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.sln --filter FullyQualifiedName~AppSettingsControllerTests`
Expected: échec de compilation — `AppSettingsController` et `SetAppSettingRequest` n'existent pas.

- [ ] **Step 3: Créer le modèle de requête**

`src/snoopy.microservice/Models/SetAppSettingRequest.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models;

/// <summary>Corps de PUT /api/AppSettings. Les deux champs doivent nommer une entrée du registre.</summary>
public sealed class SetAppSettingRequest
{
    public string? Key { get; set; }

    public string? Value { get; set; }
}
```

- [ ] **Step 4: Créer le contrôleur**

`src/snoopy.microservice/Controllers/AppSettingsController.cs` :

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// Les réglages de l'instance — aujourd'hui ceux qui décident si le webmail s'annonce comme
/// application installable, et sous quel nom.
///
/// La lecture est anonyme : un nom d'application n'est pas un secret, et le manifest doit être
/// posé dès la page de login, où il n'y a pas de session. L'écriture est réservée aux
/// administrateurs. Comme pour les préférences de compte, la réponse porte toujours toutes les
/// clés connues avec leur défaut déjà rempli, donc le client n'en garde aucune copie.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public sealed class AppSettingsController : ApiBaseController
{
    private readonly IAppSettingStore _store;

    public AppSettingsController(IAppSettingStore store)
    {
        _store = store;
    }

    /// <summary>Tous les réglages connus, avec la valeur posée là où il y en a une.</summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">Table clé/valeur couvrant tous les réglages connus</response>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyDictionary<string, string>>> GetAppSettings(
        CancellationToken cancellationToken)
    {
        var stored = await _store.GetAsync(cancellationToken);

        return Ok(AppSettings.Effective(stored));
    }

    /// <summary>Pose un réglage.</summary>
    /// <param name="request">clé et valeur, toutes deux issues du registre</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Réglage enregistré</response>
    /// <response code="400">Clé inconnue, ou valeur que la clé n'accepte pas</response>
    /// <response code="401">Non authentifié</response>
    /// <response code="403">Pas administrateur</response>
    [HttpPut]
    [Authorize(Policy = AdminRequirement.PolicyName)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> SetAppSetting(
        SetAppSettingRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");

        var key = request.Key ?? string.Empty;
        var value = request.Value ?? string.Empty;

        if (!AppSettings.IsValid(key, value))
            return BadRequestEnveloppe($"'{value}' is not a value '{key}' accepts");

        await _store.SetAsync(key, AppSettings.Normalize(key, value), cancellationToken);

        return StatusCode(StatusCodes.Status204NoContent);
    }
}
```

- [ ] **Step 5: Lancer les tests du contrôleur**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.sln --filter FullyQualifiedName~AppSettingsControllerTests`
Expected: PASS

- [ ] **Step 6: Lancer toute la suite backend**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.sln`
Expected: PASS — aucune régression.

- [ ] **Step 7: Commit**

```bash
git add src/snoopy.microservice/Models/SetAppSettingRequest.cs \
        src/snoopy.microservice/Controllers/AppSettingsController.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Controllers/AppSettingsControllerTests.cs
git commit -m "API des reglages d'instance: GET anonyme, PUT reserve aux administrateurs"
```

---

### Task 3: Frontend — `api.js` et le hook `useAppSettings`

**Files:**
- Modify: `src/frontend/src/api.js` (l'objet `api`, à côté de `getPreferences` / `setPreference`)
- Create: `src/frontend/src/hooks/useAppSettings.ts`
- Test: `src/frontend/src/api.test.js` (ajouts)
- Test: `src/frontend/src/hooks/useAppSettings.test.tsx`

**Interfaces:**
- Consumes: `GET`/`PUT /api/AppSettings` (Task 2).
- Produces:
  - `api.getAppSettings(options) → Promise<Record<string, string>>`
  - `api.setAppSetting(key, value) → Promise<void>`
  - `APP_SETTING_KEYS = { installable, name, shortName }`
  - `type AppSettings = Record<string, string>`
  - `useAppSettings() → UseQueryResult<AppSettings>`
  - `useSetAppSetting() → UseMutationResult<void, Error, { key: string; value: string }>`
  - `installableOf(settings: AppSettings) → boolean`

- [ ] **Step 1: Écrire les tests (échouent)**

Dans `src/frontend/src/api.test.js`, ajouter ces deux cas juste après ceux de `getPreferences` /
`setPreference` (environ ligne 966). Ils utilisent l'aide `mockFetch` et l'import dynamique déjà
en place dans le fichier :

```js
  it('reads every app setting in one call', async () => {
    mockFetch(200, { json: { 'app.name': 'Snoopy mail' } })
    const { api } = await import('./api.js')

    await expect(api.getAppSettings()).resolves.toEqual({ 'app.name': 'Snoopy mail' })
    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/AppSettings'),
      expect.objectContaining({ method: 'GET' }))
  })

  it('sends the key and the value in the body', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.setAppSetting('app.name', 'Snoopy mail')

    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/AppSettings'),
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ key: 'app.name', value: 'Snoopy mail' }),
      }))
  })
```

Créer `src/frontend/src/hooks/useAppSettings.test.tsx` :

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { installableOf, useAppSettings } from './useAppSettings'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn(), setAppSetting: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

describe('useAppSettings', () => {
  beforeEach(() => vi.clearAllMocks())

  it('answers the map the backend sent', async () => {
    mocks.getAppSettings.mockResolvedValue({ 'app.installable': 'true' })

    const { result } = renderHook(() => useAppSettings(), { wrapper })

    await waitFor(() => expect(result.current.data).toEqual({ 'app.installable': 'true' }))
  })
})

describe('installableOf', () => {
  // Exactement 'true' : une valeur absente ou fautive doit laisser l'application discrète,
  // jamais l'annoncer par accident.
  it('is true only for the exact string', () => {
    expect(installableOf({ 'app.installable': 'true' })).toBe(true)
    expect(installableOf({ 'app.installable': 'True' })).toBe(false)
    expect(installableOf({})).toBe(false)
  })
})
```

- [ ] **Step 2: Lancer les tests pour vérifier qu'ils échouent**

Run: `cd src/frontend && npm test -- useAppSettings api.test`
Expected: FAIL — `useAppSettings` n'existe pas, `api.getAppSettings` n'est pas une fonction.

- [ ] **Step 3: Ajouter les deux méthodes à `api.js`**

Dans `src/frontend/src/api.js`, sous `setPreference` dans l'objet `api` :

```js
  getAppSettings: (options) =>
    request('GET', '/api/AppSettings', undefined, options),

  setAppSetting: (key, value) =>
    request('PUT', '/api/AppSettings', { key, value }),
```

- [ ] **Step 4: Créer le hook**

`src/frontend/src/hooks/useAppSettings.ts` :

```ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api.js'

/**
 * Les réglages de l'instance, pas ceux du compte : ils décident si le webmail s'annonce comme
 * application installable et sous quel nom. La lecture est anonyme, donc ce hook sert aussi la
 * page de login.
 *
 * Le backend répond toutes les clés connues avec ses défauts déjà remplis — aucune copie de
 * ceux-ci ici, sans quoi les deux divergeraient au premier changement.
 */
export const APP_SETTING_KEYS = {
  installable: 'app.installable',
  name: 'app.name',
  shortName: 'app.shortName',
} as const

export type AppSettings = Record<string, string>

const queryKey = ['appSettings'] as const

export function useAppSettings() {
  return useQuery({
    queryKey,
    queryFn: ({ signal }) => api.getAppSettings({ signal }) as Promise<AppSettings>,
    staleTime: 5 * 60 * 1000,
  })
}

export function useSetAppSetting() {
  const client = useQueryClient()

  return useMutation({
    mutationFn: ({ key, value }: { key: string; value: string }) => api.setAppSetting(key, value),
    // onSettled et non onSuccess : un refus doit laisser l'écran sur l'état du serveur plutôt
    // que sur un mensonge optimiste.
    onSettled: () => client.invalidateQueries({ queryKey }),
  })
}

/** Exactement 'true' : une valeur absente ou fautive laisse l'application discrète. */
export function installableOf(settings: AppSettings): boolean {
  return settings[APP_SETTING_KEYS.installable] === 'true'
}
```

- [ ] **Step 5: Lancer les tests**

Run: `cd src/frontend && npm test -- useAppSettings api.test`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/api.js src/frontend/src/api.test.js \
        src/frontend/src/hooks/useAppSettings.ts src/frontend/src/hooks/useAppSettings.test.tsx
git commit -m "Frontend: lecture et ecriture des reglages d'instance"
```

---

### Task 4: Icônes et constructeur de manifest

**Files:**
- Create: `src/frontend/public/icon-192.png`
- Create: `src/frontend/public/icon-512.png`
- Create: `src/frontend/src/lib/webAppManifest.ts`
- Test: `src/frontend/src/lib/webAppManifest.test.ts`

**Interfaces:**
- Consumes: `APP_SETTING_KEYS`, `installableOf`, `type AppSettings` (Task 3).
- Produces: `buildManifest(settings: AppSettings | undefined, origin: string) → WebAppManifest | null`, et le type exporté `WebAppManifest`.

- [ ] **Step 1: Générer les deux icônes**

Le logo source de 3808 px n'est plus dans l'arbre : il vit dans l'historique. Ne **jamais** agrandir le `logo-192.png` livré — le résultat est flou à 512.

```bash
cd "$(git rev-parse --show-toplevel)"
mkdir -p src/frontend/public
git show 1ba476c^:src/frontend/src/assets/logo_circle.jpg > /tmp/logo_source.png
python - <<'PY'
from PIL import Image
import numpy as np

src = Image.open('/tmp/logo_source.png').convert('RGBA')
# Alpha prémultipliée avant réduction : sans elle le bord transparent bave en sombre.
arr = np.asarray(src).astype(float)
arr[..., :3] *= arr[..., 3:4] / 255.0
pre = Image.fromarray(arr.astype('uint8'), 'RGBA')

for size in (512, 192):
    small = np.asarray(pre.resize((size, size), Image.LANCZOS)).astype(float)
    alpha = small[..., 3:4] / 255.0
    small[..., :3] = np.where(alpha > 0, small[..., :3] / np.where(alpha > 0, alpha, 1), 0)
    out = Image.fromarray(np.clip(small, 0, 255).astype('uint8'), 'RGBA')
    out.save(f'src/frontend/public/icon-{size}.png', optimize=True)
    print(size, 'ok')
PY
```

Le source fait 3808×3854 : la réduction en carré l'écrase de 1,2 %, exactement comme le
`logo-192.png` déjà livré. C'est voulu — le cadrage des icônes reste cohérent entre elles.

- [ ] **Step 2: Vérifier que les deux fichiers sont carrés et aux bonnes tailles**

Run:
```bash
python -c "from PIL import Image; [print(p, Image.open(p).size) for p in ('src/frontend/public/icon-192.png','src/frontend/public/icon-512.png')]"
```
Expected: `(192, 192)` puis `(512, 512)`.

- [ ] **Step 3: Écrire le test du constructeur (échoue)**

Créer `src/frontend/src/lib/webAppManifest.test.ts` :

```ts
import { describe, it, expect } from 'vitest'
import { buildManifest } from './webAppManifest'

const ORIGIN = 'https://account.mail.weesky.net'

const enabled = {
  'app.installable': 'true',
  'app.name': 'Snoopy mail',
  'app.shortName': 'Snoopy',
}

describe('buildManifest', () => {
  it('answers null while the settings have not arrived', () => {
    expect(buildManifest(undefined, ORIGIN)).toBeNull()
  })

  it('answers null when the app is disabled', () => {
    expect(buildManifest({ ...enabled, 'app.installable': 'false' }, ORIGIN)).toBeNull()
  })

  // Un manifest sans nom est refusé par le navigateur : mieux vaut ne rien poser du tout.
  it('answers null when a name is missing or blank', () => {
    expect(buildManifest({ ...enabled, 'app.name': '' }, ORIGIN)).toBeNull()
    expect(buildManifest({ ...enabled, 'app.shortName': '   ' }, ORIGIN)).toBeNull()
  })

  it('carries the names the admin set', () => {
    const manifest = buildManifest(enabled, ORIGIN)!

    expect(manifest.name).toBe('Snoopy mail')
    expect(manifest.short_name).toBe('Snoopy')
  })

  // Un blob: a un chemin opaque : une URL relative n'y est pas résoluble de façon fiable.
  it('spells every URL absolutely', () => {
    const manifest = buildManifest(enabled, ORIGIN)!

    const urls = [
      manifest.id, manifest.start_url, manifest.scope,
      ...manifest.icons.map(i => i.src),
      ...manifest.shortcuts.map(s => s.url),
      ...manifest.protocol_handlers.map(p => p.url),
    ]
    expect(urls.every(url => url.startsWith(`${ORIGIN}/`))).toBe(true)
  })

  it('offers the two icon sizes the install criteria require', () => {
    const manifest = buildManifest(enabled, ORIGIN)!

    expect(manifest.icons.map(i => i.sizes)).toEqual(['192x192', '512x512'])
  })

  it('opens standalone at the root, which already redirects to the mailbox', () => {
    const manifest = buildManifest(enabled, ORIGIN)!

    expect(manifest.display).toBe('standalone')
    expect(manifest.start_url).toBe(`${ORIGIN}/`)
  })

  it('registers the two shortcuts and the mailto handler', () => {
    const manifest = buildManifest(enabled, ORIGIN)!

    expect(manifest.shortcuts.map(s => s.name)).toEqual(['New message', 'Contacts'])
    expect(manifest.protocol_handlers).toEqual(
      [{ protocol: 'mailto', url: `${ORIGIN}/mail/compose?mailto=%s` }])
  })
})
```

- [ ] **Step 4: Lancer le test pour vérifier qu'il échoue**

Run: `cd src/frontend && npm test -- webAppManifest`
Expected: FAIL — `./webAppManifest` introuvable.

- [ ] **Step 5: Écrire le constructeur**

`src/frontend/src/lib/webAppManifest.ts` :

```ts
import { APP_SETTING_KEYS, installableOf, type AppSettings } from '../hooks/useAppSettings'

interface Icon { src: string; sizes: string; type: string }
interface Shortcut { name: string; url: string }
interface ProtocolHandler { protocol: string; url: string }

export interface WebAppManifest {
  id: string
  name: string
  short_name: string
  start_url: string
  scope: string
  display: 'standalone'
  theme_color: string
  background_color: string
  icons: Icon[]
  shortcuts: Shortcut[]
  protocol_handlers: ProtocolHandler[]
}

// La palette night en mode clair, celle que reçoit un compte sans préférence. Un manifest ne
// porte qu'une couleur : il ne peut pas suivre les huit palettes × deux modes.
const THEME_COLOR = '#182238'
const BACKGROUND_COLOR = '#f6f3ef'

/**
 * Le manifest que le navigateur lira, ou null quand il ne faut rien poser du tout.
 *
 * Toutes les URL sont absolues : un blob: a un chemin opaque, y résoudre une référence relative
 * n'est pas fiable. Les construire depuis l'origine courante rend au passage le manifest correct
 * sur account comme sur account-dev, sans réglage de build.
 */
export function buildManifest(
  settings: AppSettings | undefined, origin: string,
): WebAppManifest | null {
  if (!settings || !installableOf(settings)) return null

  const name = (settings[APP_SETTING_KEYS.name] ?? '').trim()
  const shortName = (settings[APP_SETTING_KEYS.shortName] ?? '').trim()
  if (!name || !shortName) return null

  return {
    id: `${origin}/`,
    name,
    short_name: shortName,
    start_url: `${origin}/`,
    scope: `${origin}/`,
    display: 'standalone',
    theme_color: THEME_COLOR,
    background_color: BACKGROUND_COLOR,
    icons: [
      { src: `${origin}/icon-192.png`, sizes: '192x192', type: 'image/png' },
      { src: `${origin}/icon-512.png`, sizes: '512x512', type: 'image/png' },
    ],
    shortcuts: [
      { name: 'New message', url: `${origin}/mail/compose` },
      { name: 'Contacts', url: `${origin}/contacts` },
    ],
    protocol_handlers: [{ protocol: 'mailto', url: `${origin}/mail/compose?mailto=%s` }],
  }
}
```

- [ ] **Step 6: Lancer le test**

Run: `cd src/frontend && npm test -- webAppManifest`
Expected: PASS

- [ ] **Step 7: Vérifier que le build copie les icônes verbatim**

Run: `cd src/frontend && npm run build && ls dist/icon-192.png dist/icon-512.png`
Expected: les deux fichiers existent **sans empreinte dans le nom**. C'est la condition — un
manifest ne peut pas désigner `icon-512-B6BKEni0.png`, dont le nom change à chaque build.

- [ ] **Step 8: Commit**

```bash
git add src/frontend/public/icon-192.png src/frontend/public/icon-512.png \
        src/frontend/src/lib/webAppManifest.ts src/frontend/src/lib/webAppManifest.test.ts
git commit -m "Icones 192/512 et constructeur du manifest"
```

---

### Task 5: Injection du manifest

**Files:**
- Create: `src/frontend/src/hooks/useWebAppManifest.ts`
- Modify: `src/frontend/src/App.tsx`
- Test: `src/frontend/src/hooks/useWebAppManifest.test.tsx`

**Interfaces:**
- Consumes: `useAppSettings` (Task 3), `buildManifest` (Task 4).
- Produces: `useWebAppManifest() → void` — effet seul, sans valeur de retour.

- [ ] **Step 1: Écrire le test (échoue)**

Créer `src/frontend/src/hooks/useWebAppManifest.test.tsx` :

```tsx
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useWebAppManifest } from './useWebAppManifest'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn(), setAppSetting: vi.fn() }))
vi.mock('../api.js', () => ({ api: mocks }))

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const enabled = {
  'app.installable': 'true',
  'app.name': 'Snoopy mail',
  'app.shortName': 'Snoopy',
}

function manifestLink() {
  return document.head.querySelector('link[rel="manifest"]')
}

describe('useWebAppManifest', () => {
  // jsdom n'implémente ni createObjectURL ni revokeObjectURL : elles sont posées ici et
  // retirées après, plutôt que remplacées globalement — mailtoSeed se sert du vrai URL.
  beforeEach(() => {
    vi.clearAllMocks()
    URL.createObjectURL = vi.fn(() => 'blob:mock')
    URL.revokeObjectURL = vi.fn()
  })
  afterEach(() => {
    manifestLink()?.remove()
    delete (URL as Partial<typeof URL>).createObjectURL
    delete (URL as Partial<typeof URL>).revokeObjectURL
  })

  it('posts a manifest link when the app is enabled', async () => {
    mocks.getAppSettings.mockResolvedValue(enabled)

    renderHook(() => useWebAppManifest(), { wrapper })

    await waitFor(() => expect(manifestLink()).not.toBeNull())
    expect(manifestLink()!.getAttribute('href')).toBe('blob:mock')
  })

  // L'interrupteur fermé ne doit rien poser du tout : c'est ce qui évite que l'icône
  // d'installation apparaisse puis disparaisse au chargement.
  it('posts nothing when the app is disabled', async () => {
    mocks.getAppSettings.mockResolvedValue({ ...enabled, 'app.installable': 'false' })

    renderHook(() => useWebAppManifest(), { wrapper })

    await waitFor(() => expect(mocks.getAppSettings).toHaveBeenCalled())
    expect(manifestLink()).toBeNull()
  })

  it('posts nothing when the settings cannot be read', async () => {
    mocks.getAppSettings.mockRejectedValue(new Error('offline'))

    renderHook(() => useWebAppManifest(), { wrapper })

    await waitFor(() => expect(mocks.getAppSettings).toHaveBeenCalled())
    expect(manifestLink()).toBeNull()
  })

  // Sans révocation, chaque passage laisserait un Blob vivant pour la durée du document.
  it('removes the link and revokes its url on unmount', async () => {
    mocks.getAppSettings.mockResolvedValue(enabled)

    const { unmount } = renderHook(() => useWebAppManifest(), { wrapper })
    await waitFor(() => expect(manifestLink()).not.toBeNull())

    unmount()

    expect(manifestLink()).toBeNull()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock')
  })
})
```

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `cd src/frontend && npm test -- useWebAppManifest`
Expected: FAIL — `./useWebAppManifest` introuvable.

- [ ] **Step 3: Écrire le hook**

`src/frontend/src/hooks/useWebAppManifest.ts` :

```ts
import { useEffect } from 'react'
import { buildManifest } from '../lib/webAppManifest'
import { useAppSettings } from './useAppSettings'

/**
 * Pose le manifest que le navigateur lira pour proposer l'installation.
 *
 * Il est construit en mémoire plutôt que servi comme fichier : la spécification veut start_url
 * de même origine que le manifest, ce qui exclut l'API, et le frontend est un tas de fichiers
 * statiques sans route capable de le composer. Un blob: hérite de l'origine du document, donc
 * la vérification passe.
 *
 * Rien n'est posé tant que les réglages n'ont pas répondu « activé ». L'alternative — un
 * manifest statique posé d'emblée puis retiré — faisait apparaître puis disparaître l'icône
 * d'installation, ce qui se lit comme un défaut d'affichage.
 */
export function useWebAppManifest(): void {
  const { data } = useAppSettings()

  useEffect(() => {
    const manifest = buildManifest(data, window.location.origin)
    if (!manifest) return

    const url = URL.createObjectURL(
      new Blob([JSON.stringify(manifest)], { type: 'application/manifest+json' }))
    const link = document.createElement('link')
    link.rel = 'manifest'
    link.href = url
    document.head.appendChild(link)

    return () => {
      link.remove()
      URL.revokeObjectURL(url)
    }
  }, [data])
}
```

- [ ] **Step 4: Lancer le test**

Run: `cd src/frontend && npm test -- useWebAppManifest`
Expected: PASS

- [ ] **Step 5: Monter le hook dans l'application**

Dans `src/frontend/src/App.tsx`, ajouter l'import et le composant, puis le rendre **à l'intérieur
de `QueryClientProvider`** (le hook interroge TanStack) et **au-dessus du routeur**, pour que la
page de login soit couverte :

```tsx
import { useWebAppManifest } from './hooks/useWebAppManifest'

/** Ne rend rien : il pose le <link rel="manifest">. Hors du routeur pour couvrir /login, qui est
    la première page que voit un nouvel utilisateur — et donc là où l'installation se propose. */
function InstallManifest() {
  useWebAppManifest()
  return null
}
```

et dans le JSX de `App` :

```tsx
    <QueryClientProvider client={queryClient}>
      <InstallManifest />
      <ThemeProvider>
```

- [ ] **Step 6: Vérifier lint, types et suite complète**

Run: `cd src/frontend && npm run lint && npm run typecheck && npm test`
Expected: PASS partout.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/hooks/useWebAppManifest.ts \
        src/frontend/src/hooks/useWebAppManifest.test.tsx src/frontend/src/App.tsx
git commit -m "Injection du manifest en blob quand l'application est activee"
```

---

### Task 6: Client `mailto:`

**Files:**
- Create: `src/frontend/src/modules/mail/compose/mailtoSeed.ts`
- Modify: `src/frontend/src/modules/mail/compose/ComposeView.tsx` (la ligne `const seed = state?.seed ?? null`)
- Test: `src/frontend/src/modules/mail/compose/mailtoSeed.test.ts`
- Test: `src/frontend/src/modules/mail/compose/ComposeView.test.tsx` (ajout)

**Interfaces:**
- Consumes: `type ComposeSeed` (`./composeSeed`), `isValidAddress` (`./RecipientsField`).
- Produces: `mailtoSeedFrom(search: string) → ComposeSeed | null`.

- [ ] **Step 1: Écrire le test du parseur (échoue)**

Créer `src/frontend/src/modules/mail/compose/mailtoSeed.test.ts` :

```ts
import { describe, it, expect } from 'vitest'
import { mailtoSeedFrom } from './mailtoSeed'

describe('mailtoSeedFrom', () => {
  it('answers null without a mailto parameter', () => {
    expect(mailtoSeedFrom('')).toBeNull()
    expect(mailtoSeedFrom('?folder=INBOX')).toBeNull()
  })

  it('answers null on anything that is not a mailto url', () => {
    expect(mailtoSeedFrom('?mailto=https%3A%2F%2Fexample.com')).toBeNull()
    expect(mailtoSeedFrom('?mailto=not%20a%20url')).toBeNull()
  })

  it('takes the recipient from the path', () => {
    const seed = mailtoSeedFrom('?mailto=mailto%3Aalice%40weesky.be')!

    expect(seed.to).toEqual(['alice@weesky.be'])
    expect(seed.action).toBe('editAsNew')
  })

  it('reads to, cc, bcc and subject', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent(
        'mailto:alice@weesky.be?cc=bob@weesky.be&bcc=carol@weesky.be&subject=Hello there'))!

    expect(seed.to).toEqual(['alice@weesky.be'])
    expect(seed.cc).toEqual(['bob@weesky.be'])
    expect(seed.bcc).toEqual(['carol@weesky.be'])
    expect(seed.subject).toBe('Hello there')
  })

  it('accepts several comma-separated recipients', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent('mailto:alice@weesky.be,bob@weesky.be'))!

    expect(seed.to).toEqual(['alice@weesky.be', 'bob@weesky.be'])
  })

  // Le lien vient du système d'exploitation, donc du monde extérieur.
  it('drops an address that is not one', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent('mailto:alice@weesky.be,rubbish'))!

    expect(seed.to).toEqual(['alice@weesky.be'])
  })

  it('escapes the body instead of trusting it as html', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent('mailto:alice@weesky.be?body=<img src=x onerror=alert(1)>'))!

    expect(seed.html).not.toContain('<img')
    expect(seed.html).toContain('&lt;img')
  })

  // %0A et non un vrai saut de ligne : l'analyseur d'URL supprime tabulations et sauts de ligne
  // pendant l'analyse, donc un caractère brut ici ne testerait rien.
  it('turns the body newlines into line breaks', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent('mailto:alice@weesky.be?body=one%0Atwo'))!

    expect(seed.html).toContain('one<br>two')
  })

  it('leaves the body empty when the link carries none', () => {
    expect(mailtoSeedFrom('?mailto=mailto%3Aalice%40weesky.be')!.html).toBe('')
  })

  it('carries nothing a reply would carry', () => {
    const seed = mailtoSeedFrom('?mailto=mailto%3Aalice%40weesky.be')!

    expect(seed.attachments).toEqual([])
    expect(seed.inReplyTo).toBeNull()
    expect(seed.references).toEqual([])
    expect(seed.draftRef).toBeNull()
    expect(seed.fromAddress).toBeNull()
  })
})
```

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `cd src/frontend && npm test -- mailtoSeed`
Expected: FAIL — `./mailtoSeed` introuvable.

- [ ] **Step 3: Écrire le parseur**

`src/frontend/src/modules/mail/compose/mailtoSeed.ts` :

```ts
import type { ComposeSeed } from './composeSeed'
import { isValidAddress } from './RecipientsField'

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;')
}

function addressesOf(...raw: string[]): string[] {
  return raw
    .flatMap(value => value.split(','))
    .map(value => value.trim())
    .filter(isValidAddress)
}

function decode(value: string): string {
  try {
    return decodeURIComponent(value)
  } catch {
    return value
  }
}

/**
 * Une URL mailto: (RFC 6068) devient la graine que le composeur sait déjà ouvrir.
 *
 * Elle arrive du système d'exploitation, donc du monde extérieur : le corps est du texte brut
 * et est échappé avant d'entrer dans un éditeur HTML, et les adresses passent par le même
 * contrôle que celles qu'un utilisateur tape. Les en-têtes autres que to, cc, bcc, subject et
 * body sont ignorés.
 */
export function mailtoSeedFrom(search: string): ComposeSeed | null {
  const raw = new URLSearchParams(search).get('mailto')
  if (!raw) return null

  let url: URL
  try {
    url = new URL(raw)
  } catch {
    return null
  }
  if (url.protocol !== 'mailto:') return null

  const params = url.searchParams
  const body = params.get('body') ?? ''

  return {
    action: 'editAsNew',
    to: addressesOf(decode(url.pathname), params.get('to') ?? ''),
    cc: addressesOf(params.get('cc') ?? ''),
    bcc: addressesOf(params.get('bcc') ?? ''),
    subject: params.get('subject') ?? '',
    html: body ? `<div>${escapeHtml(body).replace(/\r?\n/g, '<br>')}</div>` : '',
    fromAddress: null,
    attachments: [],
    inReplyTo: null,
    references: [],
    draftRef: null,
    nameHints: {},
  }
}
```

- [ ] **Step 4: Lancer le test**

Run: `cd src/frontend && npm test -- mailtoSeed`
Expected: PASS

- [ ] **Step 5: Raccorder le composeur**

Dans `src/frontend/src/modules/mail/compose/ComposeView.tsx`, ajouter l'import :

```tsx
import { mailtoSeedFrom } from './mailtoSeed'
```

puis remplacer

```tsx
  const seed = state?.seed ?? null
```

par

```tsx
  // Un mailto: arrive par l'URL, pas par l'état d'historique : le système d'exploitation ouvre
  // une adresse froide, sans navigation React derrière elle.
  const seed = useMemo(
    () => state?.seed ?? mailtoSeedFrom(location.search), [state?.seed, location.search])
```

- [ ] **Step 6: Écrire le test du raccordement**

Dans `src/frontend/src/modules/mail/compose/ComposeView.test.tsx`, ouvrir l'aide existante à une
chaîne de recherche plutôt que d'en introduire une seconde. La signature devient :

```tsx
function renderCompose(from = 'INBOX', seed?: ComposeSeed, search = '') {
```

et l'entrée d'historique porte cette chaîne :

```tsx
    { initialEntries: ['/mail', { pathname: '/mail/compose', search, state: { from, seed } }], initialIndex: 1 },
```

Les appels existants gardent leur comportement : `search` vaut `''` par défaut, et un `seed`
absent laisse `state.seed` à `undefined`, que le composeur traite déjà comme « pas de graine ».

Puis ajouter le cas :

```tsx
  it('opens prefilled from a mailto url carrying no seed', async () => {
    renderCompose('INBOX', undefined,
      '?mailto=' + encodeURIComponent('mailto:alice@weesky.be?subject=Hello'))

    expect(await screen.findByDisplayValue('Hello')).toBeInTheDocument()
    expect(screen.getByText('alice@weesky.be')).toBeInTheDocument()
  })
```

- [ ] **Step 7: Lancer les tests du composeur**

Run: `cd src/frontend && npm test -- ComposeView mailtoSeed`
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add src/frontend/src/modules/mail/compose/mailtoSeed.ts \
        src/frontend/src/modules/mail/compose/mailtoSeed.test.ts \
        src/frontend/src/modules/mail/compose/ComposeView.tsx \
        src/frontend/src/modules/mail/compose/ComposeView.test.tsx
git commit -m "Composeur: ouverture depuis un lien mailto, corps echappe"
```

---

### Task 7: Onglet Administration « Application »

**Files:**
- Create: `src/frontend/src/modules/settings/admin/ApplicationTab.tsx`
- Modify: `src/frontend/src/modules/settings/admin/AdminPage.jsx`
- Test: `src/frontend/src/modules/settings/admin/ApplicationTab.test.tsx`

**Interfaces:**
- Consumes: `useAppSettings`, `useSetAppSetting`, `installableOf`, `APP_SETTING_KEYS` (Task 3).
- Produces: `ApplicationTab` (export par défaut), props `{ addToast: (message: string, kind?: string) => void }`.

- [ ] **Step 1: Écrire le test (échoue)**

Créer `src/frontend/src/modules/settings/admin/ApplicationTab.test.tsx` :

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import ApplicationTab from './ApplicationTab'

const mocks = vi.hoisted(() => ({ getAppSettings: vi.fn(), setAppSetting: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

const addToast = vi.fn()

function renderTab(settings: Record<string, string> = {
  'app.installable': 'true', 'app.name': 'Snoopy mail', 'app.shortName': 'Snoopy',
}) {
  mocks.getAppSettings.mockResolvedValue(settings)
  mocks.setAppSetting.mockResolvedValue(undefined)
  return render(<ApplicationTab addToast={addToast} />, { wrapper })
}

describe('ApplicationTab', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows the stored values, not values of its own', async () => {
    renderTab({ 'app.installable': 'true', 'app.name': 'Weesky Mail', 'app.shortName': 'Weesky' })

    expect(await screen.findByLabelText('Application name')).toHaveValue('Weesky Mail')
    expect(screen.getByLabelText('Short name')).toHaveValue('Weesky')
    expect(screen.getByLabelText('Enable app installation')).toBeChecked()
  })

  it('saves the toggle as soon as it is flipped', async () => {
    renderTab()
    const toggle = await screen.findByLabelText('Enable app installation')

    await userEvent.click(toggle)

    await waitFor(() => expect(mocks.setAppSetting)
      .toHaveBeenCalledWith('app.installable', 'false'))
  })

  // Nommer une application qu'on n'expose pas n'a pas de sens ; les griser le dit sans retirer
  // les valeurs de l'écran.
  it('disables the names while the app is off', async () => {
    renderTab({ 'app.installable': 'false', 'app.name': 'Snoopy mail', 'app.shortName': 'Snoopy' })

    expect(await screen.findByLabelText('Application name')).toBeDisabled()
    expect(screen.getByLabelText('Short name')).toBeDisabled()
  })

  it('saves both names on Save', async () => {
    renderTab()
    const name = await screen.findByLabelText('Application name')

    await userEvent.clear(name)
    await userEvent.type(name, 'Weesky Mail')
    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(mocks.setAppSetting).toHaveBeenCalledWith('app.name', 'Weesky Mail'))
    expect(mocks.setAppSetting).toHaveBeenCalledWith('app.shortName', 'Snoopy')
  })

  it('reports a refused save instead of claiming success', async () => {
    renderTab()
    mocks.setAppSetting.mockRejectedValue(new Error('Short name is too long'))
    await screen.findByLabelText('Application name')

    await userEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Short name is too long', 'error'))
  })
})
```

- [ ] **Step 2: Lancer le test pour vérifier qu'il échoue**

Run: `cd src/frontend && npm test -- ApplicationTab`
Expected: FAIL — `./ApplicationTab` introuvable.

- [ ] **Step 3: Écrire l'onglet**

`src/frontend/src/modules/settings/admin/ApplicationTab.tsx` :

```tsx
import { useEffect, useState } from 'react'
import LoadingBlock from '../../../components/LoadingBlock'
import {
  APP_SETTING_KEYS, installableOf, useAppSettings, useSetAppSetting,
} from '../../../hooks/useAppSettings'

interface Props {
  addToast: (message: string, kind?: string) => void
}

/**
 * Le webmail s'annonce-t-il comme application installable, et sous quel nom.
 *
 * Réglage d'instance, donc réservé à l'administration : le nom est celui que verront tous les
 * utilisateurs sous l'icône. Couper l'interrupteur ne désinstalle personne — une application
 * déjà installée le reste, elle cesse seulement d'être proposée aux autres.
 */
export default function ApplicationTab({ addToast }: Props) {
  const { data: settings, isLoading, isError } = useAppSettings()
  const setSetting = useSetAppSetting()
  const [name, setName] = useState('')
  const [shortName, setShortName] = useState('')

  // Les champs sont réamorcés sur la réponse du serveur : après un enregistrement c'est la
  // valeur retenue, après un refus c'est l'état du serveur plutôt qu'un mensonge optimiste.
  useEffect(() => {
    if (!settings) return
    setName(settings[APP_SETTING_KEYS.name] ?? '')
    setShortName(settings[APP_SETTING_KEYS.shortName] ?? '')
  }, [settings])

  if (isLoading) return <LoadingBlock />
  if (isError || !settings) return <p>Could not load the settings.</p>

  const enabled = installableOf(settings)

  async function save(key: string, value: string, message: string) {
    try {
      await setSetting.mutateAsync({ key, value })
      addToast(message)
    } catch (error) {
      addToast(error instanceof Error ? error.message : 'Could not save the setting', 'error')
    }
  }

  async function saveNames() {
    try {
      await setSetting.mutateAsync({ key: APP_SETTING_KEYS.name, value: name })
      await setSetting.mutateAsync({ key: APP_SETTING_KEYS.shortName, value: shortName })
      addToast('The app name was saved')
    } catch (error) {
      addToast(error instanceof Error ? error.message : 'Could not save the name', 'error')
    }
  }

  return (
    <>
      {/* .field-h pose le libellé à côté du contrôle : sans la paire htmlFor/id le contrôle
          n'a aucun nom accessible. */}
      <div className="field-h is-setting">
        <label htmlFor="app-installable">Enable app installation</label>
        <label className="toggle-switch">
          <input
            id="app-installable"
            type="checkbox"
            checked={enabled}
            disabled={setSetting.isPending}
            onChange={event => save(
              APP_SETTING_KEYS.installable, String(event.target.checked),
              event.target.checked
                ? 'The webmail can now be installed as an app'
                : 'The webmail is no longer offered for installation')}
          />
          <span className="toggle-track" />
        </label>
      </div>

      <div className="field-h is-setting">
        <label htmlFor="app-name">Application name</label>
        <input
          id="app-name"
          type="text"
          maxLength={60}
          value={name}
          disabled={!enabled || setSetting.isPending}
          onChange={event => setName(event.target.value)}
        />
      </div>

      <div className="field-h is-setting">
        <label htmlFor="app-short-name">Short name</label>
        <input
          id="app-short-name"
          type="text"
          maxLength={12}
          value={shortName}
          disabled={!enabled || setSetting.isPending}
          onChange={event => setShortName(event.target.value)}
        />
      </div>

      <button
        type="button"
        className="btn-primary"
        disabled={!enabled || setSetting.isPending}
        onClick={saveNames}
      >
        Save
      </button>
    </>
  )
}
```

- [ ] **Step 4: Lancer le test**

Run: `cd src/frontend && npm test -- ApplicationTab`
Expected: PASS

- [ ] **Step 5: Ajouter l'onglet à la barre**

Dans `src/frontend/src/modules/settings/admin/AdminPage.jsx` — l'import :

```jsx
import ApplicationTab from './ApplicationTab'
```

l'aide, dans `ADMIN_HELP` :

```jsx
  application: "Offers the webmail for installation as a desktop app: browsers then show an install icon in the address bar, and the app opens in its own window. The name and short name are what users see under the icon. Switching this off stops offering it — it does not uninstall the app for anyone who already installed it.",
```

le bouton, après *Virtual domains* :

```jsx
            <button className={`admin-tab${activeTab === 'application' ? ' is-active' : ''}`}
              onClick={() => setActiveTab('application')}>Application</button>
```

et le rendu, après `VirtualDomainsTab` :

```jsx
            {activeTab === 'application' && <ApplicationTab addToast={addToast} />}
```

- [ ] **Step 6: Vérifier lint, types, suite complète et build**

Run: `cd src/frontend && npm run lint && npm run typecheck && npm test && npm run build`
Expected: PASS partout.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/modules/settings/admin/ApplicationTab.tsx \
        src/frontend/src/modules/settings/admin/ApplicationTab.test.tsx \
        src/frontend/src/modules/settings/admin/AdminPage.jsx
git commit -m "Administration: onglet Application pour activer et nommer la PWA"
```

---

## Vérification finale, après la dernière tâche

Ce que les tests ne peuvent pas prouver — jsdom n'a pas de critères d'installabilité :

- [ ] Appliquer `docs/superpowers/webmail-app-settings-table.md` sur `snoopy_webmail_dev`.
- [ ] Pousser la branche (le déploiement est automatique, environnement `dev` hors `master`).
- [ ] Sur `account-dev`, ouvrir Administration → Application, activer, enregistrer les deux noms.
- [ ] Recharger, et vérifier que **Edge** affiche l'icône d'installation dans la barre d'adresse.
- [ ] Installer, puis vérifier : la fenêtre s'ouvre sans onglets, le nom court est sous l'icône,
      le clic droit sur l'icône de la barre des tâches offre *New message* et *Contacts*.
- [ ] Couper l'interrupteur, recharger dans un profil qui n'a pas installé l'application, et
      vérifier qu'aucune icône n'apparaît — et surtout qu'elle n'apparaît pas fugitivement.
- [ ] Cliquer un lien `mailto:` depuis une autre application et vérifier que le composeur s'ouvre
      prérempli. (Edge propose l'enregistrement comme client `mailto:` après l'installation ;
      l'accord de l'utilisateur est requis, il ne se fait pas d'office.)
