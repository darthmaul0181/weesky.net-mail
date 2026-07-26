# Trusted senders Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a reader approve one sender's remote images once, from the message that raised the question, and revoke it the same way.

**Architecture:** A `trusted_senders` table in the `snoopy_webmail` database, capped per account, exposed by a small REST controller. The trust check runs **client-side in the reader** — the sanitiser is never told about it, so a message body stays one document for every account and the message cache never depends on the list. `last_used` is refreshed server-side by piggybacking on the message-detail read, and a daily sweeper drops entries nobody has used within the retention.

**Tech Stack:** ASP.NET Core .NET 10, EF Core (Pomelo MySQL), xUnit + Moq + EF Core InMemory, React 18 + TypeScript, TanStack Query, Vitest + @testing-library/react.

**Spec:** `docs/superpowers/specs/2026-07-26-webmail-trusted-senders-design.md`

## Global Constraints

- **Passwords are never hashed server-side.** Not touched by this work, but the rule stands: MariaDB triggers encrypt. Never call a hashing function on a password.
- **No EF migrations.** Every table in `snoopy_webmail` is created by hand. New tables ship with a prerequisite doc under `docs/superpowers/` and must be applied to **both** `snoopy_webmail` and `snoopy_webmail_dev` before deploy.
- **Addresses are stored canonical** — `IdentityResolver.Canonical(address)` = `Trim().ToLowerInvariant()`. Reuse it; never write a second folding rule. The table collates `utf8mb4_bin`, so a casing difference splits one sender into two rows.
- **UI copy is English.** Exact strings, character for character: `Always show images from this sender`, `Block sender's images`, `Trust my contacts`, `Available once Contacts ships.`
- **Structured logging only.** `ILogger` with message templates, never string interpolation.
- **Cancellation tokens on every async method.**
- **C# style:** file-scoped namespaces, one type per file, primary constructors for DI, `sealed`, `internal` by default, collection expressions, no `_` prefix on primary-constructor fields.
- **`Assert.IsType<T>` is an exact type check.** `BadRequest(body)` returns `BadRequestObjectResult`; the `FromResult()`/`StatusCode()` helpers return a plain `ObjectResult`. Match the one the code actually returns.
- **Run `dotnet test`, not `dotnet test --no-build`**, whenever a task adds a new test file.
- **Do not commit `.claude/settings.local.json`.** It is modified in the working tree and stays out of every commit. Stage files explicitly, never `git add -A`.
- **Do not push.** Commit only; the repository owner pushes.

---

## File Structure

**Backend — create**
- `Data/Preferences/TrustedSender.cs` — the entity, one row per approved address.
- `Repositories/ITrustedSenderStore.cs` — the store contract.
- `Repositories/TrustedSenderStore.cs` — list / add (capped) / remove / touch / sweep.
- `Controllers/TrustedSendersController.cs` — the three verbs.
- `Models/TrustedSenderRequest.cs` — the POST body.
- `Models/TrustedSenderOptions.cs` — `RetentionDays`.
- `Services/TrustedSenderSweeper.cs` — the daily background sweep.
- `docs/superpowers/webmail-trusted-senders-table.md` — manual DDL prerequisite.

**Backend — modify**
- `Data/Preferences/PreferencesDbContext.cs` — key + `DbSet`.
- `Configuration/ApplicationServicesConfiguration.cs` — store registration, options binding, hosted service.
- `Controllers/MailController.cs` — the `last_used` touch on message detail.
- `appsettings.json` — the `TrustedSenders` section.

**Frontend — create**
- `src/icons/ChevronDownIcon.tsx`
- `src/icons/ImageOffIcon.tsx`
- `src/modules/mail/reader/canonicalAddress.ts` — the client half of the folding rule.

**Frontend — modify**
- `src/api.js` — three methods.
- `src/modules/mail/queries.ts` — query key, `useTrustedSenders`, `useTrustSender`.
- `src/modules/mail/reader/MessageReader.tsx` — derived consent, split button, kebab entry.
- `src/styles/mail.css` — `.banner-split` / `.banner-split-more`.
- `src/modules/settings/general/GeneralPage.tsx` — the disabled Contacts row.

---

## Task 1: The table, the entity and the store

