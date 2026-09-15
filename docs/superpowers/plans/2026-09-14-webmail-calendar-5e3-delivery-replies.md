# Agenda 5e3 — réponses appliquées à la livraison : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan package by package (A to F; each package is one dispatch and one review, its numbered parts keep their own commits). Steps use checkbox (`- [ ]`) syntax for tracking. Les rapports de sous-agents dans le scratchpad sont préfixés `5e3-paquet-X-…`.

**Spec :** [2026-09-14-webmail-calendar-5e3-delivery-replies-design.md](../specs/2026-09-14-webmail-calendar-5e3-delivery-replies-design.md) — toute « décision N » citée ici y renvoie, et elle fait foi.

**Goal :** Dovecot applique la réponse d'un invité dans l'agenda de l'organisateur au moment où il livre le mail, par une porte interne du microservice protégée par une clé générée dans Administration ; l'ouverture du mail reste un secours.

**Architecture :** `InvitationReplyApplier` est coupé en un cœur `ApplyIcsAsync(user, ics, cause)` que deux entrées appellent — la récupération IMAP d'aujourd'hui et la nouvelle porte `POST /api/Delivery/CalendarReplies`, qui lit le mail brut avec MimeKit et choisit la partie calendrier par la même règle que le chemin IMAP. La clé (empreinte SHA-256, `enabled`, `last_call_at`) vit dans une table à une ligne derrière un fournisseur singleton invalidé à l'écriture ; `/api/DeliveryReplyKey` (politique `Admin`) la gère ; une section d'Administration > Application l'affiche. Côté serveur mail, une règle Sieve globale et un script shell, documentés, non versionnés ailleurs.

**Tech stack :** .NET 10, ASP.NET Core, EF Core (InMemory en test), MimeKit/MailKit 4.17, xUnit 2.9.3, Moq 4.20.72 ; React 18, TypeScript, TanStack Query, react-i18next, Vitest + Testing Library.

## Global Constraints

