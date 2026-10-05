# Migrations de schéma automatiques — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Faire évoluer le schéma de `scotty_webmail` par des scripts SQL numérotés que DbUp applique, au démarrage de l'API ou par `scotty.microservice migrate`, avec un compte de schéma distinct du compte du service.

**Architecture:** Une classe statique `SchemaMigrations` (core, `Data/`) charge les scripts embarqués `Data/Migrations/NNNN_nom.sql`, dit lesquels manquent, les applique sous un verrou nommé, et porte les deux points d'entrée : `EnsureCurrent` (démarrage, après `builder.Build()`) et `RunCommand` (`migrate`, en tête de `Program.cs`). Les tests SQL tournent sur un MariaDB Testcontainers, sautés sans Docker hors CI.

**Tech Stack:** ASP.NET Core .NET 10, DbUp (`dbup-mysql`), MySqlConnector, EF Core/Pomelo, xUnit 2.9, Testcontainers.MariaDb, Xunit.SkippableFact ; GitHub Actions.

**Spec:** `docs/superpowers/2026-10-05-schema-migrations-design.md`

## Global Constraints

- Journal : table `schema_migrations`, dans la base de la chaîne de connexion. Un script ne nomme jamais la base (`USE`, `scotty_webmail.`).
- Noms de scripts : `NNNN_nom.sql` (quatre chiffres, minuscules, `_`), numérotation continue depuis `0001`. Le journal enregistre ce nom court, pas le nom de ressource.
- Variables DbUp désactivées (`WithVariablesDisabled`).
- Verrou : `GET_LOCK('scotty_webmail_schema', 60)` sur une connexion dédiée, tenue pendant toute la mise à jour.
- Chaînes : `ConnectionStrings:WebmailPreferencesDatabase` (service, inchangée) et `ConnectionStrings:WebmailSchema` (schéma, facultative).
- Message de refus, exactement : `Schema is behind: {noms séparés par ", "} not applied. Run \`scotty.microservice migrate\`, or set ConnectionStrings__WebmailSchema.`
- `migrate` : chaîne lue dans `ConnectionStrings__WebmailSchema`, sinon première ligne de l'entrée standard ; code 0 si la base est à jour à la fin, 1 sinon.
- Une modification de structure par script (règle pour les migrations futures ; `0001` est l'exception, c'est l'état initial).
- Le compte du service garde `SELECT, INSERT, UPDATE, DELETE`.
- Messages et logs en anglais ; logs structurés (`ILogger`, pas d'interpolation).
- Commentaires seulement quand le code ne s'explique pas, 3 lignes max. Pas de duplication.
- Tests backend : `dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés). Avant chaque commit, `git checkout -- src/scotty.microservice/ApiDocumentation.xml` si `git status` le montre modifié.
- Dépôt en `autocrlf=true` : remplacements multi-lignes dans un fichier existant par script python qui respecte le `\r\n` du fichier ; vérifier `git diff`.
- Commits : deux lignes max, anglais, heredoc `git commit -F - <<'EOF'`, terminés par `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Ne jamais pousser.

## Review Focus

1. Une base créée par l'ancien `install.sql` (tables présentes, pas de `schema_migrations`) avec le script de mise en route : `0001` est vu comme appliqué, rien n'est rejoué, l'API démarre. Test dans la tâche 2 (journal pré-rempli à la main avec le nom exact).
2. Le compte du service sans droit de création, sur une base sans `schema_migrations` : la vérification de démarrage lit sans erreur et nomme `0001_initial.sql`, au lieu d'échouer sur un `CREATE`. Test dans la tâche 2 (compte `SELECT` seul).
3. Un script dont un commentaire ou une chaîne contient `;` ou `$` : appliqué tel quel (découpage et variables). Test dans la tâche 2 (`0001` en contient ; un script de test le vérifie explicitement).
4. `migrate` avec une chaîne fausse ou une base injoignable : code 1 et un message lisible, pas de pile d'appels. Test dans la tâche 3.
5. Un `deploy.yml` dont l'étape `migrate` échoue : le service n'est pas redémarré. Vérifié à la lecture du YAML (tâche 5, `set -e` et ordre des commandes).

---

### Task 1: Les scripts embarqués et `0001_initial.sql`

**Files:**
- Create: `src/scotty.microservice/Data/Migrations/0001_initial.sql`
- Create: `src/scotty.microservice/Data/SchemaMigrations.cs`
- Modify: `src/scotty.microservice/scotty.microservice.core.csproj` (paquet `dbup-mysql`, ressources `Data\Migrations\*.sql`)
- Test: `src/scotty.microservice/scotty.microservice.Tests/Data/SchemaMigrationsScriptsTests.cs` (create)

**Interfaces:**
- Produces (namespace `weesky.Scotty.Microservice.Data`) :
  - `internal static class SchemaMigrations`
  - `public const string JournalTable = "schema_migrations";`
  - `public static IReadOnlyList<SqlScript> Embedded { get; }` — triés par nom ordinal, nom court (`0001_initial.sql`)
  - `internal static IReadOnlyList<SqlScript> Load(Assembly assembly)`

- [ ] **Step 1: Ajouter le paquet et les ressources**

```bash
cd src/scotty.microservice && dotnet add scotty.microservice.core.csproj package dbup-mysql
```

Dans `scotty.microservice.core.csproj`, un `ItemGroup` :

```xml
  <ItemGroup>
    <EmbeddedResource Include="Data\Migrations\*.sql" />
  </ItemGroup>
```

- [ ] **Step 2: Écrire `0001_initial.sql` à partir d'`install.sql`**

Copier les sections 3 (« Tables », à partir de la ligne `-- ---` qui précède `--  3. Tables`) et 4 (« Keys and indexes ») d'`install/install.sql`, jusqu'à la ligne `-- ---` qui précède `--  5. Verification`, exclue. Ni `CREATE DATABASE`, ni `USE`, ni `CREATE USER`, ni `GRANT`, ni la vérification. En tête, ajouter :

```sql
-- The schema as install/install.sql created it before schema migrations existed (27 tables).
-- Never edit: a database that has run it will not run it again. Change the schema in a new script.
SET NAMES utf8mb4;
```

Vérifier : `grep -c "^CREATE TABLE" src/scotty.microservice/Data/Migrations/0001_initial.sql` → 27 ; `grep -nE "USE |scotty_webmail\`?\.|GRANT|CREATE USER" …` → rien.

- [ ] **Step 3: Write the failing test**

`src/scotty.microservice/scotty.microservice.Tests/Data/SchemaMigrationsScriptsTests.cs` :

```csharp
using System.Text.RegularExpressions;
using weesky.Scotty.Microservice.Data;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Data;

/// <summary>A shipped script is never edited and never renumbered: the journal knows it by name.</summary>
public sealed class SchemaMigrationsScriptsTests
{
    [Fact]
    public void Embedded_StartsWithTheInitialSchema()
    {
        var first = SchemaMigrations.Embedded[0];

        Assert.Equal("0001_initial.sql", first.Name);
        Assert.Equal(27, Regex.Matches(first.Contents, @"^CREATE TABLE", RegexOptions.Multiline).Count);
    }

    [Fact]
    public void Embedded_AreNumberedFromOneWithoutGapOrDuplicate()
    {
        var names = SchemaMigrations.Embedded.Select(s => s.Name).ToList();

        Assert.All(names, name => Assert.Matches(@"^\d{4}_[a-z0-9_]+\.sql$", name));
        Assert.Equal(Enumerable.Range(1, names.Count), names.Select(n => int.Parse(n[..4])));
    }

    [Fact]
    public void Embedded_NeverNameTheDatabase()
    {
        Assert.All(SchemaMigrations.Embedded, script =>
            Assert.DoesNotMatch(@"(?im)^\s*USE\s|`?scotty_webmail`?\.", script.Contents));
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~SchemaMigrationsScriptsTests"`
Expected: FAIL à la compilation — `SchemaMigrations` n'existe pas.

- [ ] **Step 5: Implement**

`src/scotty.microservice/Data/SchemaMigrations.cs` :

```csharp
using System.Reflection;
using DbUp.Engine;

namespace weesky.Scotty.Microservice.Data;

/// <summary>
/// The scotty_webmail schema as numbered SQL scripts (Data/Migrations), applied by DbUp and journalled
/// in <see cref="JournalTable"/> under their short name.
/// </summary>
internal static class SchemaMigrations
{
    public const string JournalTable = "schema_migrations";
    private const string ResourceMarker = ".Migrations.";

    public static IReadOnlyList<SqlScript> Embedded { get; } = Load(typeof(SchemaMigrations).Assembly);

    internal static IReadOnlyList<SqlScript> Load(Assembly assembly) =>
    [
        .. assembly.GetManifestResourceNames()
            .Where(name => name.Contains(ResourceMarker, StringComparison.Ordinal)
                           && name.EndsWith(".sql", StringComparison.Ordinal))
            .Select(name => new SqlScript(
                name[(name.LastIndexOf(ResourceMarker, StringComparison.Ordinal) + ResourceMarker.Length)..],
                Read(assembly, name)))
            .OrderBy(script => script.Name, StringComparer.Ordinal),
    ];

    private static string Read(Assembly assembly, string resource)
    {
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        return reader.ReadToEnd();
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~SchemaMigrationsScriptsTests"`
Expected: PASS (3).

- [ ] **Step 7: Commit**

```bash
git checkout -- src/scotty.microservice/ApiDocumentation.xml 2>/dev/null
git add src/scotty.microservice/Data src/scotty.microservice/scotty.microservice.core.csproj \
  src/scotty.microservice/scotty.microservice.Tests/Data/SchemaMigrationsScriptsTests.cs
git commit -F - <<'EOF'
Embed the schema as numbered DbUp scripts, starting with the current 27 tables

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 2: Appliquer et vérifier, sur un vrai MariaDB

**Files:**
- Modify: `src/scotty.microservice/Data/SchemaMigrations.cs`
- Modify: `src/scotty.microservice/scotty.microservice.Tests/scotty.microservice.Tests.csproj` (paquets `Testcontainers.MariaDb`, `Xunit.SkippableFact`, `MySqlConnector` si non transitif)
- Create: `src/scotty.microservice/scotty.microservice.Tests/Infrastructure/MariaDbFixture.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Data/SchemaMigrationsDatabaseTests.cs` (create)

**Interfaces:**
- Consumes: `SchemaMigrations.Embedded`, `JournalTable` (tâche 1).
- Produces :
  - `public static IReadOnlyList<string> Pending(string connectionString, IReadOnlyList<SqlScript>? scripts = null)` — lecture seule, n'écrit rien, ne crée pas le journal
  - `public static IReadOnlyList<string> Apply(string schemaConnectionString, IReadOnlyList<SqlScript>? scripts = null)` — noms appliqués, dans l'ordre ; lève `InvalidOperationException` (« Schema migration {nom} failed: {erreur} » ou « Could not take the schema lock within 60 s… »)
  - `MariaDbFixture` : `bool Unavailable`, `string RootConnectionString`, `Task<string> NewDatabaseAsync()` (base vide `utf8mb4_bin`, renvoie sa chaîne root), `Task<string> NewReadOnlyUserAsync(string database)` (compte `SELECT` seul, renvoie sa chaîne)

- [ ] **Step 1: Ajouter les paquets de test**

```bash
cd src/scotty.microservice/scotty.microservice.Tests
dotnet add package Testcontainers.MariaDb
dotnet add package Xunit.SkippableFact
```

- [ ] **Step 2: La fixture**

`Infrastructure/MariaDbFixture.cs` :

```csharp
using MySqlConnector;
using Testcontainers.MariaDb;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Infrastructure;

/// <summary>
/// One MariaDB per test run, a fresh database per test. Without Docker the tests are skipped on a
/// workstation and fail on CI, where a skipped schema test would be a silent hole.
/// </summary>
public sealed class MariaDbFixture : IAsyncLifetime
{
    private readonly MariaDbContainer _container = new MariaDbBuilder().WithImage("mariadb:11.4").Build();

    public bool Unavailable { get; private set; }
    public string RootConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
        }
        catch (Exception) when (Environment.GetEnvironmentVariable("CI") != "true")
        {
            Unavailable = true;
            return;
        }

        RootConnectionString = new MySqlConnectionStringBuilder(_container.GetConnectionString())
        {
            UserID = "root",
            Database = "",
        }.ConnectionString;
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<string> NewDatabaseAsync()
    {
        var name = $"t_{Guid.NewGuid():N}";
        await ExecuteAsync($"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_bin");
        return new MySqlConnectionStringBuilder(RootConnectionString) { Database = name }.ConnectionString;
    }

    public async Task<string> NewReadOnlyUserAsync(string database)
    {
        var user = $"r{Guid.NewGuid():N}"[..16];
        await ExecuteAsync($"CREATE USER '{user}'@'%' IDENTIFIED BY 'readonly'");
        await ExecuteAsync($"GRANT SELECT ON `{database}`.* TO '{user}'@'%'");
        return new MySqlConnectionStringBuilder(RootConnectionString)
        {
            UserID = user, Password = "readonly", Database = database,
        }.ConnectionString;
    }

    public async Task ExecuteAsync(string sql, string? connectionString = null)
    {
        await using var connection = new MySqlConnection(connectionString ?? RootConnectionString);
        await connection.OpenAsync();
        await new MySqlCommand(sql, connection).ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(nameof(MariaDbCollection))]
public sealed class MariaDbCollection : ICollectionFixture<MariaDbFixture>;
```

`CollectionDefinition` et la fixture dans un même fichier contredisent « un type par fichier » : mettre `MariaDbCollection` dans `Infrastructure/MariaDbCollection.cs`. Si `MARIADB_ROOT_PASSWORD` n'est pas le mot de passe de la chaîne par défaut (vérifier en exécutant), le fixer avec `.WithPassword(...)` sur le builder et l'utiliser pour `root`.

- [ ] **Step 3: Write the failing tests**

`Data/SchemaMigrationsDatabaseTests.cs` :

```csharp
using DbUp.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MySqlConnector;
using weesky.Scotty.Microservice.Data;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Data;

[Collection(nameof(MariaDbCollection))]
public sealed class SchemaMigrationsDatabaseTests(MariaDbFixture db)
{
    private static readonly IReadOnlyList<SqlScript> TwoScripts =
    [
        SchemaMigrations.Embedded[0],
        new("0002_probe.sql", "-- a comment; with $dollar$ and a semicolon\nCREATE TABLE probe (id int NOT NULL);"),
    ];

    [SkippableFact]
    public async Task Apply_CreatesTheWholeSchemaOnAnEmptyDatabase()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();

        Assert.Equal(["0001_initial.sql"], SchemaMigrations.Apply(connection));

        Assert.Equal(27 + 1, await CountTablesAsync(connection));   // + schema_migrations
        Assert.Empty(SchemaMigrations.Pending(connection));
    }

    [SkippableFact]
    public async Task Apply_TwiceAppliesNothingTheSecondTime()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();
        SchemaMigrations.Apply(connection);

        Assert.Empty(SchemaMigrations.Apply(connection));
    }

    [SkippableFact]
    public async Task Apply_RunsScriptsHoldingSemicolonsAndDollarsAsWritten()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();

        Assert.Equal(["0001_initial.sql", "0002_probe.sql"], SchemaMigrations.Apply(connection, TwoScripts));
        Assert.Equal(27 + 2, await CountTablesAsync(connection));
    }

    [SkippableFact]
    public async Task Pending_NamesEveryScriptUnderAReadOnlyAccountWithoutWritingAnything()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var root = await db.NewDatabaseAsync();
        var readOnly = await db.NewReadOnlyUserAsync(new MySqlConnectionStringBuilder(root).Database);

        Assert.Equal(["0001_initial.sql"], SchemaMigrations.Pending(readOnly));
        Assert.Equal(0, await CountTablesAsync(root));
    }

    /// <summary>
    /// The owner's databases were created by the old install.sql and are adopted by hand: the journal
    /// table and its one row are written by the bootstrap script, then nothing must run again.
    /// </summary>
    [SkippableFact]
    public async Task Pending_TrustsAJournalWrittenByTheBootstrapScript()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();
        await db.ExecuteAsync(SchemaMigrations.Embedded[0].Contents, connection);
        await db.ExecuteAsync(await File.ReadAllTextAsync(BootstrapJournalPath()), connection);

        Assert.Empty(SchemaMigrations.Pending(connection));
        Assert.Empty(SchemaMigrations.Apply(connection));
    }

    [SkippableFact]
    public async Task Apply_DoesNotJournalAFailedScriptAndNamesIt()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();
        IReadOnlyList<SqlScript> broken = [SchemaMigrations.Embedded[0], new("0002_broken.sql", "CREATE TABLE ;")];

        var error = Assert.Throws<InvalidOperationException>(() => SchemaMigrations.Apply(connection, broken));

        Assert.Contains("0002_broken.sql", error.Message);
        Assert.Equal(["0002_broken.sql"], SchemaMigrations.Pending(connection, broken));
    }

    [SkippableFact]
    public async Task Apply_RunsEachScriptOnceWhenTwoStartsMigrateTogether()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();

        var applied = await Task.WhenAll(
            Task.Run(() => SchemaMigrations.Apply(connection)),
            Task.Run(() => SchemaMigrations.Apply(connection)));

        Assert.Equal(["0001_initial.sql"], applied.SelectMany(names => names));
    }

    [SkippableFact]
    public async Task TheSchemaHoldsEveryTableAndColumnTheModelMaps()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();
        SchemaMigrations.Apply(connection);
        var columns = await ColumnsAsync(connection);

        using var context = new PreferencesDbContext(new DbContextOptionsBuilder<PreferencesDbContext>()
            .UseMySql(connection, ServerVersion.AutoDetect(connection)).Options);
        var missing = context.Model.GetEntityTypes()
            .Where(entity => entity.GetTableName() is not null)
            .SelectMany(entity => entity.GetProperties().Select(property =>
            {
                var table = entity.GetTableName()!;
                return $"{table}.{property.GetColumnName(StoreObjectIdentifier.Table(table, entity.GetSchema()))}";
            }))
            .Where(column => !columns.Contains(column))
            .ToList();

        Assert.Empty(missing);
    }

    private static string BootstrapJournalPath() =>
        Path.Combine(AppContext.BaseDirectory, "Data", "schema-migrations-bootstrap-journal.sql");

    private static async Task<int> CountTablesAsync(string connection) =>
        Convert.ToInt32(await ScalarAsync(connection,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE()"));

    private static async Task<HashSet<string>> ColumnsAsync(string connection)
    {
        await using var c = new MySqlConnection(connection);
        await c.OpenAsync();
        var reader = await new MySqlCommand(
            "SELECT CONCAT(table_name, '.', column_name) FROM information_schema.columns WHERE table_schema = DATABASE()",
            c).ExecuteReaderAsync();
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync()) set.Add(reader.GetString(0));
        return set;
    }

    private static async Task<object?> ScalarAsync(string connection, string sql)
    {
        await using var c = new MySqlConnection(connection);
        await c.OpenAsync();
        return await new MySqlCommand(sql, c).ExecuteScalarAsync();
    }
}
```

`schema-migrations-bootstrap-journal.sql` est la moitié « journal » du script de mise en route (tâche 5) : il vit dans `docs/operations/schema-migrations-bootstrap.md` pour le propriétaire, et dans `scotty.microservice.Tests/Data/schema-migrations-bootstrap-journal.sql` (copié à la sortie : `<None Include="Data\*.sql" CopyToOutputDirectory="PreserveNewest" />`) pour ce test. Il est écrit à l'étape 5 de cette tâche, à partir de ce que DbUp crée réellement.

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~SchemaMigrationsDatabaseTests"`
Expected: FAIL à la compilation — `Pending` et `Apply` n'existent pas. Docker doit tourner sur le poste (sinon tout est sauté : le lancer pour cette tâche).