**Files:**
- Create: `src/snoopy.microservice/Data/Preferences/TrustedSender.cs`
- Create: `src/snoopy.microservice/Repositories/ITrustedSenderStore.cs`
- Create: `src/snoopy.microservice/Repositories/TrustedSenderStore.cs`
- Create: `docs/superpowers/webmail-trusted-senders-table.md`
- Modify: `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs`
- Modify: `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs:75-89`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/TrustedSenderStoreTests.cs`

**Interfaces:**
- Consumes: `PreferencesDbContext`, `IdentityResolver.Canonical(string)`, `PreferencesTestDbContext(string dbName)`.
- Produces:
  - `TrustedSender { Guid UserId; string Address; DateTime LastUsed }`
  - `ITrustedSenderStore.ListAsync(Guid, CancellationToken) -> Task<IReadOnlyList<string>>`
  - `ITrustedSenderStore.AddAsync(Guid, string, CancellationToken) -> Task<Result>`
  - `ITrustedSenderStore.RemoveAsync(Guid, string, CancellationToken) -> Task`
  - `ITrustedSenderStore.TouchAsync(Guid, string, CancellationToken) -> Task`
  - `ITrustedSenderStore.SweepExpiredAsync(TimeSpan, CancellationToken) -> Task<int>`
  - `TrustedSenderStore.MaxPerAccount = 1000`, `TrustedSenderStore.CapReached` (message constant)

- [ ] **Step 1: Write the DDL prerequisite doc**

Create `docs/superpowers/webmail-trusted-senders-table.md`:

````markdown
# Prérequis serveur — table `trusted_senders`

**À appliquer sur les deux bases** (`snoopy_webmail` et `snoopy_webmail_dev`) avant de déployer
le backend qui expose `/api/TrustedSenders`.

Le projet n'utilise pas les migrations EF : la création des tables est manuelle, comme pour
`sending_identities` (voir `webmail-users-table.md`).

## Pourquoi cette table

Les expéditeurs dont l'utilisateur a accepté les images distantes une fois pour toutes. La liste
se construit un clic à la fois depuis le lecteur et se révoque au même endroit ; aucun écran de
gestion ne l'expose.

`last_used` est rafraîchie à l'ouverture d'un message de cet expéditeur, au plus une fois par
jour. Un balayage quotidien supprime les entrées dépassant `TrustedSenders:RetentionDays`
(365 par défaut). Ce n'est pas ce qui borne la table : c'est le plafond de 1 000 lignes par
compte, appliqué par `TrustedSenderStore`.

## Script

```sql
CREATE TABLE IF NOT EXISTS `snoopy_webmail`.`trusted_senders` (
  `user_id`   CHAR(36)     NOT NULL,
  `address`   VARCHAR(320) NOT NULL COMMENT 'Forme canonique minuscule ; 320 = max RFC 5321',
  `last_used` DATETIME     NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma',
  PRIMARY KEY (`user_id`, `address`),
  CONSTRAINT `fk_trusted_senders_user`
    FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS `snoopy_webmail_dev`.`trusted_senders` (
  `user_id`   CHAR(36)     NOT NULL,
  `address`   VARCHAR(320) NOT NULL COMMENT 'Forme canonique minuscule ; 320 = max RFC 5321',
  `last_used` DATETIME     NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma',
  PRIMARY KEY (`user_id`, `address`),
  CONSTRAINT `fk_trusted_senders_user`
    FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

Aucun `GRANT` à rejouer : les utilisateurs `snoopy_webmail`/`snoopy_webmail_dev` ont déjà
`SELECT, INSERT, UPDATE, DELETE` sur toute la base.

**Pas de `DEFAULT CURRENT_TIMESTAMP` sur `last_used`**, pour la même raison que
`users.creation_date` n'en a pas : la valeur appartient au code, donc une lecture ne peut jamais
la déplacer.

## Vérification

```sql
SELECT TABLE_NAME, TABLE_COLLATION FROM information_schema.TABLES
 WHERE TABLE_SCHEMA = 'snoopy_webmail' AND TABLE_NAME = 'trusted_senders';
-- attendu : trusted_senders | utf8mb4_bin
```

## Désinstallation

```sql
DROP TABLE IF EXISTS `snoopy_webmail`.`trusted_senders`;
DROP TABLE IF EXISTS `snoopy_webmail_dev`.`trusted_senders`;
```

La perdre remet chaque compte au blocage par message. Aucun message n'est concerné.
````

- [ ] **Step 2: Write the entity**

Create `src/snoopy.microservice/Data/Preferences/TrustedSender.cs`:

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One sender whose remote images this account loads without asking. Addresses are stored
/// canonical (trimmed, lower-case): the table collates in binary, so a casing difference would
/// split one sender into two rows and the second would silently never match.
/// </summary>
[Table("trusted_senders")]
public sealed class TrustedSender
{
    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("address")]
    public string Address { get; set; } = string.Empty;

    [Column("last_used")]
    public DateTime LastUsed { get; set; }
}
```

- [ ] **Step 3: Register the entity on the context**

In `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs`, add inside `OnModelCreating` after the `SendingIdentity` line:

```csharp
        modelBuilder.Entity<TrustedSender>().HasKey(t => new { t.UserId, t.Address });
```

and after the `SendingIdentities` property:

```csharp
    public DbSet<TrustedSender> TrustedSenders { get; set; }
```

- [ ] **Step 4: Write the failing store tests**

Create `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/TrustedSenderStoreTests.cs`:

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class TrustedSenderStoreTests
{
    private static TrustedSenderStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName));

    [Fact]
    public async Task Add_ThenList_ReturnsTheAddress()
    {
        var db = nameof(Add_ThenList_ReturnsTheAddress);
        var user = Guid.NewGuid();

        var result = await CreateStore(db).AddAsync(user, "news@example.com", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("news@example.com",
            Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None)));
    }

    // The table collates binary. Folding on the way in is the only thing stopping one sender
    // from becoming two rows, the second of which would never match anything.
    [Fact]
    public async Task Add_FoldsCaseAndSurroundingSpace()
    {
        var db = nameof(Add_FoldsCaseAndSurroundingSpace);
        var user = Guid.NewGuid();

        await CreateStore(db).AddAsync(user, "  News@Example.COM ", CancellationToken.None);

        Assert.Equal("news@example.com",
            Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None)));
    }

    [Fact]
    public async Task Add_Twice_KeepsOneRow()
    {
        var db = nameof(Add_Twice_KeepsOneRow);
        var user = Guid.NewGuid();
        await CreateStore(db).AddAsync(user, "news@example.com", CancellationToken.None);

        await CreateStore(db).AddAsync(user, "NEWS@example.com", CancellationToken.None);

        Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
    }

    // The cap is what bounds the table, not the retention sweep: a TTL deletes after the fact
    // and bounds nothing in between.
    [Fact]
    public async Task Add_AtTheCap_IsRefused()
    {
        var db = nameof(Add_AtTheCap_IsRefused);
        var user = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        for (var i = 0; i < TrustedSenderStore.MaxPerAccount; i++)
        {
            context.TrustedSenders.Add(new TrustedSender
            {
                UserId = user, Address = $"sender{i}@example.com", LastUsed = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync(CancellationToken.None);

        var result = await CreateStore(db).AddAsync(user, "one-too-many@example.com", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TrustedSenderStore.CapReached, result.Error);
    }

    // Re-approving an address already stored must not be refused by the cap: it adds no row.
    [Fact]
    public async Task Add_AtTheCap_StillAcceptsAnAddressAlreadyStored()
    {
        var db = nameof(Add_AtTheCap_StillAcceptsAnAddressAlreadyStored);
        var user = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        for (var i = 0; i < TrustedSenderStore.MaxPerAccount; i++)
        {
            context.TrustedSenders.Add(new TrustedSender
            {
                UserId = user, Address = $"sender{i}@example.com", LastUsed = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync(CancellationToken.None);

        var result = await CreateStore(db).AddAsync(user, "sender0@example.com", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Remove_DropsTheRow()
    {
        var db = nameof(Remove_DropsTheRow);
        var user = Guid.NewGuid();
        await CreateStore(db).AddAsync(user, "news@example.com", CancellationToken.None);

        await CreateStore(db).RemoveAsync(user, "NEWS@Example.com", CancellationToken.None);

        Assert.Empty(await CreateStore(db).ListAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task Remove_UnknownAddress_IsNotAnError()
    {
        var db = nameof(Remove_UnknownAddress_IsNotAnError);
        var user = Guid.NewGuid();

        await CreateStore(db).RemoveAsync(user, "stranger@example.com", CancellationToken.None);

        Assert.Empty(await CreateStore(db).ListAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task List_IsScopedToTheAccount()
    {
        var db = nameof(List_IsScopedToTheAccount);
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await CreateStore(db).AddAsync(mine, "mine@example.com", CancellationToken.None);
        await CreateStore(db).AddAsync(theirs, "theirs@example.com", CancellationToken.None);

        Assert.Equal("mine@example.com",
            Assert.Single(await CreateStore(db).ListAsync(mine, CancellationToken.None)));
    }

    [Fact]
    public async Task Touch_MovesTheDateOnAStaleRow()
    {
        var db = nameof(Touch_MovesTheDateOnAStaleRow);
        var user = Guid.NewGuid();
        var seeded = new PreferencesTestDbContext(db);
        seeded.TrustedSenders.Add(new TrustedSender
        {
            UserId = user, Address = "news@example.com", LastUsed = DateTime.UtcNow.AddDays(-40)
        });
        await seeded.SaveChangesAsync(CancellationToken.None);

        await CreateStore(db).TouchAsync(user, "News@Example.com", CancellationToken.None);

        var row = Assert.Single(new PreferencesTestDbContext(db).TrustedSenders.ToList());
        Assert.Equal(DateTime.UtcNow.Date, row.LastUsed.Date);
    }

    // The reason the touch is affordable at all: one write a day per approved sender, not one
    // per message opened. Drop this and every reopen costs an UPDATE.
    [Fact]
    public async Task Touch_TwiceInADay_WritesOnlyOnce()
    {
        var db = nameof(Touch_TwiceInADay_WritesOnlyOnce);
        var user = Guid.NewGuid();
        var seeded = new PreferencesTestDbContext(db);
        var alreadyToday = DateTime.UtcNow.Date.AddHours(1);
        seeded.TrustedSenders.Add(new TrustedSender
        {
            UserId = user, Address = "news@example.com", LastUsed = alreadyToday
        });
        await seeded.SaveChangesAsync(CancellationToken.None);

        await CreateStore(db).TouchAsync(user, "news@example.com", CancellationToken.None);

        var row = Assert.Single(new PreferencesTestDbContext(db).TrustedSenders.ToList());
        Assert.Equal(alreadyToday, row.LastUsed);
    }

    // Every message opened goes through the touch. Creating a row here would approve a sender
    // the user never chose — the opposite of the whole feature.
    [Fact]
    public async Task Touch_CreatesNothingForAnUnapprovedSender()
    {
        var db = nameof(Touch_CreatesNothingForAnUnapprovedSender);
        var user = Guid.NewGuid();

        await CreateStore(db).TouchAsync(user, "stranger@example.com", CancellationToken.None);

        Assert.Empty(await CreateStore(db).ListAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task Sweep_RemovesPastTheRetentionAndSparesInsideIt()
    {
        var db = nameof(Sweep_RemovesPastTheRetentionAndSparesInsideIt);
        var user = Guid.NewGuid();
        var seeded = new PreferencesTestDbContext(db);
        seeded.TrustedSenders.Add(new TrustedSender
        {
            UserId = user, Address = "stale@example.com", LastUsed = DateTime.UtcNow.AddDays(-400)
        });
        seeded.TrustedSenders.Add(new TrustedSender
        {
            UserId = user, Address = "fresh@example.com", LastUsed = DateTime.UtcNow.AddDays(-10)
        });
        await seeded.SaveChangesAsync(CancellationToken.None);

        var removed = await CreateStore(db).SweepExpiredAsync(TimeSpan.FromDays(365), CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.Equal("fresh@example.com",
            Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None)));
    }
}
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~TrustedSenderStoreTests`
Expected: build failure — `TrustedSenderStore` and `ITrustedSenderStore` do not exist.

- [ ] **Step 6: Write the store contract**

Create `src/snoopy.microservice/Repositories/ITrustedSenderStore.cs`:

```csharp
using CSharpFunctionalExtensions;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// The senders whose remote images an account loads without being asked. Addresses go in and
/// come back canonical; callers never fold them themselves.
/// </summary>
internal interface ITrustedSenderStore
{
    Task<IReadOnlyList<string>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Adds, or refreshes an address already stored. Fails only when the cap is reached.</summary>
    Task<Result> AddAsync(Guid userId, string address, CancellationToken cancellationToken);

    /// <summary>Removes it. An address that is not stored is not an error.</summary>
    Task RemoveAsync(Guid userId, string address, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a stored address as still in use, at most once a day. Creates nothing: this runs
    /// for every message opened, approved sender or not.
    /// </summary>
    Task TouchAsync(Guid userId, string address, CancellationToken cancellationToken);

    /// <summary>Drops every row untouched for longer than <paramref name="retention"/>.</summary>
    Task<int> SweepExpiredAsync(TimeSpan retention, CancellationToken cancellationToken);
}
```

- [ ] **Step 7: Write the store**

Create `src/snoopy.microservice/Repositories/TrustedSenderStore.cs`:

```csharp
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class TrustedSenderStore(PreferencesDbContext context) : ITrustedSenderStore
{
    /// <summary>
    /// What actually bounds the table. The retention sweep deletes after the fact and bounds
    /// nothing in the meantime; this refuses the row that would exceed the ceiling.
    /// </summary>
    internal const int MaxPerAccount = 1000;

    internal const string CapReached = "You have reached the maximum number of trusted senders";

    public async Task<IReadOnlyList<string>> ListAsync(Guid userId, CancellationToken cancellationToken)
        => await context.TrustedSenders.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.Address)
            .Select(t => t.Address)
            .ToListAsync(cancellationToken);

    public async Task<Result> AddAsync(Guid userId, string address, CancellationToken cancellationToken)
    {
        var canonical = IdentityResolver.Canonical(address);
        var existing = await FindAsync(userId, canonical, cancellationToken);

        if (existing == null)
        {
            // Counted only on the branch that adds a row, so re-approving a stored address is
            // never refused by a cap it does not push against.
            var stored = await context.TrustedSenders.CountAsync(t => t.UserId == userId, cancellationToken);
            if (stored >= MaxPerAccount) return Result.Failure(CapReached);

            context.TrustedSenders.Add(new TrustedSender
            {
                UserId = userId, Address = canonical, LastUsed = DateTime.UtcNow
            });
        }
        else
        {
            existing.LastUsed = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task RemoveAsync(Guid userId, string address, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, IdentityResolver.Canonical(address), cancellationToken);
        if (row == null) return;

        context.TrustedSenders.Remove(row);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task TouchAsync(Guid userId, string address, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, IdentityResolver.Canonical(address), cancellationToken);
        var now = DateTime.UtcNow;
        if (row == null || row.LastUsed.Date == now.Date) return;

        row.LastUsed = now;
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> SweepExpiredAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - retention;
        // Loaded then removed rather than ExecuteDeleteAsync: the InMemory provider the tests run
        // on never translates SQL, so a bulk-delete would be covered by nothing that could fail.
        var stale = await context.TrustedSenders
            .Where(t => t.LastUsed < cutoff)
            .ToListAsync(cancellationToken);
        if (stale.Count == 0) return 0;

        context.TrustedSenders.RemoveRange(stale);
        await context.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }

    private async Task<TrustedSender?> FindAsync(Guid userId, string canonical, CancellationToken cancellationToken)
        => await context.TrustedSenders.FindAsync([userId, canonical], cancellationToken);
}
```

- [ ] **Step 8: Register the store**

In `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs`, inside `AddRepositories`, after the `IWebmailUserStore` line:

```csharp
        services.AddScoped<ITrustedSenderStore, TrustedSenderStore>();
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~TrustedSenderStoreTests`
Expected: PASS, 12 tests, zero build warnings.

- [ ] **Step 10: Commit**

```bash
git add src/snoopy.microservice/Data/Preferences/TrustedSender.cs \
        src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs \
        src/snoopy.microservice/Repositories/ITrustedSenderStore.cs \
        src/snoopy.microservice/Repositories/TrustedSenderStore.cs \
        src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Repositories/TrustedSenderStoreTests.cs \
        docs/superpowers/webmail-trusted-senders-table.md
git commit -F - <<'EOF'
Backend: table et store des expediteurs approuves

Plafond a 1000 lignes par compte, adresses canoniques, balayage par retention.
EOF
```

---

## Task 2: The API

**Files:**
- Create: `src/snoopy.microservice/Models/TrustedSenderRequest.cs`
- Create: `src/snoopy.microservice/Controllers/TrustedSendersController.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/TrustedSendersControllerTests.cs`

**Interfaces:**
- Consumes: `ITrustedSenderStore` (Task 1), `ApiBaseController.AuthenticatedUser`, `ApiBaseController.BadRequestEnveloppe(string)`, `ControllerTestHelpers.CreateAuthenticatedContext(string, string)`.
- Produces: `GET /api/TrustedSenders`, `POST /api/TrustedSenders`, `DELETE /api/TrustedSenders?address=`.

- [ ] **Step 1: Write the request DTO**

Create `src/snoopy.microservice/Models/TrustedSenderRequest.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace weesky.Snoopy.Microservice.Models;

/// <summary>One sender to start trusting. Stored canonical, so casing here is immaterial.</summary>
public sealed record TrustedSenderRequest
{
    [Required]
    [StringLength(320, MinimumLength = 3)]
    public string Address { get; init; } = string.Empty;
}
```

- [ ] **Step 2: Write the failing controller tests**

Create `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/TrustedSendersControllerTests.cs`:

```csharp
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class TrustedSendersControllerTests
{
    private readonly Mock<ITrustedSenderStore> _store = new();

    private TrustedSendersController CreateController()
    {
        var controller = new TrustedSendersController(_store.Object);
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com");
        return controller;
    }

    [Fact]
    public async Task List_Returns200WithTheAddresses()
    {
        _store.Setup(s => s.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(["news@example.com"]);

        var result = await CreateController().List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(["news@example.com"], Assert.IsAssignableFrom<IReadOnlyList<string>>(ok.Value));
    }

    [Fact]
    public async Task Add_WhenAccepted_Returns204()
    {
        _store.Setup(s => s.AddAsync(It.IsAny<Guid>(), "news@example.com", It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success());

        var result = await CreateController()
            .Add(new TrustedSenderRequest { Address = "news@example.com" }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Add_WithAnUnparsableAddress_Returns400AndNeverReachesTheStore()
    {
        var result = await CreateController()
            .Add(new TrustedSenderRequest { Address = "not-an-address" }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("That is not a valid email address",
            Assert.IsType<ResultEnveloppe>(bad.Value).Message);
        _store.Verify(s => s.AddAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Add_AtTheCap_Returns400CarryingTheStoreMessage()
    {
        _store.Setup(s => s.AddAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(TrustedSenderStore.CapReached));

        var result = await CreateController()
            .Add(new TrustedSenderRequest { Address = "news@example.com" }, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(TrustedSenderStore.CapReached, Assert.IsType<ResultEnveloppe>(bad.Value).Message);
    }

    [Fact]
    public async Task Remove_Returns204()
    {
        var result = await CreateController().Remove("news@example.com", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _store.Verify(s => s.RemoveAsync(It.IsAny<Guid>(), "news@example.com", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Idempotent for the reason DELETE /api/Mail/Attachments/{id} is: a 404 would confirm which
    // addresses this account has approved, and the caller can do nothing with the distinction.
    [Fact]
    public async Task Remove_UnknownAddress_StillReturns204()
    {
        _store.Setup(s => s.RemoveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var result = await CreateController().Remove("stranger@example.com", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Remove_WithNoAddress_Returns204AndNeverReachesTheStore()
    {
        var result = await CreateController().Remove("  ", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _store.Verify(s => s.RemoveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~TrustedSendersControllerTests`
Expected: build failure — `TrustedSendersController` does not exist.

- [ ] **Step 4: Write the controller**

Create `src/snoopy.microservice/Controllers/TrustedSendersController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimeKit;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// Senders whose remote images this account loads without asking — a webmail preference, not
/// mail-server data, so no IMAP session and no credentials cookie. The reader tests the list
/// itself; the sanitiser is never told about it, which is what keeps one message body good for
/// every account.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class TrustedSendersController(ITrustedSenderStore store) : ApiBaseController
{
    /// <summary>The approved addresses, canonical and sorted.</summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The addresses</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<string>>> List(CancellationToken cancellationToken)
        => Ok(await store.ListAsync(AuthenticatedUser.WebmailUid, cancellationToken));

    /// <summary>Starts trusting one sender. Approving an address already stored is not an error.</summary>
    /// <param name="request">the address to trust</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Trusted</response>
    /// <response code="400">The address does not parse, or the account is at its ceiling</response>
    /// <response code="401">Not authenticated</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> Add(TrustedSenderRequest request, CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Address))
            return BadRequestEnveloppe("An address is required");

        if (!MailboxAddress.TryParse(request.Address, out _))
            return BadRequestEnveloppe("That is not a valid email address");

        var result = await store.AddAsync(AuthenticatedUser.WebmailUid, request.Address, cancellationToken);
        return result.IsFailure ? BadRequestEnveloppe(result.Error) : NoContent();
    }

    /// <summary>
    /// Stops trusting one sender. Always 204, unknown address included: a 404 would confirm
    /// which addresses this account has approved, and the caller gains nothing from the answer.
    /// </summary>
    /// <param name="address">the address to stop trusting</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">No longer trusted, whether it was or not</response>
    /// <response code="401">Not authenticated</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> Remove([FromQuery] string address, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(address))
            await store.RemoveAsync(AuthenticatedUser.WebmailUid, address, cancellationToken);

        return NoContent();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~TrustedSendersControllerTests`
Expected: PASS, 7 tests.

**On the spec's "401 unauthenticated" case:** it is carried by the class-level `[Authorize]`
attribute and is not reachable from these tests, which invoke the actions directly with an
authenticated `ControllerContext` — no middleware runs. `AccountControllerTests` has the same
shape and the same gap. Do not add a test that fakes a 401 by other means: it would assert the
fake, not the attribute. The attribute's presence is the thing to check in review.

- [ ] **Step 6: Run the whole backend suite**

Run: `cd src/snoopy.microservice && dotnet test`
Expected: PASS, zero build warnings.

- [ ] **Step 7: Commit**

```bash
git add src/snoopy.microservice/Models/TrustedSenderRequest.cs \
        src/snoopy.microservice/Controllers/TrustedSendersController.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Controllers/TrustedSendersControllerTests.cs
git commit -F - <<'EOF'
Backend: endpoints des expediteurs approuves

GET/POST/DELETE, suppression idempotente comme celle des pieces jointes stagees.
EOF
```

---

## Task 3: Refreshing `last_used` on message open

**Files:**
- Modify: `src/snoopy.microservice/Controllers/MailController.cs:27-35` (constructor), `:420-437` (`GetMessage`)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs:37` (helper) and new tests

**Interfaces:**
- Consumes: `ITrustedSenderStore.TouchAsync` (Task 1), `MailMessageDetail.FromAddress`.
- Produces: nothing new — a side effect on the existing `GET /api/Mail/Messages/Detail`.

- [ ] **Step 1: Write the failing tests**

Add to `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs`, inside the existing class:

```csharp
    // The reader is already fetching this message; a dedicated client call would buy a second
    // round trip per open for nothing.
    [Fact]
    public async Task GetMessage_RecordsTheSenderUse()
    {
        var detail = new MailMessageDetail { FromAddress = "news@example.com" };
        _messages.Setup(m => m.GetAsync(It.IsAny<User>(), It.IsAny<string>(), "INBOX", 42u,
                                        It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(detail));

        await CreateController().GetMessage("INBOX", 42, CancellationToken.None);

        _trustedSenders.Verify(
            s => s.TouchAsync(It.IsAny<Guid>(), "news@example.com", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Rule 5: IMAP first, bookkeeping second. A failed write degrades, it never fails the read
    // the caller actually asked for.
    [Fact]
    public async Task GetMessage_WhenRecordingTheUseThrows_StillReturnsTheMessage()
    {
        var detail = new MailMessageDetail { FromAddress = "news@example.com" };
        _messages.Setup(m => m.GetAsync(It.IsAny<User>(), It.IsAny<string>(), "INBOX", 42u,
                                        It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Success(detail));
        _trustedSenders.Setup(s => s.TouchAsync(It.IsAny<Guid>(), It.IsAny<string>(),
                                                It.IsAny<CancellationToken>()))
                       .ThrowsAsync(new InvalidOperationException("database is away"));

        var result = await CreateController().GetMessage("INBOX", 42, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(detail, ok.Value);
    }

    [Fact]
    public async Task GetMessage_WhenTheReadFails_RecordsNothing()
    {
        _messages.Setup(m => m.GetAsync(It.IsAny<User>(), It.IsAny<string>(), "INBOX", 42u,
                                        It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Failure<MailMessageDetail>(ImapSession.MessageNotFound));

        await CreateController().GetMessage("INBOX", 42, CancellationToken.None);

        _trustedSenders.Verify(
            s => s.TouchAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

Add the mock field beside the other `Mock<>` fields at the top of the class:

```csharp
    private readonly Mock<ITrustedSenderStore> _trustedSenders = new();
```

Add `using weesky.Snoopy.Microservice.Repositories;` to the file's usings if it is not already there.

- [ ] **Step 2: Extend the test controller factory**

In `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs`, at line 37, add the two new constructor arguments in position — the store, then a logger:

```csharp
        return new MailController(_folders.Object, _messages.Object, _credentials.Object, _roleStore.Object,
            _staged.Object, _sender.Object, _quotes.Object, _drafts.Object, _trustedSenders.Object,
            NullLogger<MailController>.Instance)
```

Add `using Microsoft.Extensions.Logging.Abstractions;` to the file's usings.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~MailControllerTests`
Expected: build failure — `MailController` takes eight arguments, not ten.

- [ ] **Step 4: Extend the controller's constructor**

In `src/snoopy.microservice/Controllers/MailController.cs`, replace the primary constructor parameter list:

```csharp
public sealed class MailController(
    IMailFolderRepository folders,
    IMailMessageRepository messages,
    IMailCredentialStore credentials,
    IFolderRoleStore roleStore,
    IStagedAttachmentStore staged,
    IMailSender sender,
    IQuotePreparer quotes,
    IDraftSaver drafts,
    ITrustedSenderStore trustedSenders,
    ILogger<MailController> logger) : ApiBaseController
```

Add `using weesky.Snoopy.Microservice.Repositories;` to the file's usings if absent.

- [ ] **Step 5: Record the use in `GetMessage`**

In `GetMessage`, replace the tail of the method (from the `MessageNotFound` check to the return) with:

```csharp
        if (result.IsFailure && result.Error == ImapSession.MessageNotFound)
        {
            return NotFoundEnveloppe(result.Error);
        }

        if (result.IsSuccess)
        {
            await RecordSenderUseAsync(result.Value.FromAddress, cancellationToken);
        }

        return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
    }

    /// <summary>
    /// Keeps an approved sender's entry alive while it is still earning its place. Does nothing
    /// for a sender nobody approved, and never fails the read: bookkeeping degrades, it does not
    /// take the caller's message down with it.
    /// </summary>
    private async Task RecordSenderUseAsync(string? fromAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fromAddress)) return;

        try
        {
            await trustedSenders.TouchAsync(AuthenticatedUser.WebmailUid, fromAddress, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record the trusted-sender use for {Address}", fromAddress);
        }
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~MailControllerTests`
Expected: PASS, including the three new tests.

- [ ] **Step 7: Commit**

```bash
git add src/snoopy.microservice/Controllers/MailController.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs
git commit -F - <<'EOF'
Backend: rafraichit l'usage d'un expediteur a l'ouverture

Ecriture en piggyback sur le detail du message, non fatale, une fois par jour.
EOF
```

---

## Task 4: The retention sweep

**Files:**
- Create: `src/snoopy.microservice/Models/TrustedSenderOptions.cs`
- Create: `src/snoopy.microservice/Services/TrustedSenderSweeper.cs`
- Modify: `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs` (`AddSnoopyOptions`, `AddMailServices`)
- Modify: `src/snoopy.microservice/appsettings.json`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/TrustedSenderSweeperTests.cs`

**Interfaces:**
- Consumes: `ITrustedSenderStore.SweepExpiredAsync` (Task 1).
- Produces: `TrustedSenderOptions.RetentionDays` (default 365), `TrustedSenderSweeper.SweepOnceAsync(CancellationToken)`.

- [ ] **Step 1: Write the options type**

Create `src/snoopy.microservice/Models/TrustedSenderOptions.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models;

/// <summary>Bound from the "TrustedSenders" section of appsettings.json.</summary>
public sealed class TrustedSenderOptions
{
    /// <summary>
    /// Days an approved sender keeps its allowance without a message of theirs being opened.
    /// A year: long enough that a yearly statement still finds its sender approved.
    /// </summary>
    public int RetentionDays { get; set; } = 365;
}
```

- [ ] **Step 2: Write the failing sweeper test**

Create `src/snoopy.microservice/snoopy.microservice.Tests/Services/TrustedSenderSweeperTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class TrustedSenderSweeperTests
{
    private readonly Mock<ITrustedSenderStore> _store = new();

    // The store and its DbContext are scoped; this service is a singleton. Resolving the store
    // through a scope is the whole point of the test — injecting it directly compiles and throws
    // on the first tick, which no compiler will tell you.
    private TrustedSenderSweeper CreateSweeper(int retentionDays = 365)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _store.Object);

        return new TrustedSenderSweeper(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TrustedSenderOptions { RetentionDays = retentionDays }),
            NullLogger<TrustedSenderSweeper>.Instance);
    }

    [Fact]
    public async Task SweepOnce_ResolvesTheStoreFromAScopeAndSweeps()
    {
        _store.Setup(s => s.SweepExpiredAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(3);

        await CreateSweeper().SweepOnceAsync(CancellationToken.None);

        _store.Verify(s => s.SweepExpiredAsync(TimeSpan.FromDays(365), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SweepOnce_UsesTheConfiguredRetention()
    {
        _store.Setup(s => s.SweepExpiredAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(0);

        await CreateSweeper(retentionDays: 30).SweepOnceAsync(CancellationToken.None);

        _store.Verify(s => s.SweepExpiredAsync(TimeSpan.FromDays(30), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~TrustedSenderSweeperTests`
Expected: build failure — `TrustedSenderSweeper` does not exist.

- [ ] **Step 4: Write the sweeper**

Create `src/snoopy.microservice/Services/TrustedSenderSweeper.cs`:

```csharp
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Daily GC over the trusted senders, so an allowance nobody uses any more does not outlive its
/// usefulness. It is not what bounds the table — the per-account cap in
/// <see cref="TrustedSenderStore"/> is.
/// </summary>
internal sealed class TrustedSenderSweeper(
    IServiceScopeFactory scopes,
    IOptions<TrustedSenderOptions> options,
    ILogger<TrustedSenderSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // A sweep that throws must not take the host down with it; the next tick retries.
                logger.LogError(ex, "The trusted sender sweep failed");
            }
        }
    }

    /// <summary>
    /// One pass. Opens a scope of its own because the store and its DbContext are scoped while
    /// this service is a singleton — injecting the store directly compiles and throws here.
    /// </summary>
    internal async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITrustedSenderStore>();

        var removed = await store.SweepExpiredAsync(
            TimeSpan.FromDays(options.Value.RetentionDays), cancellationToken);

        // Every tick logs, zero included: the line is also the sweeper's heartbeat.
        logger.LogInformation("Trusted sender sweep: {Count} row(s) removed", removed);
    }
}
```

- [ ] **Step 5: Bind the options and register the hosted service**

In `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs`, in `AddSnoopyOptions` after the `MailOptions` line:

```csharp
        services.AddOptions<TrustedSenderOptions>().Bind(configuration.GetSection("TrustedSenders"));
```

and in `AddMailServices` after the `StagedAttachmentSweeper` line:

```csharp
        services.AddHostedService<TrustedSenderSweeper>();
```

- [ ] **Step 6: Add the configuration section**

In `src/snoopy.microservice/appsettings.json`, add a sibling of `"Mail"`:

```json
  "TrustedSenders": {
    "RetentionDays": 365
  },
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~TrustedSenderSweeperTests`
Expected: PASS, 2 tests.

- [ ] **Step 8: Run the whole backend suite**

Run: `cd src/snoopy.microservice && dotnet test`
Expected: PASS, zero build warnings.

- [ ] **Step 9: Commit**

```bash
git add src/snoopy.microservice/Models/TrustedSenderOptions.cs \
        src/snoopy.microservice/Services/TrustedSenderSweeper.cs \
        src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs \
        src/snoopy.microservice/appsettings.json \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/TrustedSenderSweeperTests.cs
git commit -F - <<'EOF'
Backend: balayage quotidien des expediteurs approuves

Retention configurable a 365 jours, un scope par tick puisque le store est scoped.
EOF
```

---

## Task 5: The client data layer

**Files:**
- Create: `src/frontend/src/modules/mail/reader/canonicalAddress.ts`
- Create: `src/frontend/src/modules/mail/reader/canonicalAddress.test.ts`
- Modify: `src/frontend/src/api.js:138-143` (beside the identity methods)
- Modify: `src/frontend/src/modules/mail/queries.ts:52` (keys) and `:202` (after `useReplaceIdentities`)
- Test: `src/frontend/src/api.test.js`

**Interfaces:**
- Consumes: `request` (api.js), `useAccountId`, `mailKeys` (queries.ts).
- Produces:
  - `canonicalAddress(address: string | null | undefined) -> string`
  - `api.getTrustedSenders()`, `api.trustSender(address)`, `api.untrustSender(address)`
  - `useTrustedSenders() -> UseQueryResult<Set<string>>`
  - `useTrustSender() -> UseMutationResult<void, Error, { address: string; trusted: boolean }>`

- [ ] **Step 1: Write the failing canonicalisation test**

Create `src/frontend/src/modules/mail/reader/canonicalAddress.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { canonicalAddress } from './canonicalAddress'

describe('canonicalAddress', () => {
  it('lower-cases and trims, mirroring the backend', () => {
    expect(canonicalAddress('  News@Example.COM ')).toBe('news@example.com')
  })

  it('answers an empty string for a missing address', () => {
    expect(canonicalAddress(null)).toBe('')
    expect(canonicalAddress(undefined)).toBe('')
  })
})
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd src/frontend && npx vitest run src/modules/mail/reader/canonicalAddress.test.ts`
Expected: FAIL — cannot resolve `./canonicalAddress`.

- [ ] **Step 3: Write the canonicalisation helper**

Create `src/frontend/src/modules/mail/reader/canonicalAddress.ts`:

```ts
/**
 * The client half of the folding rule, mirroring `IdentityResolver.Canonical` on the backend.
 * The table collates in binary, so the two sides must fold identically or an approved sender
 * quietly stops matching the message it was approved from.
 */
export function canonicalAddress(address: string | null | undefined): string {
  return (address ?? '').trim().toLowerCase()
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `cd src/frontend && npx vitest run src/modules/mail/reader/canonicalAddress.test.ts`
Expected: PASS, 2 tests.

- [ ] **Step 5: Add the API methods**

In `src/frontend/src/api.js`, after `putIdentities` (line 143):

```js
  getTrustedSenders: () =>
    request('GET', '/api/TrustedSenders'),

  trustSender: (address) =>
    request('POST', '/api/TrustedSenders', { address }),

  // The address travels in the query string, so it is encoded here rather than at call sites.
  untrustSender: (address) =>
    request('DELETE', `/api/TrustedSenders?address=${encodeURIComponent(address)}`),
```

- [ ] **Step 6: Write the API tests**

Add a new `describe` block to `src/frontend/src/api.test.js`, using the file's own `mockFetch(status, { json })` helper and its dynamic-import style (the module is re-imported per test because `beforeEach` calls `vi.resetModules()`):

```js
describe('trusted senders', () => {
  it('getTrustedSenders reads the list', async () => {
    mockFetch(200, { json: ['news@example.com'] })
    const { api } = await import('./api.js')

    await expect(api.getTrustedSenders()).resolves.toEqual(['news@example.com'])

    const [url, options] = globalThis.fetch.mock.calls[0]
    expect(url).toContain('/api/TrustedSenders')
    expect(options.method).toBe('GET')
  })

  it('trustSender posts the address', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.trustSender('news@example.com')

    const [, options] = globalThis.fetch.mock.calls[0]
    expect(options.method).toBe('POST')
    expect(JSON.parse(options.body)).toEqual({ address: 'news@example.com' })
  })

  // A '+' is a legal local-part character and decodes to a space, so an unencoded query string
  // would untrust a different address than the one asked for.
  it('untrustSender encodes the address into the query string', async () => {
    mockFetch(204)
    const { api } = await import('./api.js')

    await api.untrustSender('news+weekly@example.com')

    const [url, options] = globalThis.fetch.mock.calls[0]
    expect(url).toContain('address=news%2Bweekly%40example.com')
    expect(options.method).toBe('DELETE')
  })
})
```

- [ ] **Step 7: Add the query key and hooks**

In `src/frontend/src/modules/mail/queries.ts`, add to `mailKeys` after the `identities` line:

```ts
  trustedSenders: (accountId: string) => ['mail', accountId, 'trustedSenders'] as const,
```

and after `useReplaceIdentities` (line 202):

```ts
/** The senders whose remote images load without asking. Long staleTime: it changes only from
    the reader, which invalidates it. The Set is built by `select` so the reader can test one
    address per render without rebuilding it. */
export function useTrustedSenders() {
  const accountId = useAccountId()

  return useQuery({
    queryKey: mailKeys.trustedSenders(accountId),
    queryFn: () => api.getTrustedSenders() as Promise<string[]>,
    staleTime: 5 * 60_000,
    select: (addresses): Set<string> => new Set(addresses),
  })
}

/** One mutation for both directions — the two differ by a verb, not by a workflow. */
export function useTrustSender() {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ address, trusted }: { address: string; trusted: boolean }) =>
      (trusted ? api.trustSender(address) : api.untrustSender(address)) as Promise<void>,
    // Settled, not success: a refused call must leave the reader showing the server's state.
    onSettled: () => queryClient.invalidateQueries({ queryKey: mailKeys.trustedSenders(accountId) }),
  })
}
```

- [ ] **Step 8: Run lint, typecheck and the tests**

Run: `cd src/frontend && npm run lint && npm run typecheck && npx vitest run src/api.test.js src/modules/mail/reader/canonicalAddress.test.ts`
Expected: PASS, no lint or type errors.

- [ ] **Step 9: Commit**

```bash
git add src/frontend/src/api.js \
        src/frontend/src/api.test.js \
        src/frontend/src/modules/mail/queries.ts \
        src/frontend/src/modules/mail/reader/canonicalAddress.ts \
        src/frontend/src/modules/mail/reader/canonicalAddress.test.ts
git commit -F - <<'EOF'
Webmail: couche client des expediteurs approuves

Trois methodes API, une requete cachee en Set, une mutation pour les deux sens.
EOF
```

---

## Task 6: The reader — granting and revoking

**Files:**
- Create: `src/frontend/src/icons/ChevronDownIcon.tsx`
- Create: `src/frontend/src/icons/ImageOffIcon.tsx`
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx`
- Modify: `src/frontend/src/styles/mail.css` (after the `.reader-blocked-images .btn:hover` rule, line 980)
- Test: `src/frontend/src/modules/mail/reader/MessageReader.test.tsx`

**Interfaces:**
- Consumes: `useTrustedSenders`, `useTrustSender`, `canonicalAddress` (Task 5); `DropdownMenu`, `MenuEntry`; `alwaysShowImagesOf`.
- Produces: nothing consumed downstream.

- [ ] **Step 1: Write the two icons**

Create `src/frontend/src/icons/ChevronDownIcon.tsx`:

```tsx
export default function ChevronDownIcon({ size = 14 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M4.5 7.5l5.5 6 5.5-6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}
```

Create `src/frontend/src/icons/ImageOffIcon.tsx`:

```tsx
export default function ImageOffIcon({ size = 18 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5">
      <path d="M3.5 4.5h13v11h-13z" strokeLinejoin="round" />
      <path d="M3.5 13l4-3.5 3 2.5" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M2.5 2.5l15 15" strokeLinecap="round" />
    </svg>
  )
}
```

- [ ] **Step 2: Write the failing reader tests**

In `src/frontend/src/modules/mail/reader/MessageReader.test.tsx`, add three entries to the `mocks` object hoisted at line 10:

```ts
  getTrustedSenders: vi.fn(),
  trustSender: vi.fn(),
  untrustSender: vi.fn(),
```

then to the `api` mock at lines 26-37, after `prepareQuote`:

```ts
    getTrustedSenders: mocks.getTrustedSenders,
    trustSender: mocks.trustSender,
    untrustSender: mocks.untrustSender,
```

then to the `beforeEach` at line 128, after `mocks.getAliases.mockResolvedValue([])`:

```ts
    mocks.getTrustedSenders.mockResolvedValue([])
    mocks.trustSender.mockResolvedValue(undefined)
    mocks.untrustSender.mockResolvedValue(undefined)
```

Then add the tests inside the top-level `describe('MessageReader', …)`. They use the file's own
`blocked` fixture (line 98, `blockedImageCount: 2`, sender `alice@x.be`), its `wrapper`, and
`fireEvent` — **this file does not import `userEvent`; do not add it**:

```tsx
  it('offers the chevron beside Show images', async () => {
    mocks.getMailMessage.mockResolvedValue(blocked)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByRole('button', { name: 'Show images' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'More image options' })).toBeInTheDocument()
  })

  // The address is folded before it leaves, so an approved sender still matches the message it
  // was approved from whatever casing the server reported.
  it('trusts the sender from the chevron menu, canonicalised', async () => {
    mocks.getMailMessage.mockResolvedValue({ ...blocked, fromAddress: 'Alice@X.BE' })

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    fireEvent.click(await screen.findByRole('button', { name: 'More image options' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Always show images from this sender' }))

    await waitFor(() => expect(mocks.trustSender).toHaveBeenCalledWith('alice@x.be'))
  })

  // The whole point: no banner, no button, and the images actually restored in the document.
  it('shows a trusted sender images with no banner at all', async () => {
    mocks.getTrustedSenders.mockResolvedValue(['alice@x.be'])
    mocks.getMailMessage.mockResolvedValue(blocked)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Show images' })).not.toBeInTheDocument())
    expect(screen.queryByText(/remote image/i)).not.toBeInTheDocument()
    expect(screen.getByTitle('Message body').getAttribute('srcdoc'))
      .toContain('src="https://t.example/p.gif"')
  })

  it('offers the revocation in the kebab for a trusted sender', async () => {
    mocks.getTrustedSenders.mockResolvedValue(['alice@x.be'])
    mocks.getMailMessage.mockResolvedValue(blocked)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Show images' })).not.toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))
    fireEvent.click(screen.getByRole('menuitem', { name: "Block sender's images" }))

    await waitFor(() => expect(mocks.untrustSender).toHaveBeenCalledWith('alice@x.be'))
  })

  it('keeps the revocation out of the kebab for an untrusted sender', async () => {
    mocks.getMailMessage.mockResolvedValue(blocked)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

    expect(screen.queryByRole('menuitem', { name: "Block sender's images" })).not.toBeInTheDocument()
  })

  // With the global setting on, revoking changes nothing visible. An entry whose effect cannot
  // be seen misleads more than an absent one helps.
  it('hides the revocation while remote images always load', async () => {
    mocks.getPreferences.mockResolvedValue(
      { 'mail.pageSize': '30', 'mail.alwaysShowImages': 'true' })
    mocks.getTrustedSenders.mockResolvedValue(['alice@x.be'])
    mocks.getMailMessage.mockResolvedValue(blocked)

    render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')
    fireEvent.click(screen.getByRole('button', { name: 'Message actions' }))

    expect(screen.queryByRole('menuitem', { name: "Block sender's images" })).not.toBeInTheDocument()
  })
```

The kebab's accessible name is **`Message actions`** (`ReaderActions.tsx:89`) — not "More actions",
which belongs to the per-attachment split menus. The `beforeEach` already re-seeds
`getPreferences`, so the last test's override does not leak into the others.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd src/frontend && npx vitest run src/modules/mail/reader/MessageReader.test.tsx`
Expected: FAIL — no "More image options" button, no revocation entry.

- [ ] **Step 4: Derive the consent in the reader**

In `src/frontend/src/modules/mail/reader/MessageReader.tsx`, add the imports:

```tsx
import ChevronDownIcon from '../../../icons/ChevronDownIcon'
import ImageOffIcon from '../../../icons/ImageOffIcon'
import { canonicalAddress } from './canonicalAddress'
```

and add `useTrustSender, useTrustedSenders` to the existing `../queries` import list.

Below `const prepare = usePrepareQuote()` (line 79), add:

```tsx
  const { data: trustedSenders } = useTrustedSenders()
  const setTrust = useTrustSender()
```

Then replace the `showImages` line (line 109) with:

```tsx
  const senderAddress = canonicalAddress(data?.fromAddress)
  const senderTrusted = senderAddress !== '' && trustedSenders?.has(senderAddress) === true
  const alwaysShow = !!preferences && alwaysShowImagesOf(preferences)
  const showImages = imagesShown || alwaysShow || senderTrusted
```

`imagesShown` keeps meaning "the user clicked Show images on *this* message", so the per-message reset effect at line 85 stays untouched.

- [ ] **Step 5: Split the banner button**

Replace the banner block (lines 300-308) with:

```tsx
      {data.blockedImageCount > 0 && !showImages && (
        <div className="reader-blocked-images">
          <span>
            {data.blockedImageCount} remote image{data.blockedImageCount > 1 ? 's were' : ' was'} blocked.
            Loading them tells the sender you opened this message.
          </span>
          {/* The chevron can only ever grant: an approved sender has no banner to hang it from. */}
          <span className="banner-split">
            <button type="button" className="btn" onClick={() => setImagesShown(true)}>Show images</button>
            <DropdownMenu
              ariaLabel="More image options"
              className="banner-split-more"
              trigger={<ChevronDownIcon size={13} />}
              items={[{
                label: 'Always show images from this sender',
                // A malformed message can carry images and no parsable sender; posting an empty
                // address would just earn a 400 nobody surfaces.
                disabled: senderAddress === '',
                onSelect: () => setTrust.mutate({ address: senderAddress, trusted: true }),
              }]}
            />
          </span>
        </div>
      )}
```

- [ ] **Step 6: Add the revocation to the kebab**

After the `const actions: MenuEntry[] = [...]` block (ends line 223), add:

```tsx
  // Only for an approved sender, and only while the global setting is off: with it on, revoking
  // changes nothing on screen, and an entry whose effect is invisible misleads.
  if (senderTrusted && !alwaysShow) {
    actions.push('separator', {
      label: "Block sender's images",
      icon: <ImageOffIcon size={18} />,
      onSelect: () => setTrust.mutate({ address: senderAddress, trusted: false }),
    })
  }
```

- [ ] **Step 7: Style the split**

In `src/frontend/src/styles/mail.css`, after the `.reader-blocked-images .btn:hover` rule (line 980):

```css
/* The banner's button splits like an attachment chip does, with one difference: both halves
   carry the accent fill, so the seam is the readable foreground at low alpha rather than a
   --border hairline, which would read as dirt on the blue.
   The .btn rule is written through .reader-blocked-images so it outranks that block's
   margin-left:auto whatever the source order — the pair is what hugs the right edge now. */
.banner-split { margin-left: auto; flex: none; display: inline-flex; align-items: stretch; }
.reader-blocked-images .banner-split .btn { margin-left: 0; border-radius: var(--radius-sm) 0 0 var(--radius-sm); }
.banner-split .dropdown-root { display: flex; }
.banner-split-more {
  display: flex;
  align-items: center;
  padding: 0 7px;
  border: none;
  border-left: 1px solid color-mix(in srgb, var(--action-primary-fg) 32%, transparent);
  border-radius: 0 var(--radius-sm) var(--radius-sm) 0;
  background: var(--action-primary);
  color: var(--action-primary-fg);
  cursor: pointer;
}
.banner-split-more:hover { background: var(--action-primary-hover); }
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `cd src/frontend && npx vitest run src/modules/mail/reader/MessageReader.test.tsx`
Expected: PASS, including the six new tests and every pre-existing one.

- [ ] **Step 9: Run lint, typecheck and the whole frontend suite**

Run: `cd src/frontend && npm run lint && npm run typecheck && npx vitest run`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add src/frontend/src/icons/ChevronDownIcon.tsx \
        src/frontend/src/icons/ImageOffIcon.tsx \
        src/frontend/src/modules/mail/reader/MessageReader.tsx \
        src/frontend/src/modules/mail/reader/MessageReader.test.tsx \
        src/frontend/src/styles/mail.css
git commit -F - <<'EOF'
Webmail: approuver un expediteur depuis le bouton Show images

Chevron pour accorder, entree de kebab pour revoquer, masquee sous le reglage global.
EOF
```

---

## Task 7: The Contacts row in Settings

**Files:**
- Modify: `src/frontend/src/modules/settings/general/GeneralPage.tsx:192-196` (after the always-show-images note)
- Test: `src/frontend/src/modules/settings/general/GeneralPage.test.tsx`

**Interfaces:**
- Consumes: the file's own `ToggleRow`.
- Produces: nothing.

- [ ] **Step 1: Write the failing test**

Add to `src/frontend/src/modules/settings/general/GeneralPage.test.tsx`, following the file's existing render helper:

```tsx
  it('shows the contacts row disabled with its note', async () => {
    renderPage()

    const row = await screen.findByLabelText('Trust my contacts')
    expect(row).toBeDisabled()
    expect(row).not.toBeChecked()
    expect(screen.getByText('Available once Contacts ships.')).toBeInTheDocument()
  })
```

- [ ] **Step 2: Run it to verify it fails**

Run: `cd src/frontend && npx vitest run src/modules/settings/general/GeneralPage.test.tsx`
Expected: FAIL — no element labelled "Trust my contacts".

- [ ] **Step 3: Add the row**

In `src/frontend/src/modules/settings/general/GeneralPage.tsx`, immediately after the `alwaysShowImagesOf(preferences) && ...settings-note` block (ends line 196):

```tsx
          {/* Disabled until Contacts exists. No preference key is declared for it: nothing can
              write one while the row is disabled, and a registry entry nothing can reach is dead
              code with dead validation. When Contacts ships this becomes a real key, and the row
              greys whenever "Always show remote images" is on — the scopes nest rather than
              compete, so only the narrower one gives way. */}
          <ToggleRow
            id="trust-contacts"
            label="Trust my contacts"
            checked={false}
            disabled
            onChange={() => {}}
          />

          <p className="settings-note">Available once Contacts ships.</p>
```

- [ ] **Step 4: Run it to verify it passes**

Run: `cd src/frontend && npx vitest run src/modules/settings/general/GeneralPage.test.tsx`
Expected: PASS.

- [ ] **Step 5: Run lint, typecheck and the whole frontend suite**

Run: `cd src/frontend && npm run lint && npm run typecheck && npx vitest run`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/modules/settings/general/GeneralPage.tsx \
        src/frontend/src/modules/settings/general/GeneralPage.test.tsx
git commit -F - <<'EOF'
Webmail: annonce le reglage Trust my contacts, desactive

Aucune cle de preference: rien ne peut l'ecrire tant que la ligne est grisee.
EOF
```

---

## Before deploying

**The DDL must be applied first.** Run `docs/superpowers/webmail-trusted-senders-table.md` on
`snoopy_webmail` **and** `snoopy_webmail_dev` before the backend ships. Without the table, every
call to `/api/TrustedSenders` answers 500; the reader degrades to "no sender is trusted" rather
than breaking, but the banner never stops coming back and nothing can be approved.

Pushing deploys: the branch decides the target (`prod` on `master`, `dev` on anything else).