- Backend : `cd src && dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés). Frontend : `cd src/frontend && npm run lint && npm run typecheck && npm test`.
- `src/snoopy.microservice/ApiDocumentation.xml` est régénéré par `dotnet test` : `git checkout -- src/snoopy.microservice/ApiDocumentation.xml` avant chaque commit.
- Messages de commit : deux lignes max, jamais de `@` en début ou fin ; heredoc `git commit -F -`, jamais de here-string PowerShell. Terminer par les lignes d'attribution en vigueur dans la session.
- Dépôt en `autocrlf=true` : Edit/Write écrivent du LF, c'est attendu. Un test ne fige jamais une fin de ligne : il compare des lignes logiques, ou passe par `Fixtures/MimeWire.TextOf`.
- Locales `fr` : espace insécable (U+00A0) avant `:` `;` `!` `?` et à l'intérieur des « » ; `npm test -- keys` et `parity` doivent rester verts ; aucune clé n'atteint `t()` par une variable.
- L'API omet les champs `null` (`WhenWritingNull`) : côté TypeScript un champ nullable est optionnel (`?`), jamais `| null`.
- Les enums sortent en JSON par `JsonStringEnumConverter` **sans** politique de casse : `Applied`, `AlreadyApplied`, … tels que nommés en C#. La spec écrit `applied` ; le nom C# fait foi, le script ignore la réponse.
- Le succès d'une action est la charge nue (`Ok(value)`, comme `ApplyReply` et `SchedulingAccount`) ; `ResultEnveloppe` n'enveloppe que les erreurs. La spec dit « enveloppe comme partout » : c'est ceci qu'elle veut dire.
- Un contrôleur MVC est public, son constructeur aussi : un type `internal` ne peut pas y être injecté. D'où l'interface publique `IDeliveryReplyApplier` (partie 4).
- Ne jamais journaliser un texte venu du mail sans `LogText.Safe` (partie 4).
- Aucune écriture en base, aucune ligne de journal par tentative que l'Internet peut multiplier (décision 8).

## Fichiers

**Backend, créés**
- `Data/Preferences/DeliveryReplyKey.cs` — l'entité à une ligne.
- `Repositories/IDeliveryKeyStore.cs`, `Repositories/DeliveryKeyStore.cs` — lecture, remplacement, activation, suppression, `RecordCallAsync`.
- `Services/Calendar/Delivery/DeliveryKeys.cs` — génération, hachage, comparaison en temps constant.
- `Services/Calendar/Delivery/IDeliveryKeyProvider.cs`, `DeliveryKeyProvider.cs` — le cache singleton.
- `Services/Calendar/Delivery/DeliveryMailReader.cs` — MimeKit borné → partie calendrier → texte.
- `Services/Calendar/Delivery/DeliveryMailbox.cs` — validation de l'en-tête boîte.
- `Services/Calendar/Delivery/DeliveryRefusals.cs` — le journal des refus, une ligne par minute au plus.
- `Services/Calendar/Invitations/IDeliveryReplyApplier.cs`, `IcsReplyOutcome.cs` — le cœur et sa projection.
- `Services/LogText.cs` — assainissement à la frontière du journal.
- `Models/Calendar/DeliveryReplyResponse.cs`, `DeliveryReplyOutcome.cs`, `DeliveryReplyKeyResponse.cs`, `DeliveryReplyKeyGenerated.cs`, `DeliveryReplyKeyEnableRequest.cs`.
- `Controllers/DeliveryController.cs`, `Controllers/DeliveryReplyKeyController.cs`.
- Tests : `Data/DeliveryReplyKeyTests.cs`, `Repositories/DeliveryKeyStoreTests.cs`, `Services/Calendar/Delivery/*Tests.cs`, `Controllers/DeliveryControllerTests.cs`, `DeliveryReplyKeyControllerTests.cs`, `DeliveryRouteSurfaceTests.cs`, `Services/Calendar/Invitations/InvitationReplyApplierIcsTests.cs`, fixtures `Fixtures/Mails/*.eml`.

**Backend, modifiés**
- `Data/Preferences/RevisionCause.cs` (+ `Delivery`), `PreferencesDbContext.cs` (DbSet + mapping).
- `Services/Calendar/Invitations/InvitationReplyApplier.cs` (le cœur), `Services/MailMessageMapper.cs` (la règle générique).
- `Configuration/SecurityConfiguration.cs` (`AddRateLimiters`), `ApplicationServicesConfiguration.cs` (DI), `snoopy.microservice.host/Program.cs` (l'appel renommé).
- `CLAUDE.md` (microservice), `DESIGN.md`.

**Frontend**
- `src/api.js` (+4 méthodes), `src/lib/apiErrorMessage.ts` (+2 codes), `locales/{en,fr}/errors.json`, `locales/{en,fr}/admin.json` (bloc `deliveryReplies`).
- `modules/settings/admin/useDeliveryReplyKey.ts`, `DeliveryRepliesSection.tsx`, `DeliveryKeyDialog.tsx`, `deliveryReplyKeyErrors.ts`, tests ; `ApplicationTab.tsx` (rendu de la section) ; `index.css` (règles `.dlv-*`).

**Docs**
- `docs/superpowers/webmail-delivery-reply-key-table.md` (créé), `webmail-calendar-tables.md` (§ 5e3), `webmail-delivery-replies-prerequisite.md` (créé), `reverse-proxy-prerequisite.md`, `calendar-5e3-residuals.md` (créé).

Six paquets, un sous-agent chacun sauf D : **A** (1-3, la clé) → **B** (4-5, le cœur) → **C** (6-7, la porte) ; **D** (8, la maquette, session principale) → **E** (9-10, l'écran), en parallèle de B et C ; **F** (11-12, les docs) en fin. Chaque partie numérotée garde son commit ; la relecture se fait par paquet.

---

## Paquet A — La clé — enum, table, store, fournisseur, endpoint admin

Un sous-agent backend. Trois parties, un commit chacune ; la relecture porte sur le paquet entier.

### 1. la cause `delivery` et les scripts SQL

**Files :**
- Modify : `src/snoopy.microservice/Data/Preferences/RevisionCause.cs`
- Modify : `src/snoopy.microservice/snoopy.microservice.Tests/Data/CalendarEntitiesTests.cs:110-120`
- Modify : `docs/superpowers/webmail-calendar-tables.md` (la ligne `cause` du `CREATE`, l. 109, et un § « Tranche 5e3 » après le § 5e2)
- Create : `docs/superpowers/webmail-delivery-reply-key-table.md`

**Interfaces :**
- Produces : `RevisionCause.Delivery`, converti en `'delivery'` par le convertisseur existant de `PreferencesDbContext` (`v.ToString().ToLowerInvariant()`), rien d'autre à faire côté EF.

- [ ] **Step 1 : le test rouge**

Dans `CalendarEntitiesTests.cs`, sous le test qui vérifie `rejected`/`webmail` (l. 117-118), ajouter au même test :

```csharp
        Assert.Equal("delivery", converter.ConvertToProvider(RevisionCause.Delivery));
        Assert.Equal(RevisionCause.Delivery, converter.ConvertFromProvider("delivery"));
```

- [ ] **Step 2 : vérifier l'échec**

Run : `cd src && dotnet build snoopy.microservice/snoopy.microservice.Tests 2>&1 | grep -E "error CS" | head -3`
Expected : `CS0117: 'RevisionCause' does not contain a definition for 'Delivery'`.

- [ ] **Step 3 : l'enum**

Après `Scheduling` dans `RevisionCause.cs` :

```csharp
    Scheduling,

    /// <summary>A guest's REPLY applied by the mail server at delivery (spec 5e3, décision 11): the
    /// user was not there, and the history must not say they were.</summary>
    Delivery
```

(Ajouter la virgule après `Scheduling`.)

- [ ] **Step 4 : vérifier le vert**

Run : `cd src && dotnet test snoopy.microservice/snoopy.microservice.Tests --filter "FullyQualifiedName~CalendarEntitiesTests"`
Expected : tout vert.

- [ ] **Step 5 : le SQL des révisions**

Dans `docs/superpowers/webmail-calendar-tables.md`, ligne 109, remplacer la liste ENUM du `CREATE` par
`ENUM('put','webmail','import','delete','rejected','scheduling','delivery')`, puis ajouter après le § « Tranche 5e2 — deux colonnes » (avant « Trois écarts… ») :

````markdown
## Tranche 5e3 — une cause de révision

Une réponse d'invité appliquée par le serveur mail à la livraison (spec 5e3, décision 11) n'est
ni un `PUT` ni la main de l'utilisateur : l'historique doit le dire. Valeur ajoutée en fin de
liste, compatible avec le service en place, à passer **avant** le déploiement sur les deux bases.

```sql
ALTER TABLE `calendar_revisions`
  MODIFY `cause` ENUM('put','webmail','import','delete','rejected','scheduling','delivery') NOT NULL;
```

`contact_revisions.cause` partage l'enum C# mais pas la valeur : un contact n'est jamais archivé
pour cette cause, son ENUM MariaDB reste tel quel.
````

- [ ] **Step 6 : le document de la table de la clé**

Créer `docs/superpowers/webmail-delivery-reply-key-table.md` :

````markdown
# Prérequis serveur — table `delivery_reply_key`

**À appliquer sur les deux bases** (`snoopy_webmail` et `snoopy_webmail_dev`) avant de déployer
le backend qui expose `/api/DeliveryReplyKey` et `/api/Delivery/CalendarReplies`
(spec 5e3). Le projet n'utilise pas les migrations EF : la création est manuelle, comme pour
`scheduling_service_account`.

## Pourquoi cette table

La clé que le serveur mail présente pour faire appliquer une réponse d'invité à la livraison, et
le réglage qui active ce mécanisme. Une seule ligne (`id = 1`). Pas dans `app_settings`, qui se
lit sans connexion : le réglage lui-même dirait à tout Internet que la porte est ouverte.

- **`key_hash`** : SHA-256 de la clé, jamais la clé. Une fuite de la base n'ouvre pas la porte.
- **`last_call_at`** : dernier appel dont la clé était valide ; remis à `NULL` à chaque génération.
- **`enabled`** : le réglage. Désactivé, la porte répond 404 à tout le monde.
- **`updated_at`** en `DATETIME(6)` : le jeton de concurrence des écritures de l'écran ;
  `last_call_at` s'écrit sans le faire bouger.

## Script

```sql
CREATE TABLE IF NOT EXISTS `snoopy_webmail`.`delivery_reply_key` (
  `id`           TINYINT UNSIGNED NOT NULL DEFAULT 1 COMMENT 'Toujours 1 : une seule ligne',
  `key_hash`     VARBINARY(32)    NOT NULL COMMENT 'SHA-256 de la clé — jamais la clé',
  `created_at`   DATETIME(6)      NOT NULL COMMENT 'UTC',
  `last_call_at` DATETIME(6)      NULL     COMMENT 'UTC ; dernier appel accepté',
  `enabled`      TINYINT(1)       NOT NULL DEFAULT 0,
  `updated_at`   DATETIME(6)      NOT NULL COMMENT 'UTC ; posée par le code',
  PRIMARY KEY (`id`),
  CONSTRAINT `ck_delivery_reply_key_single` CHECK (`id` = 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS `snoopy_webmail_dev`.`delivery_reply_key` (
  `id`           TINYINT UNSIGNED NOT NULL DEFAULT 1 COMMENT 'Toujours 1 : une seule ligne',
  `key_hash`     VARBINARY(32)    NOT NULL COMMENT 'SHA-256 de la clé — jamais la clé',
  `created_at`   DATETIME(6)      NOT NULL COMMENT 'UTC',
  `last_call_at` DATETIME(6)      NULL     COMMENT 'UTC ; dernier appel accepté',
  `enabled`      TINYINT(1)       NOT NULL DEFAULT 0,
  `updated_at`   DATETIME(6)      NOT NULL COMMENT 'UTC ; posée par le code',
  PRIMARY KEY (`id`),
  CONSTRAINT `ck_delivery_reply_key_single` CHECK (`id` = 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

Passer aussi l'`ALTER` de `calendar_revisions` du § « Tranche 5e3 » de `webmail-calendar-tables.md`.

## Vérification

1. `SHOW CREATE TABLE delivery_reply_key;` montre les six colonnes et la contrainte.
2. Administration > Application montre la section « Réponses aux invitations à la livraison »,
   sans clé, l'interrupteur grisé.
3. « Générer une clé », puis `SELECT id, created_at, last_call_at, enabled FROM delivery_reply_key;`
   rend une ligne, `enabled = 0`.

## Retour arrière

La table peut rester : la version précédente ne la lit pas. La règle Sieve peut rester aussi,
ses appels répondent 404. Un `DROP TABLE` oblige à régénérer la clé et à la recopier sur le
serveur mail.
````

- [ ] **Step 7 : commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice/Data/Preferences/RevisionCause.cs src/snoopy.microservice/snoopy.microservice.Tests/Data/CalendarEntitiesTests.cs docs/superpowers/webmail-calendar-tables.md docs/superpowers/webmail-delivery-reply-key-table.md
git commit -F - <<'EOF'
Calendar 5e3: delivery revision cause, key table script
EOF
```

---

### 2. l'entité, le store et le fournisseur de la clé

**Files :**
- Create : `src/snoopy.microservice/Data/Preferences/DeliveryReplyKey.cs`
- Modify : `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs:25-27` (mapping) et `:195` (DbSet)
- Create : `src/snoopy.microservice/Repositories/IDeliveryKeyStore.cs`, `DeliveryKeyStore.cs`
- Create : `src/snoopy.microservice/Services/Calendar/Delivery/DeliveryKeys.cs`, `IDeliveryKeyProvider.cs`, `DeliveryKeyProvider.cs`
- Modify : `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs:111,156`
- Test : `snoopy.microservice.Tests/Repositories/DeliveryKeyStoreTests.cs`, `Services/Calendar/Delivery/DeliveryKeysTests.cs`, `DeliveryKeyProviderTests.cs`

**Interfaces :**
- Produces :
  - `public sealed class DeliveryReplyKey { byte Id; byte[] KeyHash; DateTime CreatedAt; DateTime? LastCallAt; bool Enabled; DateTime UpdatedAt }`, `DeliveryReplyKey.SingletonId = 1`.
  - `public enum DeliveryKeyWrite { Written, NoKey, Conflict }`.
  - `public interface IDeliveryKeyStore { Task<DeliveryReplyKey?> FindAsync(ct); Task<bool> ReplaceAsync(byte[] keyHash, DateTime now, ct); Task<DeliveryKeyWrite> SetEnabledAsync(bool enabled, DateTime now, ct); Task<DeliveryKeyWrite> DeleteAsync(ct); Task RecordCallAsync(DateTime calledAt, ct); }`.
  - `internal static class DeliveryKeys { const int KeyBytes = 32; string Generate(); byte[] Hash(string key); bool Matches(string? presented, byte[] storedHash); }`.
  - `public sealed record DeliveryKeySnapshot(byte[] KeyHash, bool Enabled)`.
  - `public interface IDeliveryKeyProvider { Task<DeliveryKeySnapshot?> GetAsync(ct); Task InvalidateAsync(ct); }`.

- [ ] **Step 1 : les tests rouges du store**

`snoopy.microservice.Tests/Repositories/DeliveryKeyStoreTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class DeliveryKeyStoreTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateTime T0 = new(2026, 9, 14, 18, 0, 0, DateTimeKind.Utc);
    private readonly string database = Guid.NewGuid().ToString("N");
    private static readonly byte[] HashA = Enumerable.Repeat((byte)1, 32).ToArray();
    private static readonly byte[] HashB = Enumerable.Repeat((byte)2, 32).ToArray();

    private DeliveryKeyStore Store() => new(new PreferencesTestDbContext(database));

    [Fact]
    public async Task Replace_CreatesTheRow_ThenRewritesIt_ForgettingTheLastCall_KeepingEnabled()
    {
        Assert.Null(await Store().FindAsync(None));
        Assert.True(await Store().ReplaceAsync(HashA, T0, None));
        Assert.Equal(DeliveryKeyWrite.Written, await Store().SetEnabledAsync(true, T0.AddMinutes(1), None));
        await Store().RecordCallAsync(T0.AddMinutes(2), None);

        Assert.True(await Store().ReplaceAsync(HashB, T0.AddMinutes(3), None));

        var row = (await Store().FindAsync(None))!;
        Assert.Equal((HashB, T0.AddMinutes(3), (DateTime?)null, true, T0.AddMinutes(3)),
            (row.KeyHash, row.CreatedAt, row.LastCallAt, row.Enabled, row.UpdatedAt));
    }

    [Fact]
    public async Task SetEnabled_And_Delete_SayNoKey_WhenNoRow()
    {
        Assert.Equal(DeliveryKeyWrite.NoKey, await Store().SetEnabledAsync(true, T0, None));
        Assert.Equal(DeliveryKeyWrite.NoKey, await Store().DeleteAsync(None));
    }

    [Fact]
    public async Task RecordCall_MovesLastCallAt_WithoutTouchingUpdatedAt()
    {
        await Store().ReplaceAsync(HashA, T0, None);

        await Store().RecordCallAsync(T0.AddMinutes(5), None);

        var row = (await Store().FindAsync(None))!;
        Assert.Equal((T0.AddMinutes(5), T0), (row.LastCallAt, row.UpdatedAt));
    }

    [Fact]
    public async Task RecordCall_WithoutARow_DoesNothing()
    {
        await Store().RecordCallAsync(T0, None);
        Assert.Null(await Store().FindAsync(None));
    }

    [Fact]
    public async Task Delete_RemovesTheRow()
    {
        await Store().ReplaceAsync(HashA, T0, None);
        Assert.Equal(DeliveryKeyWrite.Written, await Store().DeleteAsync(None));
        Assert.Null(await Store().FindAsync(None));
    }
}
```

- [ ] **Step 2 : vérifier l'échec**

Run : `cd src && dotnet build snoopy.microservice/snoopy.microservice.Tests 2>&1 | grep -c "error CS"`
Expected : des erreurs `CS0246` (types inconnus).

- [ ] **Step 3 : l'entité et le mapping**

`Data/Preferences/DeliveryReplyKey.cs` :

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// The key the mail server presents to have a guest's reply applied at delivery, and the switch
/// that opens that door (spec 5e3). One row for the instance, never in app_settings: that table
/// is read without a session, and the switch alone would tell the Internet the door is open.
/// </summary>
[Table("delivery_reply_key")]
public sealed class DeliveryReplyKey
{
    public const byte SingletonId = 1;

    [Column("id")]
    public byte Id { get; set; } = SingletonId;

    /// <summary>SHA-256 of the key. Never the key.</summary>
    [Column("key_hash")]
    public byte[] KeyHash { get; set; } = [];

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC; the last call whose key matched. Null again after every generation.</summary>
    [Column("last_call_at")]
    public DateTime? LastCallAt { get; set; }

    [Column("enabled")]
    public bool Enabled { get; set; }

    /// <summary>UTC. The concurrency token of the admin screen's writes; a recorded call leaves it alone.</summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
```

Dans `PreferencesDbContext.OnModelCreating`, après les trois lignes `SchedulingServiceAccount` (l. 25-27) :

```csharp
        modelBuilder.Entity<DeliveryReplyKey>().HasKey(k => k.Id);
        modelBuilder.Entity<DeliveryReplyKey>().Property(k => k.Id).ValueGeneratedNever();
        modelBuilder.Entity<DeliveryReplyKey>().Property(k => k.UpdatedAt).IsConcurrencyToken();
```

Et à côté du `DbSet<SchedulingServiceAccount>` (l. 195) :

```csharp
    public DbSet<DeliveryReplyKey> DeliveryReplyKeys { get; set; }
```

- [ ] **Step 4 : le store**

`Repositories/IDeliveryKeyStore.cs` :

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

public enum DeliveryKeyWrite { Written, NoKey, Conflict }

/// <summary>The single row of the delivery key. It validates nothing: the caller does.</summary>
public interface IDeliveryKeyStore
{
    Task<DeliveryReplyKey?> FindAsync(CancellationToken cancellationToken);

    /// <summary>Creates or rewrites the row with a new hash, forgetting the last call and keeping the
    /// switch. Last writer wins: a lost race is retried once; false, writing nothing, when it loses twice.</summary>
    Task<bool> ReplaceAsync(byte[] keyHash, DateTime now, CancellationToken cancellationToken);

    Task<DeliveryKeyWrite> SetEnabledAsync(bool enabled, DateTime now, CancellationToken cancellationToken);

    Task<DeliveryKeyWrite> DeleteAsync(CancellationToken cancellationToken);

    /// <summary>Moves last_call_at alone. Never loses against the screen and never makes it lose: the
    /// token is left untouched and a lost race is simply retried. Nothing without a row.</summary>
    Task RecordCallAsync(DateTime calledAt, CancellationToken cancellationToken);
}
```

`Repositories/DeliveryKeyStore.cs` :

```csharp
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class DeliveryKeyStore(PreferencesDbContext context)
    : ScopedStore<DeliveryReplyKey>(context), IDeliveryKeyStore
{
    public Task<DeliveryReplyKey?> FindAsync(CancellationToken cancellationToken)
        => Untracked.FirstOrDefaultAsync(k => k.Id == DeliveryReplyKey.SingletonId, cancellationToken);

    public async Task<bool> ReplaceAsync(byte[] keyHash, DateTime now, CancellationToken cancellationToken)
        => await LastWriterWinsAsync(async () =>
        {
            var row = await RowAsync(cancellationToken);
            if (row is null) Set.Add(row = new DeliveryReplyKey());
            row.KeyHash = keyHash;
            row.CreatedAt = now;
            row.LastCallAt = null;
            row.UpdatedAt = now;
            await Context.SaveChangesAsync(cancellationToken);
            return DeliveryKeyWrite.Written;
        }) == DeliveryKeyWrite.Written;

    public Task<DeliveryKeyWrite> SetEnabledAsync(bool enabled, DateTime now, CancellationToken cancellationToken)
        => LastWriterWinsAsync(async () =>
        {
            var row = await RowAsync(cancellationToken);
            if (row is null) return DeliveryKeyWrite.NoKey;
            row.Enabled = enabled;
            row.UpdatedAt = now;
            await Context.SaveChangesAsync(cancellationToken);
            return DeliveryKeyWrite.Written;
        });

    public Task<DeliveryKeyWrite> DeleteAsync(CancellationToken cancellationToken)
        => LastWriterWinsAsync(async () =>
        {
            var row = await RowAsync(cancellationToken);
            if (row is null) return DeliveryKeyWrite.NoKey;
            Set.Remove(row);
            await Context.SaveChangesAsync(cancellationToken);
            return DeliveryKeyWrite.Written;
        });

    public async Task RecordCallAsync(DateTime calledAt, CancellationToken cancellationToken)
    {
        // Three tries, no token bump: the screen's write may land meanwhile, and neither side loses.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var row = await RowAsync(cancellationToken);
            if (row is null) return;
            row.LastCallAt = calledAt;
            try
            {
                await Context.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                Context.ChangeTracker.Clear();
            }
        }
    }

    private Task<DeliveryReplyKey?> RowAsync(CancellationToken cancellationToken)
        => Set.FirstOrDefaultAsync(k => k.Id == DeliveryReplyKey.SingletonId, cancellationToken);

    private async Task<DeliveryKeyWrite> LastWriterWinsAsync(Func<Task<DeliveryKeyWrite>> write)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await write();
            }
            catch (DbUpdateException ex) when (LostRace(ex))
            {
                Context.ChangeTracker.Clear();
                if (attempt == 2) return DeliveryKeyWrite.Conflict;
            }
        }
    }

    private static bool LostRace(DbUpdateException ex) =>
        ex is DbUpdateConcurrencyException || ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry };
}
```

- [ ] **Step 5 : vérifier le vert du store**

Run : `cd src && dotnet test snoopy.microservice/snoopy.microservice.Tests --filter "FullyQualifiedName~DeliveryKeyStoreTests"`
Expected : 5 verts.

- [ ] **Step 6 : les tests rouges de `DeliveryKeys`**

`Services/Calendar/Delivery/DeliveryKeysTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Services.Calendar.Delivery;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Delivery;

public sealed class DeliveryKeysTests
{
    [Fact]
    public void Generate_Is43Base64UrlCharacters_AndNeverRepeats()
    {
        var a = DeliveryKeys.Generate();
        var b = DeliveryKeys.Generate();
        Assert.Equal(43, a.Length);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", a);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Matches_OnlyTheKeyThatWasHashed()
    {
        var key = DeliveryKeys.Generate();
        var hash = DeliveryKeys.Hash(key);
        Assert.Equal(32, hash.Length);
        Assert.True(DeliveryKeys.Matches(key, hash));
        Assert.False(DeliveryKeys.Matches(key + "x", hash));
        Assert.False(DeliveryKeys.Matches(null, hash));
        Assert.False(DeliveryKeys.Matches("", hash));
    }
}
```

- [ ] **Step 7 : `DeliveryKeys`**

`Services/Calendar/Delivery/DeliveryKeys.cs` :

```csharp
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace weesky.Snoopy.Microservice.Services.Calendar.Delivery;

/// <summary>256 random bits, shown once, kept as an unsalted SHA-256: a dictionary means nothing
/// against a random key, and the compare is constant-time (spec 5e3, décision 6).</summary>
internal static class DeliveryKeys
{
    internal const int KeyBytes = 32;

    internal static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(KeyBytes));

    internal static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    internal static bool Matches(string? presented, byte[] storedHash) =>
        !string.IsNullOrEmpty(presented) && CryptographicOperations.FixedTimeEquals(Hash(presented), storedHash);
}
```

- [ ] **Step 8 : le fournisseur, test puis code**

`Services/Calendar/Delivery/DeliveryKeyProviderTests.cs` :

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar.Delivery;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Delivery;

public sealed class DeliveryKeyProviderTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private readonly string database = Guid.NewGuid().ToString("N");

    private DeliveryKeyProvider Provider()
    {
        var services = new ServiceCollection();
        services.AddScoped<IDeliveryKeyStore>(_ => new DeliveryKeyStore(new PreferencesTestDbContext(database)));
        return new DeliveryKeyProvider(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DeliveryKeyProvider>.Instance);
    }

    [Fact]
    public async Task Get_IsNullWithoutARow_AndCachesTheRow_UntilInvalidated()
    {
        var provider = Provider();
        Assert.Null(await provider.GetAsync(None));

        var hash = DeliveryKeys.Hash("k");
        await new DeliveryKeyStore(new PreferencesTestDbContext(database)).ReplaceAsync(hash, DateTime.UtcNow, None);
        Assert.Null(await provider.GetAsync(None));

        await provider.InvalidateAsync(None);
        var snapshot = await provider.GetAsync(None);
        Assert.Equal((hash, false), (snapshot!.KeyHash, snapshot.Enabled));
    }
}
```

`IDeliveryKeyProvider.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Services.Calendar.Delivery;

public sealed record DeliveryKeySnapshot(byte[] KeyHash, bool Enabled);

/// <summary>The delivery key as Administration stored it, read once and kept in memory until the
/// admin screen writes it again. The cache lives in this process (same limit as the service account).</summary>
public interface IDeliveryKeyProvider
{
    /// <summary>Null when no key is stored; throws when the database cannot be read.</summary>
    Task<DeliveryKeySnapshot?> GetAsync(CancellationToken cancellationToken);

    /// <summary>Forgets the cached key and loads the stored one again. Never throws.</summary>
    Task InvalidateAsync(CancellationToken cancellationToken);
}
```

`DeliveryKeyProvider.cs` — la structure de `ServiceAccountProvider`, un état « inconnu » distinct de « pas de ligne » :

```csharp
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services.Calendar.Delivery;

internal sealed class DeliveryKeyProvider(IServiceScopeFactory scopes, ILogger<DeliveryKeyProvider> logger)
    : IDeliveryKeyProvider
{
    private sealed record Loaded(DeliveryKeySnapshot? Key);

    private readonly SemaphoreSlim loading = new(1, 1);
    private readonly Lock swap = new();
    private Loaded? loaded;
    private long generation;

    public async Task<DeliveryKeySnapshot?> GetAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref loaded) is { } cached) return cached.Key;

        await loading.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref loaded) is { } loadedMeanwhile) return loadedMeanwhile.Key;

            long loadedFor;
            lock (swap) loadedFor = generation;
            var key = await LoadAsync(cancellationToken);
            lock (swap)
                if (loadedFor == generation) loaded = new Loaded(key);
            return key;
        }
        finally
        {
            loading.Release();
        }
    }

    public async Task InvalidateAsync(CancellationToken cancellationToken)
    {
        lock (swap)
        {
            generation++;
            loaded = null;
        }
        try
        {
            await GetAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The delivery key could not be reloaded; the next delivery call retries");
        }
    }

    private async Task<DeliveryKeySnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<IDeliveryKeyStore>().FindAsync(cancellationToken);
        return row is null ? null : new DeliveryKeySnapshot(row.KeyHash, row.Enabled);
    }
}
```

- [ ] **Step 9 : DI**

Dans `ApplicationServicesConfiguration.cs`, après la ligne 111 (`AddSingleton<IServiceAccountProvider, …>`) :

```csharp
        services.AddSingleton<IDeliveryKeyProvider, DeliveryKeyProvider>();
```

et après la ligne 156 (`AddScoped<ISchedulingAccountStore, …>`) :

```csharp
        services.AddScoped<IDeliveryKeyStore, DeliveryKeyStore>();
```

(ajouter `using weesky.Snoopy.Microservice.Services.Calendar.Delivery;`).

- [ ] **Step 10 : vérifier le vert, commit**

Run : `cd src && dotnet test snoopy.microservice/snoopy.microservice.Tests --filter "FullyQualifiedName~Delivery"`
Expected : 8 verts.

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add -A src/snoopy.microservice/Data src/snoopy.microservice/Repositories src/snoopy.microservice/Services/Calendar/Delivery src/snoopy.microservice/Configuration src/snoopy.microservice/snoopy.microservice.Tests
git commit -F - <<'EOF'
Calendar 5e3: delivery key entity, store and cached provider
EOF
```

---

### 3. `DeliveryReplyKeyController`

**Files :**
- Create : `src/snoopy.microservice/Models/Calendar/DeliveryReplyKeyResponse.cs`, `DeliveryReplyKeyGenerated.cs`, `DeliveryReplyKeyEnableRequest.cs`
- Create : `src/snoopy.microservice/Controllers/DeliveryReplyKeyController.cs`
- Test : `snoopy.microservice.Tests/Controllers/DeliveryReplyKeyControllerTests.cs`

**Interfaces :**
- Consumes : `IDeliveryKeyStore`, `IDeliveryKeyProvider`, `DeliveryKeys`, `TimeProvider` (partie 2).
- Produces : `GET/POST/PUT/DELETE api/DeliveryReplyKey` ; codes `delivery_key_missing`, `delivery_key_changed_concurrently` (repris par le frontend, partie 9).

- [ ] **Step 1 : les modèles**

```csharp
// Models/Calendar/DeliveryReplyKeyResponse.cs
namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>The stored key without the key: whether one exists, the switch, and the two dates the card shows.</summary>
public sealed record DeliveryReplyKeyResponse(bool Configured, bool Enabled, DateTime? CreatedAt, DateTime? LastCallAt)
{
    public static readonly DeliveryReplyKeyResponse None = new(false, false, null, null);
}

// Models/Calendar/DeliveryReplyKeyGenerated.cs
namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>The key in clear, once. Never stored, never returned again.</summary>
public sealed record DeliveryReplyKeyGenerated(string Key);

// Models/Calendar/DeliveryReplyKeyEnableRequest.cs
namespace weesky.Snoopy.Microservice.Models.Calendar;

public sealed record DeliveryReplyKeyEnableRequest(bool Enabled);
```

- [ ] **Step 2 : les tests rouges**

`Controllers/DeliveryReplyKeyControllerTests.cs` :

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar.Delivery;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class DeliveryReplyKeyControllerTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private readonly string _database = Guid.NewGuid().ToString("N");
    private readonly Mock<IDeliveryKeyProvider> _provider = new();
    private readonly MutableTimeProvider _clock = new();

    private DeliveryKeyStore Store() => new(new PreferencesTestDbContext(_database));

    private DeliveryReplyKeyController Controller()
    {
        var controller = new DeliveryReplyKeyController(Store(), _provider.Object, _clock)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return controller;
    }

    [Fact]
    public async Task Get_SaysNotConfigured_ThenTheDates()
    {
        var none = (await Controller().GetDeliveryReplyKey(None)).Value!;
        Assert.Equal(DeliveryReplyKeyResponse.None, none);

        var generated = await Controller().GenerateDeliveryReplyKey(None);
        var got = (await Controller().GetDeliveryReplyKey(None)).Value!;
        Assert.Equal((true, false, (DateTime?)null), (got.Configured, got.Enabled, got.LastCallAt));
        Assert.Equal(DateTimeKind.Utc, got.CreatedAt!.Value.Kind);
        Assert.IsType<OkObjectResult>(generated.Result);
    }

    [Fact]
    public async Task Generate_ReturnsTheKeyOnce_NoStore_AndInvalidates_AndRevokesThePrevious()
    {
        var controller = Controller();
        var first = (DeliveryReplyKeyGenerated)((OkObjectResult)(await controller.GenerateDeliveryReplyKey(None)).Result!).Value!;
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        Assert.Equal(43, first.Key.Length);
        Assert.True(DeliveryKeys.Matches(first.Key, (await Store().FindAsync(None))!.KeyHash));

        var second = (DeliveryReplyKeyGenerated)((OkObjectResult)(await Controller().GenerateDeliveryReplyKey(None)).Result!).Value!;
        var row = (await Store().FindAsync(None))!;
        Assert.False(DeliveryKeys.Matches(first.Key, row.KeyHash));
        Assert.True(DeliveryKeys.Matches(second.Key, row.KeyHash));
        _provider.Verify(p => p.InvalidateAsync(CancellationToken.None), Times.Exactly(2));
    }

    [Fact]
    public async Task Enable_WithoutAKey_Is409_DisableIs204_EnableWithAKeyIs204()
    {
        var refused = await Controller().SetDeliveryReplies(new DeliveryReplyKeyEnableRequest(true), None);
        Assert.Equal(DeliveryReplyKeyController.KeyMissing,
            ((ResultEnveloppe)((ConflictObjectResult)refused).Value!).Message);
        Assert.IsType<NoContentResult>(await Controller().SetDeliveryReplies(new DeliveryReplyKeyEnableRequest(false), None));

        await Controller().GenerateDeliveryReplyKey(None);
        Assert.IsType<NoContentResult>(await Controller().SetDeliveryReplies(new DeliveryReplyKeyEnableRequest(true), None));
        Assert.True((await Store().FindAsync(None))!.Enabled);
        _provider.Verify(p => p.InvalidateAsync(CancellationToken.None), Times.Exactly(2));
    }

    [Fact]
    public async Task Delete_Is404WithoutAKey_And204WithOne_AndDisables()
    {
        Assert.IsType<NotFoundObjectResult>(await Controller().DeleteDeliveryReplyKey(None));

        await Controller().GenerateDeliveryReplyKey(None);
        await Controller().SetDeliveryReplies(new DeliveryReplyKeyEnableRequest(true), None);
        Assert.IsType<NoContentResult>(await Controller().DeleteDeliveryReplyKey(None));
        Assert.Null(await Store().FindAsync(None));
        Assert.Equal(DeliveryReplyKeyResponse.None, (await Controller().GetDeliveryReplyKey(None)).Value);
    }
}
```

- [ ] **Step 3 : le contrôleur**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar.Delivery;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>The key the mail server presents at <c>POST /api/Delivery/CalendarReplies</c>, and the
/// switch that opens that door (spec 5e3). Admin-only; the key is shown once, at generation.</summary>
[Route("api/[controller]")]
[ApiController]
[Authorize(Policy = AdminRequirement.PolicyName)]
public sealed class DeliveryReplyKeyController(IDeliveryKeyStore store, IDeliveryKeyProvider keys, TimeProvider clock)
    : ApiBaseController
{
    internal const string KeyMissing = "delivery_key_missing";
    internal const string ChangedConcurrently = "delivery_key_changed_concurrently";

    /// <summary>Whether a key exists, the switch, and the dates the card shows. Never the key.</summary>
    /// <response code="200">The state</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DeliveryReplyKeyResponse>> GetDeliveryReplyKey(CancellationToken cancellationToken)
    {
        var row = await store.FindAsync(cancellationToken);
        return Ok(row is null ? DeliveryReplyKeyResponse.None
            : new DeliveryReplyKeyResponse(true, row.Enabled, Utc(row.CreatedAt), row.LastCallAt is { } at ? Utc(at) : null));
    }

    /// <summary>Generates the key, or replaces it — the previous one is refused from this answer on.
    /// The key is returned once and never stored; the last-call date starts over.</summary>
    /// <response code="200"><c>{ key }</c>, once</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    /// <response code="409">Concurrent writes won twice (<c>delivery_key_changed_concurrently</c>)</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeliveryReplyKeyGenerated>> GenerateDeliveryReplyKey(CancellationToken cancellationToken)
    {
        var key = DeliveryKeys.Generate();
        if (!await store.ReplaceAsync(DeliveryKeys.Hash(key), clock.GetUtcNow().UtcDateTime, cancellationToken))
            return ConflictEnveloppe(ChangedConcurrently);
        await keys.InvalidateAsync(CancellationToken.None);
        Response.Headers.CacheControl = "no-store";
        return Ok(new DeliveryReplyKeyGenerated(key));
    }

    /// <summary>Opens or closes the door. Opening needs a key.</summary>
    /// <response code="204">Switched</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    /// <response code="409">Enabling with no key stored (<c>delivery_key_missing</c>), or concurrent writes won twice</response>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> SetDeliveryReplies(DeliveryReplyKeyEnableRequest request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequestEnveloppe("Request body is required");
        var write = await store.SetEnabledAsync(request.Enabled, clock.GetUtcNow().UtcDateTime, cancellationToken);
        if (write is DeliveryKeyWrite.NoKey) return request.Enabled ? ConflictEnveloppe(KeyMissing) : NoContent();
        if (write is DeliveryKeyWrite.Conflict) return ConflictEnveloppe(ChangedConcurrently);
        await keys.InvalidateAsync(CancellationToken.None);
        return NoContent();
    }

    /// <summary>Removes the key and closes the door.</summary>
    /// <response code="204">Removed</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not an administrator</response>
    /// <response code="404">No key stored (<c>delivery_key_missing</c>)</response>
    /// <response code="409">Concurrent writes won twice</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> DeleteDeliveryReplyKey(CancellationToken cancellationToken)
    {
        var write = await store.DeleteAsync(cancellationToken);
        if (write is DeliveryKeyWrite.NoKey) return NotFoundEnveloppe(KeyMissing);
        if (write is DeliveryKeyWrite.Conflict) return ConflictEnveloppe(ChangedConcurrently);
        await keys.InvalidateAsync(CancellationToken.None);
        return NoContent();
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
```

Le `[HttpPut]` ajoute 400 en pratique (corps absent) : ajouter `[ProducesResponseType(StatusCodes.Status400BadRequest)]` sur `SetDeliveryReplies` et le compter dans la partie 7.

- [ ] **Step 4 : vérifier le vert, commit**

Run : `cd src && dotnet test snoopy.microservice/snoopy.microservice.Tests --filter "FullyQualifiedName~DeliveryReplyKeyControllerTests"`
Expected : 4 verts.

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add -A src/snoopy.microservice/Models/Calendar src/snoopy.microservice/Controllers/DeliveryReplyKeyController.cs src/snoopy.microservice/snoopy.microservice.Tests/Controllers/DeliveryReplyKeyControllerTests.cs
git commit -F - <<'EOF'
Calendar 5e3: admin endpoint for the delivery key
EOF
```

---

## Paquet B — Le cœur — l'applier coupé en deux, une seule règle de sélection, le lecteur MimeKit

Un sous-agent backend. Les deux parties sont les deux moitiés de la décision 2 : un seul chemin d'application, une seule règle de sélection.

### 4. le cœur `ApplyIcsAsync` et l'assainissement du journal

**Files :**
- Create : `src/snoopy.microservice/Services/LogText.cs`
- Create : `src/snoopy.microservice/Services/Calendar/Invitations/IcsReplyOutcome.cs`, `IDeliveryReplyApplier.cs`
- Create : `src/snoopy.microservice/Models/Calendar/DeliveryReplyOutcome.cs`, `DeliveryReplyResponse.cs`
- Modify : `src/snoopy.microservice/Services/Calendar/Invitations/InvitationReplyApplier.cs`
- Modify : `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs:187`
- Test : `snoopy.microservice.Tests/Services/LogTextTests.cs`, `Services/Calendar/Invitations/InvitationReplyApplierIcsTests.cs`

**Interfaces :**
- Consumes : `RevisionCause.Delivery` (partie 1).
- Produces :
  - `internal static class LogText { static string Safe(string? value, int max = 200) }`.
  - `internal sealed record IcsReplyOutcome(ParsedInvitation? Parsed, InvitationContext? Context, bool Applied, bool AlreadyApplied, DavWriteStatus? WriteStatus, string? Refusal)`.
  - `public enum DeliveryReplyOutcome { Applied, AlreadyApplied, NotApplicable, NotAReply, UnknownMailbox, Conflict }`.
  - `public sealed record DeliveryReplyResponse(DeliveryReplyOutcome Outcome, string? Uid, string? Detail)`.
  - `public interface IDeliveryReplyApplier { Task<DeliveryReplyResponse> ApplyAsync(User user, string ics, CancellationToken ct); }` — implémenté par `InvitationReplyApplier`, cause `Delivery`.
  - `InvitationReplyApplier.ApplyIcsAsync(User, string ics, RevisionCause, CancellationToken)` (internal).

- [ ] **Step 1 : `LogText`, test puis code**

```csharp
// Tests/Services/LogTextTests.cs
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class LogTextTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("abc-123", "abc-123")]
    [InlineData("line\r\nINJECTED: x\tend", "line??INJECTED: x?end")]
    [InlineData("bell[0m", "?bell?[0m")]
    public void Safe_ReplacesEveryControlCharacter(string? value, string expected) =>
        Assert.Equal(expected, LogText.Safe(value));

    [Fact]
    public void Safe_TruncatesWithAnEllipsis()
    {
        var text = LogText.Safe(new string('a', 300));
        Assert.Equal(201, text.Length);
        Assert.EndsWith("…", text);
    }
}
```

```csharp
// Services/LogText.cs
namespace weesky.Snoopy.Microservice.Services;

/// <summary>Text from a mail, made safe for the plain-text log: the file sink escapes nothing, so a
/// CR/LF in a UID would forge a log line. One replacement per control character, a bounded length.</summary>
internal static class LogText
{
    internal static string Safe(string? value, int max = 200)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var cut = value.Length > max ? value[..max] + "…" : value;
        return string.Create(cut.Length, cut, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = char.IsControl(source[i]) ? '?' : source[i];
        });
    }
}
```

Run : `cd src && dotnet test snoopy.microservice/snoopy.microservice.Tests --filter "FullyQualifiedName~LogTextTests"` → 5 verts.

- [ ] **Step 2 : les types du cœur et de la projection**

```csharp
// Services/Calendar/Invitations/IcsReplyOutcome.cs
using weesky.Snoopy.Microservice.Models.Dav;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

/// <summary>What applying a REPLY's text did. Exactly one of: a refusal (<see cref="Refusal"/>, the
/// file is not a reply or nothing can be applied), <see cref="AlreadyApplied"/>, a write the
/// calendar did not take (<see cref="WriteStatus"/> other than Created/Replaced), or <see cref="Applied"/>.
/// <see cref="Context"/> is the state after the write when applied, before it otherwise.</summary>
internal sealed record IcsReplyOutcome(
    ParsedInvitation? Parsed, InvitationContext? Context, bool Applied, bool AlreadyApplied,
    DavWriteStatus? WriteStatus, string? Refusal);

// Models/Calendar/DeliveryReplyOutcome.cs
namespace weesky.Snoopy.Microservice.Models.Calendar;

public enum DeliveryReplyOutcome { Applied, AlreadyApplied, NotApplicable, NotAReply, UnknownMailbox, Conflict }

// Models/Calendar/DeliveryReplyResponse.cs
namespace weesky.Snoopy.Microservice.Models.Calendar;

/// <summary>What the delivery door did with the mail (spec 5e3). <see cref="Detail"/> is the
/// <c>ReplyStatus</c> or <c>too_large</c> behind NotApplicable, the <c>DavWriteStatus</c> behind Conflict.</summary>
public sealed record DeliveryReplyResponse(DeliveryReplyOutcome Outcome, string? Uid, string? Detail);

// Services/Calendar/Invitations/IDeliveryReplyApplier.cs
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;

namespace weesky.Snoopy.Microservice.Services.Calendar.Invitations;

/// <summary>The delivery door's entry into the reply applier: text in, outcome out, cause Delivery.
/// A REPLY's size and its mailbox are the caller's checks.</summary>
public interface IDeliveryReplyApplier
{
    Task<DeliveryReplyResponse> ApplyAsync(User user, string ics, CancellationToken cancellationToken);
}
```

- [ ] **Step 3 : les tests rouges du cœur**

`Services/Calendar/Invitations/InvitationReplyApplierIcsTests.cs` :

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

/// <summary>The text entry of the applier — what the delivery door calls — over mocks, and its
/// projection onto the door's outcomes.</summary>
public sealed class InvitationReplyApplierIcsTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private readonly User _user = new("alice@weesky.be") { WebmailUid = Guid.NewGuid() };
    private readonly Mock<ICalendarEventStore> _events = new();
    private readonly Mock<IDavCalendarWriter> _writer = new();

    private static string Fixture(string name) =>
        InvitationParserTests.Fixture(name).Replace("aaaa1111-bbbb-2222-cccc-3333dddd4444", "web-1111-2222");

    private InvitationReplyApplier Sut() => new(new InvitationPartLoader(Mock.Of<IMailMessageRepository>()),
        new InvitationReader(Mock.Of<IUserAddresses>(MockBehavior.Strict), _events.Object), _writer.Object,
        NullLogger<InvitationReplyApplier>.Instance);

    private StoredEventRef Invited(string owner = "webmail") =>
        new(Guid.NewGuid(), Guid.NewGuid(), "phone-name.ics", Fixture("webmail-invited"), owner, "abc123");

    [Fact]
    public async Task ARequest_IsNotAReply()
    {
        var outcome = await Sut().ApplyIcsAsync(_user, Fixture("google-request"), RevisionCause.Delivery, None);
        Assert.Equal((InvitationReplyApplier.NotAReply, (ParsedInvitation?)null), (outcome.Refusal, outcome.Parsed));
        var projected = await ((IDeliveryReplyApplier)Sut()).ApplyAsync(_user, Fixture("google-request"), None);
        Assert.Equal(new DeliveryReplyResponse(DeliveryReplyOutcome.NotAReply, null, null), projected);
    }

    [Fact]
    public async Task AnUnknownUid_IsNotApplicable_WithTheStatusAsDetail()
    {
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", None)).ReturnsAsync([]);
        var projected = await ((IDeliveryReplyApplier)Sut()).ApplyAsync(_user, Fixture("google-reply"), None);
        Assert.Equal(new DeliveryReplyResponse(DeliveryReplyOutcome.NotApplicable, "web-1111-2222", "UnknownUid"), projected);
    }

    [Fact]
    public async Task AnApplicableReply_IsWrittenUnderTheCauseGiven_AndAppliedIsDistinctFromAlreadyApplied()
    {
        var stored = Invited();
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", None)).ReturnsAsync([stored]);
        _writer.Setup(w => w.PutAsync(_user.WebmailUid, stored.CalendarId, "phone-name.ics", It.IsAny<string>(), None, false, "\"abc123\"", RevisionCause.Delivery))
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Replaced, null, null, 0));

        var outcome = await Sut().ApplyIcsAsync(_user, Fixture("google-reply"), RevisionCause.Delivery, None);

        Assert.Equal((true, false, (string?)null), (outcome.Applied, outcome.AlreadyApplied, outcome.Refusal));
        _writer.VerifyAll();

        // The stored file now holds the answer: the same reply is "already applied", nothing is written.
        var answered = stored with { IcsRaw = PartStatRewriter.Rewrite(stored.IcsRaw, "marc.dupont@example.org", "DECLINED", "20260913T100000Z")! };
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", None)).ReturnsAsync([answered]);
        var again = await ((IDeliveryReplyApplier)Sut()).ApplyAsync(_user, Fixture("google-reply"), None);
        Assert.Equal(new DeliveryReplyResponse(DeliveryReplyOutcome.AlreadyApplied, "web-1111-2222", null), again);
        _writer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AWriteTheCalendarDoesNotTake_IsAConflict_WithTheStatusAsDetail()
    {
        var stored = Invited();
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", None)).ReturnsAsync([stored]);
        _writer.Setup(w => w.PutAsync(_user.WebmailUid, stored.CalendarId, "phone-name.ics", It.IsAny<string>(), None, false, "\"abc123\"", RevisionCause.Delivery))
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.PreconditionFailed, null, null, 0));

        var projected = await ((IDeliveryReplyApplier)Sut()).ApplyAsync(_user, Fixture("google-reply"), None);

        Assert.Equal(new DeliveryReplyResponse(DeliveryReplyOutcome.Conflict, "web-1111-2222", "PreconditionFailed"), projected);
    }
}
```

- [ ] **Step 4 : vérifier l'échec**

Run : `cd src && dotnet build snoopy.microservice/snoopy.microservice.Tests 2>&1 | grep "error CS" | head -3`
Expected : `ApplyIcsAsync` inconnu, `IDeliveryReplyApplier` non implémenté.

- [ ] **Step 5 : le cœur**

Remplacer le corps de `InvitationReplyApplier.cs` (garder le `<summary>` de classe, ajouter `IDeliveryReplyApplier`) :

```csharp
internal sealed class InvitationReplyApplier(
    InvitationPartLoader parts, InvitationReader reader, IDavCalendarWriter writer, ILogger<InvitationReplyApplier> logger)
    : IInvitationReplyApplier, IDeliveryReplyApplier
{
    internal const string NotAReply = "reply_not_a_reply";
    internal const string NotApplicable = "reply_not_applicable";

    public async Task<Result<ApplyReplyResponse, ResponderFailure>> ApplyAsync(
        User user, MailAccountConnection connection, ApplyReplyRequest request, CancellationToken cancellationToken)
    {
        var ics = await parts.LoadAsync(user, connection, request.Folder, request.Uid, request.Part, cancellationToken);
        if (ics.IsFailure) return Result.Failure<ApplyReplyResponse, ResponderFailure>(ics.Error);

        var outcome = await ApplyIcsAsync(user, ics.Value, RevisionCause.Webmail, cancellationToken);
        if (outcome.Refusal is { } refusal) return Result.Failure<ApplyReplyResponse, ResponderFailure>(new ResponderFailure(400, refusal));
        var error = outcome.WriteStatus is { } status && !outcome.Applied ? InvitationResponder.CodeOf(status) : null;
        return Result.Success<ApplyReplyResponse, ResponderFailure>(
            new ApplyReplyResponse(InvitationReader.Block(outcome.Parsed!, outcome.Context!, request.Part), error is null, error));
    }

    async Task<DeliveryReplyResponse> IDeliveryReplyApplier.ApplyAsync(User user, string ics, CancellationToken cancellationToken)
    {
        var outcome = await ApplyIcsAsync(user, ics, RevisionCause.Delivery, cancellationToken);
        var uid = outcome.Parsed?.Uid;
        return outcome switch
        {
            { Refusal: NotAReply } => new(DeliveryReplyOutcome.NotAReply, null, null),
            { Refusal: not null } => new(DeliveryReplyOutcome.NotApplicable, uid, outcome.Context?.Reply?.Status.ToString()),
            { AlreadyApplied: true } => new(DeliveryReplyOutcome.AlreadyApplied, uid, null),
            { Applied: true } => new(DeliveryReplyOutcome.Applied, uid, null),
            _ => new(DeliveryReplyOutcome.Conflict, uid, outcome.WriteStatus?.ToString()),
        };
    }

    /// <summary>The one application path, whichever door brought the text (spec 5e3, décision 2).</summary>
    internal async Task<IcsReplyOutcome> ApplyIcsAsync(User user, string ics, RevisionCause cause, CancellationToken cancellationToken)
    {
        if (InvitationParser.Read(ics).Invitation is not { Method: InvitationMethod.Reply } parsed)
            return new IcsReplyOutcome(null, null, false, false, null, NotAReply);

        var context = await reader.ResolveReplyAsync(user, parsed, cancellationToken);
        if (context is not { Reply: { Status: ReplyStatus.Applicable } reply, Stored: { } stored })
            return new IcsReplyOutcome(parsed, context, false, false, null, NotApplicable);
        if (reply.Applied) return new IcsReplyOutcome(parsed, context, false, true, null, null);

        var rewritten = PartStatRewriter.Rewrite(stored.IcsRaw, reply.Email, reply.PartStat, parsed.DtStamp);
        if (rewritten is null) return new IcsReplyOutcome(parsed, context, false, false, null, NotApplicable);
        var outcome = await writer.PutAsync(user.WebmailUid, stored.CalendarId, stored.DavName, rewritten,
            cancellationToken, ifMatch: DavPropertyTables.EntityTag(stored.IcsHash), cause: cause);
        if (outcome.Status is not (DavWriteStatus.Created or DavWriteStatus.Replaced))
        {
            logger.LogWarning("The reply to {Uid} was not applied to {DavName}: {Status}",
                LogText.Safe(parsed.Uid), stored.DavName, outcome.Status);
            return new IcsReplyOutcome(parsed, context, false, false, outcome.Status, null);
        }

        return new IcsReplyOutcome(parsed, await reader.ResolveReplyAsync(user, parsed, cancellationToken), true, false, outcome.Status, null);
    }
}
```

`using weesky.Snoopy.Microservice.Models.Calendar;` est déjà là ; ajouter `using weesky.Snoopy.Microservice.Services;` si `LogText` n'est pas résolu. **Le comportement de l'entrée IMAP ne change pas** : mêmes codes, même bloc, « déjà appliquée » toujours rendue `Applied: true` comme avant (l'erreur est nulle).

- [ ] **Step 6 : DI**

Ligne 187 de `ApplicationServicesConfiguration.cs`, remplacer `services.AddScoped<IInvitationReplyApplier, InvitationReplyApplier>();` par :

```csharp
        services.AddScoped<InvitationReplyApplier>();
        services.AddScoped<IInvitationReplyApplier>(provider => provider.GetRequiredService<InvitationReplyApplier>());
        services.AddScoped<IDeliveryReplyApplier>(provider => provider.GetRequiredService<InvitationReplyApplier>());
```

- [ ] **Step 7 : vérifier le vert — les anciens tests aussi**

Run : `cd src && dotnet test snoopy.microservice/snoopy.microservice.Tests --filter "FullyQualifiedName~InvitationReplyApplier|FullyQualifiedName~CalendarInvitationsController"`
Expected : tout vert, dont `InvitationReplyApplierTests` et `InvitationReplyApplierStoreTests` inchangés.

- [ ] **Step 8 : commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add -A src/snoopy.microservice/Services src/snoopy.microservice/Models/Calendar src/snoopy.microservice/Configuration src/snoopy.microservice/snoopy.microservice.Tests
git commit -F - <<'EOF'
Calendar 5e3: reply applier split into a text core, log text sanitised
EOF
```

---

### 5. la règle de sélection partagée et le lecteur du mail brut

**Files :**
- Modify : `src/snoopy.microservice/Services/MailMessageMapper.cs:92-111`
- Create : `src/snoopy.microservice/Services/Calendar/Delivery/DeliveryMailReader.cs`
- Create : `snoopy.microservice.Tests/Fixtures/Mails/outlook-reply.eml`, `gmail-reply.eml`, `ics-attachment-only.eml`, `publish.eml`, `forwarded-reply.eml`, `deep-nesting.eml` (généré par le test)
- Modify : `snoopy.microservice.Tests/snoopy.microservice.Tests.csproj` (copier `Fixtures/Mails/*.eml` comme les `.ics` — reprendre l'`ItemGroup` qui copie `Fixtures\Invitations\*.ics`)
- Test : `snoopy.microservice.Tests/Services/MailMessageMapperTests.cs` (rien à changer : les tests existants restent verts), `Services/Calendar/Delivery/DeliveryMailReaderTests.cs`

**Interfaces :**
- Produces :
  - `MailMessageMapper.IsCalendarPart(ContentType? type, string? fileName)` et `CalendarPart<T>(IEnumerable<T> parts, Func<T, ContentType?> typeOf, Func<T, string?> nameOf) where T : class` — la règle, générique ; les deux surcharges `BodyPartBasic` existantes deviennent des délégations ; nouvelle surcharge `CalendarPart(MimeMessage message)` → `MimePart?`, sur `message.BodyParts.OfType<MimePart>()` (**jamais `MimeIterator`**).
  - `internal enum DeliveryPartStatus { None, TooLarge, Found }`, `internal sealed record DeliveryCalendarPart(DeliveryPartStatus Status, string? Ics)`.
  - `internal static class DeliveryMailReader { const int MaxMimeDepth = 16; const int MaxParts = 256; static Task<DeliveryCalendarPart> ReadAsync(Stream body, CancellationToken ct) }`.

- [ ] **Step 1 : les mails d'exemple**

Fins de ligne : les fixtures sont lues en octets par MimeKit, LF ou CRLF indifférent ; les écrire en LF (Write).

`Fixtures/Mails/outlook-reply.eml` — le mail Hotmail du 14 septembre, réduit à sa structure (la partie calendrier annoncée seule, base64 de la réponse `google-reply.ics` remplacée par une réponse en clair) :

```
From: =?utf-8?Q?Micha=C3=ABl?= <mick0181@hotmail.com>
To: Mick <darth@weesky.be>
Subject: =?utf-8?Q?Accept=C3=A9=C2=A0:_Hello_World!_(4)?=
Date: Mon, 14 Sep 2026 16:40:01 +0000
Message-ID: <AS8P251MB0958@AS8P251MB0958.EURP251.PROD.OUTLOOK.COM>
Content-Type: multipart/alternative; boundary="_000_outlook_"
MIME-Version: 1.0

--_000_outlook_
Content-Type: text/plain; charset="windows-1256"
Content-Transfer-Encoding: quoted-printable


--_000_outlook_
Content-Type: text/html; charset="windows-1256"
Content-Transfer-Encoding: quoted-printable

<html><body></body></html>
--_000_outlook_
Content-Type: text/calendar; charset="utf-8"; method=REPLY
Content-Transfer-Encoding: 8bit

BEGIN:VCALENDAR
METHOD:REPLY
PRODID:Microsoft Exchange Server 2010
VERSION:2.0
BEGIN:VEVENT
ATTENDEE;PARTSTAT=ACCEPTED;CN=Michaël:mailto:marc.dupont@example.org
UID:aaaa1111-bbbb-2222-cccc-3333dddd4444
SUMMARY;LANGUAGE=fr-BE:Accepté : Réunion de rentrée
DTSTART;TZID=Romance Standard Time:20261005T100000
DTEND;TZID=Romance Standard Time:20261005T110000
DTSTAMP:20260914T163957Z
SEQUENCE:0
END:VEVENT
END:VCALENDAR

--_000_outlook_--
```

`Fixtures/Mails/gmail-reply.eml` — la partie annoncée **et** un `invite.ics` joint sans méthode (Gmail) ; l'annoncée doit gagner :

```
From: Marc Dupont <marc.dupont@example.org>
To: alice@weesky.be
Subject: Declined: Reunion de rentree
Date: Sun, 13 Sep 2026 10:00:00 +0000
Message-ID: <gmail-1@mail.gmail.com>
Content-Type: multipart/mixed; boundary="mixed"
MIME-Version: 1.0

--mixed
Content-Type: multipart/alternative; boundary="alt"

--alt
Content-Type: text/plain; charset="UTF-8"

Marc Dupont a refuse.
--alt
Content-Type: text/calendar; charset="UTF-8"; method=REPLY
Content-Transfer-Encoding: 7bit

BEGIN:VCALENDAR
METHOD:REPLY
PRODID:-//Google Inc//Google Calendar 70.9054//EN
VERSION:2.0
BEGIN:VEVENT
UID:aaaa1111-bbbb-2222-cccc-3333dddd4444
ATTENDEE;PARTSTAT=DECLINED;CN=Marc Dupont:mailto:marc.dupont@example.org
DTSTAMP:20260913T100000Z
SEQUENCE:0
SUMMARY:Declined: Reunion de rentree
DTSTART;TZID=Europe/Brussels:20261005T100000
DTEND;TZID=Europe/Brussels:20261005T110000
END:VEVENT
END:VCALENDAR
--alt--
--mixed
Content-Type: application/ics; name="invite.ics"
Content-Disposition: attachment; filename="invite.ics"
Content-Transfer-Encoding: base64

QkVHSU46VkNBTEVOREFSDQpNRVRIT0Q6UFVCTElTSA0KVkVSU0lPTjoyLjANCkVORDpWQ0FMRU5EQVINCg==
--mixed--
```

`Fixtures/Mails/ics-attachment-only.eml` — un `.ics` joint, aucune méthode annoncée (retenu par la règle du microservice, pas par Sieve) :

```
From: someone@example.org
To: alice@weesky.be
Subject: reply
Date: Sun, 13 Sep 2026 10:00:00 +0000
Message-ID: <att-1@example.org>
Content-Type: multipart/mixed; boundary="m"
MIME-Version: 1.0

--m
Content-Type: text/plain

see attached
--m
Content-Type: application/octet-stream; name="reply.ics"
Content-Disposition: attachment; filename="reply.ics"

BEGIN:VCALENDAR
METHOD:REPLY
VERSION:2.0
BEGIN:VEVENT
UID:aaaa1111-bbbb-2222-cccc-3333dddd4444
ATTENDEE;PARTSTAT=TENTATIVE:mailto:marc.dupont@example.org
DTSTAMP:20260913T100000Z
DTSTART:20261005T080000Z
END:VEVENT
END:VCALENDAR
--m--
```

`Fixtures/Mails/publish.eml` — `method=PUBLISH` : rien à retenir :

```
From: someone@example.org
To: alice@weesky.be
Subject: publish
Date: Sun, 13 Sep 2026 10:00:00 +0000
Message-ID: <pub-1@example.org>
Content-Type: text/calendar; charset="utf-8"; method=PUBLISH
MIME-Version: 1.0

BEGIN:VCALENDAR
METHOD:PUBLISH
VERSION:2.0
END:VCALENDAR
```

`Fixtures/Mails/forwarded-reply.eml` — la réponse enfermée dans un `message/rfc822` : retenue par aucun chemin :

```
From: alice@weesky.be
To: alice@weesky.be
Subject: Fwd: Declined
Date: Sun, 13 Sep 2026 11:00:00 +0000
Message-ID: <fwd-1@weesky.be>
Content-Type: multipart/mixed; boundary="fwd"
MIME-Version: 1.0

--fwd
Content-Type: text/plain

forwarded
--fwd
Content-Type: message/rfc822

From: marc.dupont@example.org
To: alice@weesky.be
Subject: Declined
Content-Type: text/calendar; charset="utf-8"; method=REPLY
MIME-Version: 1.0

BEGIN:VCALENDAR
METHOD:REPLY
VERSION:2.0
BEGIN:VEVENT
UID:aaaa1111-bbbb-2222-cccc-3333dddd4444
ATTENDEE;PARTSTAT=DECLINED:mailto:marc.dupont@example.org
DTSTAMP:20260913T100000Z
DTSTART:20261005T080000Z
END:VEVENT
END:VCALENDAR
--fwd--
```

Dans le `.csproj` de test, à côté de l'`ItemGroup` des `.ics`, ajouter la même chose pour `Fixtures\Mails\*.eml` (`CopyToOutputDirectory=PreserveNewest`).

- [ ] **Step 2 : les tests rouges du lecteur**

`Services/Calendar/Delivery/DeliveryMailReaderTests.cs` :

```csharp
using System.Text;
using MailKit;
using MimeKit;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Delivery;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Delivery;

public sealed class DeliveryMailReaderTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    internal static Stream Mail(string name) =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Mails", name + ".eml"));

    [Theory]
    [InlineData("outlook-reply", "ACCEPTED")]
    [InlineData("gmail-reply", "DECLINED")]
    [InlineData("ics-attachment-only", "TENTATIVE")]
    public async Task Read_FindsTheReply_TheSamePartTheImapRuleWouldPick(string mail, string partStat)
    {
        using var stream = Mail(mail);
        var read = await DeliveryMailReader.ReadAsync(stream, None);

        Assert.Equal(DeliveryPartStatus.Found, read.Status);
        var parsed = InvitationParser.Read(read.Ics!).Invitation!;
        Assert.Equal((InvitationMethod.Reply, "aaaa1111-bbbb-2222-cccc-3333dddd4444", partStat),
            (parsed.Method, parsed.Uid, parsed.Attendees[0].PartStat));
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("forwarded-reply")]
    public async Task Read_FindsNothing_WhenNoPartIsRetained(string mail)
    {
        using var stream = Mail(mail);
        Assert.Equal(new DeliveryCalendarPart(DeliveryPartStatus.None, null), await DeliveryMailReader.ReadAsync(stream, None));
    }

    /// <summary>The two doors read the same parts: MimeKit's BodyParts stops at message/rfc822, as
    /// MailKit's does — the forwarded reply above is reached by neither. A BodyPartMessage is not a
    /// calendar part on the IMAP side either.</summary>
    [Fact]
    public void TheImapRule_DoesNotSeeInsideAForwardedMessage()
    {
        var forwarded = new BodyPartMessage { ContentType = new ContentType("message", "rfc822") };
        Assert.Null(MailMessageMapper.CalendarPart([new BodyPartText { ContentType = new ContentType("text", "plain") }, forwarded]));
    }

    [Fact]
    public async Task Read_SaysTooLarge_PastTheIcsCeiling()
    {
        var big = "From: a@b\r\nContent-Type: text/calendar; method=REPLY\r\n\r\n" + new string('x', IcsGuards.MaxIcsBytes + 1);
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(big));
        Assert.Equal(DeliveryPartStatus.TooLarge, (await DeliveryMailReader.ReadAsync(stream, None)).Status);
    }

    [Fact]
    public async Task Read_SurvivesDeepNesting_AndFindsNothing()
    {
        var builder = new StringBuilder("From: a@b\r\nMIME-Version: 1.0\r\n");
        for (var depth = 0; depth < 200; depth++)
            builder.Append($"Content-Type: multipart/mixed; boundary=\"b{depth}\"\r\n\r\n--b{depth}\r\n");
        builder.Append("Content-Type: text/calendar; method=REPLY\r\n\r\nBEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n");
        for (var depth = 199; depth >= 0; depth--) builder.Append($"--b{depth}--\r\n");
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(builder.ToString()));

        var read = await DeliveryMailReader.ReadAsync(stream, None);

        Assert.Equal(DeliveryPartStatus.None, read.Status);
    }

    [Fact]
    public async Task Read_RefusesMoreThanMaxParts()
    {
        var builder = new StringBuilder("From: a@b\r\nMIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary=\"b\"\r\n\r\n");
        for (var i = 0; i <= DeliveryMailReader.MaxParts; i++) builder.Append("--b\r\nContent-Type: text/plain\r\n\r\nx\r\n");
        builder.Append("--b\r\nContent-Type: text/calendar; method=REPLY\r\n\r\nBEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n--b--\r\n");
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(builder.ToString()));

        Assert.Equal(DeliveryPartStatus.None, (await DeliveryMailReader.ReadAsync(stream, None)).Status);
    }

    [Fact]
    public async Task Read_GarbageIsNothing()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("not a mail at all"));
        Assert.Equal(DeliveryPartStatus.None, (await DeliveryMailReader.ReadAsync(stream, None)).Status);
    }
}
```

- [ ] **Step 3 : vérifier l'échec**

Run : `cd src && dotnet build snoopy.microservice/snoopy.microservice.Tests 2>&1 | grep "error CS" | head -3`
Expected : `DeliveryMailReader` inconnu.

- [ ] **Step 4 : la règle générique dans `MailMessageMapper`**

Remplacer les deux méthodes `IsCalendarPart`/`CalendarPart` (l. 92-111) par :

```csharp
    /// <summary>A calendar part by type or by name: Google and Outlook slip the invitation into the
    /// multipart/alternative as text/calendar with no name, and attach an invite.ics besides.</summary>
    internal static bool IsCalendarPart(ContentType? type, string? fileName) =>
        type is not null && (type.IsMimeType("text", "calendar") || type.IsMimeType("application", "ics"))
        || fileName is { } name && name.EndsWith(".ics", StringComparison.OrdinalIgnoreCase);

    internal static bool IsCalendarPart(BodyPartBasic part) => IsCalendarPart(part.ContentType, part.FileName);

    /// <summary>The one calendar part worth downloading (décision 1): a part whose Content-Type
    /// announces a handled method wins, then one announcing none; a part announcing another method
    /// (PUBLISH…) is never it. Document order otherwise. One rule for the two doors (spec 5e3,
    /// décision 2): the IMAP structure and a MimeKit message project onto it.</summary>
    internal static T? CalendarPart<T>(IEnumerable<T> parts, Func<T, ContentType?> typeOf, Func<T, string?> nameOf) where T : class
    {
        T? unannounced = null;
        foreach (var part in parts)
        {
            var type = typeOf(part);
            if (!IsCalendarPart(type, nameOf(part))) continue;
            var method = type?.Parameters["method"]?.Trim().ToUpperInvariant();
            if (method is "REQUEST" or "CANCEL" or "REPLY") return part;
            if (method is null) unannounced ??= part;
        }
        return unannounced;
    }

    internal static BodyPartBasic? CalendarPart(IEnumerable<BodyPartBasic> parts) =>
        CalendarPart(parts, p => p.ContentType, p => p.FileName);

    /// <summary>BodyParts, never MimeIterator: BodyParts stops at message/rfc822 exactly as MailKit's
    /// does, and that is what keeps the two doors reading the same parts (measured on 4.17).</summary>
    internal static MimePart? CalendarPart(MimeMessage message) =>
        CalendarPart(message.BodyParts.OfType<MimePart>(), p => p.ContentType, p => p.FileName);
```

- [ ] **Step 5 : le lecteur**

`Services/Calendar/Delivery/DeliveryMailReader.cs` :

```csharp
using MimeKit;
using weesky.Snoopy.Microservice.Services.Calendar;

namespace weesky.Snoopy.Microservice.Services.Calendar.Delivery;

internal enum DeliveryPartStatus { None, TooLarge, Found }

internal sealed record DeliveryCalendarPart(DeliveryPartStatus Status, string? Ics)
{
    internal static readonly DeliveryCalendarPart Nothing = new(DeliveryPartStatus.None, null);
    internal static readonly DeliveryCalendarPart TooLarge = new(DeliveryPartStatus.TooLarge, null);
}

/// <summary>The raw mail the mail server posts, to the calendar text the applier reads. Bounded
/// where the attacker sits: nesting during the parse (a StackOverflowException is not catchable),
/// the part count and the decoded size after it (spec 5e3, § Le traitement).</summary>
internal static class DeliveryMailReader
{
    internal const int MaxMimeDepth = 16;
    internal const int MaxParts = 256;

    internal static async Task<DeliveryCalendarPart> ReadAsync(Stream body, CancellationToken cancellationToken)
    {
        var options = new ParserOptions { MaxMimeDepth = MaxMimeDepth };
        MimeMessage message;
        try
        {
            message = await MimeMessage.LoadAsync(options, body, cancellationToken);
        }
        catch (FormatException)
        {
            return DeliveryCalendarPart.Nothing;
        }

        if (message.BodyParts.Take(MaxParts + 1).Count() > MaxParts) return DeliveryCalendarPart.Nothing;
        if (MailMessageMapper.CalendarPart(message) is not { Content: not null } part) return DeliveryCalendarPart.Nothing;

        using var decoded = new MemoryStream();
        await part.Content.DecodeToAsync(decoded, cancellationToken);
        if (decoded.Length > IcsGuards.MaxIcsBytes) return DeliveryCalendarPart.TooLarge;
        return new DeliveryCalendarPart(DeliveryPartStatus.Found,
            MailMessageMapper.DecodeText(decoded.GetBuffer(), (int)decoded.Length, part.ContentType.Charset));
    }
}
```

Si `Read_GarbageIsNothing` échoue parce que MimeKit lit « not a mail at all » comme un corps sans en-têtes (il est tolérant), c'est acceptable : la partie calendrier est alors absente et le résultat est `None` de toute façon — vérifier que le test passe tel quel, sinon remplacer l'assertion par `Assert.Equal(DeliveryPartStatus.None, …)` seulement.

- [ ] **Step 6 : vérifier le vert**

Run : `cd src && dotnet test snoopy.microservice/snoopy.microservice.Tests --filter "FullyQualifiedName~DeliveryMailReaderTests|FullyQualifiedName~MailMessageMapperTests"`
Expected : tout vert.

- [ ] **Step 7 : commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add -A src/snoopy.microservice/Services/MailMessageMapper.cs src/snoopy.microservice/Services/Calendar/Delivery src/snoopy.microservice/snoopy.microservice.Tests
git commit -F - <<'EOF'
Calendar 5e3: one calendar-part rule for IMAP and raw mail, bounded MIME reader
EOF
```

---

## Paquet C — La porte — le contrôleur, le limiteur, le test de surface

Un sous-agent backend, après A et B.

### 6. la porte `POST /api/Delivery/CalendarReplies`

**Files :**
- Create : `src/snoopy.microservice/Services/Calendar/Delivery/DeliveryMailbox.cs`, `DeliveryRefusals.cs`
- Create : `src/snoopy.microservice/Controllers/DeliveryController.cs`
- Modify : `src/snoopy.microservice/Configuration/SecurityConfiguration.cs:88-100` (commentaire CORS) et `:154-176` (`AddRateLimiters`)
- Modify : `src/snoopy.microservice.host/Program.cs:23`
- Test : `snoopy.microservice.Tests/Services/Calendar/Delivery/DeliveryMailboxTests.cs`, `DeliveryRefusalsTests.cs`, `Controllers/DeliveryControllerTests.cs`

**Interfaces :**
- Consumes : `IDeliveryKeyProvider`, `IDeliveryKeyStore.RecordCallAsync`, `DeliveryKeys.Matches` (partie 2) ; `IDeliveryReplyApplier` (partie 4) ; `DeliveryMailReader` (partie 5) ; `IWebmailUserStore.FindByEmailAsync`.
- Produces : `POST api/Delivery/CalendarReplies` — `[AllowAnonymous]`, `[RequestSizeLimit(DeliveryController.MaxBodyBytes)]`, `[EnableRateLimiting("delivery")]` ; en-têtes `X-Delivery-Key`, `X-Delivery-Mailbox` ; `DeliveryMailbox.TryNormalize(string? raw, out string mailbox)` ; `DeliveryRefusals.Note(bool disabled)` → `int?` (le nombre à journaliser, ou null pour se taire).

- [ ] **Step 1 : `DeliveryMailbox`, test puis code**

```csharp
// Tests/Services/Calendar/Delivery/DeliveryMailboxTests.cs
using weesky.Snoopy.Microservice.Services.Calendar.Delivery;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Delivery;

public sealed class DeliveryMailboxTests
{
    [Theory]
    [InlineData(" Darth@Weesky.be ", "darth@weesky.be")]
    [InlineData("a.b+c@example.org", "a.b+c@example.org")]
    public void Normalizes_TrimmedAndLowered(string raw, string expected)
    {
        Assert.True(DeliveryMailbox.TryNormalize(raw, out var mailbox));
        Assert.Equal(expected, mailbox);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("noatsign")]
    [InlineData("@weesky.be")]
    [InlineData("darth@")]
    [InlineData("two@at@weesky.be")]
    [InlineData("darth@weesky.be\r\nX-Forged: 1")]
    [InlineData("dar th@weesky.be")]
    [InlineData("@weesky.be")]
    public void Refuses_WhatIsNotOneAddress(string? raw) =>
        Assert.False(DeliveryMailbox.TryNormalize(raw, out _));

    [Fact]
    public void Refuses_Over320Characters() =>
        Assert.False(DeliveryMailbox.TryNormalize(new string('a', 315) + "@x.be", out _));
}
```

```csharp
// Services/Calendar/Delivery/DeliveryMailbox.cs
namespace weesky.Snoopy.Microservice.Services.Calendar.Delivery;

/// <summary>The X-Delivery-Mailbox header, validated before it is logged or handed to
/// <c>new User(...)</c> (which throws on anything but one '@'). Exactly one address, no whitespace,
/// no control character, the shape WebmailUserStore.FindByEmailAsync canonicalises to.</summary>
internal static class DeliveryMailbox
{
    private const int MaxLength = 320;

    internal static bool TryNormalize(string? raw, out string mailbox)
    {
        mailbox = raw?.Trim().ToLowerInvariant() ?? string.Empty;
        if (mailbox.Length is 0 or > MaxLength) return false;
        if (mailbox.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))) return false;
        var at = mailbox.IndexOf('@');
        return at > 0 && at < mailbox.Length - 1 && mailbox.IndexOf('@', at + 1) < 0;
    }
}
```

- [ ] **Step 2 : `DeliveryRefusals`, test puis code** — une ligne de journal par minute au plus (décision 8)

```csharp
// Tests/Services/Calendar/Delivery/DeliveryRefusalsTests.cs
using weesky.Snoopy.Microservice.Services.Calendar.Delivery;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Delivery;

public sealed class DeliveryRefusalsTests
{
    [Fact]
    public void TheFirstRefusalSpeaks_TheNextOnesWaitAMinute_ThenOneSpeaksForAll()
    {
        var clock = new MutableTimeProvider();
        var refusals = new DeliveryRefusals(clock);

        Assert.Equal(1, refusals.Note());
        Assert.Null(refusals.Note());
        Assert.Null(refusals.Note());
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Null(refusals.Note());
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(4, refusals.Note());
        Assert.Null(refusals.Note());
    }
}
```

(`MutableTimeProvider.Advance` : vérifier le nom de la méthode dans `Tests/Infrastructure/MutableTimeProvider.cs` et l'adapter.)

```csharp
// Services/Calendar/Delivery/DeliveryRefusals.cs
namespace weesky.Snoopy.Microservice.Services.Calendar.Delivery;

/// <summary>Refused delivery calls are what the Internet can multiply: they get one log line a
/// minute at most, carrying the count since the last one (spec 5e3, décision 8). A singleton.</summary>
internal sealed class DeliveryRefusals(TimeProvider clock)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly Lock gate = new();
    private DateTimeOffset? lastSpoken;
    private int pending;

    /// <summary>The number of refusals to report now, or null to stay silent.</summary>
    internal int? Note()
    {
        lock (gate)
        {
            pending++;
            var now = clock.GetUtcNow();
            if (lastSpoken is { } spoken && now - spoken < Window) return null;
            lastSpoken = now;
            var count = pending;
            pending = 0;
            return count;
        }
    }
}
```

Enregistrer dans `ApplicationServicesConfiguration.cs` à côté du fournisseur : `services.AddSingleton<DeliveryRefusals>();`.

- [ ] **Step 3 : le limiteur et le commentaire CORS**

Dans `SecurityConfiguration.cs`, sur la politique CORS (l. ~90-100), au-dessus de `.WithHeaders(...)` :

```csharp
                // X-Delivery-Key stays OUT of this list: it is what makes a browser's preflight fail
                // on POST /api/Delivery/CalendarReplies, so no page can be steered into that door
                // (spec 5e3). Adding it here would open the door to CSRF.
```

Remplacer `AddLoginRateLimiter` (l. 154-176) par :

```csharp
    /// <summary>
    /// Two policies. <c>login</c> bounds password guessing on the three endpoints that verify one
    /// (the login itself, attaching a mailbox, re-entering an attached mailbox's password),
    /// partitioned by the caller's address — which only means anything once
    /// <see cref="AddProxyForwardedHeaders"/> has put the real one there. <c>delivery</c> bounds
    /// the concurrency of the mail server's door: every legitimate call comes from one address, so
    /// a per-address window would be one global bucket anyone could drain (spec 5e3, décision 13);
    /// what this protects is the process, 503 past 8 in flight and 16 queued, and it says so.
    /// </summary>
    public static IServiceCollection AddRateLimiters(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("login", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromMinutes(1),
                        PermitLimit = 5,
                        QueueLimit = 0
                    }));

            options.AddPolicy(DeliveryPolicy, _ =>
                RateLimitPartition.GetConcurrencyLimiter(DeliveryPolicy, _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = 8,
                    QueueLimit = 16,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                }));

            options.OnRejected = (context, _) =>
            {
                var policy = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                if (policy != DeliveryPolicy) return ValueTask.CompletedTask;
                context.HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.HttpContext.RequestServices.GetRequiredService<ILogger<DeliveryController>>()
                    .LogWarning("A delivery call was refused: more than 8 in flight and 16 waiting");
                return ValueTask.CompletedTask;
            };
        });

    public const string DeliveryPolicy = "delivery";
```

(usings : `Microsoft.AspNetCore.RateLimiting`, `System.Threading.RateLimiting`, `weesky.Snoopy.Microservice.Controllers`.) Dans `Program.cs:23`, `.AddLoginRateLimiter()` → `.AddRateLimiters()`. Mettre à jour le bloc de `reverse-proxy-prerequisite.md` qui liste les routes limitées : ajouter une ligne « `POST /api/Delivery/CalendarReplies` — concurrence, pas par adresse ».

- [ ] **Step 4 : les tests rouges du contrôleur**

`Controllers/DeliveryControllerTests.cs` :

```csharp
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar.Delivery;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using weesky.Snoopy.Microservice.Tests.Services.Calendar.Delivery;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class DeliveryControllerTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private const string Key = "k-k-k";
    private static readonly byte[] Hash = DeliveryKeys.Hash(Key);
    private readonly Mock<IDeliveryKeyProvider> _keys = new();
    private readonly Mock<IDeliveryKeyStore> _store = new();
    private readonly Mock<IWebmailUserStore> _users = new();
    private readonly Mock<IDeliveryReplyApplier> _applier = new();
    private readonly Mock<ILogger<DeliveryController>> _logger = new();
    private readonly MutableTimeProvider _clock = new();

    private DeliveryController Controller(string? key = Key, string? mailbox = "darth@weesky.be", string mail = "outlook-reply")
    {
        var http = new DefaultHttpContext();
        if (key is not null) http.Request.Headers[DeliveryController.KeyHeader] = key;
        if (mailbox is not null) http.Request.Headers[DeliveryController.MailboxHeader] = mailbox;
        http.Request.Body = DeliveryMailReaderTests.Mail(mail);
        return new DeliveryController(_keys.Object, _store.Object, _users.Object, _applier.Object,
            new DeliveryRefusals(_clock), _clock, _logger.Object)
        { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private void KeyIs(bool enabled) => _keys.Setup(k => k.GetAsync(None)).ReturnsAsync(new DeliveryKeySnapshot(Hash, enabled));

    [Fact]
    public async Task Disabled_Is404Bare_EvenWithTheRightKey()
    {
        KeyIs(enabled: false);
        Assert.IsType<NotFoundResult>((await Controller().ApplyCalendarReply(None)).Result);
        _store.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public async Task AWrongOrMissingKey_Is404Bare(string? key)
    {
        KeyIs(enabled: true);
        Assert.IsType<NotFoundResult>((await Controller(key: key).ApplyCalendarReply(None)).Result);
        _store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NoKeyStored_Is404Bare()
    {
        _keys.Setup(k => k.GetAsync(None)).ReturnsAsync((DeliveryKeySnapshot?)null);
        Assert.IsType<NotFoundResult>((await Controller().ApplyCalendarReply(None)).Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("darth@weesky.be")]
    public async Task ABadMailboxHeader_Is400_BeforeTheKeyIsEvenRead(string? mailbox)
    {
        var result = (await Controller(mailbox: mailbox).ApplyCalendarReply(None)).Result;
        Assert.IsType<BadRequestObjectResult>(result);
        _keys.VerifyNoOtherCalls();
        _logger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnUnknownMailbox_IsSaidSo_AfterTheCallIsRecorded()
    {
        KeyIs(enabled: true);
        _users.Setup(u => u.FindByEmailAsync("darth@weesky.be", None)).ReturnsAsync((WebmailAccount?)null);

        var response = (await Controller().ApplyCalendarReply(None)).Value!;

        Assert.Equal(new DeliveryReplyResponse(DeliveryReplyOutcome.UnknownMailbox, null, null), response);
        _store.Verify(s => s.RecordCallAsync(_clock.GetUtcNow().UtcDateTime, None), Times.Once);
    }

    [Fact]
    public async Task AReply_ReachesTheApplier_AsTheMailboxOwner()
    {
        KeyIs(enabled: true);
        var id = Guid.NewGuid();
        _users.Setup(u => u.FindByEmailAsync("darth@weesky.be", None)).ReturnsAsync(new WebmailAccount(id, Guid.NewGuid()));
        User? seen = null;
        _applier.Setup(a => a.ApplyAsync(It.IsAny<User>(), It.Is<string>(ics => ics.Contains("PARTSTAT=ACCEPTED")), None))
            .Callback<User, string, CancellationToken>((u, _, _) => seen = u)
            .ReturnsAsync(new DeliveryReplyResponse(DeliveryReplyOutcome.Applied, "u", null));

        var response = (await Controller().ApplyCalendarReply(None)).Value!;

        Assert.Equal(DeliveryReplyOutcome.Applied, response.Outcome);
        Assert.Equal(("darth@weesky.be", id), (seen!.Email, seen.WebmailUid));
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("forwarded-reply")]
    public async Task NotAReply_WhenNoPartIsRetained(string mail)
    {
        KeyIs(enabled: true);
        _users.Setup(u => u.FindByEmailAsync("darth@weesky.be", None)).ReturnsAsync(new WebmailAccount(Guid.NewGuid(), Guid.NewGuid()));

        var response = (await Controller(mail: mail).ApplyCalendarReply(None)).Value!;

        Assert.Equal(DeliveryReplyOutcome.NotAReply, response.Outcome);
        _applier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RefusedCalls_LogOnceAMinute()
    {
        KeyIs(enabled: true);
        var controller = Controller(key: "wrong");
        await controller.ApplyCalendarReply(None);
        await Controller(key: "wrong").ApplyCalendarReply(None);
        _logger.VerifyLog(LogLevel.Warning, Times.Once());
    }
}
```

(`VerifyLog` : l'aide de `Tests/Fixtures/LoggerAssertions.cs` — vérifier sa signature et l'adapter ; à défaut, `_logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once)`.) Pour le cas `too_large`, la borne est testée dans `DeliveryMailReaderTests` ; ajouter ici un test que `DeliveryPartStatus.TooLarge` devient `NotApplicable`/`too_large` en injectant un mail construit comme dans `Read_SaysTooLarge_PastTheIcsCeiling` via `http.Request.Body`.

- [ ] **Step 5 : le contrôleur**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using weesky.Snoopy.Microservice.Configuration;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Delivery;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The mail server's door (spec 5e3): Dovecot's Sieve script posts a guest's REPLY here at
/// delivery, with the mailbox owner and the shared key, and the reply is applied without a
/// session. Anonymous by design; the key is the authentication, and a wrong one is a 404, not a
/// 401 — the door must not say whether it exists (décision 7).
/// </summary>
[Route("api/Delivery")]
[ApiController]
[AllowAnonymous]
public sealed class DeliveryController(
    IDeliveryKeyProvider keys, IDeliveryKeyStore store, IWebmailUserStore users, IDeliveryReplyApplier applier,
    DeliveryRefusals refusals, TimeProvider clock, ILogger<DeliveryController> logger) : ApiBaseController
{
    internal const string KeyHeader = "X-Delivery-Key";
    internal const string MailboxHeader = "X-Delivery-Mailbox";
    internal const int MaxBodyBytes = 5 * 1024 * 1024;
    internal const string TooLarge = "too_large";

    /// <summary>Applies the calendar REPLY a raw mail carries into the mailbox owner's calendar.</summary>
    /// <response code="200">What was done: <c>{ outcome, uid?, detail? }</c></response>
    /// <response code="400"><c>X-Delivery-Mailbox</c> is not one address</response>
    /// <response code="404">The door is closed, or the key is wrong — indistinguishable on purpose</response>
    /// <response code="413">The mail is over 5 MB</response>
    [HttpPost("CalendarReplies")]
    [RequestSizeLimit(MaxBodyBytes)]
    [EnableRateLimiting(SecurityConfiguration.DeliveryPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<DeliveryReplyResponse>> ApplyCalendarReply(CancellationToken cancellationToken)
    {
        // Validated before anything is logged: the header is the one thing here that reaches the log.
        if (!DeliveryMailbox.TryNormalize(Request.Headers[MailboxHeader], out var mailbox))
            return BadRequestEnveloppe($"{MailboxHeader} must be one e-mail address");

        var key = await keys.GetAsync(cancellationToken);
        if (key is null || !key.Enabled || !DeliveryKeys.Matches(Request.Headers[KeyHeader], key.KeyHash))
        {
            if (refusals.Note() is { } count)
                logger.LogWarning("Delivery calls refused: {Count} since the last notice (door {State})",
                    count, key is { Enabled: true } ? "open, wrong key" : "closed");
            return NotFound();
        }
        await store.RecordCallAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);

        var account = await users.FindByEmailAsync(mailbox, cancellationToken);
        if (account is null) return Answer(new DeliveryReplyResponse(DeliveryReplyOutcome.UnknownMailbox, null, null), mailbox);

        var part = await DeliveryMailReader.ReadAsync(Request.Body, cancellationToken);
        var response = part.Status switch
        {
            DeliveryPartStatus.None => new DeliveryReplyResponse(DeliveryReplyOutcome.NotAReply, null, null),
            DeliveryPartStatus.TooLarge => new DeliveryReplyResponse(DeliveryReplyOutcome.NotApplicable, null, TooLarge),
            _ => await applier.ApplyAsync(new User(mailbox) { WebmailUid = account.Value.Id }, part.Ics!, cancellationToken),
        };
        return Answer(response, mailbox);
    }

    private ActionResult<DeliveryReplyResponse> Answer(DeliveryReplyResponse response, string mailbox)
    {
        var level = response.Outcome is DeliveryReplyOutcome.Conflict ? LogLevel.Warning : LogLevel.Information;
        logger.Log(level, "Delivery reply for {Mailbox}: {Outcome} {Uid} {Detail}",
            mailbox, response.Outcome, LogText.Safe(response.Uid), LogText.Safe(response.Detail));
        return Ok(response);
    }
}
```

`Request.Headers[…]` est un `StringValues` : passer `.ToString()` à `TryNormalize`/`Matches` (elles prennent `string?`).

- [ ] **Step 6 : vérifier le vert, suite complète, commit**

Run : `cd src && dotnet test`
Expected : tout vert (dont les 5153 + 289 d'avant).

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add -A src/snoopy.microservice src/snoopy.microservice.host/Program.cs docs/superpowers/reverse-proxy-prerequisite.md
git commit -F - <<'EOF'
Calendar 5e3: delivery door for guests' replies, concurrency limiter
EOF
```

---

### 7. le test de surface de route

**Files :**
- Create : `snoopy.microservice.Tests/Controllers/DeliveryRouteSurfaceTests.cs`

**Interfaces :**
- Consumes : `ControllerRouteSurface` (existant), les deux contrôleurs (partie 3 et 6).

- [ ] **Step 1 : le test** — dans sa propre classe, car `MailRouteSurfaceTests` exige `[Authorize]` sur tout son lot :

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

/// <summary>Pins the delivery door and its key endpoint: anonymous where it must be, admin-only
/// where it must be, and the three filters the door cannot lose without becoming a hole.</summary>
public sealed class DeliveryRouteSurfaceTests
{
    private static IReadOnlyList<ControllerRouteSurface.Action> Surface(string prefix) =>
        [.. ControllerRouteSurface.Of(ControllerRouteSurface.ControllersOf(typeof(ApiBaseController).Assembly))
            .Where(a => a.Prefix == prefix)];

    [Fact]
    public void The_door_is_one_anonymous_post_with_a_size_limit_and_the_delivery_policy()
    {
        var door = Assert.Single(Surface("api/Delivery"));
        Assert.Equal("POST api/Delivery/CalendarReplies", $"{door.Verb} {door.Route}");
        Assert.NotNull(door.Controller.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(door.Controller.GetCustomAttribute<AuthorizeAttribute>(inherit: true));
        Assert.Equal(5 * 1024 * 1024, door.Method.GetCustomAttribute<RequestSizeLimitAttribute>()!.Bytes);
        Assert.Equal("delivery", door.Method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.Equal([200, 400, 404, 413], door.Method.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(a => a.StatusCode).OrderBy(c => c));
    }

    [Fact]
    public void The_key_endpoint_is_admin_only_with_four_verbs()
    {
        var actions = Surface("api/DeliveryReplyKey");
        Assert.Equal(["DELETE api/DeliveryReplyKey", "GET api/DeliveryReplyKey", "POST api/DeliveryReplyKey", "PUT api/DeliveryReplyKey"],
            actions.Select(a => $"{a.Verb} {a.Route}").OrderBy(r => r, StringComparer.Ordinal).ToList());
        var controller = actions.Select(a => a.Controller).Distinct().Single();
        Assert.Equal(AdminRequirement.PolicyName, controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.All(actions, a => Assert.Empty(a.Method.GetCustomAttributes<AllowAnonymousAttribute>()));
    }
}
```

`RequestSizeLimitAttribute.Bytes` : si la propriété n'est pas publique sur cette version d'ASP.NET, lire le champ par réflexion comme `MailRouteSurfaceTests` lit les filtres, ou comparer `door.Method.GetCustomAttributes<RequestSizeLimitAttribute>()` à un élément unique et pinner `DeliveryController.MaxBodyBytes == 5 * 1024 * 1024` à côté.

- [ ] **Step 2 : vérifier le vert, commit**

Run : `cd src && dotnet test snoopy.microservice/snoopy.microservice.Tests --filter "FullyQualifiedName~RouteSurface"`

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml
git add src/snoopy.microservice/snoopy.microservice.Tests/Controllers/DeliveryRouteSurfaceTests.cs
git commit -F - <<'EOF'
Calendar 5e3: route surface pinned for the delivery door and its key
EOF
```

---

## Paquet D — La maquette — session principale

Pas de sous-agent : l'Artifact, ta validation, puis E.

### 8. la maquette de la section (session principale, pas un sous-agent)

**Files :**
- Create : `probes/delivery-replies-mockup.html` (scratchpad, publié en Artifact)

Suivre `feedback_mockups_via_artifacts` : relever les jetons réels (`index.css` : `.svc-account-section*`, `.svc-account-card`, `.admin-list-item`, `.toggle-switch`, `.field-h`, `.modal`, `.btn-ghost`, `--success`, `--text-muted`, `--danger`) avant de dessiner ; charger le skill `artifact-design` ; une planche par état, en français **et** en anglais, thème clair et sombre :

1. Sans clé : icône `ShieldAlertIcon` grise, la phrase d'aide de la spec (§ L'écran, avec « Les réponses sont alors inscrites sans qu'un utilisateur les ait vues. »), bouton « Générer une clé », interrupteur grisé.
2. Avec clé, désactivé, aucun appel : interrupteur off, « Clé créée le 14 sept. », « Aucun appel reçu depuis la création de la clé », Régénérer / Supprimer (icônes `admin-icon-btn`).
3. Avec clé, activé, appel reçu : pastille verte « Dernier appel reçu le 14 sept., 18:40 » (même skin que `.svc-account-pill.is-ok`).
4. La fenêtre de la clé : champ en lecture seule, monospace, bouton Copier, « Elle ne sera plus affichée », le chemin `/etc/dovecot/calendar-reply.conf`.
5. La confirmation de régénération (texte de la spec) — réutilise `DeleteConfirmModal` avec `title`/`confirmLabel`/`message`.
6. Largeur tablette (container query comme `.svc-account-slot`) et téléphone 360.

- [ ] **Step 1 :** construire la planche, publier l'Artifact, envoyer le lien.
- [ ] **Step 2 :** attendre la validation de l'utilisateur ; noter ses retours dans le paquet E avant de le lancer. **Aucun code d'interface avant ce oui.**

---

## Paquet E — L'écran — API cliente, hook, locales, section, fenêtre de la clé, tests

Un sous-agent frontend, après D (la maquette validée fait foi sur l'aspect). Peut avancer en parallèle de B et C.

### 9. l'API cliente, le hook, les codes d'erreur et les locales

**Files :**
- Modify : `src/frontend/src/api.js` (après `adminTestSchedulingAccount`, l. ~517)
- Modify : `src/frontend/src/lib/apiErrorMessage.ts` (`CODES`, après `password_required_for_new_endpoint`)
- Modify : `src/frontend/src/locales/en/errors.json`, `fr/errors.json`, `en/admin.json`, `fr/admin.json`
- Create : `src/frontend/src/modules/settings/admin/useDeliveryReplyKey.ts`, `deliveryReplyKeyErrors.ts`
- Test : `src/frontend/src/modules/settings/admin/useDeliveryReplyKey.test.ts`

**Interfaces :**
- Consumes : `GET/POST/PUT/DELETE /api/DeliveryReplyKey` (partie 3).
- Produces :
  - `api.adminGetDeliveryReplyKey()`, `api.adminGenerateDeliveryReplyKey()`, `api.adminSetDeliveryReplies({ enabled })`, `api.adminDeleteDeliveryReplyKey()`.
  - `interface DeliveryReplyKey { configured: boolean; enabled: boolean; createdAt?: string; lastCallAt?: string }`, `interface DeliveryReplyKeyGenerated { key: string }`.
  - `useDeliveryReplyKey()`, `useGenerateDeliveryKey()`, `useSetDeliveryReplies()` (mutation sur `boolean`), `useDeleteDeliveryKey()` — toutes invalident `onSettled`.
  - `deliveryErrorMessage(err, t, fallback)`.
  - Clés `admin:deliveryReplies.*` (liste ci-dessous) ; `errors:deliveryKeyMissing`, `errors:deliveryKeyChangedConcurrently`.

- [ ] **Step 1 : `api.js`**

```js
  adminGetDeliveryReplyKey: () =>
    request('GET', '/api/DeliveryReplyKey'),

  adminGenerateDeliveryReplyKey: () =>
    request('POST', '/api/DeliveryReplyKey'),

  adminSetDeliveryReplies: (body) =>
    request('PUT', '/api/DeliveryReplyKey', body),

  adminDeleteDeliveryReplyKey: () =>
    request('DELETE', '/api/DeliveryReplyKey'),
```

- [ ] **Step 2 : les codes**

Dans `CODES` :

```ts
  delivery_key_missing: 'errors:deliveryKeyMissing',
  delivery_key_changed_concurrently: 'errors:deliveryKeyChangedConcurrently',
```

`en/errors.json` (après `schedulingAccountChangedConcurrently`) :

```json
  "deliveryKeyMissing": "Generate a key before enabling delivery-time replies.",
  "deliveryKeyChangedConcurrently": "The key changed in the meantime: it was reloaded.",
```

`fr/errors.json` (insécables U+00A0 avant `:`) :

```json
  "deliveryKeyMissing": "Générez une clé avant d’activer le traitement à la livraison.",
  "deliveryKeyChangedConcurrently": "La clé a été modifiée entre-temps : elle a été rechargée.",
```

- [ ] **Step 3 : les locales de la section** — bloc `deliveryReplies` dans `admin.json`, après `scheduling` :

`en` :

```json
  "deliveryReplies": {
    "title": "Guests' replies at delivery",
    "intro": "The mail server can apply guests' replies the moment it delivers them, without waiting for the mail to be opened. Replies are then written without anyone having seen them. Opening the mail still applies a reply the delivery missed.",
    "loadFailed": "Could not load the delivery key.",
    "generate": "Generate a key",
    "regenerate": "Regenerate",
    "toggle": "Apply replies at delivery",
    "createdOn": "Key created on {{date}}",
    "lastCallOn": "Last call received on {{date}}",
    "noCall": "No call received since the key was created",
    "noKeyHint": "Generate a key, copy it to the mail server, then enable.",
    "dialogTitle": "Delivery key",
    "dialogIntro": "Copy this key now: it will not be shown again. Paste it into /etc/dovecot/calendar-reply.conf on the mail server, then enable the switch.",
    "copy": "Copy",
    "copied": "Key copied.",
    "copyFailed": "Could not copy: select the key and copy it by hand.",
    "generated": "A new key was generated.",
    "generateFailed": "Could not generate the key.",
    "regenerateTitle": "Regenerate the key?",
    "regenerateMessage": "The mail server will be refused until its configuration holds the new key. Replies are still applied when the mail is opened meanwhile.",
    "enabled": "Replies are now applied at delivery.",
    "disabled": "Replies are now applied when the mail is opened.",
    "toggleFailed": "Could not change the setting.",
    "deleteMessage": "The key will be deleted and delivery-time replies disabled. The mail server's configuration will have to receive a new key.",
    "deleted": "The key was deleted.",
    "deleteFailed": "Could not delete the key.",
    "keyGone": "The key no longer exists.",
    "keyLabel": "delivery key"
  }
```

`fr` (U+00A0 avant `:` `;` `!` `?` et dans « ») :

```json
  "deliveryReplies": {
    "title": "Réponses des invités à la livraison",
    "intro": "Le serveur mail peut appliquer les réponses des invités au moment où il les livre, sans attendre que le mail soit ouvert. Les réponses sont alors inscrites sans qu’un utilisateur les ait vues. Ouvrir le mail applique toujours une réponse que la livraison aurait manquée.",
    "loadFailed": "Impossible de charger la clé de livraison.",
    "generate": "Générer une clé",
    "regenerate": "Régénérer",
    "toggle": "Traiter les réponses à la livraison",
    "createdOn": "Clé créée le {{date}}",
    "lastCallOn": "Dernier appel reçu le {{date}}",
    "noCall": "Aucun appel reçu depuis la création de la clé",
    "noKeyHint": "Générez une clé, copiez-la sur le serveur mail, puis activez.",
    "dialogTitle": "Clé de livraison",
    "dialogIntro": "Copiez cette clé maintenant : elle ne sera plus affichée. Collez-la dans /etc/dovecot/calendar-reply.conf sur le serveur mail, puis activez l’interrupteur.",
    "copy": "Copier",
    "copied": "Clé copiée.",
    "copyFailed": "Impossible de copier : sélectionnez la clé et copiez-la à la main.",
    "generated": "Une nouvelle clé a été générée.",
    "generateFailed": "Impossible de générer la clé.",
    "regenerateTitle": "Régénérer la clé ?",
    "regenerateMessage": "Le serveur mail sera refusé tant que sa configuration n’aura pas reçu la nouvelle clé. Entre-temps, les réponses s’appliquent à l’ouverture du mail.",
    "enabled": "Les réponses sont désormais appliquées à la livraison.",
    "disabled": "Les réponses sont désormais appliquées à l’ouverture du mail.",
    "toggleFailed": "Impossible de modifier le réglage.",
    "deleteMessage": "La clé sera supprimée et le traitement à la livraison désactivé. La configuration du serveur mail devra recevoir une nouvelle clé.",
    "deleted": "La clé a été supprimée.",
    "deleteFailed": "Impossible de supprimer la clé.",
    "keyGone": "La clé n’existe plus.",
    "keyLabel": "clé de livraison"
  }
```

Écrire le `fr` avec un script Python (` `) plutôt qu'avec Edit, qui pose une espace ordinaire (`feedback_nbsp_edit_tool`). Les textes de la maquette validée (paquet D) priment sur ceux-ci s'ils diffèrent.

- [ ] **Step 4 : le hook, test puis code**

`useDeliveryReplyKey.test.ts` :

```ts
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import { createElement, type ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { api } from '../../../api.js'
import { useDeliveryReplyKey, useGenerateDeliveryKey, useSetDeliveryReplies } from './useDeliveryReplyKey'

vi.mock('../../../api.js', () => ({ api: {
  adminGetDeliveryReplyKey: vi.fn(), adminGenerateDeliveryReplyKey: vi.fn(),
  adminSetDeliveryReplies: vi.fn(), adminDeleteDeliveryReplyKey: vi.fn(),
}, ApiError: class extends Error {} }))

function wrapper() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return { client, Wrapper: ({ children }: { children: ReactNode }) => createElement(QueryClientProvider, { client }, children) }
}

describe('useDeliveryReplyKey', () => {
  it('reads the state and refreshes it after a write, even a refused one', async () => {
    vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: false, enabled: false })
    vi.mocked(api.adminSetDeliveryReplies).mockRejectedValue(new Error('409'))
    const { Wrapper } = wrapper()
    const { result } = renderHook(() => ({ q: useDeliveryReplyKey(), set: useSetDeliveryReplies() }), { wrapper: Wrapper })
    await waitFor(() => expect(result.current.q.data).toEqual({ configured: false, enabled: false }))

    await expect(result.current.set.mutateAsync(true)).rejects.toThrow()
    await waitFor(() => expect(api.adminGetDeliveryReplyKey).toHaveBeenCalledTimes(2))
    expect(api.adminSetDeliveryReplies).toHaveBeenCalledWith({ enabled: true })
  })

  it('hands the generated key back and never caches it', async () => {
    vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: true, enabled: false })
    vi.mocked(api.adminGenerateDeliveryReplyKey).mockResolvedValue({ key: 'k' })
    const { Wrapper, client } = wrapper()
    const { result } = renderHook(() => useGenerateDeliveryKey(), { wrapper: Wrapper })
    await expect(result.current.mutateAsync()).resolves.toEqual({ key: 'k' })
    expect(client.getMutationCache().getAll()).toHaveLength(0)
  })
})
```

`useDeliveryReplyKey.ts` :

```ts
import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api } from '../../../api.js'

/** The key the mail server presents to apply guests' replies at delivery, and the switch that
    opens that door — Administration > Application, `api/DeliveryReplyKey`. The key itself is
    returned once, by the generation, and never by the query. */
