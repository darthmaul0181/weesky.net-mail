# Tranche 2a.5 — Dossiers systèmes : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** L'utilisateur peut affecter les cinq rôles systèmes (sent, drafts, trash, junk, archive) à des dossiers de sa boîte ; les choix persistent en base, survivent aux renommages, et l'arborescence affiche le libellé du rôle.

**Architecture:** Chaîne de résolution ordonnée (surcharge utilisateur → flags `SPECIAL-USE` → correspondance par nom) implémentée par un résolveur pur ; stockage dans une base MySQL séparée via un second `DbContext` ; trois endpoints sous `MailController` ; page Settings de configuration. Spec : `docs/superpowers/specs/2026-07-19-webmail-mail-2a5-system-folders-design.md`.

**Tech Stack:** .NET 10, EF Core + Pomelo (base `snoopy_webmail`), MailKit 4.17, xUnit + Moq + EF InMemory ; React 18 + TypeScript, TanStack Query 5, Vitest + Testing Library.

## Global Constraints

- Branche `webmail`. Un commit par tâche, message en anglais, trailer exact : `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Backend : `dotnet test` (jamais `--no-build` quand des fichiers de test ont été ajoutés). Namespace de tests `weesky.Snoopy.Microservice.Tests.*`, `using Xunit;` explicite. `Assert.IsType<BadRequestObjectResult>` / `<NotFoundObjectResult>` pour `BadRequest(body)` / `NotFound(body)` — jamais `ObjectResult` (type exact vérifié).
- Les chemins de dossier ne voyagent **jamais** en segment de route — query string ou corps de requête.
- Le séparateur de hiérarchie vient **toujours** de la session (`session.DirectorySeparator`), jamais d'une constante. Les tests de maintenance couvrent `.` **et** `/`.
- Rôles : exactement les chaînes `"sent"`, `"drafts"`, `"trash"`, `"junk"`, `"archive"`. Provenance : exactement `"override"`, `"specialUse"`, `"name"`. `"inbox"` n'est jamais surchargeable.
- `account_id` : toujours via `FolderRoleStore.CanonicalAccountId(email)` (trim + minuscules) — la collation de la table est binaire.
- Clé de connexion : `WebmailPreferencesDatabase`, vide dans `appsettings.json` du dépôt. Démarrage : levée d'exception si absente, message nommant `docs/superpowers/mail-2a5-database-prerequisite.md`.
- Pas de migrations EF — le schéma est géré hors EF (script du prérequis).
- Erreurs HTTP : cookie credentials absent → 401 `credentials_unavailable` ; refus du serveur mail → 502 ; dossier introuvable → 404 ; validation → 400. Messages d'erreur en anglais, ton des messages existants (« A folder name is required »).
- Frontend : UI en anglais. `npm run test`, `npm run typecheck`, `npm run lint`, `npm run build` verts à la fin de chaque tâche frontend. Aucune couleur littérale dans `mail.css`.
- MailKit : capacité `ImapCapabilities.ObjectID`, item `StatusItems.MailboxId`, propriété `IMailFolder.Id`. Toujours conditionner la demande de `MailboxId` à la capacité — demander un item STATUS non supporté est une erreur protocolaire.

## Structure de fichiers

**Backend (créés)**
| Fichier | Responsabilité |
|---|---|
| `src/snoopy.microservice/Data/Preferences/FolderRoleOverride.cs` | Entité, table `folder_role_overrides` |
| `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs` | Second DbContext, base séparée |
| `src/snoopy.microservice/Repositories/IFolderRoleStore.cs` + `FolderRoleStore.cs` | CRUD des surcharges + maintenance renommage/suppression. Ignore IMAP |
| `src/snoopy.microservice/Models/Mail/MailFolderStatus.cs` | Instantané d'identité d'un dossier vivant |
| `src/snoopy.microservice/Models/Mail/FolderRoleModels.cs` | `FolderRoles`, `FolderRoleEntry`, `StaleOverrideInfo`, `SetFolderRoleRequest` |
| `src/snoopy.microservice/Services/SpecialUseAssignment.cs` | Rôle découvert + sa source |
| `src/snoopy.microservice/Services/FolderRoleResolver.cs` | La chaîne § 4.1, pur. Ignore HTTP et la base |

**Backend (modifiés)** : `ImapSession.cs` / `IImapSession.cs` (statut de dossier, `MailboxId`, refactor `ResolveSpecialUses`), `MailFolderNode.cs` (+2 propriétés `[JsonIgnore]`), `MailFolderRepository.cs` / interface (maintenance), `MailController.cs` (3 routes + intégration), `Program.cs`, `appsettings.json`, `CLAUDE.md`.

**Frontend (créés)** : `src/modules/mail/roleLabel.ts` (+test), `src/modules/settings/mail/SystemFoldersPage.tsx` (+test).
**Frontend (modifiés)** : `api.js`, `mailTypes.ts`, `queries.ts`, `FolderTree.tsx` (+tests), `MailLayout.tsx`, `FolderDialogs.tsx` (+tests), `routes.tsx`, `SettingsLayout.tsx`, `CLAUDE.md`.

---

### Task 1: Store des surcharges (entité, contexte, dépôt, câblage)

**Files:**
- Create: `src/snoopy.microservice/Data/Preferences/FolderRoleOverride.cs`
- Create: `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs`
- Create: `src/snoopy.microservice/Repositories/IFolderRoleStore.cs`
- Create: `src/snoopy.microservice/Repositories/FolderRoleStore.cs`
- Create: `src/snoopy.microservice/snoopy.microservice.Tests/Infrastructure/PreferencesTestDbContext.cs`
- Create: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/FolderRoleStoreTests.cs`
- Modify: `src/snoopy.microservice/Program.cs` (après le bloc `AddDbContext<ApplicationDbContext>`)
- Modify: `src/snoopy.microservice/appsettings.json` (clé `WebmailPreferencesDatabase` vide)

**Interfaces:**
- Consumes: rien (feuille du graphe).
- Produces: `IFolderRoleStore` — `Task<IReadOnlyList<FolderRoleOverride>> GetAsync(string accountId, CancellationToken)` ; `Task UpsertAsync(FolderRoleOverride, CancellationToken)` ; `Task DeleteAsync(string accountId, string role, CancellationToken)` ; `Task ApplyRenameAsync(string accountId, string oldPath, string newPath, char separator, ulong newUidValidity, string? newMailboxId, CancellationToken)` ; `Task RemoveSubtreeAsync(string accountId, string path, char separator, CancellationToken)`. Et `FolderRoleStore.CanonicalAccountId(string email)` statique. Entité `FolderRoleOverride { string AccountId; string Role; string FolderPath; ulong UidValidity; string? MailboxId; DateTime UpdatedAt }`.

- [ ] **Step 1 : Entité et contexte**

`Data/Preferences/FolderRoleOverride.cs` :

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences
{
    /// <summary>
    /// One user-chosen folder-role assignment. Absence of a row means the role falls back to
    /// discovery (SPECIAL-USE, then name matching): the override is a correction layer, not a
    /// replacement, so a freshly provisioned mailbox needs no rows at all.
    /// </summary>
    [Table("folder_role_overrides")]
    public class FolderRoleOverride
    {
        [Column("account_id")]
        public string AccountId { get; set; } = string.Empty;

        /// <summary>Stable enum value ("trash", never a localised word). See FolderRoles.All.</summary>
        [Column("role")]
        public string Role { get; set; } = string.Empty;

        /// <summary>Always stored: the one identifier IMAP guarantees on every server.</summary>
        [Column("folder_path")]
        public string FolderPath { get; set; } = string.Empty;

        /// <summary>Staleness guard: catches a path reused by a different folder.</summary>
        [Column("uid_validity")]
        public ulong UidValidity { get; set; }

        /// <summary>RFC 8474 MAILBOXID — an optional aid, never the key.</summary>
        [Column("mailbox_id")]
        public string? MailboxId { get; set; }

        [Column("updated_at")]
        public DateTime UpdatedAt { get; set; }
    }
}
```

`Data/Preferences/PreferencesDbContext.cs` :

```csharp
using Microsoft.EntityFrameworkCore;

namespace weesky.Snoopy.Microservice.Data.Preferences
{
    /// <summary>
    /// Webmail user preferences. A separate database from the dovecot schema on purpose: that
    /// schema belongs to Dovecot and can be rebuilt by mail-server provisioning, which would
    /// take our data with it. Created manually — no EF migrations in this project; see
    /// docs/superpowers/mail-2a5-database-prerequisite.md.
    /// </summary>
    public class PreferencesDbContext : DbContext
    {
        public PreferencesDbContext(DbContextOptions<PreferencesDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<FolderRoleOverride>().HasKey(o => new { o.AccountId, o.Role });
        }