- [ ] **Step 5: Implement**

Ajouter à `SchemaMigrations` :

```csharp
    private const string LockName = "scotty_webmail_schema";
    private const int LockTimeoutSeconds = 60;

    public static IReadOnlyList<string> Pending(string connectionString, IReadOnlyList<SqlScript>? scripts = null) =>
        [.. Engine(connectionString, scripts ?? Embedded).GetScriptsToExecute().Select(script => script.Name)];

    // The lock is held on a connection of its own for the whole upgrade: a second start waits, then
    // finds nothing left to do. It dies with the connection if the process does.
    public static IReadOnlyList<string> Apply(string schemaConnectionString, IReadOnlyList<SqlScript>? scripts = null)
    {
        using var lockConnection = new MySqlConnection(schemaConnectionString);
        lockConnection.Open();
        if (Scalar(lockConnection, $"SELECT GET_LOCK('{LockName}', {LockTimeoutSeconds})") is not 1L)
            throw new InvalidOperationException(
                $"Could not take the schema lock within {LockTimeoutSeconds} s: another start is migrating this database.");

        try
        {
            var result = Engine(schemaConnectionString, scripts ?? Embedded).PerformUpgrade();
            if (!result.Successful)
                throw new InvalidOperationException(
                    $"Schema migration {result.ErrorScript?.Name} failed: {result.Error.Message}", result.Error);
            return [.. result.Scripts.Select(script => script.Name)];
        }
        finally
        {
            Scalar(lockConnection, $"SELECT RELEASE_LOCK('{LockName}')");
        }
    }

    private static UpgradeEngine Engine(string connectionString, IReadOnlyList<SqlScript> scripts) =>
        DeployChanges.To.MySqlDatabase(connectionString)
            .WithScripts(scripts)
            .JournalToMySqlTable(new MySqlConnectionStringBuilder(connectionString).Database, JournalTable)
            .WithVariablesDisabled()
            .LogToNowhere()
            .Build();

    private static object? Scalar(MySqlConnection connection, string sql) =>
        new MySqlCommand(sql, connection).ExecuteScalar();
```