export interface DeliveryReplyKey {
  configured: boolean
  enabled: boolean
  createdAt?: string
  lastCallAt?: string
}

export interface DeliveryReplyKeyGenerated {
  key: string
}

const DELIVERY_KEY = ['adminDeliveryReplyKey'] as const

// The generated key lives in the mutation's variables/data: gone from the cache the moment the
// dialog stops observing it.
const FORGET_KEY = { gcTime: 0 } as const

export function useDeliveryReplyKey() {
  return useQuery<DeliveryReplyKey>({
    queryKey: DELIVERY_KEY,
    queryFn: () => api.adminGetDeliveryReplyKey(),
  })
}

// onSettled, not onSuccess: a refused write must leave the screen on server state.
function refresh(client: QueryClient) {
  return () => { client.invalidateQueries({ queryKey: DELIVERY_KEY }) }
}

export function useGenerateDeliveryKey() {
  const client = useQueryClient()
  return useMutation({
    ...FORGET_KEY,
    mutationFn: () => api.adminGenerateDeliveryReplyKey() as Promise<DeliveryReplyKeyGenerated>,
    onSettled: refresh(client),
  })
}

export function useSetDeliveryReplies() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (enabled: boolean) => api.adminSetDeliveryReplies({ enabled }),
    onSettled: refresh(client),
  })
}