        public DbSet<FolderRoleOverride> FolderRoleOverrides { get; set; }
    }
}
```

- [ ] **Step 2 : Tests du store (échouent — le store n'existe pas)**

`snoopy.microservice.Tests/Infrastructure/PreferencesTestDbContext.cs` :

```csharp
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure
{
    internal class PreferencesTestDbContext : PreferencesDbContext
    {
        public PreferencesTestDbContext(string databaseName)
            : base(new DbContextOptionsBuilder<PreferencesDbContext>()
                  .UseInMemoryDatabase(databaseName)
                  .Options)
        {
        }
    }
}
```

`snoopy.microservice.Tests/Repositories/FolderRoleStoreTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories
{
    public class FolderRoleStoreTests
    {
        private static FolderRoleStore CreateStore(string dbName) =>
            new(new PreferencesTestDbContext(dbName));

        private static FolderRoleOverride Override(
            string role, string path, ulong uidValidity = 1, string? mailboxId = null,
            string accountId = "alice@weesky.be") =>
            new() { AccountId = accountId, Role = role, FolderPath = path, UidValidity = uidValidity, MailboxId = mailboxId };

        [Fact]
        public void CanonicalAccountId_TrimsAndLowercases()
        {
            Assert.Equal("alice@weesky.be", FolderRoleStore.CanonicalAccountId("  Alice@WEESKY.be "));
        }

        [Fact]
        public async Task Upsert_InsertsThenUpdatesTheSameRow()
        {
            var store = CreateStore(nameof(Upsert_InsertsThenUpdatesTheSameRow));

            await store.UpsertAsync(Override("trash", "Deleted Items", 10), CancellationToken.None);
            await store.UpsertAsync(Override("trash", "Corbeille", 20, "M1"), CancellationToken.None);

            var rows = await store.GetAsync("alice@weesky.be", CancellationToken.None);
            var row = Assert.Single(rows);
            Assert.Equal("Corbeille", row.FolderPath);
            Assert.Equal(20UL, row.UidValidity);
            Assert.Equal("M1", row.MailboxId);
        }

        [Fact]
        public async Task Get_ReturnsOnlyTheAccountsRows()
        {
            var store = CreateStore(nameof(Get_ReturnsOnlyTheAccountsRows));
            await store.UpsertAsync(Override("trash", "T"), CancellationToken.None);
            await store.UpsertAsync(Override("junk", "J", accountId: "bob@weesky.be"), CancellationToken.None);

            var rows = await store.GetAsync("alice@weesky.be", CancellationToken.None);

            Assert.Equal("trash", Assert.Single(rows).Role);
        }

        [Fact]
        public async Task Delete_IsIdempotent()
        {
            var store = CreateStore(nameof(Delete_IsIdempotent));
            await store.UpsertAsync(Override("junk", "Spam"), CancellationToken.None);

            await store.DeleteAsync("alice@weesky.be", "junk", CancellationToken.None);
            await store.DeleteAsync("alice@weesky.be", "junk", CancellationToken.None); // no throw

            Assert.Empty(await store.GetAsync("alice@weesky.be", CancellationToken.None));
        }

        // The exact row gets the re-read identity — some servers change UIDVALIDITY on rename,
        // and carrying the old value would make our own rename trip our own staleness guard.
        [Fact]
        public async Task ApplyRename_UpdatesTheExactRowWithTheFreshIdentity()
        {
            var store = CreateStore(nameof(ApplyRename_UpdatesTheExactRowWithTheFreshIdentity));
            await store.UpsertAsync(Override("trash", "Old", 10, "M-old"), CancellationToken.None);

            await store.ApplyRenameAsync("alice@weesky.be", "Old", "New", '/', 42, "M-new", CancellationToken.None);

            var row = Assert.Single(await store.GetAsync("alice@weesky.be", CancellationToken.None));
            Assert.Equal("New", row.FolderPath);
            Assert.Equal(42UL, row.UidValidity);
            Assert.Equal("M-new", row.MailboxId);
        }

        // A parent rename moves the whole subtree in IMAP — the overrides must follow.
        // Both separators, in the same test: '.' on the home server, '/' elsewhere.
        [Theory]
        [InlineData('/')]
        [InlineData('.')]
        public async Task ApplyRename_MovesTheSubtree(char separator)
        {
            var store = CreateStore(nameof(ApplyRename_MovesTheSubtree) + separator);
            await store.UpsertAsync(Override("archive", $"Projects{separator}Archive", 5), CancellationToken.None);

            await store.ApplyRenameAsync("alice@weesky.be", "Projects", "Work", separator, 99, null, CancellationToken.None);

            var row = Assert.Single(await store.GetAsync("alice@weesky.be", CancellationToken.None));
            Assert.Equal($"Work{separator}Archive", row.FolderPath);
            // A child keeps its own identity: the parent's rename does not change its
            // UIDVALIDITY. If a server does change it, the staleness guard degrades — it
            // never lies.
            Assert.Equal(5UL, row.UidValidity);
        }

        // "Projects2" starts with "Projects" but is a sibling, not a child. The prefix match
        // must include the separator, or a rename corrupts unrelated overrides.
        [Fact]
        public async Task ApplyRename_LeavesASiblingWithASharedNamePrefixAlone()
        {
            var store = CreateStore(nameof(ApplyRename_LeavesASiblingWithASharedNamePrefixAlone));
            await store.UpsertAsync(Override("archive", "Projects2/Archive", 5), CancellationToken.None);

            await store.ApplyRenameAsync("alice@weesky.be", "Projects", "Work", '/', 99, null, CancellationToken.None);

            var row = Assert.Single(await store.GetAsync("alice@weesky.be", CancellationToken.None));
            Assert.Equal("Projects2/Archive", row.FolderPath);
        }

        [Theory]
        [InlineData('/')]
        [InlineData('.')]
        public async Task RemoveSubtree_PurgesTheFolderAndItsChildren(char separator)
        {
            var store = CreateStore(nameof(RemoveSubtree_PurgesTheFolderAndItsChildren) + separator);
            await store.UpsertAsync(Override("trash", "Projects"), CancellationToken.None);
            await store.UpsertAsync(Override("archive", $"Projects{separator}Old"), CancellationToken.None);
            await store.UpsertAsync(Override("junk", "Spam"), CancellationToken.None);

            await store.RemoveSubtreeAsync("alice@weesky.be", "Projects", separator, CancellationToken.None);

            var rows = await store.GetAsync("alice@weesky.be", CancellationToken.None);
            Assert.Equal("junk", Assert.Single(rows).Role);
        }
    }
}
```

- [ ] **Step 2b : Vérifier l'échec** — `cd src/snoopy.microservice && dotnet build` → erreurs de compilation (`FolderRoleStore` inexistant). Attendu.

- [ ] **Step 3 : Implémenter le store**

`Repositories/IFolderRoleStore.cs` :

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories
{
    /// <summary>
    /// Reads and writes folder-role overrides. Knows nothing about IMAP: validity against the
    /// live mailbox is the resolver's business, and capturing uid_validity / mailbox_id from a
    /// live folder is the caller's.
    /// </summary>
    public interface IFolderRoleStore
    {
        Task<IReadOnlyList<FolderRoleOverride>> GetAsync(string accountId, CancellationToken cancellationToken);

        Task UpsertAsync(FolderRoleOverride @override, CancellationToken cancellationToken);

        /// <summary>Idempotent: clearing an absent override is not an error.</summary>
        Task DeleteAsync(string accountId, string role, CancellationToken cancellationToken);

        /// <summary>
        /// After a successful IMAP rename. The exact row gets the new path and the freshly
        /// re-read identity; subtree rows get their prefix swapped and keep their own
        /// identity. The separator comes from the live session — '.' on the home server,
        /// '/' elsewhere — never from a constant.
        /// </summary>
        Task ApplyRenameAsync(string accountId, string oldPath, string newPath, char separator,
            ulong newUidValidity, string? newMailboxId, CancellationToken cancellationToken);

        /// <summary>After a successful IMAP delete: purge the folder's row and its subtree's.</summary>
        Task RemoveSubtreeAsync(string accountId, string path, char separator, CancellationToken cancellationToken);
    }
}
```

`Repositories/FolderRoleStore.cs` :

```csharp
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories
{
    public class FolderRoleStore : IFolderRoleStore
    {
        private readonly PreferencesDbContext _context;

        public FolderRoleStore(PreferencesDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>
        /// The table's collation is binary — IMAP paths must compare byte for byte — so the
        /// account id must be written in exactly one form, or the same user splits into
        /// several accounts.
        /// </summary>
        public static string CanonicalAccountId(string email) => email.Trim().ToLowerInvariant();

        public async Task<IReadOnlyList<FolderRoleOverride>> GetAsync(string accountId, CancellationToken cancellationToken)
            => await _context.FolderRoleOverrides.AsNoTracking()
                .Where(o => o.AccountId == accountId)
                .OrderBy(o => o.Role)
                .ToListAsync(cancellationToken);

        public async Task UpsertAsync(FolderRoleOverride @override, CancellationToken cancellationToken)
        {
            var existing = await _context.FolderRoleOverrides.FirstOrDefaultAsync(
                o => o.AccountId == @override.AccountId && o.Role == @override.Role, cancellationToken);

            if (existing == null)
            {
                @override.UpdatedAt = DateTime.UtcNow;
                _context.FolderRoleOverrides.Add(@override);
            }
            else
            {
                existing.FolderPath = @override.FolderPath;
                existing.UidValidity = @override.UidValidity;
                existing.MailboxId = @override.MailboxId;
                existing.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteAsync(string accountId, string role, CancellationToken cancellationToken)
        {
            var existing = await _context.FolderRoleOverrides.FirstOrDefaultAsync(
                o => o.AccountId == accountId && o.Role == role, cancellationToken);
            if (existing == null) return;

            _context.FolderRoleOverrides.Remove(existing);
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task ApplyRenameAsync(string accountId, string oldPath, string newPath, char separator,
            ulong newUidValidity, string? newMailboxId, CancellationToken cancellationToken)
        {
            var prefix = oldPath + separator;
            var rows = await _context.FolderRoleOverrides
                .Where(o => o.AccountId == accountId
                            && (o.FolderPath == oldPath || o.FolderPath.StartsWith(prefix)))
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                if (row.FolderPath == oldPath)
                {
                    row.FolderPath = newPath;
                    row.UidValidity = newUidValidity;
                    row.MailboxId = newMailboxId;
                }
                else
                {
                    row.FolderPath = newPath + row.FolderPath.Substring(oldPath.Length);
                }
                row.UpdatedAt = DateTime.UtcNow;
            }

            // A single SaveChanges: on a relational provider this commits as one transaction.
            await _context.SaveChangesAsync(cancellationToken);
        }

        public async Task RemoveSubtreeAsync(string accountId, string path, char separator, CancellationToken cancellationToken)
        {
            var prefix = path + separator;
            var rows = await _context.FolderRoleOverrides
                .Where(o => o.AccountId == accountId
                            && (o.FolderPath == path || o.FolderPath.StartsWith(prefix)))
                .ToListAsync(cancellationToken);

            _context.FolderRoleOverrides.RemoveRange(rows);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
```

- [ ] **Step 4 : Câblage au démarrage**

Dans `Program.cs`, **juste après** le bloc `builder.Services.AddDbContext<ApplicationDbContext>(...)` :

```csharp
// User preferences (folder roles) live in their own database: the dovecot schema belongs to
// Dovecot and can be rebuilt by mail-server provisioning, which would take our data with it.
// Creation is manual — no EF migrations here. See docs/superpowers/mail-2a5-database-prerequisite.md.
var preferencesConnectionString = builder.Configuration.GetConnectionString("WebmailPreferencesDatabase");
if (string.IsNullOrEmpty(preferencesConnectionString))
{
    throw new InvalidOperationException(
        "Connection string 'WebmailPreferencesDatabase' is missing. " +
        "Apply docs/superpowers/mail-2a5-database-prerequisite.md, then configure the connection string. " +
        "Refusing to start rather than running with folder roles silently inert.");
}

var preferencesServerVersion = ServerVersion.AutoDetect(preferencesConnectionString);
builder.Services.AddDbContext<PreferencesDbContext>(options =>
{
    options.UseMySql(preferencesConnectionString, preferencesServerVersion)
        .LogTo(Console.WriteLine, LogLevel.Warning);
});
```

Ajouter le `using weesky.Snoopy.Microservice.Data.Preferences;` en tête. Puis, dans le bloc des dépôts (`AddScoped`), après `IMailMessageRepository` :

```csharp
builder.Services.AddScoped<IFolderRoleStore, FolderRoleStore>();
```

Dans `appsettings.json`, section `ConnectionStrings` :

```json
"MailUserAccountsDatabase": "",
"WebmailPreferencesDatabase": ""
```

- [ ] **Step 5 : Vérifier** — `cd src/snoopy.microservice && dotnet test` → tous verts (592 existants + 8 nouveaux). *Note : les tests n'exécutent pas `Program.cs` ; l'exécution locale du service exige la clé dans la configuration locale.*

- [ ] **Step 6 : Commit** — `git add -A && git commit` — message : `Add the folder-role override store on its own database`.

---

### Task 2: Session IMAP — découverte semée à double ensemble, identité de dossier

**Files:**
- Create: `src/snoopy.microservice/Services/SpecialUseAssignment.cs`
- Create: `src/snoopy.microservice/Models/Mail/MailFolderStatus.cs`
- Modify: `src/snoopy.microservice/Services/ImapSession.cs` (`ResolveSpecialUses`, `ListFoldersAsync`, nouveau `GetFolderStatusAsync`, constante `FolderNotFound`)
- Modify: `src/snoopy.microservice/Services/IImapSession.cs`
- Modify: `src/snoopy.microservice/Models/Mail/MailFolderNode.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapSessionTests.cs`

**Interfaces:**
- Consumes: rien de la Task 1.
- Produces:
  - `SpecialUseAssignment(string Role, string Source)` record struct, constantes `SpecialUseAssignment.FromFlag = "specialUse"`, `SpecialUseAssignment.FromName = "name"`.
  - `ImapSession.ResolveSpecialUses(IEnumerable<(string Path, string Name, string? AttributeRole)> folders, IEnumerable<string>? claimedRoles = null, IEnumerable<string>? claimedFolders = null)` → `IReadOnlyDictionary<string, SpecialUseAssignment>`.
  - `MailFolderNode` : `+ [JsonIgnore] string? AttributeRole`, `+ [JsonIgnore] string? MailboxId` (plomberie interne, jamais sérialisée vers le client).
  - `IImapSession.GetFolderStatusAsync(string path, CancellationToken)` → `Result<MailFolderStatus>` ; `MailFolderStatus { string Path; uint UidValidity; string? MailboxId; bool Selectable }` ; `ImapSession.FolderNotFound = "Folder not found"`.

- [ ] **Step 1 : Adapter les tests existants et écrire les nouveaux (échec attendu)**

Dans `ImapSessionTests.cs`, la forme des tuples change : `(Path, Name, Attributes, IsInbox)` devient `(Path, Name, AttributeRole)` et la valeur devient `SpecialUseAssignment`. **Remplacer** les trois tests `ResolveSpecialUses_*` existants par :

```csharp
        [Fact]
        public void ResolveSpecialUses_GivesEachRoleToOneFolderOnly()
        {
            var roles = ImapSession.ResolveSpecialUses(
            [
                ("Drafts", "Drafts", null),
                ("Brouillons", "Brouillons", null)
            ]);

            Assert.Equal("drafts", roles["Drafts"].Role);
            Assert.False(roles.ContainsKey("Brouillons"));
        }

        [Fact]
        public void ResolveSpecialUses_LetsTheServerFlagBeatTheNameGuess()
        {
            var roles = ImapSession.ResolveSpecialUses(
            [
                ("Drafts", "Drafts", null),
                ("Brouillons", "Brouillons", "drafts")
            ]);

            Assert.Equal("drafts", roles["Brouillons"].Role);
            Assert.Equal(SpecialUseAssignment.FromFlag, roles["Brouillons"].Source);
            Assert.False(roles.ContainsKey("Drafts"));
        }

        [Fact]
        public void ResolveSpecialUses_KeepsDistinctRolesApart()
        {
            var roles = ImapSession.ResolveSpecialUses(
            [
                ("INBOX", "INBOX", "inbox"),
                ("Sent", "Sent", null),
                ("Archive", "Archive", null),
                ("Projects", "Projects", null)
            ]);

            Assert.Equal("inbox", roles["INBOX"].Role);
            Assert.Equal("sent", roles["Sent"].Role);
            Assert.Equal(SpecialUseAssignment.FromName, roles["Sent"].Source);
            Assert.Equal("archive", roles["Archive"].Role);
            Assert.False(roles.ContainsKey("Projects"));
        }
```