(usings : `DbUp`, `DbUp.Engine`, `MySqlConnector`.) Si `GET_LOCK` renvoie un autre type que `long` avec ce pilote, comparer par `Convert.ToInt64`. Si `GetScriptsToExecute()` tente d'écrire sous le compte en lecture seule (le test le dira), lire le journal soi-même : `information_schema.tables` puis `SELECT scriptname FROM schema_migrations`, et soustraire.

Puis écrire le journal de mise en route : appliquer `0001` sur une base de test, relever `SHOW CREATE TABLE schema_migrations` et la ligne insérée, et écrire `scotty.microservice.Tests/Data/schema-migrations-bootstrap-journal.sql` :

```sql
CREATE TABLE schema_migrations (<colonnes exactes relevées>) <options relevées>;
INSERT INTO schema_migrations (scriptname, applied) VALUES ('0001_initial.sql', UTC_TIMESTAMP());
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~SchemaMigrations"`
Expected: PASS (8 tests base + 3 scripts). Si `TheSchemaHoldsEveryTableAndColumnTheModelMaps` liste des colonnes manquantes, c'est un écart réel entre `install.sql` et le modèle : ne pas toucher `0001` (c'est l'état des bases en service) ; le signaler dans le ledger et dans le rapport, avec la liste.

- [ ] **Step 7: Run the whole backend suite**