export function useDeleteDeliveryKey() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => api.adminDeleteDeliveryReplyKey(),
    onSettled: refresh(client),
  })
}
```

`deliveryReplyKeyErrors.ts` — le modèle de `schedulingAccountErrors.ts` :

```ts
import type { TFunction } from 'i18next'
import { ApiError } from '../../../api.js'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'

/** A mapped code goes through `apiErrorMessage`; a 404 (the key deleted from elsewhere) says so. */
export function deliveryErrorMessage(err: unknown, t: TFunction<'admin'>, fallback: string): string {
  const mapped = apiErrorMessage(err, '')
  if (mapped) return mapped
  if (err instanceof ApiError && err.status === 404) return t('deliveryReplies.keyGone', { ns: 'admin' })
  return fallback
}
```

- [ ] **Step 5 : vérifier, commit**

Run : `cd src/frontend && npm run lint && npm run typecheck && npm test -- useDeliveryReplyKey keys parity`
Expected : vert.

```bash
git add src/frontend/src/api.js src/frontend/src/lib/apiErrorMessage.ts src/frontend/src/locales src/frontend/src/modules/settings/admin/useDeliveryReplyKey.ts src/frontend/src/modules/settings/admin/useDeliveryReplyKey.test.ts src/frontend/src/modules/settings/admin/deliveryReplyKeyErrors.ts
git commit -F - <<'EOF'
Calendar 5e3: client API, hook and locales for the delivery key
EOF
```

---

### 10. la section, la fenêtre de la clé et leurs tests

**Files :**
- Create : `src/frontend/src/modules/settings/admin/DeliveryRepliesSection.tsx`, `DeliveryKeyDialog.tsx`
- Modify : `src/frontend/src/modules/settings/admin/ApplicationTab.tsx` (rendre `<DeliveryRepliesSection addToast={addToast} />` sous `<SchedulingAccountSection …/>`)
- Modify : `src/frontend/src/index.css` (après le bloc `.svc-account-*`, l. ~1206-1230)
- Test : `src/frontend/src/modules/settings/admin/DeliveryRepliesSection.test.tsx`

**Interfaces :**
- Consumes : les hooks et clés de la partie 9 ; `DeleteConfirmModal` (`title`, `confirmLabel`, `message`) ; `useDialogFocusTrap` (celui de `SchedulingAccountDialog.tsx` — lire ce fichier pour l'appel exact) ; `dateFormat` de `lib/intl` ; `ShieldAlertIcon`, `ShieldCheckIcon`, `PencilIcon`… (`icons/`).
- La maquette validée (paquet D) fait foi sur l'aspect ; ce qui suit est la structure.

- [ ] **Step 1 : les tests rouges**

```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from '../../../api.js'
import DeliveryRepliesSection from './DeliveryRepliesSection'