Puis **ajouter** :

```csharp
        // A folder flagged \Sent but named "Trash" used to claim both roles, and the
        // path→role inversion then crashed on the duplicate key. One folder, one role.
        [Fact]
        public void ResolveSpecialUses_NeverGivesOneFolderTwoRoles()
        {
            var roles = ImapSession.ResolveSpecialUses(
            [
                ("Weird", "Trash", "sent")
            ]);

            Assert.Equal("sent", roles["Weird"].Role);
            Assert.DoesNotContain(roles.Values, a => a.Role == "trash");
        }

        [Fact]
        public void ResolveSpecialUses_ASeededRoleIsNotClaimable()
        {
            var roles = ImapSession.ResolveSpecialUses(
                [("Drafts", "Drafts", "drafts")],
                claimedRoles: ["drafts"]);

            Assert.Empty(roles);
        }

        // Spec § 4.1, second half: the folder is taken by an override, so its flag claims
        // nothing — and the name pass hands the freed role to the next candidate.
        [Fact]
        public void ResolveSpecialUses_ASeededFolderClaimsNothingAndTheRolePassesOn()
        {
            var roles = ImapSession.ResolveSpecialUses(
                [("Drafts", "Drafts", "drafts"), ("Brouillons", "Brouillons", null)],
                claimedFolders: ["Drafts"]);

            Assert.False(roles.ContainsKey("Drafts"));
            Assert.Equal("drafts", roles["Brouillons"].Role);
            Assert.Equal(SpecialUseAssignment.FromName, roles["Brouillons"].Source);
        }
```

Les tests `ResolveSpecialUse_*` (singulier), `SpecialUseFromName_*` et `ParentPath_*` restent inchangés.

- [ ] **Step 2 : Vérifier l'échec** — `dotnet build` → erreurs de compilation sur la nouvelle signature. Attendu.

- [ ] **Step 3 : Implémenter**

`Services/SpecialUseAssignment.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>Where a discovered role came from: a server SPECIAL-USE flag, or a name guess.</summary>
    public readonly record struct SpecialUseAssignment(string Role, string Source)
    {
        public const string FromFlag = "specialUse";
        public const string FromName = "name";
    }
}
```

`Models/Mail/MailFolderStatus.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail
{
    /// <summary>Identity snapshot of one live folder, read for override bookkeeping.</summary>
    public class MailFolderStatus
    {
        public string Path { get; set; } = string.Empty;

        public uint UidValidity { get; set; }

        /// <summary>RFC 8474 MAILBOXID when the server supports OBJECTID; null otherwise.</summary>
        public string? MailboxId { get; set; }

        public bool Selectable { get; set; } = true;
    }
}
```

Dans `Models/Mail/MailFolderNode.cs`, ajouter `using System.Text.Json.Serialization;` et, après `UidValidity` :

```csharp
        /// <summary>
        /// Role derived from the folder's SPECIAL-USE flags alone, before uniqueness. Internal
        /// plumbing for the resolution chain — never serialised to the client, which only sees
        /// the final SpecialUse.
        /// </summary>
        [JsonIgnore]
        public string? AttributeRole { get; set; }

        /// <summary>RFC 8474 MAILBOXID when the server supports OBJECTID. Internal plumbing.</summary>
        [JsonIgnore]
        public string? MailboxId { get; set; }
```

Dans `ImapSession.cs`, **remplacer** `ResolveSpecialUses` par :

```csharp
        /// <summary>
        /// Assigns discovered roles, each to at most one folder — and each folder to at most
        /// one role.
        /// </summary>
        /// <remarks>
        /// Two claim sets, not one. Claimed roles keep a mailbox holding both "Drafts" and
        /// "Brouillons" from ending up with two drafts folders. Claimed folders keep one
        /// folder from holding two roles — a folder flagged \Sent but named "Trash" used to
        /// claim both, which is undecidable to display. Callers may seed both sets: the role
        /// resolver runs user overrides first and hands discovery only the leftovers.
        /// </remarks>
        public static IReadOnlyDictionary<string, SpecialUseAssignment> ResolveSpecialUses(
            IEnumerable<(string Path, string Name, string? AttributeRole)> folders,
            IEnumerable<string>? claimedRoles = null,
            IEnumerable<string>? claimedFolders = null)
        {
            var candidates = folders.ToList();
            var roles = new HashSet<string>(claimedRoles ?? [], StringComparer.Ordinal);
            var taken = new HashSet<string>(claimedFolders ?? [], StringComparer.Ordinal);
            var result = new Dictionary<string, SpecialUseAssignment>(StringComparer.Ordinal);

            foreach (var folder in candidates)
            {
                if (folder.AttributeRole is { } role && !roles.Contains(role) && !taken.Contains(folder.Path))
                {
                    roles.Add(role);
                    taken.Add(folder.Path);
                    result[folder.Path] = new SpecialUseAssignment(role, SpecialUseAssignment.FromFlag);
                }
            }

            foreach (var folder in candidates)
            {
                if (SpecialUseFromName(folder.Name) is { } role && !roles.Contains(role) && !taken.Contains(folder.Path))
                {
                    roles.Add(role);
                    taken.Add(folder.Path);
                    result[folder.Path] = new SpecialUseAssignment(role, SpecialUseAssignment.FromName);
                }
            }

            return result;
        }
```

Dans `ListFoldersAsync`, remplacer le calcul des rôles et la construction du nœud :

```csharp
                var ordered = folders.OrderBy(f => f.FullName, StringComparer.Ordinal).ToList();
                var attributeRoles = ordered.ToDictionary(
                    f => f.FullName,
                    f => SpecialUseFromAttributes(f.Attributes, IsInbox(f)),
                    StringComparer.Ordinal);
                var roleByPath = ResolveSpecialUses(
                    ordered.Select(f => (f.FullName, f.Name, attributeRoles[f.FullName])));
```

et dans la boucle :

```csharp
                        SpecialUse = roleByPath.TryGetValue(folder.FullName, out var assignment)
                            ? assignment.Role
                            : null,
                        AttributeRole = attributeRoles[folder.FullName],
                        MailboxId = folder.Id,
```

Toujours dans `ListFoldersAsync`, conditionner l'item `MailboxId` à la capacité — le bloc qui appelle `GetFoldersAsync` devient :

```csharp
                var statusItems = StatusItems.Count | StatusItems.Unread | StatusItems.UidValidity;
                if (_client.Capabilities.HasFlag(ImapCapabilities.ObjectID))
                    statusItems |= StatusItems.MailboxId;

                var folders = await _client.GetFoldersAsync(personal, statusItems, subscribedOnly: false, cancellationToken);
```

Ajouter la constante à côté de `MessageNotFound` / `AttachmentNotFound` :

```csharp
        public const string FolderNotFound = "Folder not found";
```

Ajouter la méthode (après `SetSubscriptionAsync`) :

```csharp
        public async Task<Result<MailFolderStatus>> GetFolderStatusAsync(string path, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            try
            {
                var folder = await _client.GetFolderAsync(path, cancellationToken);

                var selectable = (folder.Attributes & FolderAttributes.NonExistent) == 0
                                 && (folder.Attributes & FolderAttributes.NoSelect) == 0;

                // STATUS on a \NoSelect folder is a protocol error; the caller rejects the
                // folder on Selectable alone, so there is nothing more to read.
                if (!selectable)
                {
                    return Result.Success(new MailFolderStatus { Path = folder.FullName, Selectable = false });
                }

                var items = StatusItems.UidValidity;
                if (_client.Capabilities.HasFlag(ImapCapabilities.ObjectID))
                    items |= StatusItems.MailboxId;
                await folder.StatusAsync(items, cancellationToken);

                return Result.Success(new MailFolderStatus
                {
                    Path = folder.FullName,
                    UidValidity = folder.UidValidity,
                    MailboxId = folder.Id,
                    Selectable = true
                });
            }
            catch (FolderNotFoundException)
            {
                return Result.Failure<MailFolderStatus>(FolderNotFound);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read the status of {Folder}", path);
                return Result.Failure<MailFolderStatus>("Unable to read the folder");
            }
        }
```

Dans `IImapSession.cs`, ajouter :

```csharp
        /// <summary>
        /// Identity snapshot of one folder, read live: path, UIDVALIDITY, MAILBOXID when the
        /// server supports OBJECTID, and selectability. Fails with ImapSession.FolderNotFound
        /// when the path no longer resolves.
        /// </summary>
        Task<Result<MailFolderStatus>> GetFolderStatusAsync(string path, CancellationToken cancellationToken);
```

- [ ] **Step 4 : Vérifier** — `dotnet test` → verts. Si `StatusItems.MailboxId` ou `folder.Id` ne compilent pas sous MailKit 4.17, chercher le nom exact via `grep -ri "mailboxid" ~/.nuget/packages/mailkit/4.17.0/` — la capacité s'appelle `ImapCapabilities.ObjectID`.

- [ ] **Step 5 : Commit** — message : `Teach discovery the dual claim sets and read live folder identity`.

---