Run: `dotnet test src/scotty.microservice.sln`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git checkout -- src/scotty.microservice/ApiDocumentation.xml 2>/dev/null
git add src/scotty.microservice/Data/SchemaMigrations.cs src/scotty.microservice/scotty.microservice.Tests
git commit -F - <<'EOF'
Apply pending schema scripts under a named lock, tested against a real MariaDB

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 3: Démarrage et commande `migrate`

**Files:**
- Modify: `src/scotty.microservice/Data/SchemaMigrations.cs`
- Modify: `src/scotty.microservice.host/Program.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Data/SchemaMigrationsEntryPointsTests.cs` (create)

**Interfaces:**
- Consumes: `Pending`, `Apply` (tâche 2).
- Produces :
  - `public static void EnsureCurrent(IConfiguration configuration, ILogger logger)`
  - `public static int RunCommand(string? fromEnvironment, TextReader input, TextWriter output, TextWriter error)`

- [ ] **Step 1: Write the failing tests**

`Data/SchemaMigrationsEntryPointsTests.cs` :

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using weesky.Scotty.Microservice.Data;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Data;

[Collection(nameof(MariaDbCollection))]
public sealed class SchemaMigrationsEntryPointsTests(MariaDbFixture db)
{
    private static IConfiguration Settings(string service, string? schema = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WebmailPreferencesDatabase"] = service,
            ["ConnectionStrings:WebmailSchema"] = schema,
        }).Build();

    [SkippableFact]
    public async Task EnsureCurrent_RefusesABehindSchemaWithoutASchemaConnection()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();

        var error = Assert.Throws<InvalidOperationException>(
            () => SchemaMigrations.EnsureCurrent(Settings(connection), NullLogger.Instance));

        Assert.Equal(
            "Schema is behind: 0001_initial.sql not applied. Run `scotty.microservice migrate`, " +
            "or set ConnectionStrings__WebmailSchema.", error.Message);
    }

    [SkippableFact]
    public async Task EnsureCurrent_MigratesWhenASchemaConnectionIsSet()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();

        SchemaMigrations.EnsureCurrent(Settings(connection, schema: connection), NullLogger.Instance);

        Assert.Empty(SchemaMigrations.Pending(connection));
    }

    [SkippableFact]
    public async Task EnsureCurrent_StartsOnACurrentSchema()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();
        SchemaMigrations.Apply(connection);

        SchemaMigrations.EnsureCurrent(Settings(connection), NullLogger.Instance);
    }

    [SkippableFact]
    public async Task RunCommand_ReadsTheConnectionFromStandardInputAndExitsZero()
    {
        Skip.If(db.Unavailable, "Docker is not running");
        var connection = await db.NewDatabaseAsync();
        var output = new StringWriter();

        var code = SchemaMigrations.RunCommand(null, new StringReader(connection + "\n"), output, TextWriter.Null);

        Assert.Equal(0, code);
        Assert.Contains("0001_initial.sql", output.ToString());
        Assert.Empty(SchemaMigrations.Pending(connection));
    }

    [Fact]
    public void RunCommand_ExitsOneWithoutAConnection()
    {
        var error = new StringWriter();

        Assert.Equal(1, SchemaMigrations.RunCommand(null, TextReader.Null, TextWriter.Null, error));
        Assert.Contains("ConnectionStrings__WebmailSchema", error.ToString());
    }

    [Fact]
    public void RunCommand_ExitsOneWithAReadableMessageOnAnUnreachableDatabase()
    {
        var error = new StringWriter();

        var code = SchemaMigrations.RunCommand(
            "Server=127.0.0.1;Port=1;Database=x;User=x;Password=x;Connection Timeout=2",
            TextReader.Null, TextWriter.Null, error);

        Assert.Equal(1, code);
        Assert.DoesNotContain("   at ", error.ToString());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~SchemaMigrationsEntryPointsTests"`
Expected: FAIL à la compilation — `EnsureCurrent`, `RunCommand` n'existent pas.

- [ ] **Step 3: Implement**

Ajouter à `SchemaMigrations` :

```csharp
    public static void EnsureCurrent(IConfiguration configuration, ILogger logger)
    {
        var schema = configuration.GetConnectionString("WebmailSchema");
        if (!string.IsNullOrWhiteSpace(schema))
        {
            foreach (var name in Apply(schema)) logger.LogInformation("Schema migration {Script} applied", name);
            return;
        }

        var pending = Pending(configuration.GetConnectionString("WebmailPreferencesDatabase")!);
        if (pending.Count > 0)
            throw new InvalidOperationException(
                $"Schema is behind: {string.Join(", ", pending)} not applied. " +
                "Run `scotty.microservice migrate`, or set ConnectionStrings__WebmailSchema.");
    }

    // `sudo` empties the environment and a command line shows in `ps`: standard input carries it then.
    public static int RunCommand(string? fromEnvironment, TextReader input, TextWriter output, TextWriter error)
    {
        var connection = string.IsNullOrWhiteSpace(fromEnvironment) ? input.ReadLine()?.Trim() : fromEnvironment;
        if (string.IsNullOrEmpty(connection))
        {
            error.WriteLine("No schema connection: set ConnectionStrings__WebmailSchema, or pass it on standard input.");
            return 1;
        }

        try
        {
            var applied = Apply(connection);
            foreach (var name in applied) output.WriteLine($"Schema migration {name} applied");
            if (applied.Count == 0) output.WriteLine("Schema is current.");
            return 0;
        }
        catch (Exception e) when (e is InvalidOperationException or MySqlException)
        {
            error.WriteLine(e.Message);
            return 1;
        }
    }
```

(usings : `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Logging`.) La connexion ne figure jamais dans un message : seuls les noms de scripts et l'erreur SQL.

`Program.cs` — tout en haut, avant `var builder = …` :

```csharp
if (args is ["migrate"])
{
    Environment.ExitCode = SchemaMigrations.RunCommand(
        Environment.GetEnvironmentVariable("ConnectionStrings__WebmailSchema"),
        Console.IsInputRedirected ? Console.In : TextReader.Null, Console.Out, Console.Error);
    return;
}
```

et, juste après `var app = builder.Build();` :

```csharp
SchemaMigrations.EnsureCurrent(app.Configuration, app.Logger);
```

(`using weesky.Scotty.Microservice.Data;`.) Après `Build()` et non avant : le logger existe, et rien ne lit encore la base — les services d'arrière-plan démarrent à `Run()`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/scotty.microservice/scotty.microservice.Tests --filter "FullyQualifiedName~SchemaMigrations"`
Expected: PASS.

- [ ] **Step 5: Verify the command by hand**

Run : `dotnet run --project src/scotty.microservice.host -- migrate < /dev/null` → code 1, message « No schema connection… ». Si Docker tourne, avec une base de la fixture : non nécessaire, les tests le couvrent.

- [ ] **Step 6: Run the whole backend suite and commit**

```bash
dotnet test src/scotty.microservice.sln
git checkout -- src/scotty.microservice/ApiDocumentation.xml 2>/dev/null
git add src/scotty.microservice/Data/SchemaMigrations.cs src/scotty.microservice.host/Program.cs \
  src/scotty.microservice/scotty.microservice.Tests/Data/SchemaMigrationsEntryPointsTests.cs
git commit -F - <<'EOF'
Check the schema at start, migrating when a schema connection is set, and add the migrate command

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 4: `install.sql`, guide, réglages, fiches et version

**Files:**
- Modify: `install/install.sql`, `install/README.md`, `install/scotty.microservice.env`
- Modify: `docs/operations/birthdays-calendar-migration.md`, `docs/operations/app-logo-migration.md`
- Modify: `src/scotty.microservice/CLAUDE.md`, `src/scotty.microservice/VERSION` (`1.2.0` → `1.3.0`)

**Interfaces:**
- Consumes: les messages exacts des tâches 2 et 3, le nom du compte `scotty_webmail_schema`.

Pas de test automatique ; vérification par relecture et `grep` des messages cités. Le guide reste en anglais, simple, sans mention du serveur du propriétaire.

- [ ] **Step 1: `install.sql`**

Retirer les sections 3 et 4 (elles vivent dans `0001`). Section 2 : ajouter le compte de schéma, avec un placeholder `__SCHEMA_PASSWORD__` :

```sql
--  The schema account creates and changes the tables: the service uses it once at start, to apply
--  what is missing, then serves every request through the account above.
CREATE USER IF NOT EXISTS 'scotty_webmail_schema'@'__HOST__'
  IDENTIFIED BY '__SCHEMA_PASSWORD__';

GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES
  ON `scotty_webmail`.*
  TO 'scotty_webmail_schema'@'__HOST__';
```

Mettre à jour : l'en-tête (« Creates Scotty's database and the two accounts. The tables are created by the service on its first start. » ; trois placeholders ; plus de « NEW installation only » : le script est rejouable), le commentaire de la section 2 (« No CREATE, DROP or ALTER: the service never migrates… » → les requêtes passent par ce compte, le schéma par l'autre), la vérification (deux `SHOW GRANTS`), la désinstallation (les deux `DROP USER`). Renuméroter les sections.

- [ ] **Step 2: `install/README.md`**

- Étape 2.1 : le tableau des placeholders gagne `__SCHEMA_PASSWORD__` (« A second new password, for the account that creates the tables »).
- Étape 2 : là où le guide dit que le compte a `SELECT, INSERT, UPDATE, DELETE` (vers la ligne 184), ajouter le compte de schéma.
- Étape 3.3 : ligne de tableau `ConnectionStrings__WebmailSchema` — « The same as the line above, with `scotty_webmail_schema` and its password. The service uses it at start to create or update its tables. »
- « Updating » : « The service updates its tables itself when it starts. » ; garder la phrase sur `install.sql` adaptée (rejouable, ne crée que les comptes).
- Tableau « When the service won't start » :
  - `Schema is behind: … not applied` | `ConnectionStrings__WebmailSchema` is missing: put it back, then restart
  - `Schema migration … failed` | The database refused a change; the message says why. Fix it, then restart
  - `Could not take the schema lock` | Another start is updating the tables; wait a minute, then restart

`install/scotty.microservice.env` : sous `ConnectionStrings__WebmailPreferencesDatabase`, dans la même section CHANGE :

```ini
ConnectionStrings__WebmailSchema=Server=127.0.0.1;Port=3306;Database=scotty_webmail;User=scotty_webmail_schema;Password=CHANGE_ME;
```

et le titre de section devient « the two database passwords you chose in step 2 ».

- [ ] **Step 3: Fiches historiques et CLAUDE.md**

En tête des deux fiches `docs/operations/*-migration.md` : « **Historical.** Already part of `0001_initial.sql`: a database created or updated after schema migrations (2026-10) needs nothing from this page. »

`src/scotty.microservice/CLAUDE.md` :
- rule 5 du Mail (« Database creation is manual — see install/README.md; the service refuses to start without… ») : la base et les comptes sont manuels, les tables viennent des migrations ;
- une sous-section « Schema migrations » dans Architecture : scripts `Data/Migrations/NNNN_nom.sql`, journal `schema_migrations`, une modification de structure par script, jamais modifier un script livré, ajouter d'abord / retirer à la version suivante, pas de nom de base dans un script, tests sur MariaDB Testcontainers (sautés sans Docker hors CI).

- [ ] **Step 4: Version et vérification des messages**

`src/scotty.microservice/VERSION` : `1.3.0`. Puis :

```bash
grep -n "Schema is behind\|Schema migration\|Could not take the schema lock" src/scotty.microservice/Data/SchemaMigrations.cs
```

Expected : chaque début de message du tableau du guide apparaît tel quel.

- [ ] **Step 5: Commit**

```bash
git add install docs/operations src/scotty.microservice/CLAUDE.md src/scotty.microservice/VERSION
git commit -F - <<'EOF'
install.sql creates the database and two accounts; the guide and env file add the schema account; API 1.3.0

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```

---

### Task 5: `deploy.yml` et la mise en route du serveur du propriétaire

**Files:**
- Modify: `.github/workflows/deploy.yml` (job `deploy`, étape « Deploy microservice »)
- Create: `docs/operations/schema-migrations-bootstrap.md`

**Interfaces:**
- Consumes: la commande `migrate` (tâche 3), `scotty.microservice.Tests/Data/schema-migrations-bootstrap-journal.sql` (tâche 2).

- [ ] **Step 1: `deploy.yml`**

Dans « Deploy microservice », retirer `&& \ sudo systemctl restart $SERVICE_NAME` de la commande SSH existante, puis ajouter à la suite du même `run:` :

```bash
          printf '%s\n' "$SCHEMA_CONNECTION" | ssh -i ~/.ssh/deploy_key \
            -o ConnectTimeout=30 -o ServerAliveInterval=60 \
            ${{ secrets.DEPLOY_USER }}@${{ secrets.DEPLOY_HOST }} \
            "sudo $DEPLOY_PATH/scotty.microservice migrate"
          ssh -i ~/.ssh/deploy_key -o ConnectTimeout=30 \
            ${{ secrets.DEPLOY_USER }}@${{ secrets.DEPLOY_HOST }} \
            "sudo systemctl restart $SERVICE_NAME"
```

et, au niveau de l'étape :

```yaml
        env:
          SCHEMA_CONNECTION: ${{ secrets.WEBMAIL_SCHEMA_CONNECTION }}
```

Un `run:` GitHub s'exécute sous `bash -e` : une sortie 1 de `migrate` arrête l'étape avant le redémarrage. Le secret passe par l'environnement du runner puis par l'entrée standard SSH : il n'apparaît ni dans la commande distante ni dans le log (GitHub masque les secrets).

- [ ] **Step 2: La fiche de mise en route**

`docs/operations/schema-migrations-bootstrap.md` (français, comme les autres fiches), à jouer **une fois** sur la prod et `dev`, **avant** le premier déploiement de cette version :

1. Créer le compte de schéma (adapter l'hôte et le mot de passe) :
   ```sql
   CREATE USER 'scotty_webmail_schema'@'<hôte>' IDENTIFIED BY '<mot de passe>';
   GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES
     ON `<base>`.* TO 'scotty_webmail_schema'@'<hôte>';
   ```
2. Inscrire l'état actuel comme `0001` : le contenu exact de `scotty.microservice.Tests/Data/schema-migrations-bootstrap-journal.sql`, après `USE <base>;`. Prérequis : les deux fiches historiques ont été appliquées sur cette base (vrai pour la prod et `dev`).
3. GitHub › Settings › Environments › `prod` puis `dev` › secret `WEBMAIL_SCHEMA_CONNECTION` = `Server=…;Port=3306;Database=<base>;User=scotty_webmail_schema;Password=<mot de passe>;`.
4. Si le compte de déploiement a des droits `sudo` limités à une liste de commandes : y ajouter `<chemin>/scotty.microservice migrate` pour les deux environnements.
5. Vérifier : `SELECT scriptname FROM schema_migrations;` → `0001_initial.sql`.

- [ ] **Step 3: Relire le workflow**

Run : `python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/deploy.yml'))"` (ou `pip install pyyaml` d'abord) → aucune erreur. Relire : l'ordre est extraction → `migrate` → `restart`, et aucune autre étape ne relance le service.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/deploy.yml docs/operations/schema-migrations-bootstrap.md
git commit -F - <<'EOF'
Migrate the schema on deploy, before the restart, from an environment secret; document the one-time bootstrap

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
EOF
```