vi.mock('../../../api.js', () => ({ api: {
  adminGetDeliveryReplyKey: vi.fn(), adminGenerateDeliveryReplyKey: vi.fn(),
  adminSetDeliveryReplies: vi.fn(), adminDeleteDeliveryReplyKey: vi.fn(),
}, ApiError: class extends Error { status = 0 } }))

function mount() {
  const addToast = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><DeliveryRepliesSection addToast={addToast} /></QueryClientProvider>)
  return addToast
}

describe('DeliveryRepliesSection', () => {
  beforeEach(() => { vi.clearAllMocks() })

  it('without a key: generate is offered, the switch is disabled', async () => {
    vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: false, enabled: false })
    mount()
    expect(await screen.findByRole('button', { name: 'Generate a key' })).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'Apply replies at delivery' })).toBeDisabled()
  })

  it('generating shows the key once, with Copy', async () => {
    vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: false, enabled: false })
    vi.mocked(api.adminGenerateDeliveryReplyKey).mockResolvedValue({ key: 'abc-key' })
    const addToast = mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Generate a key' }))
    const dialog = await screen.findByRole('dialog', { name: 'Delivery key' })
    expect(dialog).toHaveTextContent('abc-key')
    Object.assign(navigator, { clipboard: { writeText: vi.fn().mockResolvedValue(undefined) } })
    await userEvent.click(screen.getByRole('button', { name: 'Copy' }))
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith('abc-key')
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Key copied.'))
  })

  it('with a key and no call: the dates, the switch off, regenerate behind a confirmation', async () => {
    vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: true, enabled: false, createdAt: '2026-09-14T16:00:00Z' })
    vi.mocked(api.adminGenerateDeliveryReplyKey).mockResolvedValue({ key: 'new-key' })
    mount()
    expect(await screen.findByText('No call received since the key was created')).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'Apply replies at delivery' })).not.toBeChecked()
    await userEvent.click(screen.getByRole('button', { name: 'Regenerate' }))
    expect(screen.getByText('Regenerate the key?')).toBeInTheDocument()
    expect(api.adminGenerateDeliveryReplyKey).not.toHaveBeenCalled()
  })

  it('with a call received: the pill; the switch writes and toasts', async () => {
    vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: true, enabled: true, createdAt: '2026-09-14T16:00:00Z', lastCallAt: '2026-09-14T16:40:00Z' })
    vi.mocked(api.adminSetDeliveryReplies).mockResolvedValue(undefined)
    const addToast = mount()
    expect(await screen.findByText(/Last call received on/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('checkbox', { name: 'Apply replies at delivery' }))
    expect(api.adminSetDeliveryReplies).toHaveBeenCalledWith({ enabled: false })
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Replies are now applied when the mail is opened.'))
  })

  it('deleting asks first, then toasts', async () => {
    vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: true, enabled: false, createdAt: '2026-09-14T16:00:00Z' })
    vi.mocked(api.adminDeleteDeliveryReplyKey).mockResolvedValue(undefined)
    const addToast = mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Delete' }))
    await userEvent.click(screen.getAllByRole('button', { name: 'Delete' }).at(-1)!)
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('The key was deleted.'))
  })
})
```

(Les libellés `Delete` viennent du namespace `common` — vérifier `actions.delete` dans `locales/en/common.json` et adapter.)

- [ ] **Step 2 : la fenêtre**

`DeliveryKeyDialog.tsx` :

```tsx
import { useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useDialogFocusTrap } from '../../../hooks/useDialogFocusTrap'