### Task 3: FolderRoleResolver — la chaîne, pure

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/FolderRoleModels.cs`
- Create: `src/snoopy.microservice/Services/FolderRoleResolver.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/FolderRoleResolverTests.cs`

**Interfaces:**
- Consumes: `ImapSession.ResolveSpecialUses(tuples, claimedRoles, claimedFolders)` (Task 2), `MailFolderNode.AttributeRole/MailboxId` (Task 2), `FolderRoleOverride` (Task 1).
- Produces:
  - `FolderRoles.All` (`IReadOnlyList<string>` = sent, drafts, trash, junk, archive), `FolderRoles.IsValid(string?)`.
  - `FolderRoleResolver.Resolve(IReadOnlyList<MailFolderNode> tree, IReadOnlyList<FolderRoleOverride> overrides)` → `FolderRoleResolution { IReadOnlyList<FolderRoleEntry> Roles; IReadOnlyDictionary<string,string> RoleByPath }`.
  - `FolderRoleEntry { string Role; string? FolderPath; string? Provenance; StaleOverrideInfo? StaleOverride }`, `StaleOverrideInfo { string FolderPath }`, `SetFolderRoleRequest { string? Role; string? FolderPath }`.

- [ ] **Step 1 : Modèles**

`Models/Mail/FolderRoleModels.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail
{
    /// <summary>
    /// The five user-assignable roles. "inbox" is deliberately absent: INBOX is fixed by the
    /// IMAP protocol itself, so there is nothing to correct.
    /// </summary>
    public static class FolderRoles
    {
        public static readonly IReadOnlyList<string> All = ["sent", "drafts", "trash", "junk", "archive"];

        public static bool IsValid(string? role) => role != null && All.Contains(role);
    }

    /// <summary>One role as the Settings page sees it: what it resolves to, and why.</summary>
    public class FolderRoleEntry
    {
        public string Role { get; set; } = string.Empty;

        /// <summary>Resolved folder path, or null when no source provides one.</summary>
        public string? FolderPath { get; set; }

        /// <summary>"override", "specialUse" or "name". Null when the role is unresolved.</summary>
        public string? Provenance { get; set; }

        /// <summary>
        /// Set when the user's stored choice no longer matches a live folder. Kept and
        /// signalled, never auto-deleted — the row only dies by the user's hand — and it
        /// coexists with a discovery-resolved FolderPath (spec § 5.3).
        /// </summary>
        public StaleOverrideInfo? StaleOverride { get; set; }
    }

    public class StaleOverrideInfo
    {
        public string FolderPath { get; set; } = string.Empty;
    }

    public class SetFolderRoleRequest
    {
        public string? Role { get; set; }

        public string? FolderPath { get; set; }
    }
}
```

- [ ] **Step 2 : Tests du résolveur (échec — il n'existe pas)**

`snoopy.microservice.Tests/Services/FolderRoleResolverTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services
{
    public class FolderRoleResolverTests
    {
        private static MailFolderNode Node(string path, string? attributeRole = null,
            uint uidValidity = 1, string? mailboxId = null, bool selectable = true, string? name = null) =>
            new()
            {
                Path = path,
                Name = name ?? path,
                AttributeRole = attributeRole,
                UidValidity = uidValidity,
                MailboxId = mailboxId,
                Selectable = selectable,
            };

        private static FolderRoleOverride Override(string role, string path,
            ulong uidValidity = 1, string? mailboxId = null) =>
            new() { AccountId = "alice@weesky.be", Role = role, FolderPath = path, UidValidity = uidValidity, MailboxId = mailboxId };

        private static FolderRoleEntry Entry(FolderRoleResolution resolution, string role) =>
            resolution.Roles.Single(e => e.Role == role);

        [Fact]
        public void WithoutOverrides_MatchesDiscoveryUnchanged()
        {
            var tree = new List<MailFolderNode>
            {
                Node("INBOX", attributeRole: "inbox"),
                Node("Deleted Items", attributeRole: "trash"),
                Node("Archive"),                                  // name fallback only
                Node("Projects"),
            };

            var resolution = FolderRoleResolver.Resolve(tree, []);

            Assert.Equal("inbox", resolution.RoleByPath["INBOX"]);
            Assert.Equal("trash", resolution.RoleByPath["Deleted Items"]);
            Assert.Equal("archive", resolution.RoleByPath["Archive"]);
            Assert.False(resolution.RoleByPath.ContainsKey("Projects"));
            Assert.Equal("specialUse", Entry(resolution, "trash").Provenance);
            Assert.Equal("name", Entry(resolution, "archive").Provenance);
        }

        [Fact]
        public void AnOverrideBeatsAServerFlag()
        {
            var tree = new List<MailFolderNode> { Node("Corbeille"), Node("Deleted Items", attributeRole: "trash") };

            var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Corbeille")]);

            var trash = Entry(resolution, "trash");
            Assert.Equal("Corbeille", trash.FolderPath);
            Assert.Equal("override", trash.Provenance);
            Assert.Null(trash.StaleOverride);
            // The flagged folder lost the role and gets nothing: it shows under its own name.
            Assert.False(resolution.RoleByPath.ContainsKey("Deleted Items"));
        }

        // Spec § 4.1, the case a roles-only implementation gets wrong: trash overridden onto
        // the flagged Drafts folder. Drafts must not also claim "drafts" — and the freed role
        // goes to the name-matched candidate instead.
        [Fact]
        public void AFolderTakenByAnOverrideClaimsNothingAtDiscovery()
        {
            var tree = new List<MailFolderNode> { Node("Drafts", attributeRole: "drafts"), Node("Brouillons") };

            var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Drafts")]);

            Assert.Equal("trash", resolution.RoleByPath["Drafts"]);
            Assert.Equal("drafts", resolution.RoleByPath["Brouillons"]);
            Assert.Equal("Brouillons", Entry(resolution, "drafts").FolderPath);
            Assert.Equal("name", Entry(resolution, "drafts").Provenance);
        }

        [Fact]
        public void ARoleWithNoSourceStaysNull()
        {
            var resolution = FolderRoleResolver.Resolve([Node("Projects")], []);

            var junk = Entry(resolution, "junk");
            Assert.Null(junk.FolderPath);
            Assert.Null(junk.Provenance);
            Assert.Null(junk.StaleOverride);
        }

        // Stale (path gone), and discovery still fructifies: both facts coexist (§ 5.3).
        [Fact]
        public void AStaleOverrideIsSignalledWhileDiscoveryFillsTheRole()
        {
            var tree = new List<MailFolderNode> { Node("Deleted Items", attributeRole: "trash") };

            var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Gone")]);

            var trash = Entry(resolution, "trash");
            Assert.Equal("Gone", trash.StaleOverride!.FolderPath);
            Assert.Equal("Deleted Items", trash.FolderPath);
            Assert.Equal("specialUse", trash.Provenance);
        }

        // Path reuse is the failure mode that lies rather than degrades: same path, different
        // folder, caught by UIDVALIDITY.
        [Fact]
        public void AReusedPathIsStale()
        {
            var tree = new List<MailFolderNode> { Node("Trash", uidValidity: 99) };

            var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Trash", uidValidity: 10)]);

            Assert.NotNull(Entry(resolution, "trash").StaleOverride);
            Assert.False(resolution.RoleByPath.ContainsKey("Trash") && resolution.RoleByPath["Trash"] == "trash"
                         && Entry(resolution, "trash").Provenance == "override");
        }

        // MAILBOXID beats the path: the folder was renamed by another client, the id still
        // finds it.
        [Fact]
        public void AMailboxIdMatchSurvivesARename()
        {
            var tree = new List<MailFolderNode> { Node("Renamed", mailboxId: "M1", uidValidity: 50) };

            var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Old", uidValidity: 10, mailboxId: "M1")]);

            var trash = Entry(resolution, "trash");
            Assert.Equal("Renamed", trash.FolderPath);
            Assert.Equal("override", trash.Provenance);
            Assert.Null(trash.StaleOverride);
        }

        // Stored id but a server that no longer offers OBJECTID: fall back to path + guard.
        [Fact]
        public void AStoredMailboxIdWithoutServerSupportFallsBackToThePath()
        {
            var tree = new List<MailFolderNode> { Node("Trash", uidValidity: 10) };

            var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Trash", uidValidity: 10, mailboxId: "M1")]);

            Assert.Equal("override", Entry(resolution, "trash").Provenance);
        }

        [Fact]
        public void ANonSelectableFolderIsStale()
        {
            var tree = new List<MailFolderNode> { Node("Container", selectable: false) };

            var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Container")]);

            Assert.NotNull(Entry(resolution, "trash").StaleOverride);
        }

        // INBOX is fixed by the protocol: an override pointing at it is invalid, and INBOX
        // keeps its role whatever the stored rows say.
        [Fact]
        public void AnOverrideCannotClaimTheInbox()
        {
            var tree = new List<MailFolderNode> { Node("INBOX", attributeRole: "inbox") };

            var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "INBOX")]);

            Assert.Equal("inbox", resolution.RoleByPath["INBOX"]);
            Assert.NotNull(Entry(resolution, "trash").StaleOverride);
        }

        // Two rows pointing at the same folder (belt-and-braces: the PUT rejects this). The
        // first role in FolderRoles.All order wins; the second is treated as stale.
        [Fact]
        public void TwoOverridesOnTheSameFolderResolveDeterministically()
        {
            var tree = new List<MailFolderNode> { Node("X") };

            var resolution = FolderRoleResolver.Resolve(
                tree, [Override("trash", "X"), Override("junk", "X")]);

            Assert.Equal("override", Entry(resolution, "trash").Provenance);   // trash < junk in All order
            Assert.NotNull(Entry(resolution, "junk").StaleOverride);
        }

        [Fact]
        public void ResolvesAcrossNestedFolders()
        {
            var parent = Node("Projects");
            parent.Children.Add(Node("Projects/Archive", name: "Archive"));

            var resolution = FolderRoleResolver.Resolve([parent], []);

            Assert.Equal("archive", resolution.RoleByPath["Projects/Archive"]);
        }
    }
}
```

- [ ] **Step 3 : Vérifier l'échec** — `dotnet build` → `FolderRoleResolver` inexistant. Attendu.

- [ ] **Step 4 : Implémenter**

`Services/FolderRoleResolver.cs` :

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services
{
    /// <summary>
    /// The role resolution chain (spec § 4.1): user overrides, then SPECIAL-USE flags, then
    /// name matching — each level filling only what the previous one left. Level 2, an
    /// admin-set domain default, was evaluated and rejected; its slot stays vacant on purpose.
    ///
    /// Pure over its inputs — the tree and the stored overrides. No IMAP, no database, no
    /// HTTP: that is what makes every staleness rule testable without a server.
    /// </summary>
    public static class FolderRoleResolver
    {
        public static FolderRoleResolution Resolve(
            IReadOnlyList<MailFolderNode> tree,
            IReadOnlyList<FolderRoleOverride> overrides)
        {
            var flat = new List<MailFolderNode>();
            Flatten(tree, flat);

            var byPath = flat.ToDictionary(n => n.Path, StringComparer.Ordinal);
            var byMailboxId = flat.Where(n => n.MailboxId != null)
                                  .ToDictionary(n => n.MailboxId!, StringComparer.Ordinal);

            // The chain tracks BOTH sets. Tracking roles alone is the natural bug: it passes
            // every test except the one where an override takes a flagged folder, whose flag
            // would then claim a second role for it.
            var claimedRoles = new HashSet<string>(StringComparer.Ordinal);
            var claimedFolders = new HashSet<string>(StringComparer.Ordinal);
            var roleByPath = new Dictionary<string, string>(StringComparer.Ordinal);

            // INBOX first, before any override: it is fixed by the protocol itself, so no
            // stored row may displace it or claim its folder.
            var inbox = flat.FirstOrDefault(n => n.AttributeRole == "inbox");
            if (inbox != null)
            {
                roleByPath[inbox.Path] = "inbox";
                claimedRoles.Add("inbox");
                claimedFolders.Add(inbox.Path);
            }

            // Level 1: user overrides, walked in FolderRoles.All order so ties are deterministic.
            var entries = new List<FolderRoleEntry>();
            foreach (var role in FolderRoles.All)
            {
                var entry = new FolderRoleEntry { Role = role };
                entries.Add(entry);

                var @override = overrides.FirstOrDefault(o => o.Role == role);
                if (@override == null) continue;

                var node = ResolveOverride(@override, byPath, byMailboxId);
                if (node != null && node.Selectable && !claimedFolders.Contains(node.Path))
                {
                    claimedRoles.Add(role);
                    claimedFolders.Add(node.Path);
                    roleByPath[node.Path] = role;
                    entry.FolderPath = node.Path;
                    entry.Provenance = "override";
                }
                else
                {
                    // Kept and signalled (§ 5.3), never auto-deleted; discovery below may
                    // still fill the role, and both facts then coexist in the entry.
                    entry.StaleOverride = new StaleOverrideInfo { FolderPath = @override.FolderPath };
                }
            }

            // Levels 3 and 4: discovery over whatever roles and folders the overrides left.
            var discovered = ImapSession.ResolveSpecialUses(
                flat.Select(n => (n.Path, n.Name, n.AttributeRole)),
                claimedRoles,
                claimedFolders);

            foreach (var (path, assignment) in discovered)
            {
                roleByPath[path] = assignment.Role;

                var entry = entries.FirstOrDefault(e => e.Role == assignment.Role);
                if (entry != null && entry.Provenance == null)
                {
                    entry.FolderPath = path;
                    entry.Provenance = assignment.Source;
                }
            }

            return new FolderRoleResolution { Roles = entries, RoleByPath = roleByPath };
        }

        /// <summary>
        /// A stored override resolves by MAILBOXID when both sides carry one — immune to
        /// renames, ours and other clients' alike — and otherwise by path guarded by
        /// UIDVALIDITY, which catches the one failure mode that lies rather than degrades: a
        /// deleted folder whose path was reused by a different one.
        /// </summary>
        private static MailFolderNode? ResolveOverride(
            FolderRoleOverride @override,
            IReadOnlyDictionary<string, MailFolderNode> byPath,
            IReadOnlyDictionary<string, MailFolderNode> byMailboxId)
        {
            if (@override.MailboxId != null && byMailboxId.TryGetValue(@override.MailboxId, out var byId))
                return byId;

            return byPath.TryGetValue(@override.FolderPath, out var node)
                   && node.UidValidity == @override.UidValidity
                ? node
                : null;
        }

        private static void Flatten(IReadOnlyList<MailFolderNode> nodes, List<MailFolderNode> into)
        {
            foreach (var node in nodes)
            {
                into.Add(node);
                Flatten(node.Children, into);
            }
        }
    }

    public sealed class FolderRoleResolution
    {
        /// <summary>Exactly the five assignable roles, in FolderRoles.All order.</summary>
        public required IReadOnlyList<FolderRoleEntry> Roles { get; init; }

        /// <summary>
        /// Authoritative path→role map, "inbox" included. GET /Folders stamps this onto the
        /// tree's SpecialUse, so the client always sees the chain's output.
        /// </summary>
        public required IReadOnlyDictionary<string, string> RoleByPath { get; init; }
    }
}
```

- [ ] **Step 5 : Vérifier** — `dotnet test` → verts.