interface Props {
  keyValue: string
  addToast: (message: string, kind?: string) => void
  onClose: () => void
}

/** The key, once. Read-only field so it can be selected by hand when the clipboard is refused. */
export default function DeliveryKeyDialog({ keyValue, addToast, onClose }: Props) {
  const { t } = useTranslation('admin')
  const dialogRef = useRef<HTMLDivElement>(null)
  useDialogFocusTrap(dialogRef, onClose)

  async function copy() {
    try {
      await navigator.clipboard.writeText(keyValue)
      addToast(t('deliveryReplies.copied'))
    } catch {
      addToast(t('deliveryReplies.copyFailed'), 'error')
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" role="dialog" aria-modal="true" aria-labelledby="dlv-key-title" ref={dialogRef}
        onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title" id="dlv-key-title">{t('deliveryReplies.dialogTitle')}</span>
          <button type="button" className="modal-close" onClick={onClose} aria-label={t('actions.close', { ns: 'common' })}>✕</button>
        </div>
        <p className="dlv-key-intro">{t('deliveryReplies.dialogIntro')}</p>
        <div className="dlv-key-row">
          <input className="dlv-key-value" type="text" readOnly value={keyValue} onFocus={e => e.currentTarget.select()} />
          <button type="button" className="btn btn-primary btn-auto" onClick={() => void copy()}>{t('deliveryReplies.copy')}</button>
        </div>
      </div>
    </div>
  )
}
```

(Adapter l'import et la signature de `useDialogFocusTrap` à `SchedulingAccountDialog.tsx` ; `actions.close` : vérifier la clé dans `common.json`.)

- [ ] **Step 3 : la section**

`DeliveryRepliesSection.tsx` — le squelette de `SchedulingAccountSection.tsx` :

```tsx
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import LoadingBlock from '../../../components/LoadingBlock'
import RefreshIcon from '../../../icons/RefreshIcon.jsx'
import ShieldAlertIcon from '../../../icons/ShieldAlertIcon'
import ShieldCheckIcon from '../../../icons/ShieldCheckIcon'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import { dateFormat } from '../../../lib/intl'
import DeliveryKeyDialog from './DeliveryKeyDialog'
import { deliveryErrorMessage } from './deliveryReplyKeyErrors'
import {
  useDeleteDeliveryKey, useDeliveryReplyKey, useGenerateDeliveryKey, useSetDeliveryReplies,
} from './useDeliveryReplyKey'

interface Props {
  addToast: (message: string, kind?: string) => void
}

const day = () => dateFormat({ day: 'numeric', month: 'short' })
const stamp = () => dateFormat({ day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

/**
 * The key the mail server presents to apply guests' replies at delivery, and the switch (spec
 * 5e3). No "Test" button, unlike the sending account above: a test from here would prove the door
 * answers, not that Dovecot calls it — the last-call date is the only honest signal (décision 8).
 */
export default function DeliveryRepliesSection({ addToast }: Props) {
  const { t } = useTranslation('admin')
  const { data: key, isLoading, isError } = useDeliveryReplyKey()
  const generate = useGenerateDeliveryKey()
  const setEnabled = useSetDeliveryReplies()
  const remove = useDeleteDeliveryKey()
  const [shownKey, setShownKey] = useState<string | null>(null)
  const [confirming, setConfirming] = useState<'regenerate' | 'delete' | null>(null)

  async function runGenerate() {
    try {
      const { key: fresh } = await generate.mutateAsync()
      setConfirming(null)
      setShownKey(fresh)
      addToast(t('deliveryReplies.generated'))
    } catch (err) {
      addToast(deliveryErrorMessage(err, t, t('deliveryReplies.generateFailed')), 'error')
    }
  }

  async function toggle(enabled: boolean) {
    try {
      await setEnabled.mutateAsync(enabled)
      addToast(t(enabled ? 'deliveryReplies.enabled' : 'deliveryReplies.disabled'))
    } catch (err) {
      addToast(deliveryErrorMessage(err, t, t('deliveryReplies.toggleFailed')), 'error')
    }
  }

  async function confirmDelete() {
    try {
      await remove.mutateAsync()
      addToast(t('deliveryReplies.deleted'))
    } catch (err) {
      addToast(deliveryErrorMessage(err, t, t('deliveryReplies.deleteFailed')), 'error')
    } finally {
      setConfirming(null)
    }
  }

  function renderCard() {
    if (isLoading) return <LoadingBlock />
    if (isError || !key) return <p>{t('deliveryReplies.loadFailed')}</p>
    const configured = key.configured
    return (
      <div className={configured ? 'admin-list-item svc-account-card' : 'admin-list-item svc-account-card is-empty'}>
        {configured ? <ShieldCheckIcon /> : <ShieldAlertIcon />}
        <div className="svc-account-info">
          <div className="field-h is-setting dlv-toggle">
            <label htmlFor="dlv-enabled">{t('deliveryReplies.toggle')}</label>
            <label className="toggle-switch">
              <input id="dlv-enabled" type="checkbox" checked={key.enabled} disabled={!configured || setEnabled.isPending}
                onChange={e => void toggle(e.target.checked)} />
              <span className="toggle-track" />
            </label>
          </div>
          {configured ? (
            <div className="svc-account-meta">
              {t('deliveryReplies.createdOn', { date: day().format(new Date(key.createdAt!)) })}
            </div>
          ) : <p className="svc-account-empty-text">{t('deliveryReplies.noKeyHint')}</p>}
        </div>
        {configured && (key.lastCallAt
          ? <span className="svc-account-pill is-ok">{t('deliveryReplies.lastCallOn', { date: stamp().format(new Date(key.lastCallAt)) })}</span>
          : <span className="svc-account-pill">{t('deliveryReplies.noCall')}</span>)}
        <div className="admin-list-item-actions">
          {configured ? (
            <>
              <button type="button" className="admin-icon-btn" title={t('deliveryReplies.regenerate')}
                aria-label={t('deliveryReplies.regenerate')} onClick={() => setConfirming('regenerate')}>
                <RefreshIcon />
              </button>
              <button type="button" className="admin-icon-btn is-danger" title={t('actions.delete', { ns: 'common' })}
                aria-label={t('actions.delete', { ns: 'common' })} onClick={() => setConfirming('delete')}>
                <TrashIcon />
              </button>
            </>
          ) : (
            <button type="button" className="btn btn-primary btn-auto" disabled={generate.isPending} onClick={() => void runGenerate()}>
              {t('deliveryReplies.generate')}
            </button>
          )}
        </div>
      </div>
    )
  }

  return (
    <div className="svc-account-section">
      <h2 className="svc-account-section-title">{t('deliveryReplies.title')}</h2>
      <p className="svc-account-section-intro">{t('deliveryReplies.intro')}</p>
      <div className="svc-account-slot">{renderCard()}</div>

      {shownKey !== null && (
        <DeliveryKeyDialog keyValue={shownKey} addToast={addToast} onClose={() => setShownKey(null)} />
      )}
      {confirming === 'regenerate' && (
        <DeleteConfirmModal title={t('deliveryReplies.regenerateTitle')} confirmLabel={t('deliveryReplies.regenerate')}
          message={t('deliveryReplies.regenerateMessage')} loading={generate.isPending}
          onConfirm={() => void runGenerate()} onClose={() => setConfirming(null)} />
      )}
      {confirming === 'delete' && (
        <DeleteConfirmModal entityLabel={t('deliveryReplies.keyLabel')} message={t('deliveryReplies.deleteMessage')}
          loading={remove.isPending} onConfirm={() => void confirmDelete()} onClose={() => setConfirming(null)} />
      )}
    </div>
  )
}
```

`RefreshIcon` : vérifier qu'il existe dans `icons/` (il sert au bouton Refresh de la colonne mail) ; sinon prendre l'icône que la maquette a retenue. Le bouton Régénérer de `DeleteConfirmModal` est rouge par construction : acceptable, c'est une action qui révoque.

CSS, après le bloc `.svc-account-*` :

```css
.dlv-toggle { margin: 0 0 4px; }
.dlv-key-intro { font-size: 13px; color: var(--text-muted); margin: 0 0 12px; }
.dlv-key-row { display: flex; gap: 8px; align-items: center; }
.dlv-key-value { flex: 1; min-width: 0; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 13px; }
```

Dans `ApplicationTab.tsx`, sous `<SchedulingAccountSection addToast={addToast} />` : `<DeliveryRepliesSection addToast={addToast} />` (import en tête).

- [ ] **Step 4 : vérifier, commit**

Run : `cd src/frontend && npm run lint && npm run typecheck && npm test`
Expected : tout vert (3464 + les nouveaux).

```bash
git add src/frontend/src/modules/settings/admin src/frontend/src/index.css
git commit -F - <<'EOF'
Calendar 5e3: delivery replies section in Administration
EOF
```

---

## Paquet F — Les docs — le prérequis serveur et les documents du dépôt

Un sous-agent ; en fin, après C et E.

### 11. le prérequis serveur (Dovecot, Sieve, script, nginx)

**Files :**
- Create : `docs/superpowers/webmail-delivery-replies-prerequisite.md`

Le contenu ci-dessous est le document, dans son intégralité ; le relire contre § « Le serveur mail » de la spec avant de committer.

````markdown
# Prérequis serveur — réponses d'invités appliquées à la livraison (5e3)

Dovecot 2.4.5 (Pigeonhole). **Rien n'est à activer côté webmail tant que la procédure de
vérification (§ Avant d'activer) n'est pas passée** : un programme Sieve qui échoue fait sauter
les règles personnelles de tous les utilisateurs, sans signal.

Prérequis : la table `delivery_reply_key` (`webmail-delivery-reply-key-table.md`) et l'`ALTER`
de `calendar_revisions` (`webmail-calendar-tables.md` § 5e3), sur les deux bases.

## 1. Dovecot

Dans la configuration (`doveconf -n` doit montrer le bloc après édition) :

```
sieve_plugins {
  sieve_extprograms = yes
}
sieve_global_extensions {
  vnd.dovecot.execute = yes
}
sieve_execute_bin_dir = /usr/lib/dovecot/sieve-execute
sieve_script calendar_replies {
  type = before
  driver = file
  path = /etc/dovecot/sieve/calendar-replies.sieve
  bin_path = /var/lib/dovecot/sieve/calendar-replies.svbin
}
```

`vnd.dovecot.execute` **dans `sieve_global_extensions` seulement, jamais dans `sieve_extensions`** :
les utilisateurs écrivent leurs règles depuis l'onglet Règles du webmail, et cette extension leur
donnerait le droit de faire exécuter un programme par le serveur. `mime` et `foreverypart` sont
actives par défaut ; si `sievec` refuse la règle sur l'une d'elles, ajouter
`sieve_extensions { mime = yes; foreverypart = yes }`.

`/var/lib/dovecot/sieve/` doit exister et appartenir à `vmail` (le `.svbin` s'y dépose, sinon
Dovecot recompile à chaque livraison).

## 2. La règle — `/etc/dovecot/sieve/calendar-replies.sieve`

```
require ["mime", "foreverypart", "vnd.dovecot.execute"];
if size :under 5M {
  foreverypart {
    if allof (
      anyof (
        allof (header :mime :type "Content-Type" "text",
               header :mime :subtype "Content-Type" "calendar"),
        allof (header :mime :type "Content-Type" "application",
               header :mime :subtype "Content-Type" "ics")),
      header :mime :param "method" "Content-Type" "REPLY") {
      execute :pipe "calendar-reply";
      break;
    }
  }
}
```

Compiler : `sievec /etc/dovecot/sieve/calendar-replies.sieve /var/lib/dovecot/sieve/calendar-replies.svbin`
puis `chown vmail /var/lib/dovecot/sieve/calendar-replies.svbin`. **À refaire à chaque modification.**
La règle n'arrête rien (ni `stop` ni `discard`) : les règles de l'utilisateur s'appliquent ensuite.

## 3. Le fichier de configuration — `/etc/dovecot/calendar-reply.conf`

`root:vmail`, mode `0640`. Contenu exact (une option `curl` par ligne) :

```
url = "https://<ip-ou-hôte-du-microservice>/api/Delivery/CalendarReplies"
header = "X-Delivery-Key: <la clé générée dans Administration>"
header = "Content-Type: message/rfc822"
connect-timeout = 2
max-time = 5
silent
output = "/dev/null"
```

Adresse : une IP littérale ou une entrée `/etc/hosts`, pas un nom à résoudre à chaque livraison.
Tout processus tournant sous `vmail` peut lire ce fichier ; ce que la clé permet est borné (spec
5e3, décision 12 : réécrire la réponse d'un invité déjà présent, rien d'autre).

## 4. Le script — `/usr/lib/dovecot/sieve-execute/calendar-reply`

`root:root`, mode `0755`. **L'ordre des instructions est la sécurité** : tout stdin est lu avant
toute décision, sinon Dovecot fait échouer l'action.

```sh
#!/bin/sh
# Applies a guest's calendar REPLY at delivery through the webmail's internal door (5e3).
# Always exits 0: a failed Sieve action skips the user's own rules for this mail.
tmp=$(mktemp) || exit 0
trap 'rm -f "$tmp"' EXIT
cat > "$tmp"                                   # read everything first, unconditionally
[ -n "$USER" ] || exit 0                        # LDA without a user: nothing to do
[ "$(wc -c < "$tmp")" -le 5242880 ] || exit 0   # belt under the rule's size :under 5M
timeout 8 curl --config /etc/dovecot/calendar-reply.conf \
  -H "X-Delivery-Mailbox: $USER" --data-binary "@$tmp" >/dev/null 2>&1
exit 0
```

`timeout` (coreutils) borne tout, résolution comprise, sous les 10 s de
`sieve_execute_exec_timeout` ; `--max-time` seul ne borne que le transfert.

## 5. nginx

```
location /api/Delivery/ {
    allow <ip du serveur mail>;
    deny all;
    client_max_body_size 6m;   # le défaut, 1 Mo, refuserait une réponse plus grosse
    # … le proxy_pass et les en-têtes du bloc /api/ existant
}
```

## 6. Avant d'activer — la procédure

1. `sievec` sans erreur ; `doveconf -n | grep -A4 calendar_replies` montre le bloc.
2. Depuis le serveur mail, sous `vmail` : `curl -v --config /etc/dovecot/calendar-reply.conf
   -H "X-Delivery-Mailbox: <une boîte>" --data-binary @<un mail qui n'est PAS une réponse>`
   → `404` tant que le réglage est désactivé (la clé et l'adresse sont justes si le journal du
   service porte « Delivery calls refused … door closed »), `200` avec `NotAReply` une fois activé.
   **Jamais une vraie réponse ici** : sur une porte activée, elle s'appliquerait.
3. Dans Administration > Application : activer l'interrupteur.
4. Une livraison réelle d'une réponse d'invité (répondre depuis Gmail ou Outlook.com à une
   invitation du webmail) : l'agenda est à jour **avant** d'ouvrir le mail, la carte affiche
   « Dernier appel reçu le … », le journal porte `Applied`. Puis vérifier qu'une règle
   personnelle de cet utilisateur (un tri en dossier) s'est encore appliquée sur ce mail.
5. **Le microservice arrêté** (`systemctl stop snoopy.microservice`), une seconde réponse : le
   mail arrive, la règle personnelle s'applique encore, le journal Dovecot ne porte aucune
   erreur `execute`. Redémarrer le service ; ouvrir le mail applique la réponse en secours.

## 7. Identifiant de la boîte

`USER` est l'identifiant Dovecot du propriétaire de la boîte ; il doit être l'adresse avec
laquelle l'utilisateur se connecte au webmail. Un utilisateur qui se connecte par un alias a sa
ligne `users` sur l'alias : la livraison répond `UnknownMailbox` à chaque fois (visible dans le
journal), et ses réponses ne s'appliquent qu'à l'ouverture.

## 8. Retour arrière

Désactiver l'interrupteur dans Administration suffit : la porte répond 404, le script sort en 0,
la règle peut rester. La version précédente du service ne connaît pas la table ni la route.
````

- [ ] **Step 1 :** écrire le fichier tel quel ; commit.

```bash
git add docs/superpowers/webmail-delivery-replies-prerequisite.md
git commit -F - <<'EOF'
Calendar 5e3: Dovecot, Sieve, script and nginx prerequisite
EOF
```

---

### 12. les documents du dépôt

**Files :**
- Modify : `src/snoopy.microservice/CLAUDE.md` (§ Controllers : deux entrées ; § « The platform seam » l. 81 : ajouter `/api/DeliveryReplyKey` aux routes `Admin`)
- Modify : `src/snoopy.microservice/DESIGN.md` (une section « Delivery door » : la porte, la clé, le limiteur, CORS)
- Modify : `docs/superpowers/reverse-proxy-prerequisite.md` (renvoi vers le § 5 du prérequis 5e3)
- Modify : `docs/superpowers/specs/2026-09-14-webmail-calendar-5e3-delivery-replies-design.md` (une ligne « livrée par … » en tête, comme 5e)
- Create : `docs/superpowers/calendar-5e3-residuals.md`

- [ ] **Step 1 : `CLAUDE.md`**

Dans la liste des contrôleurs, après `AliasesController` :

```markdown
- `DeliveryReplyKeyController` — admin-only `GET/POST/PUT/DELETE /api/DeliveryReplyKey`: the key the mail server presents at delivery, generated here and shown once (SHA-256 stored), and the switch that opens the door
- `DeliveryController` — `POST /api/Delivery/CalendarReplies`, **anonymous by design**: Dovecot's Sieve script posts a raw mail with `X-Delivery-Key` and `X-Delivery-Mailbox`, and a guest's REPLY is applied into the mailbox owner's calendar under `RevisionCause.Delivery`. A wrong key and a closed door are both a bare 404 (the door must not say whether it exists); `X-Delivery-Key` must never enter the CORS `WithHeaders` list, which is what keeps a browser out; the `delivery` rate-limit policy is a concurrency limiter, not a per-address window (every legitimate call comes from one address). See `docs/superpowers/webmail-delivery-replies-prerequisite.md`
```

Ligne 81 : `the four `api/SchedulingAccount` verbs (…)` → ajouter `, the four `api/DeliveryReplyKey` verbs (the delivery key — so on such a platform the delivery door always answers 404)`.

- [ ] **Step 2 : `DESIGN.md`** — ajouter en fin une section courte reprenant décisions 7, 12 et 13 de la spec (cinq à dix lignes, renvoi à la spec).

- [ ] **Step 3 : les résidus** — `docs/superpowers/calendar-5e3-residuals.md`, tableau au format de `calendar-5e2-residuals.md`, avec au moins :

| Résidu |
|---|
| Avec deux instances du service, l'autre ne voit une clé régénérée qu'au redémarrage : le fournisseur est un cache par processus (même limite que le compte d'envoi) |
| Rien ne dit à l'administrateur qu'un serveur mail appelle avec une mauvaise clé après un premier succès : pas de date du dernier refus (décision 8, écarté) |
| Une réponse enfermée dans un mail transféré n'est appliquée par aucun chemin (comme à l'ouverture) |
| Un utilisateur qui se connecte au webmail par un alias n'a jamais ses réponses appliquées à la livraison (`UnknownMailbox`) : pas de résolution d'alias côté livraison |
| Le ruling 22 (réponse forgée) s'applique désormais sans qu'un utilisateur voie la carte : consenti par l'administrateur à l'activation (décision 4) |

Plus ce que les relectures de paquet auront laissé ouvert.

- [ ] **Step 4 : commit**

```bash
git add src/snoopy.microservice/CLAUDE.md src/snoopy.microservice/DESIGN.md docs/superpowers
git commit -F - <<'EOF'
Calendar 5e3: repository docs and residuals
EOF
```

---

## Recette réelle (utilisateur)

Après déploiement sur dev : la procédure du prérequis, ses cinq étapes ; une invitation à Hotmail et à Gmail ; le réglage désactivé, la réponse s'applique à l'ouverture ; une clé régénérée sans mettre à jour le serveur (« Aucun appel reçu » sur la carte, refus dans le journal, rattrapage à l'ouverture, règles personnelles toujours appliquées) ; Thunderbird si un compte est disponible.