- [ ] **Step 6 : Commit** — message : `Add the folder-role resolution chain`.

---

### Task 4: Maintenance des surcharges dans MailFolderRepository

**Files:**
- Modify: `src/snoopy.microservice/Repositories/IMailFolderRepository.cs`
- Modify: `src/snoopy.microservice/Repositories/MailFolderRepository.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/MailFolderRepositoryTests.cs`

**Interfaces:**
- Consumes: `IFolderRoleStore` (Task 1), `IImapSession.GetFolderStatusAsync` + `MailFolderStatus` (Task 2), `FolderRoleStore.CanonicalAccountId`.
- Produces: `IMailFolderRepository.GetFolderStatusAsync(User user, string password, string path, CancellationToken)` → `Result<MailFolderStatus>`. Constructeur de `MailFolderRepository` : `(IImapConnectionFactory, IFolderRoleStore, ILogger<MailFolderRepository>)` — **changement de signature**.

- [ ] **Step 1 : Tests (échec)**

Dans `MailFolderRepositoryTests.cs`, adapter `CreateSut` et ajouter les cas. Remplacer `CreateSut` par :

```csharp
        private static (MailFolderRepository repo, Mock<IImapConnectionFactory> factory,
                        Mock<IImapSession> session, Mock<IFolderRoleStore> store) CreateSut()
        {
            var session = new Mock<IImapSession>();
            session.SetupGet(s => s.DirectorySeparator).Returns('/');

            var factory = new Mock<IImapConnectionFactory>();
            factory.Setup(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IImapSession>(session.Object));

            var store = new Mock<IFolderRoleStore>();

            var repo = new MailFolderRepository(factory.Object, store.Object, Mock.Of<ILogger<MailFolderRepository>>());
            return (repo, factory, session, store);
        }
```

Mettre à jour la déstructuration de chaque test existant (`var (repo, _, session) = CreateSut();` → `var (repo, _, session, _) = CreateSut();`). Ajouter :

```csharp
        private static void SetupRename(Mock<IImapSession> session, string newPath, uint uidValidity = 42, string? mailboxId = null)
        {
            session.Setup(s => s.RenameFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success(newPath));
            session.Setup(s => s.GetFolderStatusAsync(newPath, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success(new MailFolderStatus
                   { Path = newPath, UidValidity = uidValidity, MailboxId = mailboxId, Selectable = true }));
        }

        // The separator handed to the store is the session's — '.' here, on purpose, because a
        // constant '/' would pass every test written against '/' and break on the home server.
        [Fact]
        public async Task Rename_UpdatesOverridesWithTheSessionSeparatorAndFreshIdentity()
        {
            var (repo, _, session, store) = CreateSut();
            session.SetupGet(s => s.DirectorySeparator).Returns('.');
            SetupRename(session, "Work", uidValidity: 42, mailboxId: "M-new");

            var result = await repo.RenameFolderAsync(Alice, "hunter2", "Projects", "", "Work", CancellationToken.None);

            Assert.True(result.IsSuccess);
            store.Verify(s => s.ApplyRenameAsync("alice@weesky.be", "Projects", "Work", '.', 42UL, "M-new",
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Rename_LowercasesTheAccountId()
        {
            var (repo, _, session, store) = CreateSut();
            SetupRename(session, "Work");

            await repo.RenameFolderAsync(new User("Alice@WEESKY.be"), "hunter2", "Projects", "", "Work", CancellationToken.None);

            store.Verify(s => s.ApplyRenameAsync("alice@weesky.be", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<char>(), It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        // IMAP is the source of truth: a failed bookkeeping write degrades to discovery via
        // the staleness guard instead of failing the operation the user asked for.
        [Fact]
        public async Task Rename_StillSucceedsWhenTheStoreWriteFails()
        {
            var (repo, _, session, store) = CreateSut();
            SetupRename(session, "Work");
            store.Setup(s => s.ApplyRenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<char>(), It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("db down"));

            var result = await repo.RenameFolderAsync(Alice, "hunter2", "Projects", "", "Work", CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public async Task Rename_SkipsTheStoreWhenTheStatusReReadFails()
        {
            var (repo, _, session, store) = CreateSut();
            session.Setup(s => s.RenameFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success("Work"));
            session.Setup(s => s.GetFolderStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure<MailFolderStatus>("Unable to read the folder"));

            var result = await repo.RenameFolderAsync(Alice, "hunter2", "Projects", "", "Work", CancellationToken.None);

            Assert.True(result.IsSuccess);
            store.Verify(s => s.ApplyRenameAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<char>(), It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Rename_TouchesNothingWhenImapRefuses()
        {
            var (repo, _, session, store) = CreateSut();
            session.Setup(s => s.RenameFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure<string>("refused"));

            var result = await repo.RenameFolderAsync(Alice, "hunter2", "Projects", "", "Work", CancellationToken.None);

            Assert.True(result.IsFailure);
            store.VerifyNoOtherCalls();
        }

        [Theory]
        [InlineData('/')]
        [InlineData('.')]
        public async Task Delete_PurgesTheSubtreeOverrides(char separator)
        {
            var (repo, _, session, store) = CreateSut();
            session.SetupGet(s => s.DirectorySeparator).Returns(separator);
            session.Setup(s => s.DeleteFolderAsync("Projects", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());

            var result = await repo.DeleteFolderAsync(Alice, "hunter2", "Projects", CancellationToken.None);

            Assert.True(result.IsSuccess);
            store.Verify(s => s.RemoveSubtreeAsync("alice@weesky.be", "Projects", separator,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Delete_TouchesNothingWhenImapRefuses()
        {
            var (repo, _, session, store) = CreateSut();
            session.Setup(s => s.DeleteFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure("The inbox cannot be deleted"));

            await repo.DeleteFolderAsync(Alice, "hunter2", "INBOX", CancellationToken.None);

            store.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task GetFolderStatus_PassesThroughTheSession()
        {
            var (repo, _, session, _) = CreateSut();
            session.Setup(s => s.GetFolderStatusAsync("Archive", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success(new MailFolderStatus { Path = "Archive", UidValidity = 7 }));

            var result = await repo.GetFolderStatusAsync(Alice, "hunter2", "Archive", CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(7u, result.Value.UidValidity);
        }
```

- [ ] **Step 2 : Vérifier l'échec** — `dotnet build` → constructeur et méthodes manquants. Attendu.

- [ ] **Step 3 : Implémenter**

`IMailFolderRepository.cs`, ajouter :

```csharp
        /// <summary>Live identity of one folder — used by the role PUT to validate and capture.</summary>
        Task<Result<MailFolderStatus>> GetFolderStatusAsync(User user, string password, string path, CancellationToken cancellationToken);
```

`MailFolderRepository.cs` : nouveau champ + constructeur :

```csharp
        private readonly IImapConnectionFactory _factory;
        private readonly IFolderRoleStore _roleStore;
        private readonly ILogger<MailFolderRepository> _logger;

        public MailFolderRepository(IImapConnectionFactory factory, IFolderRoleStore roleStore, ILogger<MailFolderRepository> logger)
        {
            _factory = factory;
            _roleStore = roleStore;
            _logger = logger;
        }
```

`RenameFolderAsync` devient :

```csharp
        public async Task<Result<string>> RenameFolderAsync(User user, string password, string path, string newParentPath, string newName, CancellationToken cancellationToken)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure<string>(sessionResult.Error);
            await using var session = sessionResult.Value;

            var renamed = await session.RenameFolderAsync(path, newParentPath, newName, cancellationToken);
            if (renamed.IsFailure) return renamed;

            await TryMoveOverridesAsync(session, user, path, renamed.Value, cancellationToken);
            return renamed;
        }

        /// <summary>
        /// IMAP first, database second. If this bookkeeping fails, the stored overrides go
        /// stale and the resolver's staleness guard degrades them to discovery — the rename
        /// the user asked for is never failed over it. The identity is re-read from the
        /// renamed folder, not carried over: some servers change UIDVALIDITY on rename, and
        /// carrying the old value would make our own rename trip our own guard.
        /// </summary>
        private async Task TryMoveOverridesAsync(IImapSession session, User user, string oldPath, string newPath, CancellationToken cancellationToken)
        {
            try
            {
                var status = await session.GetFolderStatusAsync(newPath, cancellationToken);
                if (status.IsFailure)
                {
                    _logger.LogWarning(
                        "Rename of {OldPath} succeeded but the status re-read failed: {Error}. Overrides left to the staleness guard.",
                        oldPath, status.Error);
                    return;
                }

                await _roleStore.ApplyRenameAsync(
                    FolderRoleStore.CanonicalAccountId(user.Email),
                    oldPath, newPath, session.DirectorySeparator,
                    status.Value.UidValidity, status.Value.MailboxId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to move folder role overrides after renaming {OldPath}", oldPath);
            }
        }
```

`DeleteFolderAsync` devient :

```csharp
        public async Task<Result> DeleteFolderAsync(User user, string password, string path, CancellationToken cancellationToken)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure(sessionResult.Error);
            await using var session = sessionResult.Value;

            var result = await session.DeleteFolderAsync(path, cancellationToken);
            if (result.IsFailure) return result;

            try
            {
                await _roleStore.RemoveSubtreeAsync(
                    FolderRoleStore.CanonicalAccountId(user.Email), path, session.DirectorySeparator, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to purge folder role overrides after deleting {Path}", path);
            }

            return result;
        }
```

Ajouter :

```csharp
        public async Task<Result<MailFolderStatus>> GetFolderStatusAsync(User user, string password, string path, CancellationToken cancellationToken)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));

            var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
            if (sessionResult.IsFailure) return Result.Failure<MailFolderStatus>(sessionResult.Error);
            await using var session = sessionResult.Value;

            return await session.GetFolderStatusAsync(path, cancellationToken);
        }
```

- [ ] **Step 4 : Vérifier** — `dotnet test` → verts. `Rename_TouchesNothingWhenImapRefuses` garde `store.VerifyNoOtherCalls()` honnête : aucune autre méthode du store n'est appelée dans ces chemins.

- [ ] **Step 5 : Commit** — message : `Keep folder-role overrides in step with our own renames and deletes`.

---

### Task 5: Endpoints FolderRoles et intégration de la chaîne dans GET /Folders

**Files:**
- Modify: `src/snoopy.microservice/Controllers/MailController.cs`
- Modify: `src/snoopy.microservice/CLAUDE.md`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs`

**Interfaces:**
- Consumes: `IFolderRoleStore` (Task 1), `FolderRoleResolver` / `FolderRoles` / `FolderRoleEntry` / `SetFolderRoleRequest` (Task 3), `IMailFolderRepository.GetFolderStatusAsync` (Task 4), `ImapSession.FolderNotFound` (Task 2).
- Produces: `GET /api/Mail/FolderRoles` → `IReadOnlyList<FolderRoleEntry>` ; `PUT /api/Mail/FolderRoles` corps `{ role, folderPath }` → 204 ; `DELETE /api/Mail/FolderRoles?role=` → 204. Constructeur de `MailController` : `(IMailFolderRepository, IMailMessageRepository, IMailCredentialStore, IFolderRoleStore)` — **changement de signature**.

- [ ] **Step 1 : Tests (échec)**

Dans `MailControllerTests.cs` : ajouter le mock et l'initialisation par défaut dans `CreateController` :

```csharp
        private readonly Mock<IFolderRoleStore> _roleStore = new();

        private MailController CreateController()
        {
            _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>())).Returns(Result.Success("hunter2"));
            _roleStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                      .ReturnsAsync(new List<FolderRoleOverride>());

            return new MailController(_folders.Object, _messages.Object, _credentials.Object, _roleStore.Object)
            {
                ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be")
            };
        }
```

(`using weesky.Snoopy.Microservice.Data.Preferences;` en tête.) Ajouter les tests :

```csharp
        private static MailFolderNode RoleNode(string path, string? attributeRole = null, uint uidValidity = 1) =>
            new() { Path = path, Name = path, AttributeRole = attributeRole, UidValidity = uidValidity };

        private void SetupOverrides(params FolderRoleOverride[] rows)
            => _roleStore.Setup(s => s.GetAsync("alice@weesky.be", It.IsAny<CancellationToken>()))
                         .ReturnsAsync(rows.ToList());

        private void SetupStatus(string path, uint uidValidity = 1, string? mailboxId = null, bool selectable = true)
            => _folders.Setup(f => f.GetFolderStatusAsync(It.IsAny<User>(), It.IsAny<string>(), path, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(Result.Success(new MailFolderStatus
                       { Path = path, UidValidity = uidValidity, MailboxId = mailboxId, Selectable = selectable }));

        // GET /Folders now returns the chain's output, not raw discovery: the overridden
        // folder carries the overridden role, and the flagged one loses it.
        [Fact]
        public async Task GetFolders_StampsTheResolvedRolesOntoTheTree()
        {
            SetupTree(RoleNode("Deleted Items", attributeRole: "trash"), RoleNode("Corbeille"));
            SetupOverrides(new FolderRoleOverride
            { AccountId = "alice@weesky.be", Role = "trash", FolderPath = "Corbeille", UidValidity = 1 });

            var result = await CreateController().GetFolders(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var tree = Assert.IsAssignableFrom<IReadOnlyList<MailFolderNode>>(ok.Value);
            Assert.Equal("trash", tree.Single(n => n.Path == "Corbeille").SpecialUse);
            Assert.Null(tree.Single(n => n.Path == "Deleted Items").SpecialUse);
        }

        [Fact]
        public async Task GetFolderRoles_ReturnsTheFiveRolesWithProvenance()
        {
            SetupTree(RoleNode("INBOX", attributeRole: "inbox"), RoleNode("Sent", attributeRole: "sent"));

            var result = await CreateController().GetFolderRoles(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var roles = Assert.IsAssignableFrom<IReadOnlyList<FolderRoleEntry>>(ok.Value);
            Assert.Equal(5, roles.Count);
            Assert.Equal("specialUse", roles.Single(r => r.Role == "sent").Provenance);
            Assert.Null(roles.Single(r => r.Role == "archive").FolderPath);
        }

        [Fact]
        public async Task SetFolderRole_RejectsAMissingBody()
        {
            var result = await CreateController().SetFolderRole(null, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Theory]
        [InlineData("inbox")]
        [InlineData("corbeille")]
        [InlineData("")]
        public async Task SetFolderRole_RejectsAnUnknownRole(string role)
        {
            var result = await CreateController().SetFolderRole(
                new SetFolderRoleRequest { Role = role, FolderPath = "X" }, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SetFolderRole_RejectsTheInboxAsTarget()
        {
            var result = await CreateController().SetFolderRole(
                new SetFolderRoleRequest { Role = "trash", FolderPath = "INBOX" }, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        // The client's tree can be stale — another client may have deleted the folder. The
        // PUT validates against the live mailbox, never against what the client displayed.
        [Fact]
        public async Task SetFolderRole_Returns404WhenTheFolderIsGone()
        {
            _folders.Setup(f => f.GetFolderStatusAsync(It.IsAny<User>(), It.IsAny<string>(), "Gone", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Failure<MailFolderStatus>(ImapSession.FolderNotFound));

            var result = await CreateController().SetFolderRole(
                new SetFolderRoleRequest { Role = "trash", FolderPath = "Gone" }, CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result);
        }

        // A trash that cannot hold messages is not a trash — and 2b would fail writing to it.
        [Fact]
        public async Task SetFolderRole_RejectsANonSelectableFolder()
        {
            SetupStatus("Container", selectable: false);

            var result = await CreateController().SetFolderRole(
                new SetFolderRoleRequest { Role = "trash", FolderPath = "Container" }, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task SetFolderRole_RejectsAFolderAlreadyHoldingAnotherRole()
        {
            SetupStatus("X");
            SetupOverrides(new FolderRoleOverride
            { AccountId = "alice@weesky.be", Role = "junk", FolderPath = "X", UidValidity = 1 });

            var result = await CreateController().SetFolderRole(
                new SetFolderRoleRequest { Role = "trash", FolderPath = "X" }, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        // uid_validity and mailbox_id come from the live folder, captured server-side — the
        // client never supplies them.
        [Fact]
        public async Task SetFolderRole_StoresTheLiveIdentityUnderTheCanonicalAccount()
        {
            SetupStatus("Corbeille", uidValidity: 77, mailboxId: "M1");

            var result = await CreateController().SetFolderRole(
                new SetFolderRoleRequest { Role = "trash", FolderPath = "Corbeille" }, CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            _roleStore.Verify(s => s.UpsertAsync(It.Is<FolderRoleOverride>(o =>
                o.AccountId == "alice@weesky.be" && o.Role == "trash" && o.FolderPath == "Corbeille"
                && o.UidValidity == 77UL && o.MailboxId == "M1"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ClearFolderRole_RejectsAnUnknownRole()
        {
            var result = await CreateController().ClearFolderRole("poubelle", CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ClearFolderRole_DeletesAndReturns204()
        {
            var result = await CreateController().ClearFolderRole("trash", CancellationToken.None);

            Assert.IsType<NoContentResult>(result);
            _roleStore.Verify(s => s.DeleteAsync("alice@weesky.be", "trash", It.IsAny<CancellationToken>()), Times.Once);
        }
```

- [ ] **Step 2 : Vérifier l'échec** — `dotnet build` → constructeur et actions manquants. Attendu.

- [ ] **Step 3 : Implémenter le contrôleur**

Constructeur et champ :

```csharp
        private readonly IFolderRoleStore _roleStore;

        public MailController(
            IMailFolderRepository folders,
            IMailMessageRepository messages,
            IMailCredentialStore credentials,
            IFolderRoleStore roleStore)
        {
            _folders = folders;
            _messages = messages;
            _credentials = credentials;
            _roleStore = roleStore;
        }
```

`GetFolders` : après l'appel à `GetTreeAsync` réussi, appliquer la chaîne — le corps devient :

```csharp
            var password = _credentials.Retrieve(Request);
            if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

            var result = await _folders.GetTreeAsync(AuthenticatedUser, password.Value, cancellationToken);
            if (result.IsFailure)
                return StatusCode(StatusCodes.Status502BadGateway, ResultEnveloppe.CreateErrorEnveloppe(result.Error));

            // The tree's SpecialUse is the resolution chain's output, not raw discovery: a
            // user override reassigns the role, and the displaced folder shows under its own
            // name (spec § 4.1).
            var overrides = await _roleStore.GetAsync(FolderRoleStore.CanonicalAccountId(AuthenticatedUser.Email), cancellationToken);
            var resolution = FolderRoleResolver.Resolve(result.Value, overrides);
            StampRoles(result.Value, resolution.RoleByPath);

            return Ok(result.Value);
```

avec, en bas de classe :

```csharp
        private static void StampRoles(IReadOnlyList<MailFolderNode> nodes, IReadOnlyDictionary<string, string> roleByPath)
        {
            foreach (var node in nodes)
            {
                node.SpecialUse = roleByPath.TryGetValue(node.Path, out var role) ? role : null;
                StampRoles(node.Children, roleByPath);
            }
        }
```

Les trois actions (placer après les actions Folders existantes) :

```csharp
        /// <summary>
        /// The five assignable roles, each with what it resolves to and why: the user's
        /// override, a server SPECIAL-USE flag, or a name match. A stale override — its folder
        /// renamed or deleted outside this app — is signalled alongside whatever discovery
        /// now yields; it is kept, never auto-deleted (spec § 5.3).
        /// </summary>
        /// <param name="cancellationToken">cancellation token</param>
        /// <response code="200">The five roles</response>
        /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
        /// <response code="502">The mail server could not be reached</response>
        [HttpGet("FolderRoles")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<ActionResult<IReadOnlyList<FolderRoleEntry>>> GetFolderRoles(CancellationToken cancellationToken)
        {
            var password = _credentials.Retrieve(Request);
            if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

            var tree = await _folders.GetTreeAsync(AuthenticatedUser, password.Value, cancellationToken);
            if (tree.IsFailure)
                return StatusCode(StatusCodes.Status502BadGateway, ResultEnveloppe.CreateErrorEnveloppe(tree.Error));

            var overrides = await _roleStore.GetAsync(FolderRoleStore.CanonicalAccountId(AuthenticatedUser.Email), cancellationToken);
            var resolution = FolderRoleResolver.Resolve(tree.Value, overrides);

            return Ok(resolution.Roles);
        }

        /// <summary>
        /// Assigns a role to a folder. Validated against the live mailbox, never against the
        /// client's tree: the folder must exist, be selectable, and not be the inbox. The
        /// identity guard (uid_validity, mailbox_id) is captured server-side from the live
        /// folder — the client only names the role and the path.
        /// </summary>
        /// <param name="request">role and folder path</param>
        /// <param name="cancellationToken">cancellation token</param>
        /// <response code="204">Override stored</response>
        /// <response code="400">Unknown role, missing path, inbox target, non-selectable folder, or folder already holding another role</response>
        /// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
        /// <response code="404">The folder no longer exists</response>
        /// <response code="502">The mail server could not be reached</response>
        [HttpPut("FolderRoles")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<ActionResult> SetFolderRole(SetFolderRoleRequest request, CancellationToken cancellationToken)
        {
            if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));
            if (!FolderRoles.IsValid(request.Role)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Unknown folder role"));
            if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder path is required"));
            if (string.Equals(request.FolderPath, "INBOX", StringComparison.OrdinalIgnoreCase))
                return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("The inbox cannot be assigned a role"));

            var password = _credentials.Retrieve(Request);
            if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

            var status = await _folders.GetFolderStatusAsync(AuthenticatedUser, password.Value, request.FolderPath, cancellationToken);
            if (status.IsFailure)
            {
                return status.Error == ImapSession.FolderNotFound
                    ? NotFound(ResultEnveloppe.CreateErrorEnveloppe(status.Error))
                    : StatusCode(StatusCodes.Status502BadGateway, ResultEnveloppe.CreateErrorEnveloppe(status.Error));
            }

            if (!status.Value.Selectable)
                return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("This folder cannot hold messages"));

            var accountId = FolderRoleStore.CanonicalAccountId(AuthenticatedUser.Email);
            var overrides = await _roleStore.GetAsync(accountId, cancellationToken);
            if (overrides.Any(o => o.FolderPath == request.FolderPath && o.Role != request.Role))
                return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("This folder already holds another role"));

            await _roleStore.UpsertAsync(new FolderRoleOverride
            {
                AccountId = accountId,
                Role = request.Role!,
                FolderPath = request.FolderPath,
                UidValidity = status.Value.UidValidity,
                MailboxId = status.Value.MailboxId
            }, cancellationToken);

            return NoContent();
        }

        /// <summary>Clears an override; the role goes back to discovery. Idempotent.</summary>
        /// <param name="role">role to clear</param>
        /// <param name="cancellationToken">cancellation token</param>
        /// <response code="204">Override cleared, or was already absent</response>
        /// <response code="400">Unknown role</response>
        /// <response code="401">Not authenticated</response>
        [HttpDelete("FolderRoles")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<ActionResult> ClearFolderRole([FromQuery] string? role, CancellationToken cancellationToken)
        {
            if (!FolderRoles.IsValid(role)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Unknown folder role"));

            await _roleStore.DeleteAsync(FolderRoleStore.CanonicalAccountId(AuthenticatedUser.Email), role!, cancellationToken);
            return NoContent();
        }
```

Ajouter `using weesky.Snoopy.Microservice.Data.Preferences;` en tête du contrôleur.

- [ ] **Step 4 : Vérifier** — `dotnet test` → verts (les deux tests `GetFolders_*` existants passent toujours : sans surcharge, la chaîne reproduit la découverte — `INBOX` obtient `inbox` par le repli sur le nom même sans `AttributeRole`).

- [ ] **Step 5 : Documentation backend**

Dans `src/snoopy.microservice/CLAUDE.md` : ajouter à la liste des routes de `MailController` :

```
`GET/PUT/DELETE /api/Mail/FolderRoles` (affectation des rôles systèmes — chaîne surcharge utilisateur → SPECIAL-USE → nom)
```

et, après la règle 3 de la section Mail, une règle 4 :

```markdown
4. **Folder roles resolve through an ordered chain, and its output is what the client sees.** User override (per account, stored in the separate `snoopy_webmail` database via `PreferencesDbContext`/`IFolderRoleStore`) → `SPECIAL-USE` flags → multilingual name fallback, each level filling only what the previous left, tracking **both** claimed roles and claimed folders — one folder never holds two roles. `FolderRoleResolver` is pure over the tree and the stored rows; `GET /Folders` stamps its `RoleByPath` onto every node's `SpecialUse`. Overrides store the **path** (the only identifier IMAP guarantees), guarded by `uid_validity` against path reuse, with RFC 8474 `MAILBOXID` as an optional aid — never the key. Stale overrides are kept and signalled, never auto-deleted. Our own renames/deletes move or purge the rows (IMAP first, database second; a failed bookkeeping write degrades, never fails the user's operation). Database creation is manual — see `docs/superpowers/mail-2a5-database-prerequisite.md`; the service refuses to start without the `WebmailPreferencesDatabase` connection string.
```

- [ ] **Step 6 : Commit** — message : `Expose folder roles over HTTP and stamp the chain onto the tree`.

---

### Task 6: Frontend — couche de données et libellés de rôle

**Files:**
- Modify: `src/frontend/src/api.js` (section Mail)
- Modify: `src/frontend/src/modules/mail/api/mailTypes.ts`
- Modify: `src/frontend/src/modules/mail/queries.ts`
- Create: `src/frontend/src/modules/mail/roleLabel.ts`
- Test: `src/frontend/src/modules/mail/roleLabel.test.ts`

**Interfaces:**
- Consumes: les endpoints de la Task 5.
- Produces: `api.getFolderRoles(options)`, `api.setFolderRole(role, folderPath)`, `api.clearFolderRole(role)` ; type `FolderRoleEntry` ; hooks `useFolderRoles()`, `useSetFolderRole()`, `useClearFolderRole()` ; `roleLabel(role: string): string` et `mailKeys.folderRoles(accountId)`.

- [ ] **Step 1 : Test de roleLabel (échec)**

`src/modules/mail/roleLabel.test.ts` :

```typescript
import { describe, it, expect } from 'vitest'
import { roleLabel } from './roleLabel'

describe('roleLabel', () => {
  it.each([
    ['inbox', 'Inbox'], ['sent', 'Sent'], ['drafts', 'Drafts'],
    ['trash', 'Trash'], ['junk', 'Junk'], ['archive', 'Archive'],
  ])('labels %s as %s', (role, label) => {
    expect(roleLabel(role)).toBe(label)
  })

  it('falls back to the raw value for an unknown role', () => {
    expect(roleLabel('mystery')).toBe('mystery')
  })
})
```

- [ ] **Step 2 : Vérifier l'échec** — `cd src/frontend && npm run test -- src/modules/mail/roleLabel` → module introuvable. Attendu.

- [ ] **Step 3 : Implémenter**

`src/modules/mail/roleLabel.ts` :

```typescript
/**
 * Display label for a well-known folder role. This function is the i18n seam: today it
 * returns hard-coded English, and when the site goes multilingual only this function changes
 * — the role stays the language-independent canonical key everywhere else.
 */
const LABELS: Record<string, string> = {
  inbox: 'Inbox',
  sent: 'Sent',
  drafts: 'Drafts',
  trash: 'Trash',
  junk: 'Junk',
  archive: 'Archive',
}

export function roleLabel(role: string): string {
  return LABELS[role] ?? role
}
```

Dans `api.js`, à la fin de la section Mail (après `getMailMessage`) :

```javascript
  getFolderRoles: (options) =>
    request('GET', '/api/Mail/FolderRoles', undefined, options),

  setFolderRole: (role, folderPath) =>
    request('PUT', '/api/Mail/FolderRoles', { role, folderPath }),

  clearFolderRole: (role) =>
    request('DELETE', `/api/Mail/FolderRoles?role=${encodeURIComponent(role)}`),
```

Dans `mailTypes.ts` :

```typescript
export interface FolderRoleStaleOverride {
  folderPath: string
}

/** One assignable role: what it resolves to today, and why. */
export interface FolderRoleEntry {
  role: string
  folderPath: string | null
  provenance: 'override' | 'specialUse' | 'name' | null
  /** The user's stored choice no longer matches a live folder — kept and signalled. */
  staleOverride: FolderRoleStaleOverride | null
}
```

Dans `queries.ts` : ajouter la clé à `mailKeys` :

```typescript
  folderRoles: (accountId: string) => ['mail', accountId, 'folderRoles'] as const,
```

et, après `useMessage` :

```typescript
export function useFolderRoles() {
  const accountId = useAccountId()

  return useQuery<FolderRoleEntry[]>({
    queryKey: mailKeys.folderRoles(accountId),
    queryFn: ({ signal }) => api.getFolderRoles({ signal }),
  })
}

/**
 * Role mutations invalidate the roles AND the folder tree: the tree's labels are the chain's
 * output, so changing a role changes what the tree displays.
 */
function useRoleMutation<TArgs>(mutationFn: (args: TArgs) => Promise<unknown>) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: mailKeys.folderRoles(accountId) })
      queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) })
    },
  })
}

export const useSetFolderRole = () =>
  useRoleMutation<{ role: string; folderPath: string }>(
    ({ role, folderPath }) => api.setFolderRole(role, folderPath))

export const useClearFolderRole = () =>
  useRoleMutation<{ role: string }>(({ role }) => api.clearFolderRole(role))
```

(`FolderRoleEntry` rejoint l'import de types en tête de `queries.ts`.)

- [ ] **Step 4 : Vérifier** — `npm run test && npm run typecheck` → verts. La couverture comportementale des hooks passe par les tests de la page (Task 8), qui traversent `queries.ts` avec `api` mocké.

- [ ] **Step 5 : Commit** — message : `Add the folder-role data layer and the role label seam`.

---

### Task 7: Libellés dans l'arbre, lien depuis la gestion des dossiers

**Files:**
- Modify: `src/frontend/src/modules/mail/folders/FolderTree.tsx`
- Modify: `src/frontend/src/modules/mail/MailLayout.tsx`
- Modify: `src/frontend/src/modules/mail/folders/FolderDialogs.tsx`
- Test: `src/frontend/src/modules/mail/folders/FolderTree.test.tsx`
- Test: `src/frontend/src/modules/mail/folders/FolderDialogs.test.tsx`

**Interfaces:**
- Consumes: `roleLabel` (Task 6).
- Produces: rien de nouveau — comportement d'affichage.

- [ ] **Step 1 : Tests (échec)**

`FolderTree.test.tsx` — le libellé du rôle remplace le nom, donc les assertions sur les noms bruts changent. Mises à jour exactes :
- `renders subscribed folders and hides unsubscribed ones` : `getByText('INBOX')` → `getByText('Inbox')`.
- `marks the selected folder` : `{ name: /INBOX/ }` → `{ name: 'Inbox' }`.
- `orders well-known folders before ordinary ones` : tableau attendu `['Inbox', 'Trash', 'Alpha', 'Zebra']`.
- `always shows the inbox even when the server reports it unsubscribed` : `getByText('INBOX')` → `getByText('Inbox')`.
- `still hides an ordinary unsubscribed folder` : `getByText('INBOX')` → `getByText('Inbox')`.
- `does not badge unread counts in the trash or the junk folder` : `getByText('Deleted Items')` → `getByText('Trash')`, `getByText('Junk')` inchangé (le nom coïncide avec le libellé).

Ajouter :

```tsx
  // The role label replaces the folder name — that is the point of assigning roles — but the
  // real mailbox name must stay reachable: it lives in the button's title.
  it('shows the role label and keeps the real name as the tooltip', () => {
    const folders = [
      node({ path: 'Deleted Items', name: 'Deleted Items', specialUse: 'trash' }),
      node({ path: 'Perso', name: 'Perso' }),
    ]

    render(<FolderTree folders={folders} selectedPath={null} onSelect={vi.fn()} />)

    expect(screen.getByText('Trash')).toBeInTheDocument()
    expect(screen.queryByText('Deleted Items')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Trash' })).toHaveAttribute('title', 'Deleted Items')
    // An ordinary folder keeps its name and needs no tooltip.
    expect(screen.getByRole('button', { name: 'Perso' })).not.toHaveAttribute('title')
  })
```

`FolderDialogs.test.tsx` — les rendus doivent être enveloppés dans un routeur (le lien vers Settings l'exige). En tête : `import { MemoryRouter } from 'react-router-dom'`. Dans `renderDialogs` et dans les deux `render(...)` inline (test inbox et test toggle-switch), envelopper : `render(<MemoryRouter><FolderDialogs ... /></MemoryRouter>)`. Ajouter :

```tsx
  it('links the manage dialog to the system-folders settings', () => {
    renderDialogs()
    openManage()

    const link = screen.getByRole('link', { name: /system folders/i })
    expect(link).toHaveAttribute('href', '/settings/system-folders')
  })
```

- [ ] **Step 2 : Vérifier l'échec** — `npm run test -- src/modules/mail/folders` → échecs. Attendu.

- [ ] **Step 3 : Implémenter**

`FolderTree.tsx` : ajouter `import { roleLabel } from '../roleLabel'` et remplacer le bouton de sélection dans `FolderRow` :

```tsx
        <button
          type="button"
          className={isActive ? 'folder-row is-active' : 'folder-row'}
          aria-current={isActive ? 'true' : undefined}
          // The role label replaces the name; the real mailbox name stays one hover away, so
          // the user never loses track of which physical folder they are looking at.
          title={folder.specialUse ? folder.name : undefined}
          // A container-only folder holds no messages, so selecting it would show nothing.
          disabled={!folder.selectable}
          onClick={() => folder.selectable && onSelect(folder.path)}
        >
          <span className="folder-row-name">
            {folder.specialUse ? roleLabel(folder.specialUse) : folder.name}
          </span>
          {folder.unread && showsUnreadCount(folder)
            ? <span className="folder-row-count">{folder.unread}</span>
            : null}
        </button>
```

`MailLayout.tsx` : l'en-tête de liste suit le même libellé. Ajouter `import { roleLabel } from './roleLabel'` et remplacer le calcul de `folderName` :

```tsx
  // The list heading shows the same label as the tree: the role label when the folder has a
  // role, the leaf name otherwise — never the full path, which reads "INBOX.Linux server"
  // under a '.' separator.
  const folderNode = folders && folder
    ? flatten(folders).find(entry => entry.node.path === folder)?.node
    : undefined
  const folderName = folderNode
    ? (folderNode.specialUse ? roleLabel(folderNode.specialUse) : folderNode.name)
    : undefined
```

`FolderDialogs.tsx` : ajouter `import { Link } from 'react-router-dom'` et remplacer le hint de la popup de gestion :

```tsx
            <p className="modal-hint">
              Turning a folder off hides it from the tree. Nothing in it is deleted. To choose
              which folders act as Sent, Drafts, Trash, Junk and Archive, use{' '}
              <Link to="/settings/system-folders">system folders</Link>.
            </p>
```

- [ ] **Step 4 : Vérifier** — `npm run test && npm run typecheck` → verts.

- [ ] **Step 5 : Commit** — message : `Show role labels in the tree and link folder management to system folders`.

---

### Task 8: Page Settings « System folders »

**Files:**
- Create: `src/frontend/src/modules/settings/mail/SystemFoldersPage.tsx`
- Create: `src/frontend/src/modules/settings/mail/SystemFoldersPage.test.tsx`
- Modify: `src/frontend/src/routes.tsx`
- Modify: `src/frontend/src/modules/settings/SettingsLayout.tsx`
- Modify: `src/frontend/CLAUDE.md`

**Interfaces:**
- Consumes: `useFolders`, `useFolderRoles`, `useSetFolderRole`, `useClearFolderRole` (Task 6), `flatten` (FolderDialogs), `roleLabel` (Task 6), `useToasts`/`Toasts`.
- Produces: route `/settings/system-folders`.

- [ ] **Step 1 : Tests (échec)**

`SystemFoldersPage.test.tsx` :

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import SystemFoldersPage from './SystemFoldersPage'
import type { FolderRoleEntry, MailFolderNode } from '../../mail/api/mailTypes'

const mocks = vi.hoisted(() => ({
  getMailFolders: vi.fn(),
  getFolderRoles: vi.fn(),
  setFolderRole: vi.fn(),
  clearFolderRole: vi.fn(),
}))

vi.mock('../../../api.js', () => ({ api: mocks }))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' } }),
}))

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

function node(partial: Partial<MailFolderNode>): MailFolderNode {
  return {
    path: 'X', name: 'X', specialUse: null, selectable: true, subscribed: true,
    total: 0, unread: 0, uidValidity: 1, children: [], ...partial,
  }
}

function entry(partial: Partial<FolderRoleEntry> & { role: string }): FolderRoleEntry {
  return { folderPath: null, provenance: null, staleOverride: null, ...partial }
}

const folders = [
  node({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
  node({ path: 'Deleted Items', name: 'Deleted Items', specialUse: 'trash' }),
  node({ path: 'Corbeille', name: 'Corbeille' }),
  node({ path: 'Container', name: 'Container', selectable: false }),
]

const roles = [
  entry({ role: 'sent' }),
  entry({ role: 'drafts' }),
  entry({ role: 'trash', folderPath: 'Deleted Items', provenance: 'specialUse' }),
  entry({ role: 'junk' }),
  entry({ role: 'archive' }),
]

function renderPage() {
  mocks.getMailFolders.mockResolvedValue(folders)
  mocks.getFolderRoles.mockResolvedValue(roles)
  return render(<SystemFoldersPage />, { wrapper })
}

describe('SystemFoldersPage', () => {
  beforeEach(() => vi.clearAllMocks())

  it('offers one labelled select per assignable role', async () => {
    renderPage()

    expect(await screen.findByLabelText('Sent')).toBeInTheDocument()
    expect(screen.getByLabelText('Drafts')).toBeInTheDocument()
    expect(screen.getByLabelText('Trash')).toBeInTheDocument()
    expect(screen.getByLabelText('Junk')).toBeInTheDocument()
    expect(screen.getByLabelText('Archive')).toBeInTheDocument()
    // Inbox is fixed by the protocol: no select for it.
    expect(screen.queryByLabelText('Inbox')).not.toBeInTheDocument()
  })

  it('says what automatic currently resolves to', async () => {
    renderPage()

    const trash = await screen.findByLabelText('Trash')
    expect(trash).toHaveDisplayValue(/Automatic — Deleted Items/)
  })

  it('shows an override as the selected folder', async () => {
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getFolderRoles.mockResolvedValue([
      ...roles.filter(r => r.role !== 'trash'),
      entry({ role: 'trash', folderPath: 'Corbeille', provenance: 'override' }),
    ])
    render(<SystemFoldersPage />, { wrapper })

    expect(await screen.findByLabelText('Trash')).toHaveValue('Corbeille')
  })

  it('assigns a role through the API', async () => {
    mocks.setFolderRole.mockResolvedValue(undefined)
    renderPage()

    fireEvent.change(await screen.findByLabelText('Trash'), { target: { value: 'Corbeille' } })

    await waitFor(() => expect(mocks.setFolderRole).toHaveBeenCalledWith('trash', 'Corbeille'))
  })

  it('clears a role when Automatic is chosen', async () => {
    mocks.clearFolderRole.mockResolvedValue(undefined)
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getFolderRoles.mockResolvedValue([
      ...roles.filter(r => r.role !== 'trash'),
      entry({ role: 'trash', folderPath: 'Corbeille', provenance: 'override' }),
    ])
    render(<SystemFoldersPage />, { wrapper })

    fireEvent.change(await screen.findByLabelText('Trash'), { target: { value: '' } })

    await waitFor(() => expect(mocks.clearFolderRole).toHaveBeenCalledWith('trash'))
  })

  it('surfaces the backend message when the assignment fails', async () => {
    mocks.setFolderRole.mockRejectedValue(new Error('This folder already holds another role'))
    renderPage()

    fireEvent.change(await screen.findByLabelText('Trash'), { target: { value: 'Corbeille' } })

    expect(await screen.findByText('This folder already holds another role')).toBeInTheDocument()
  })

  // A stale override is kept and signalled (§ 5.3) — the notice and the discovery-resolved
  // value coexist on screen.
  it('signals an invalidated choice next to what resolution now yields', async () => {
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getFolderRoles.mockResolvedValue([
      ...roles.filter(r => r.role !== 'trash'),
      entry({
        role: 'trash', folderPath: 'Deleted Items', provenance: 'specialUse',
        staleOverride: { folderPath: 'Old Trash' },
      }),
    ])
    render(<SystemFoldersPage />, { wrapper })

    expect(await screen.findByText(/“Old Trash” was renamed or deleted/)).toBeInTheDocument()
    expect(screen.getByLabelText('Trash')).toHaveDisplayValue(/Automatic — Deleted Items/)
  })

  it('never offers the inbox or a non-selectable folder', async () => {
    renderPage()

    const options = Array.from((await screen.findByLabelText('Trash')).querySelectorAll('option'))
      .map(option => option.getAttribute('value'))

    expect(options).not.toContain('INBOX')
    expect(options).not.toContain('Container')
    expect(options).toContain('Corbeille')
  })

  it('excludes a folder already overridden for another role, but keeps its own', async () => {
    mocks.getMailFolders.mockResolvedValue(folders)
    mocks.getFolderRoles.mockResolvedValue([
      ...roles.filter(r => r.role !== 'junk'),
      entry({ role: 'junk', folderPath: 'Corbeille', provenance: 'override' }),
    ])
    render(<SystemFoldersPage />, { wrapper })

    const trashOptions = Array.from((await screen.findByLabelText('Trash')).querySelectorAll('option'))
      .map(option => option.getAttribute('value'))
    const junkOptions = Array.from(screen.getByLabelText('Junk').querySelectorAll('option'))
      .map(option => option.getAttribute('value'))

    expect(trashOptions).not.toContain('Corbeille')   // taken by junk
    expect(junkOptions).toContain('Corbeille')        // its own override stays choosable
  })
})
```

- [ ] **Step 2 : Vérifier l'échec** — `npm run test -- src/modules/settings/mail` → module introuvable. Attendu.

- [ ] **Step 3 : Implémenter la page**

`src/modules/settings/mail/SystemFoldersPage.tsx` :

```tsx
import { useState } from 'react'
import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import { flatten } from '../../mail/folders/FolderDialogs'
import { roleLabel } from '../../mail/roleLabel'
import { useClearFolderRole, useFolderRoles, useFolders, useSetFolderRole } from '../../mail/queries'
import type { FolderRoleEntry } from '../../mail/api/mailTypes'

const ROLES = ['sent', 'drafts', 'trash', 'junk', 'archive']

/**
 * Assigns the five system roles to folders. "Automatic" follows what the server declares —
 * the right default for a freshly provisioned mailbox — and a pick corrects it where a messy
 * history (say, both "Drafts" and "Brouillons") made the detection guess wrong.
 */
export default function SystemFoldersPage() {
  const { data: folders, isLoading: foldersLoading, isError: foldersError } = useFolders()
  const { data: roles, isLoading: rolesLoading, isError: rolesError } = useFolderRoles()
  const setRole = useSetFolderRole()
  const clearRole = useClearFolderRole()
  const { toasts, addToast, removeToast } = useToasts()
  const [pendingRole, setPendingRole] = useState<string | null>(null)

  if (foldersLoading || rolesLoading) return <p>Loading…</p>
  if (foldersError || rolesError || !folders || !roles) {
    return <p>Could not load the folder configuration.</p>
  }

  const all = flatten(folders)
  const overrideByPath = new Map(
    roles
      .filter(entry => entry.provenance === 'override' && entry.folderPath)
      .map(entry => [entry.folderPath as string, entry.role]))

  const nameOf = (path: string | null) =>
    path ? all.find(item => item.node.path === path)?.node.name ?? path : null

  async function onChange(role: string, value: string) {
    setPendingRole(role)
    try {
      if (value === '') {
        await clearRole.mutateAsync({ role })
        addToast(`${roleLabel(role)} is back to automatic detection`)
      } else {
        await setRole.mutateAsync({ role, folderPath: value })
        addToast(`${roleLabel(role)} now points at "${nameOf(value)}"`)
      }
    } catch (error) {
      addToast(error instanceof Error ? error.message : 'Could not save the folder role', 'error')
    } finally {
      setPendingRole(null)
    }
  }

  return (
    <div>
      <h2>System folders</h2>
      <p style={{ color: 'var(--text-muted)', fontSize: 13, margin: '6px 0 20px' }}>
        Which folders act as Sent, Drafts, Trash, Junk and Archive. Automatic follows what the
        server declares; pick a folder only where the detection gets it wrong.
      </p>

      {ROLES.map(role => {
        const entry = roles.find(item => item.role === role)
        const selected = entry?.provenance === 'override' ? entry.folderPath ?? '' : ''
        const options = all.filter(({ node }) =>
          node.selectable
          && node.specialUse !== 'inbox'
          && (!overrideByPath.has(node.path) || overrideByPath.get(node.path) === role))

        return (
          <div key={role}>
            <div className="field-h">
              <label htmlFor={`role-${role}`}>{roleLabel(role)}</label>
              <select
                id={`role-${role}`}
                value={selected}
                disabled={pendingRole === role}
                onChange={event => onChange(role, event.target.value)}
              >
                <option value="">{automaticLabel(entry, nameOf)}</option>
                {options.map(({ node, depth }) => (
                  <option key={node.path} value={node.path}>{' '.repeat(depth * 3)}{node.name}</option>
                ))}
              </select>
            </div>
            {entry?.staleOverride && (
              // Kept and signalled, never silently dropped: the user's choice was invalidated
              // outside this app, and this is the place where they can act on it.
              <p style={{ color: 'var(--text-muted)', fontSize: 12.5, margin: '-6px 0 12px 126px' }}>
                Your previous choice &ldquo;{entry.staleOverride.folderPath}&rdquo; was renamed
                or deleted outside this app.
              </p>
            )}
          </div>
        )
      })}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}

/** The empty option says what "automatic" currently resolves to, so choosing it is informed. */
function automaticLabel(
  entry: FolderRoleEntry | undefined,
  nameOf: (path: string | null) => string | null,
): string {
  if (!entry || entry.provenance === 'override') return 'Automatic'
  if (!entry.folderPath) return 'Automatic — not set'
  return `Automatic — ${nameOf(entry.folderPath)}`
}
```

- [ ] **Step 4 : Route et navigation**

`routes.tsx` : ajouter l'import `import SystemFoldersPage from './modules/settings/mail/SystemFoldersPage'` (non-lazy, comme `AccountPage`) et l'enfant après `appearance` :

```tsx
              { path: 'system-folders', element: <SystemFoldersPage /> },
```

`SettingsLayout.tsx` : après le lien Appearance :

```tsx
        <NavLink to="/settings/system-folders" className={paneClass}>System folders</NavLink>
```

- [ ] **Step 5 : Vérifier** — `npm run test && npm run typecheck && npm run lint && npm run build` → verts. Vérifier au passage que le test de `SettingsLayout.test.tsx` ne fige pas la liste exhaustive des liens (s'il énumère les liens, ajouter « System folders » à la liste attendue).

- [ ] **Step 6 : Documentation frontend**

Dans `src/frontend/CLAUDE.md` : ajouter la route au tableau (`/settings/system-folders   SystemFoldersPage`) ; dans la description du module settings, ajouter `mail/ — SystemFoldersPage.tsx (affectation des rôles systèmes ; les <select> excluent l'inbox, les dossiers non sélectionnables et ceux déjà surchargés pour un autre rôle)` ; et dans le module mail, une ligne : « Le libellé de rôle (`roleLabel`, couture i18n) remplace le nom du dossier dans l'arbre et l'en-tête de liste ; le nom réel reste en `title`. Une surcharge périmée est signalée dans Settings à côté de la valeur que la découverte fournit désormais. »

- [ ] **Step 7 : Commit** — message : `Add the system-folders settings page`.

---

## Self-review (fait à l'écriture)

- **Couverture spec** : § 4.1 chaîne + double ensemble → Tasks 2/3 ; § 4.3 stockage + normalisation → Task 1 ; § 4.4 péremption (chemin, uid, OBJECTID, non-sélectionnable, INBOX) → Task 3 ; § 4.5 maintenance (deux séparateurs, uid relue, IMAP d'abord) → Tasks 1/4 ; § 4.6 endpoints + contrat PUT → Task 5 ; § 4.7 unicité (exclusion côté select, rejet backend) → Tasks 5/8 ; § 5.1 page Settings + lien depuis la popup → Tasks 7/8 ; § 5.2 libellés + couture i18n + `title` → Tasks 6/7 ; § 5.3 garder-et-signaler → Tasks 3/5/8 ; § 7 démarrage refusé sans chaîne → Task 1 ; § 8 : chaque cas de test de la spec est nommé dans une tâche.
- **Hors périmètre respecté** : pas de défaut de domaine, pas de catalogue i18n, pas d'action de message.
- **Cohérence de types** : `ulong UidValidity` (entité) vs `uint` (nœud) — comparaison par élargissement implicite, assertions `77UL`/`42UL` dans les tests ; signatures de `ApplyRenameAsync` identiques dans l'interface, l'implémentation, les tests et l'appelant.
