# Webmail 2c2a — Sending Identities Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A curated From list — address + display name + default — stored in `snoopy_webmail`, managed in Settings → Identities, and honoured by `POST /api/Mail/Send`.

**Architecture:** New `sending_identities` table behind `ISendingIdentityStore`; a pure `IdentityResolver` merges stored rows + primary address + live aliases (the one place the rule is written — `GET /api/Identities` and `MailSender` both call it). Frontend adds a Settings page (list + filterable add-dialog over the aliases) and an `IdentitySelect` in the composer.

**Tech Stack:** ASP.NET Core (.NET 10), EF Core/Pomelo, MailKit/MimeKit, CSharpFunctionalExtensions, xUnit+Moq · React 18/TS, TanStack Query v5, Vitest+RTL.

**Spec:** `docs/superpowers/specs/2026-07-24-webmail-identities-2c2a-design.md` — read the sections a task cites.

## Global Constraints

- UI copy is **English**; conversation with the user is French. Commit messages concise, **2 lines of body max**, never starting/ending with `@` (use a heredoc with `git commit -F -` under the Bash tool).
- Backend style (`src/snoopy.microservice/CLAUDE.md`): file-scoped namespaces, one type per file, primary constructors for new DI classes, records for DTOs, `Result<T>` error handling, structured logging only, cancellation tokens on async, `Assert.IsType<BadRequestObjectResult>` for `BadRequest(body)`.
- Comments: only where the code alone doesn't explain; 3 lines max. No duplicated logic.
- Backend tests: `dotnet test` from `src/snoopy.microservice` (never `--no-build` when files were added).
- Frontend: from `src/frontend` — `npx vitest run <path>` per file, `npm test` full, `npm run lint`, `npm run build`.
- Addresses are **canonical (trimmed, lower-case)** everywhere they are stored or compared; the table collates binary.
- The stored label is resolved **server-side** at send time; the client never transmits a display name to `/api/Mail/Send`.
- A stale identity (alias no longer owned) is kept, flagged, excluded from the From menu — never silently deleted.
- The DB table is created **manually** (no EF migrations): the docs file in Task 1 is part of the deliverable.

## File Structure

| File | Responsibility |
|---|---|
| `Data/Preferences/SendingIdentity.cs` | EF entity for `sending_identities` |
| `Repositories/ISendingIdentityStore.cs` / `SendingIdentityStore.cs` | Get/Replace rows per account |
| `Models/Mail/SendingIdentityInfo.cs`, `IdentityEntry.cs`, `IdentityListResponse.cs`, `ReplaceIdentitiesRequest.cs` | API DTOs |
| `Services/IdentityResolver.cs` | Pure merge + validation + label lookup |
| `Controllers/IdentitiesController.cs` | GET/PUT `/api/Identities` |
| `Services/MailSender.cs`, `SmtpSession.cs`, `Controllers/MailController.cs`, `Models/Mail/SendMessageRequest.cs` | `fromAddress` on Send |
| `src/frontend/src/modules/settings/identities/{IdentitiesPage,AddIdentityDialog}.tsx`, `identityRows.ts` | Settings screen |
| `src/frontend/src/modules/mail/compose/IdentitySelect.tsx` | From selector in the composer |

---

### Task 1: `sending_identities` entity, store, and server DDL doc

**Files:**
- Create: `src/snoopy.microservice/Data/Preferences/SendingIdentity.cs`
- Create: `src/snoopy.microservice/Repositories/ISendingIdentityStore.cs`
- Create: `src/snoopy.microservice/Repositories/SendingIdentityStore.cs`
- Modify: `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs`
- Modify: `src/snoopy.microservice/Program.cs` (DI, beside `IFolderRoleStore` line ~125)
- Create: `docs/superpowers/webmail-identities-table.md`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/SendingIdentityStoreTests.cs`

**Interfaces:**
- Consumes: `PreferencesDbContext`, `PreferencesTestDbContext(string dbName)` (test infra).
- Produces: `ISendingIdentityStore` with `Task<IReadOnlyList<SendingIdentity>> GetAsync(string accountId, CancellationToken ct)` and `Task ReplaceAsync(string accountId, IReadOnlyList<SendingIdentity> identities, CancellationToken ct)`; entity `SendingIdentity { string AccountId; string Address; string DisplayName; bool IsDefault; DateTime UpdatedAt }`.

- [ ] **Step 1: Write the failing tests**

`SendingIdentityStoreTests.cs`:

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class SendingIdentityStoreTests
{
    private static SendingIdentityStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName));

    private static SendingIdentity Row(string address, string name = "Someone", bool isDefault = false) =>
        new() { Address = address, DisplayName = name, IsDefault = isDefault };

    [Fact]
    public async Task Replace_WritesTheRowsUnderTheAccount()
    {
        var store = CreateStore(nameof(Replace_WritesTheRowsUnderTheAccount));

        await store.ReplaceAsync("alice@weesky.be",
            [Row("michel@weesky.be", "Michel", isDefault: true)], CancellationToken.None);

        var rows = await store.GetAsync("alice@weesky.be", CancellationToken.None);
        var row = Assert.Single(rows);
        Assert.Equal("alice@weesky.be", row.AccountId);
        Assert.Equal("michel@weesky.be", row.Address);
        Assert.Equal("Michel", row.DisplayName);
        Assert.True(row.IsDefault);
    }

    [Fact]
    public async Task Replace_RemovesRowsAbsentFromTheNewSet()
    {
        var store = CreateStore(nameof(Replace_RemovesRowsAbsentFromTheNewSet));
        await store.ReplaceAsync("alice@weesky.be",
            [Row("a@weesky.be"), Row("b@weesky.be")], CancellationToken.None);

        await store.ReplaceAsync("alice@weesky.be", [Row("b@weesky.be", "B two")], CancellationToken.None);

        var rows = await store.GetAsync("alice@weesky.be", CancellationToken.None);
        var row = Assert.Single(rows);
        Assert.Equal("b@weesky.be", row.Address);
        Assert.Equal("B two", row.DisplayName);
    }

    [Fact]
    public async Task Replace_WithAnEmptySetClearsTheAccount()
    {
        var store = CreateStore(nameof(Replace_WithAnEmptySetClearsTheAccount));
        await store.ReplaceAsync("alice@weesky.be", [Row("a@weesky.be")], CancellationToken.None);

        await store.ReplaceAsync("alice@weesky.be", [], CancellationToken.None);

        Assert.Empty(await store.GetAsync("alice@weesky.be", CancellationToken.None));
    }

    [Fact]
    public async Task Replace_LeavesOtherAccountsAlone()
    {
        var store = CreateStore(nameof(Replace_LeavesOtherAccountsAlone));
        await store.ReplaceAsync("bob@weesky.be", [Row("bob-alias@weesky.be")], CancellationToken.None);

        await store.ReplaceAsync("alice@weesky.be", [Row("a@weesky.be")], CancellationToken.None);

        Assert.Single(await store.GetAsync("bob@weesky.be", CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter "FullyQualifiedName~SendingIdentityStoreTests"` from `src/snoopy.microservice`
Expected: compilation error — `SendingIdentity`/`SendingIdentityStore` do not exist.

- [ ] **Step 3: Implement entity, context mapping, store, DI**

`Data/Preferences/SendingIdentity.cs`:

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One curated sending identity. Addresses are stored canonical (trimmed, lower-case): the
/// table collates in binary, so a casing difference would split one identity into two.
/// </summary>
[Table("sending_identities")]
public sealed class SendingIdentity
{
    [Column("account_id")]
    public string AccountId { get; set; } = string.Empty;

    [Column("address")]
    public string Address { get; set; } = string.Empty;

    [Column("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [Column("is_default")]
    public bool IsDefault { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
```

`PreferencesDbContext.cs` — add to `OnModelCreating` and the DbSets:

```csharp
modelBuilder.Entity<SendingIdentity>().HasKey(i => new { i.AccountId, i.Address });
```

```csharp
public DbSet<SendingIdentity> SendingIdentities { get; set; }
```

`Repositories/ISendingIdentityStore.cs`:

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

public interface ISendingIdentityStore
{
    Task<IReadOnlyList<SendingIdentity>> GetAsync(string accountId, CancellationToken cancellationToken);

    /// <summary>Replaces the account's whole set in one transaction — the PUT semantics.</summary>
    Task ReplaceAsync(string accountId, IReadOnlyList<SendingIdentity> identities, CancellationToken cancellationToken);
}
```

`Repositories/SendingIdentityStore.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class SendingIdentityStore(PreferencesDbContext context) : ISendingIdentityStore
{
    public async Task<IReadOnlyList<SendingIdentity>> GetAsync(string accountId, CancellationToken cancellationToken)
        => await context.SendingIdentities.AsNoTracking()
            .Where(i => i.AccountId == accountId)
            .OrderBy(i => i.Address)
            .ToListAsync(cancellationToken);

    public async Task ReplaceAsync(string accountId, IReadOnlyList<SendingIdentity> identities, CancellationToken cancellationToken)
    {
        var existing = await context.SendingIdentities
            .Where(i => i.AccountId == accountId)
            .ToListAsync(cancellationToken);
        context.SendingIdentities.RemoveRange(existing);

        var now = DateTime.UtcNow;
        foreach (var identity in identities)
        {
            identity.AccountId = accountId;
            identity.UpdatedAt = now;
            context.SendingIdentities.Add(identity);
        }

        // A single SaveChanges: on a relational provider this commits as one transaction.
        await context.SaveChangesAsync(cancellationToken);
    }
}
```

`Program.cs` — beside `IFolderRoleStore`:

```csharp
builder.Services.AddScoped<ISendingIdentityStore, SendingIdentityStore>();
```

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~SendingIdentityStoreTests"`
Expected: 4/4 PASS.

- [ ] **Step 5: Write `docs/superpowers/webmail-identities-table.md`**

Model it on `docs/superpowers/webmail-preferences-table.md` (read it first): same heading style, French prose, idempotent script, prod **and** dev blocks, a verification query, and a rollback block. The DDL (repeat for `snoopy_webmail_dev`):

```sql
CREATE TABLE IF NOT EXISTS `snoopy_webmail`.`sending_identities` (
  `account_id`   VARCHAR(255) NOT NULL
                 COMMENT 'Forme canonique : minuscules, sans espaces',
  `address`      VARCHAR(320) NOT NULL
                 COMMENT 'Forme canonique minuscule ; 320 = longueur max RFC 5321',
  `display_name` VARCHAR(100) NOT NULL,
  `is_default`   TINYINT(1)   NOT NULL DEFAULT 0,
  `updated_at`   TIMESTAMP    NOT NULL
                 DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,

  PRIMARY KEY (`account_id`, `address`)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_bin;
```

Add a final section **« Prérequis Postfix — envoi depuis un alias »** stating: sending with `From` = alias requires `smtpd_sender_login_maps` to allow the authenticated user to use its aliases as envelope sender; without it Postfix answers 553 and the webmail surfaces « The mail server refused to send from _address_ ». To check on the server: `postconf smtpd_sender_login_maps` (a query over the alias table is the usual value) and that `reject_sender_login_mismatch` (or `reject_authenticated_sender_login_mismatch`) appears in `smtpd_sender_restrictions`.

- [ ] **Step 6: Full backend suite, commit**

Run: `dotnet test`
Expected: all green.

```bash
git add -A && git commit -F - <<'EOF'
Backend 2c2a: sending_identities table and store

Replace-all semantics per account; manual DDL documented with the
Postfix sender_login_maps prerequisite.
EOF
```

---

### Task 2: `IdentityResolver` — merge, label, validation

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/SendingIdentityInfo.cs`
- Create: `src/snoopy.microservice/Models/Mail/IdentityEntry.cs`
- Create: `src/snoopy.microservice/Services/IdentityResolver.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/IdentityResolverTests.cs`

**Interfaces:**
- Consumes: `SendingIdentity` (Task 1).
- Produces:
  - `record SendingIdentityInfo(string Address, string DisplayName, bool IsDefault, bool IsPrimary, bool Stale, bool LabelIsCustom)`
  - `record IdentityEntry { string Address; string DisplayName; bool IsDefault }` (init props, string defaults `""`)
  - `static class IdentityResolver` with `Canonical(string)`, `Resolve(IReadOnlyList<SendingIdentity> stored, string primaryAddress, string? fullName, IReadOnlyCollection<string> aliasAddresses)`, `LabelFor(IReadOnlyList<SendingIdentity> stored, string address, string? fullName)`, `Validate(IReadOnlyList<IdentityEntry> entries, string primaryAddress, IReadOnlyCollection<string> aliasAddresses, IReadOnlyCollection<string> storedAddresses)` → `Result<IReadOnlyList<SendingIdentity>>`.

`LabelIsCustom` is how the client knows whether the primary's label is an override (row) or the live `FullName` — it decides whether the primary appears in the PUT payload. It is `true` on every row-backed identity, `false` only on a synthesized primary. **Also update the spec's §4.3 JSON sample** to carry the field.

- [ ] **Step 1: Write the failing tests**

`IdentityResolverTests.cs`:

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class IdentityResolverTests
{
    private static SendingIdentity Row(string address, string name, bool isDefault = false) =>
        new() { AccountId = "mick@weesky.be", Address = address, DisplayName = name, IsDefault = isDefault };

    private static IdentityEntry Entry(string address, string name = "Someone", bool isDefault = false) =>
        new() { Address = address, DisplayName = name, IsDefault = isDefault };

    // ── Resolve ──────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_AlwaysProducesThePrimary_LabelledByFullName()
    {
        var list = IdentityResolver.Resolve([], "mick@weesky.be", "Mick Dubois", []);

        var identity = Assert.Single(list);
        Assert.Equal("mick@weesky.be", identity.Address);
        Assert.Equal("Mick Dubois", identity.DisplayName);
        Assert.True(identity.IsPrimary);
        Assert.True(identity.IsDefault);
        Assert.False(identity.Stale);
        Assert.False(identity.LabelIsCustom);
    }

    [Fact]
    public void Resolve_FallsBackToTheAddressWhenThereIsNoFullName()
    {
        var list = IdentityResolver.Resolve([], "mick@weesky.be", null, []);
        Assert.Equal("mick@weesky.be", Assert.Single(list).DisplayName);
    }

    [Fact]
    public void Resolve_APrimaryRowOverridesTheFullName()
    {
        var list = IdentityResolver.Resolve(
            [Row("mick@weesky.be", "Le Boss")], "mick@weesky.be", "Mick Dubois", []);

        var identity = Assert.Single(list);
        Assert.Equal("Le Boss", identity.DisplayName);
        Assert.True(identity.LabelIsCustom);
    }

    [Fact]
    public void Resolve_AnAliasRowThatIsNoLongerOwnedIsStale()
    {
        var list = IdentityResolver.Resolve(
            [Row("gone@weesky.be", "Ancien")], "mick@weesky.be", "Mick", ["kept@weesky.be"]);

        var stale = Assert.Single(list, i => i.Address == "gone@weesky.be");
        Assert.True(stale.Stale);
        Assert.False(stale.IsDefault);
    }

    [Fact]
    public void Resolve_TheDefaultFallsBackToThePrimaryWhenTheMarkedRowIsStale()
    {
        var list = IdentityResolver.Resolve(
            [Row("gone@weesky.be", "Ancien", isDefault: true)], "mick@weesky.be", "Mick", []);

        Assert.True(Assert.Single(list, i => i.IsPrimary).IsDefault);
    }

    [Fact]
    public void Resolve_SortsDefaultFirstThenByLabel()
    {
        var list = IdentityResolver.Resolve(
            [Row("zeta@weesky.be", "Zeta", isDefault: true), Row("beta@weesky.be", "beta")],
            "mick@weesky.be", "Mick", ["zeta@weesky.be", "beta@weesky.be"]);

        Assert.Equal(["Zeta", "beta", "Mick"], list.Select(i => i.DisplayName).ToArray());
    }

    [Fact]
    public void Resolve_ComparesAddressesCanonically()
    {
        var list = IdentityResolver.Resolve(
            [Row("MICK@weesky.be", "Custom")], " Mick@Weesky.BE ", "Mick", []);

        var identity = Assert.Single(list);
        Assert.Equal("mick@weesky.be", identity.Address);
        Assert.Equal("Custom", identity.DisplayName);
    }

    // ── LabelFor ─────────────────────────────────────────────────────────────

    [Fact]
    public void LabelFor_PrefersTheRowThenTheFullNameThenTheAddress()
    {
        var stored = new[] { Row("michel@weesky.be", "Michel D.") };
        Assert.Equal("Michel D.", IdentityResolver.LabelFor(stored, "michel@weesky.be", "Mick"));
        Assert.Equal("Mick", IdentityResolver.LabelFor(stored, "other@weesky.be", "Mick"));
        Assert.Equal("other@weesky.be", IdentityResolver.LabelFor(stored, "other@weesky.be", null));
    }

    // ── Validate ─────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_AcceptsPrimaryAliasesAndAlreadyStoredAddresses()
    {
        var result = IdentityResolver.Validate(
            [Entry("mick@weesky.be", "Me"), Entry("alias@weesky.be"), Entry("stale@weesky.be", "Old")],
            "mick@weesky.be", ["alias@weesky.be"], ["stale@weesky.be"]);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Count);
    }

    [Fact]
    public void Validate_NamesAForeignAddress()
    {
        var result = IdentityResolver.Validate(
            [Entry("intruder@evil.com")], "mick@weesky.be", [], []);

        Assert.True(result.IsFailure);
        Assert.Contains("intruder@evil.com", result.Error);
    }

    [Fact]
    public void Validate_CanonicalisesAndRefusesADuplicate()
    {
        var result = IdentityResolver.Validate(
            [Entry("Alias@weesky.be"), Entry("alias@WEESKY.be")],
            "mick@weesky.be", ["alias@weesky.be"], []);

        Assert.True(result.IsFailure);
        Assert.Contains("twice", result.Error);
    }

    [Fact]
    public void Validate_RefusesAnUnparsableAddress()
    {
        var result = IdentityResolver.Validate(
            [Entry("not an address")], "mick@weesky.be", [], []);

        Assert.True(result.IsFailure);
        Assert.Contains("not an address", result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RefusesAnEmptyDisplayName(string name)
    {
        var result = IdentityResolver.Validate(
            [Entry("mick@weesky.be", name)], "mick@weesky.be", [], []);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Validate_RefusesADisplayNameOver100CharsOrWithLineBreaks()
    {
        Assert.True(IdentityResolver.Validate(
            [Entry("mick@weesky.be", new string('x', 101))], "mick@weesky.be", [], []).IsFailure);
        Assert.True(IdentityResolver.Validate(
            [Entry("mick@weesky.be", "a\r\nb")], "mick@weesky.be", [], []).IsFailure);
    }

    [Fact]
    public void Validate_RefusesTwoDefaults()
    {
        var result = IdentityResolver.Validate(
            [Entry("mick@weesky.be", "Me", isDefault: true), Entry("a@weesky.be", "A", isDefault: true)],
            "mick@weesky.be", ["a@weesky.be"], []);

        Assert.True(result.IsFailure);
        Assert.Contains("default", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_OutputsCanonicalTrimmedRows()
    {
        var result = IdentityResolver.Validate(
            [Entry("  Alias@Weesky.BE ", "  Michel  ")], "mick@weesky.be", ["alias@weesky.be"], []);

        var row = Assert.Single(result.Value);
        Assert.Equal("alias@weesky.be", row.Address);
        Assert.Equal("Michel", row.DisplayName);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter "FullyQualifiedName~IdentityResolverTests"`
Expected: compilation error — types missing.

- [ ] **Step 3: Implement**

`Models/Mail/SendingIdentityInfo.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// One resolved sending identity. LabelIsCustom tells the client whether the label comes from a
/// stored row (true) or from the account's live FullName — that flag decides whether the primary
/// belongs in a PUT payload.
/// </summary>
public sealed record SendingIdentityInfo(
    string Address, string DisplayName, bool IsDefault, bool IsPrimary, bool Stale, bool LabelIsCustom);
```

`Models/Mail/IdentityEntry.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>One identity as the client submits it. Defaults absorb explicit JSON nulls.</summary>
public sealed record IdentityEntry
{
    public string Address { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
}
```

`Services/IdentityResolver.cs`:

```csharp
using CSharpFunctionalExtensions;
using MimeKit;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The one place the identity list is derived from its three sources — stored rows, the primary
/// address, the live alias list. GET /api/Identities and MailSender both call it, so the rule
/// cannot drift between display and send.
/// </summary>
public static class IdentityResolver
{
    public const int MaxDisplayNameLength = 100;

    public static string Canonical(string address) => address.Trim().ToLowerInvariant();

    public static IReadOnlyList<SendingIdentityInfo> Resolve(
        IReadOnlyList<SendingIdentity> stored, string primaryAddress, string? fullName,
        IReadOnlyCollection<string> aliasAddresses)
    {
        var primary = Canonical(primaryAddress);
        var owned = aliasAddresses.Select(Canonical).ToHashSet();

        var primaryRow = stored.FirstOrDefault(r => Canonical(r.Address) == primary);
        var list = new List<SendingIdentityInfo>
        {
            new(primary, LabelFor(stored, primary, fullName), IsDefault: false,
                IsPrimary: true, Stale: false, LabelIsCustom: primaryRow != null),
        };

        foreach (var row in stored)
        {
            var address = Canonical(row.Address);
            if (address == primary) continue;
            list.Add(new SendingIdentityInfo(address, row.DisplayName, IsDefault: false,
                IsPrimary: false, Stale: !owned.Contains(address), LabelIsCustom: true));
        }

        // A stale row cannot hold the default; with no live marked row it falls back to the primary.
        var marked = stored.FirstOrDefault(r => r.IsDefault
            && (Canonical(r.Address) == primary || owned.Contains(Canonical(r.Address))));
        var defaultAddress = marked == null ? primary : Canonical(marked.Address);

        return list
            .Select(i => i with { IsDefault = i.Address == defaultAddress })
            .OrderByDescending(i => i.IsDefault)
            .ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Label precedence: the stored row, then the account's FullName, then the address.</summary>
    public static string LabelFor(IReadOnlyList<SendingIdentity> stored, string address, string? fullName)
    {
        var canonical = Canonical(address);
        var row = stored.FirstOrDefault(r => Canonical(r.Address) == canonical);
        if (row != null) return row.DisplayName;
        return string.IsNullOrWhiteSpace(fullName) ? canonical : fullName;
    }

    public static Result<IReadOnlyList<SendingIdentity>> Validate(
        IReadOnlyList<IdentityEntry> entries, string primaryAddress,
        IReadOnlyCollection<string> aliasAddresses, IReadOnlyCollection<string> storedAddresses)
    {
        // Stored addresses stay acceptable so a stale identity survives a save — the "never
        // silently deleted" rule — while a NEW unknown address still cannot enter.
        var allowed = aliasAddresses.Select(Canonical)
            .Concat(storedAddresses.Select(Canonical))
            .Append(Canonical(primaryAddress))
            .ToHashSet();

        var seen = new HashSet<string>();
        var defaults = 0;
        var rows = new List<SendingIdentity>();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Address) || !MailboxAddress.TryParse(entry.Address, out _))
                return Fail($"\"{entry.Address}\" is not a valid email address");

            var address = Canonical(entry.Address);
            if (!allowed.Contains(address)) return Fail($"\"{entry.Address}\" is not one of your addresses");
            if (!seen.Add(address)) return Fail($"\"{entry.Address}\" appears twice");

            var name = entry.DisplayName?.Trim() ?? string.Empty;
            if (name.Length is < 1 or > MaxDisplayNameLength)
                return Fail($"The display name for \"{entry.Address}\" must be 1 to {MaxDisplayNameLength} characters");
            if (name.Contains('\r') || name.Contains('\n'))
                return Fail($"The display name for \"{entry.Address}\" must not contain line breaks");

            if (entry.IsDefault && ++defaults > 1) return Fail("Only one identity can be the default");

            rows.Add(new SendingIdentity { Address = address, DisplayName = name, IsDefault = entry.IsDefault });
        }
        return Result.Success<IReadOnlyList<SendingIdentity>>(rows);

        static Result<IReadOnlyList<SendingIdentity>> Fail(string error) =>
            Result.Failure<IReadOnlyList<SendingIdentity>>(error);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~IdentityResolverTests"`
Expected: all PASS.

- [ ] **Step 5: Update the spec sample and commit**

In `docs/superpowers/specs/2026-07-24-webmail-identities-2c2a-design.md` §4.3, add `"labelIsCustom": …` to each entry of the JSON sample (`true` for row-backed entries, `false` for the synthesized primary) with a one-line note of what it means.

Run: `dotnet test`
Expected: all green.

```bash
git add -A && git commit -F - <<'EOF'
Backend 2c2a: IdentityResolver merge, label and validation

Pure and shared by the Identities API and MailSender; labelIsCustom
tells the client whether the primary's label is an override.
EOF
```

---

### Task 3: `IdentitiesController` — GET/PUT `/api/Identities`

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/IdentityListResponse.cs`
- Create: `src/snoopy.microservice/Models/Mail/ReplaceIdentitiesRequest.cs`
- Create: `src/snoopy.microservice/Controllers/IdentitiesController.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/IdentitiesControllerTests.cs`

**Interfaces:**
- Consumes: `ISendingIdentityStore` (Task 1); `IdentityResolver`, `IdentityEntry`, `SendingIdentityInfo` (Task 2); `IAliasesRepository.GetAliasesAsync(User)` → `IEnumerable<Alias>` (`Name`, `Domain`); `IUsersRepository.FindByEmailAsync(string)` → `User` (`FullName`); `FolderRoleStore.CanonicalAccountId(string)`; `ControllerTestHelpers.CreateAuthenticatedContext(username, domain)`.
- Produces: `GET /api/Identities` → 200 `{ identities: SendingIdentityInfo[] }`; `PUT /api/Identities` body `{ identities: IdentityEntry[] }` → 204/400/401.

- [ ] **Step 1: Write the failing tests**

`IdentitiesControllerTests.cs`:

```csharp
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class IdentitiesControllerTests
{
    private readonly Mock<ISendingIdentityStore> _store = new();
    private readonly Mock<IAliasesRepository> _aliases = new();
    private readonly Mock<IUsersRepository> _users = new();

    private IdentitiesController CreateController(
        IReadOnlyList<SendingIdentity>? stored = null, IEnumerable<Alias>? aliases = null, string? fullName = "Mick Dubois")
    {
        _store.Setup(s => s.GetAsync("mick@weesky.be", It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored ?? []);
        _aliases.Setup(a => a.GetAliasesAsync(It.IsAny<User>()))
            .ReturnsAsync(aliases ?? []);
        _users.Setup(u => u.FindByEmailAsync("mick@weesky.be"))
            .ReturnsAsync(new User("mick@weesky.be") { FullName = fullName! });

        return new IdentitiesController(_store.Object, _aliases.Object, _users.Object)
        {
            ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("mick", "weesky.be"),
        };
    }

    private static SendingIdentity Row(string address, string name, bool isDefault = false) =>
        new() { AccountId = "mick@weesky.be", Address = address, DisplayName = name, IsDefault = isDefault };

    [Fact]
    public async Task List_MergesStoredRowsWithThePrimaryAndFlagsStale()
    {
        var controller = CreateController(
            stored: [Row("michel@weesky.be", "Michel"), Row("gone@weesky.be", "Ancien")],
            aliases: [new Alias { Name = "michel", Domain = "weesky.be" }]);

        var result = await controller.List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<IdentityListResponse>(ok.Value);
        Assert.Equal(3, response.Identities.Count);
        Assert.True(Assert.Single(response.Identities, i => i.IsPrimary).IsDefault);
        Assert.True(Assert.Single(response.Identities, i => i.Address == "gone@weesky.be").Stale);
    }

    [Fact]
    public async Task Replace_ValidSet_Returns204AndWritesCanonicalRows()
    {
        var controller = CreateController(aliases: [new Alias { Name = "michel", Domain = "weesky.be" }]);
        IReadOnlyList<SendingIdentity>? written = null;
        _store.Setup(s => s.ReplaceAsync("mick@weesky.be", It.IsAny<IReadOnlyList<SendingIdentity>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<SendingIdentity>, CancellationToken>((_, rows, _) => written = rows)
            .Returns(Task.CompletedTask);

        var request = new ReplaceIdentitiesRequest
        {
            Identities = [new IdentityEntry { Address = "Michel@Weesky.BE", DisplayName = "Michel", IsDefault = true }],
        };
        var result = await controller.Replace(request, CancellationToken.None);

        Assert.Equal(204, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Equal("michel@weesky.be", Assert.Single(written!).Address);
    }

    [Fact]
    public async Task Replace_ForeignAddress_Returns400NamingIt()
    {
        var controller = CreateController();

        var request = new ReplaceIdentitiesRequest
        {
            Identities = [new IdentityEntry { Address = "intruder@evil.com", DisplayName = "X" }],
        };
        var result = await controller.Replace(request, CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("intruder@evil.com", Assert.IsType<ResultEnveloppe>(bad.Value).Message);
        _store.Verify(s => s.ReplaceAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<SendingIdentity>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Replace_AStoredStaleAddressSurvivesValidation()
    {
        var controller = CreateController(stored: [Row("gone@weesky.be", "Ancien")]);
        _store.Setup(s => s.ReplaceAsync("mick@weesky.be", It.IsAny<IReadOnlyList<SendingIdentity>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var request = new ReplaceIdentitiesRequest
        {
            Identities = [new IdentityEntry { Address = "gone@weesky.be", DisplayName = "Ancien" }],
        };

        Assert.Equal(204, Assert.IsType<StatusCodeResult>(await controller.Replace(request, CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task Replace_NullListClearsEverything()
    {
        var controller = CreateController();
        _store.Setup(s => s.ReplaceAsync("mick@weesky.be", It.IsAny<IReadOnlyList<SendingIdentity>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await controller.Replace(new ReplaceIdentitiesRequest { Identities = null! }, CancellationToken.None);

        Assert.Equal(204, Assert.IsType<StatusCodeResult>(result).StatusCode);
        _store.Verify(s => s.ReplaceAsync("mick@weesky.be",
            It.Is<IReadOnlyList<SendingIdentity>>(rows => rows.Count == 0), It.IsAny<CancellationToken>()));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter "FullyQualifiedName~IdentitiesControllerTests"`
Expected: compilation error — controller/DTOs missing.

- [ ] **Step 3: Implement DTOs and controller**

`Models/Mail/IdentityListResponse.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>The resolved sending identities, primary always included.</summary>
public sealed record IdentityListResponse(IReadOnlyList<SendingIdentityInfo> Identities);
```

`Models/Mail/ReplaceIdentitiesRequest.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>The full replacement set — PUT semantics, so order and default are atomic.</summary>
public sealed record ReplaceIdentitiesRequest
{
    public IReadOnlyList<IdentityEntry> Identities { get; init; } = [];
}
```

`Controllers/IdentitiesController.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// Curated sending identities — a webmail preference, not mail-server data. No IMAP session and
/// no credentials cookie: both verbs are database reads, so this lives outside MailController.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class IdentitiesController(
    ISendingIdentityStore store, IAliasesRepository aliases, IUsersRepository users) : ApiBaseController
{
    /// <summary>
    /// The resolved list: the primary address always (FullName label unless overridden), then
    /// every stored row; a row whose alias vanished comes back stale, never silently dropped.
    /// </summary>
    /// <response code="200">The identities, default first</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IdentityListResponse>> List(CancellationToken cancellationToken)
    {
        var (stored, aliasAddresses, fullName) = await LoadSourcesAsync(cancellationToken);
        var resolved = IdentityResolver.Resolve(stored, AuthenticatedUser.Email, fullName, aliasAddresses);
        return Ok(new IdentityListResponse(resolved));
    }

    /// <summary>
    /// Replaces the whole set. Addresses must belong to the caller (primary, a live alias, or an
    /// already-stored row — the last keeps stale identities alive across saves).
    /// </summary>
    /// <param name="request">the full identity list</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="204">Saved</response>
    /// <response code="400">A foreign, duplicate or unparsable address, a bad label, or two defaults</response>
    /// <response code="401">Not authenticated</response>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> Replace(ReplaceIdentitiesRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));

        var (stored, aliasAddresses, _) = await LoadSourcesAsync(cancellationToken);
        var validated = IdentityResolver.Validate(
            request.Identities ?? [], AuthenticatedUser.Email,
            aliasAddresses, stored.Select(r => r.Address).ToList());
        if (validated.IsFailure) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(validated.Error));

        await store.ReplaceAsync(
            FolderRoleStore.CanonicalAccountId(AuthenticatedUser.Email), validated.Value, cancellationToken);
        return NoContent();
    }

    private async Task<(IReadOnlyList<Data.Preferences.SendingIdentity> Stored, List<string> AliasAddresses, string? FullName)>
        LoadSourcesAsync(CancellationToken cancellationToken)
    {
        var accountId = FolderRoleStore.CanonicalAccountId(AuthenticatedUser.Email);
        var stored = await store.GetAsync(accountId, cancellationToken);
        var aliasList = await aliases.GetAliasesAsync(AuthenticatedUser);
        var dbUser = await users.FindByEmailAsync(AuthenticatedUser.Email);
        return (stored, aliasList.Select(a => $"{a.Name}@{a.Domain}").ToList(), dbUser?.FullName);
    }
}
```

- [ ] **Step 4: Run tests, then the full suite**

Run: `dotnet test --filter "FullyQualifiedName~IdentitiesControllerTests"` then `dotnet test`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -F - <<'EOF'
Backend 2c2a: /api/Identities GET and PUT

Merged list served resolved; PUT replaces the set after validating
ownership, labels and the single default.
EOF
```

---

### Task 4: Send from an alias — `fromAddress` end to end (backend)

**Files:**
- Modify: `src/snoopy.microservice/Models/Mail/SendMessageRequest.cs`
- Modify: `src/snoopy.microservice/Services/IMailSender.cs`
- Modify: `src/snoopy.microservice/Services/MailSender.cs`
- Modify: `src/snoopy.microservice/Services/SmtpSession.cs`
- Modify: `src/snoopy.microservice/Controllers/MailController.cs` (`SendMessage`, ~line 730)
- Test: modify `snoopy.microservice.Tests/Services/MailSenderTests.cs`, `snoopy.microservice.Tests/Controllers/MailControllerTests.cs`; create `snoopy.microservice.Tests/Services/SmtpSessionTests.cs`

**Interfaces:**
- Consumes: `IdentityResolver.Canonical/LabelFor` (Task 2), `ISendingIdentityStore` (Task 1), `IAliasesRepository`.
- Produces: `SendMessageRequest.FromAddress: string?`; `IMailSender.ForbiddenFrom = "forbidden_from"`; `MailSender` ctor gains `IAliasesRepository aliases, ISendingIdentityStore identities` (insert after `IUsersRepository users`); `SmtpSession.DescribeFailure(Exception, MimeMessage)` internal static.

- [ ] **Step 1: Write the failing tests**

In `MailSenderTests.cs`, add the two mocks to the fixture fields and `CreateSender()` (constructor argument order: `users, aliases, identities, sanitizer, staged, smtpFactory, folders, roles, messages, logger`):

```csharp
private readonly Mock<IAliasesRepository> _aliases = new();
private readonly Mock<ISendingIdentityStore> _identities = new();
```

In `CreateSender()`, before the return, add the happy-path setups and update the constructor call:

```csharp
_aliases.Setup(a => a.GetAliasesAsync(It.IsAny<User>()))
    .ReturnsAsync([new Alias { Name = "michel", Domain = "weesky.be" }]);
_identities.Setup(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync([]);

return new MailSender(_users.Object, _aliases.Object, _identities.Object, _sanitizer.Object,
    _staged.Object, _smtpFactory.Object, _folders.Object, _roles.Object, _messages.Object,
    NullLogger<MailSender>.Instance);
```

New tests (mirror the file's existing style of capturing the `MimeMessage` through `_smtp.Setup` callbacks):

```csharp
[Fact]
public async Task SendAsync_FromAlias_UsesTheAliasWithItsStoredLabel()
{
    var sender = CreateSender();
    _identities.Setup(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync([new SendingIdentity { Address = "michel@weesky.be", DisplayName = "Michel Dubois" }]);
    MimeMessage? sent = null;
    _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
        .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
        .ReturnsAsync(Result.Success());

    var result = await sender.SendAsync(_user, "pw",
        Request() with { FromAddress = " Michel@Weesky.BE " }, CancellationToken.None);

    Assert.True(result.IsSuccess);
    var from = Assert.IsType<MailboxAddress>(sent!.From[0]);
    Assert.Equal("michel@weesky.be", from.Address);
    Assert.Equal("Michel Dubois", from.Name);
}

[Fact]
public async Task SendAsync_FromAliasWithoutARow_FallsBackToTheFullName()
{
    var sender = CreateSender();
    MimeMessage? sent = null;
    _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
        .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
        .ReturnsAsync(Result.Success());

    var result = await sender.SendAsync(_user, "pw",
        Request() with { FromAddress = "michel@weesky.be" }, CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Equal("Mick", Assert.IsType<MailboxAddress>(sent!.From[0]).Name);
}

[Fact]
public async Task SendAsync_ForeignFrom_FailsBeforeAnySmtpConnection()
{
    var sender = CreateSender();

    var result = await sender.SendAsync(_user, "pw",
        Request() with { FromAddress = "intruder@evil.com" }, CancellationToken.None);

    Assert.True(result.IsFailure);
    Assert.Equal(IMailSender.ForbiddenFrom, result.Error);
    _smtpFactory.Verify(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
        Times.Never);
}

[Fact]
public async Task SendAsync_NoFromAddress_KeepsThePrimaryBehaviour()
{
    var sender = CreateSender();
    MimeMessage? sent = null;
    _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
        .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
        .ReturnsAsync(Result.Success());

    Assert.True((await sender.SendAsync(_user, "pw", Request(), CancellationToken.None)).IsSuccess);
    Assert.Equal("mick@weesky.be", Assert.IsType<MailboxAddress>(sent!.From[0]).Address);
}
```

Add the needed `using weesky.Snoopy.Microservice.Data.Preferences;` if missing.

New `SmtpSessionTests.cs`:

```csharp
using MailKit.Net.Smtp;
using MimeKit;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class SmtpSessionTests
{
    private static MimeMessage MessageFrom(string address)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("X", address));
        return message;
    }

    [Fact]
    public void DescribeFailure_NamesTheSenderOnASenderRejection()
    {
        var ex = new SmtpCommandException(SmtpErrorCode.SenderNotAccepted,
            SmtpStatusCode.MailboxNameNotAllowed, "denied");

        Assert.Equal("The mail server refused to send from michel@weesky.be",
            SmtpSession.DescribeFailure(ex, MessageFrom("michel@weesky.be")));
    }

    [Fact]
    public void DescribeFailure_StaysGenericForAnythingElse()
    {
        Assert.Equal("The mail server refused the message",
            SmtpSession.DescribeFailure(new InvalidOperationException("boom"), MessageFrom("a@b.c")));
    }
}
```

In `MailControllerTests.cs`, beside the existing `SendMessage_*` tests (~line 1427):

```csharp
[Fact]
public async Task SendMessage_RefusesAnInvalidFromAddress()
{
    var request = new SendMessageRequest { To = ["ok@example.com"], FromAddress = "not-an-address" };

    var result = await CreateController().SendMessage(request, CancellationToken.None);

    var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
    Assert.Equal(400, bad.StatusCode);
}

[Fact]
public async Task SendMessage_NamesTheForbiddenFrom()
{
    _sender.Setup(s => s.SendAsync(It.IsAny<User>(), "hunter2", It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Failure<SendMessageResult>(IMailSender.ForbiddenFrom));
    var request = new SendMessageRequest { To = ["ok@example.com"], FromAddress = "other@weesky.be" };

    var result = await CreateController().SendMessage(request, CancellationToken.None);

    var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
    Assert.Equal(400, bad.StatusCode);
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test --filter "FullyQualifiedName~MailSenderTests|FullyQualifiedName~SmtpSessionTests|FullyQualifiedName~MailControllerTests"`
Expected: compilation errors (`FromAddress`, `ForbiddenFrom`, `DescribeFailure`, new ctor args missing).

- [ ] **Step 3: Implement**

`SendMessageRequest.cs` — add below `HtmlBody` and update the class doc (threading stays for 2c2b):

```csharp
/// <summary>Identity to send as. Null/empty means the primary address — the 2c1 behaviour.</summary>
public string? FromAddress { get; init; }
```

`IMailSender.cs` — add beside `UnknownAttachment`:

```csharp
/// <summary>Returned when the requested From is neither the primary address nor a live alias.</summary>
const string ForbiddenFrom = "forbidden_from";
```

`MailSender.cs`:
1. Add fields + ctor params `IAliasesRepository aliases, ISendingIdentityStore identities` (after `users`, keeping the file's explicit-field style: `_aliases`, `_identities`).
2. In `SendAsync`, right after `var accountId = …`, resolve and validate the From (before staged resolution is fine, but keep it before `BuildMessageAsync`):

```csharp
var fromAddress = IdentityResolver.Canonical(user.Email);
if (!string.IsNullOrWhiteSpace(request.FromAddress))
{
    var requested = IdentityResolver.Canonical(request.FromAddress);
    if (requested != fromAddress)
    {
        var owned = await _aliases.GetAliasesAsync(user);
        // The alias list, not the identity table: it alone says what the user really owns.
        if (!owned.Any(a => IdentityResolver.Canonical($"{a.Name}@{a.Domain}") == requested))
            return Result.Failure<SendMessageResult>(IMailSender.ForbiddenFrom);
    }
    fromAddress = requested;
}
```

3. Pass it through: `BuildMessageAsync(user, request, attachments, accountId, fromAddress, cancellationToken)`; in `BuildMessageAsync`, replace the `message.From.Add(...)` line and its `dbUser` context with:

```csharp
var stored = await _identities.GetAsync(accountId, cancellationToken);
var label = IdentityResolver.LabelFor(stored, fromAddress, dbUser?.FullName);
// LabelFor falls back to the address itself; on the wire that would be a redundant "a@x <a@x>".
message.From.Add(new MailboxAddress(label == fromAddress ? string.Empty : label, fromAddress));
```

`SmtpSession.cs` — replace the generic catch's message with the helper, and add it:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "SMTP refused the message");
    return Result.Failure(DescribeFailure(ex, message));
}
```

```csharp
/// <summary>
/// A sender rejection names the address: with alias identities the likely cause is Postfix's
/// smtpd_sender_login_maps not allowing that From, and the user must see it is a server rule.
/// </summary>
internal static string DescribeFailure(Exception ex, MimeMessage message)
{
    if (ex is SmtpCommandException { ErrorCode: SmtpErrorCode.SenderNotAccepted })
    {
        var sender = message.From.Mailboxes.FirstOrDefault()?.Address;
        if (sender != null) return $"The mail server refused to send from {sender}";
    }
    return "The mail server refused the message";
}
```

(`InternalsVisibleTo` for the test project already exists — the tests reach other internals; verify with `grep -r InternalsVisibleTo src/snoopy.microservice --include=*.cs --include=*.csproj`.)

`MailController.cs` `SendMessage` — after the recipient validation loop:

```csharp
if (!string.IsNullOrWhiteSpace(request.FromAddress)
    && !MailboxAddress.TryParse(RecipientParserOptions, request.FromAddress, out _))
    return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(
        $"\"{request.FromAddress}\" is not a valid email address"));
```

and after the `UnknownAttachment` mapping:

```csharp
if (result.IsFailure && result.Error == IMailSender.ForbiddenFrom)
    return BadRequest(ResultEnveloppe.CreateErrorEnveloppe(
        $"Sending from \"{request.FromAddress}\" is not allowed on this account"));
```

Update the endpoint's `<summary>`/`<response code="400">` XML to mention the From rules.

- [ ] **Step 4: Run tests, then the full suite**

Run: `dotnet test`
Expected: all green (new + existing `MailSenderTests` untouched assertions still pass).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -F - <<'EOF'
Backend 2c2a: send as a curated identity

fromAddress validated against owned addresses, label resolved
server-side; a Postfix sender rejection now names the address.
EOF
```

---

### Task 5: Settings → Identities (api, hooks, page, dialog, nav, styles)

**Files:**
- Modify: `src/frontend/src/api.js` (api object, beside `getAliases`)
- Modify: `src/frontend/src/modules/mail/api/mailTypes.ts`
- Modify: `src/frontend/src/modules/mail/queries.ts` (mailKeys + hooks)
- Create: `src/frontend/src/modules/settings/identities/identityRows.ts`
- Create: `src/frontend/src/modules/settings/identities/IdentitiesPage.tsx`
- Create: `src/frontend/src/modules/settings/identities/AddIdentityDialog.tsx`
- Modify: `src/frontend/src/modules/settings/SettingsLayout.tsx` (NavLink between Aliases and Rules)
- Modify: `src/frontend/src/routes.tsx` (lazy route `identities`)
- Modify: `src/frontend/src/index.css` (settings styles live there)
- Test: `identityRows.test.ts`, `IdentitiesPage.test.tsx`, `AddIdentityDialog.test.tsx` (same folder)

**Interfaces:**
- Consumes: `GET/PUT /api/Identities` (Task 3 shapes), `api.getAliases()` → `{ name, domain }[]`, `useAccountId()`, `useAuth().identity.displayName`, `useToasts`/`Toasts`, `StarIcon {size, filled}`, `PencilIcon`, `TrashIcon`, `.modal-overlay/.modal/.modal-header/.modal-title/.modal-close` shell, `.settings-page`, `.btn .btn-ghost/.btn-primary`.
- Produces:
  - `mailTypes.ts`: `interface SendingIdentity { address: string; displayName: string; isDefault: boolean; isPrimary: boolean; stale: boolean; labelIsCustom: boolean }`, `interface IdentityListResponse { identities: SendingIdentity[] }`, `interface AliasInfo { name: string; domain: string }`
  - `queries.ts`: `useIdentities()` (select → `SendingIdentity[]`), `useReplaceIdentities()` (mutationFn takes `IdentityRow[]`), `useAliases()`
  - `identityRows.ts`: `interface IdentityRow { address: string; displayName: string; isDefault: boolean }`, `toRows`, `withDefault`, `withLabel`, `without`, `withAdded` — all `(identities: SendingIdentity[], …) => IdentityRow[]`. **Task 6 does not depend on these.**

- [ ] **Step 1: Write the failing pure-logic tests**

`identityRows.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { toRows, withDefault, withLabel, without, withAdded } from './identityRows'
import type { SendingIdentity } from '../../mail/api/mailTypes'

function identity(over: Partial<SendingIdentity>): SendingIdentity {
  return {
    address: 'a@x.be', displayName: 'A', isDefault: false,
    isPrimary: false, stale: false, labelIsCustom: true, ...over,
  }
}
const primary = identity({ address: 'mick@x.be', displayName: 'Mick', isPrimary: true, isDefault: true, labelIsCustom: false })
const alias = identity({ address: 'michel@x.be', displayName: 'Michel' })

describe('toRows', () => {
  it('excludes a primary whose label is not overridden', () => {
    expect(toRows([primary, alias])).toEqual([
      { address: 'michel@x.be', displayName: 'Michel', isDefault: false },
    ])
  })

  it('keeps a primary carrying a label override', () => {
    const overridden = { ...primary, displayName: 'Le Boss', labelIsCustom: true }
    expect(toRows([overridden])).toEqual([{ address: 'mick@x.be', displayName: 'Le Boss', isDefault: true }])
  })
})

describe('withDefault', () => {
  it('marks the chosen alias as the only default', () => {
    expect(withDefault([primary, alias], 'michel@x.be')).toEqual([
      { address: 'michel@x.be', displayName: 'Michel', isDefault: true },
    ])
  })

  // No marked row means "the primary is the default" — choosing it just demarcates the others.
  it('choosing the primary produces no marked row', () => {
    const aliasDefault = { ...alias, isDefault: true }
    expect(withDefault([{ ...primary, isDefault: false }, aliasDefault], 'mick@x.be')).toEqual([
      { address: 'michel@x.be', displayName: 'Michel', isDefault: false },
    ])
  })
})

describe('withLabel', () => {
  it('renames an alias', () => {
    expect(withLabel([primary, alias], 'michel@x.be', ' Michel D. ')).toEqual([
      { address: 'michel@x.be', displayName: 'Michel D.', isDefault: false },
    ])
  })

  it('overriding the primary label adds its row', () => {
    expect(withLabel([primary, alias], 'mick@x.be', 'Le Boss')).toEqual([
      { address: 'mick@x.be', displayName: 'Le Boss', isDefault: true },
      { address: 'michel@x.be', displayName: 'Michel', isDefault: false },
    ])
  })

  // Clearing the primary's label removes its row, falling back to FullName — a PUT never
  // carries an empty label, which validation would refuse.
  it('clearing the primary label drops its row', () => {
    const overridden = { ...primary, displayName: 'Le Boss', labelIsCustom: true }
    expect(withLabel([overridden, alias], 'mick@x.be', '  ')).toEqual([
      { address: 'michel@x.be', displayName: 'Michel', isDefault: false },
    ])
  })

  it('clearing an alias label keeps the old one', () => {
    expect(withLabel([primary, alias], 'michel@x.be', '')).toEqual([
      { address: 'michel@x.be', displayName: 'Michel', isDefault: false },
    ])
  })
})

describe('without / withAdded', () => {
  it('removes an identity', () => {
    expect(without([primary, alias], 'michel@x.be')).toEqual([])
  })

  it('appends a new identity, never as default', () => {
    expect(withAdded([primary, alias], 'support@x.be', ' Support ')).toEqual([
      { address: 'michel@x.be', displayName: 'Michel', isDefault: false },
      { address: 'support@x.be', displayName: 'Support', isDefault: false },
    ])
  })
})
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/modules/settings/identities/identityRows.test.ts` from `src/frontend`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `identityRows.ts`**

```ts
import type { SendingIdentity } from '../../mail/api/mailTypes'

export interface IdentityRow { address: string; displayName: string; isDefault: boolean }

/** The PUT payload from the displayed list. The primary appears only when its label is
    overridden: absence of its row is what lets the label keep following FullName, and absence
    of any marked row is what "the primary is the default" looks like on the wire. */
export function toRows(identities: SendingIdentity[]): IdentityRow[] {
  return identities
    .filter(i => !i.isPrimary || i.labelIsCustom)
    .map(i => ({ address: i.address, displayName: i.displayName, isDefault: i.isDefault }))
}

export function withDefault(identities: SendingIdentity[], address: string): IdentityRow[] {
  const target = identities.find(i => i.address === address)
  const cleared = toRows(identities).map(r => ({ ...r, isDefault: false }))
  if (!target || target.isPrimary) return cleared
  return cleared.map(r => (r.address === address ? { ...r, isDefault: true } : r))
}

export function withLabel(identities: SendingIdentity[], address: string, label: string): IdentityRow[] {
  const trimmed = label.trim()
  const target = identities.find(i => i.address === address)
  if (!target) return toRows(identities)
  if (trimmed === '') {
    if (target.isPrimary) return toRows(identities.map(i => (i.address === address ? { ...i, labelIsCustom: false } : i)))
    return toRows(identities) // an alias label cannot be empty; keep the old one
  }
  return toRows(identities.map(i =>
    i.address === address ? { ...i, displayName: trimmed, labelIsCustom: true } : i))
}

export function without(identities: SendingIdentity[], address: string): IdentityRow[] {
  return toRows(identities.filter(i => i.address !== address))
}

export function withAdded(identities: SendingIdentity[], address: string, label: string): IdentityRow[] {
  return [...toRows(identities), { address, displayName: label.trim(), isDefault: false }]
}
```

- [ ] **Step 4: Run the pure tests**

Run: `npx vitest run src/modules/settings/identities/identityRows.test.ts`
Expected: PASS.

- [ ] **Step 5: api, types, hooks**

`mailTypes.ts` — append:

```ts
export interface SendingIdentity {
  address: string
  displayName: string
  isDefault: boolean
  isPrimary: boolean
  stale: boolean
  labelIsCustom: boolean
}

export interface IdentityListResponse { identities: SendingIdentity[] }

export interface AliasInfo { name: string; domain: string }
```

`api.js` — in the `api` object beside `getAliases`:

```js
getIdentities: () =>
  request('GET', '/api/Identities'),

putIdentities: (identities) =>
  request('PUT', '/api/Identities', { identities }),
```

`queries.ts` — add to `mailKeys`:

```ts
identities: (accountId: string) => ['mail', accountId, 'identities'] as const,
aliases: (accountId: string) => ['mail', accountId, 'aliases'] as const,
```

and the hooks (import `IdentityListResponse`, `SendingIdentity`, `AliasInfo` from `./api/mailTypes`; `IdentityRow` shape is declared inline to keep queries.ts free of a settings import):

```ts
/** The curated From list. Long staleTime: it changes only from Settings, which invalidates it. */
export function useIdentities() {
  const accountId = useAccountId()
  return useQuery({
    queryKey: mailKeys.identities(accountId),
    queryFn: () => api.getIdentities() as Promise<IdentityListResponse>,
    select: (data): SendingIdentity[] => data.identities,
    staleTime: 5 * 60_000,
  })
}

export function useReplaceIdentities() {
  const accountId = useAccountId()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (identities: { address: string; displayName: string; isDefault: boolean }[]) =>
      api.putIdentities(identities) as Promise<void>,
    // Settled, not success: after a refused PUT the page must fall back to the server's state.
    onSettled: () => queryClient.invalidateQueries({ queryKey: mailKeys.identities(accountId) }),
  })
}

export function useAliases(enabled = true) {
  const accountId = useAccountId()
  return useQuery({
    queryKey: mailKeys.aliases(accountId),
    queryFn: () => api.getAliases() as Promise<AliasInfo[]>,
    enabled,
    staleTime: 5 * 60_000,
  })
}
```

- [ ] **Step 6: Write the failing component tests**

`AddIdentityDialog.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import AddIdentityDialog from './AddIdentityDialog'
import { useAliases } from '../../mail/queries'

vi.mock('../../mail/queries', () => ({ useAliases: vi.fn() }))

const aliases = [
  { name: 'michel', domain: 'weesky.be' },
  { name: 'support', domain: 'weesky.be' },
  { name: 'taken', domain: 'weesky.be' },
]

describe('AddIdentityDialog', () => {
  beforeEach(() => {
    vi.mocked(useAliases).mockReturnValue({ data: aliases, isLoading: false } as never)
  })

  function renderDialog(over: Partial<Parameters<typeof AddIdentityDialog>[0]> = {}) {
    const onAdd = vi.fn()
    render(<AddIdentityDialog
      taken={['taken@weesky.be']} defaultName="Mick Dubois"
      onAdd={onAdd} onClose={vi.fn()} {...over} />)
    return onAdd
  }

  it('lists the aliases minus the ones already taken, with a count', () => {
    renderDialog()
    expect(screen.getByText('michel@weesky.be')).toBeInTheDocument()
    expect(screen.queryByText('taken@weesky.be')).toBeNull()
    expect(screen.getByText('2 of 2 aliases')).toBeInTheDocument()
  })

  it('filters as the user types', () => {
    renderDialog()
    fireEvent.change(screen.getByLabelText('Search your aliases'), { target: { value: 'mich' } })
    expect(screen.getByText('michel@weesky.be')).toBeInTheDocument()
    expect(screen.queryByText('support@weesky.be')).toBeNull()
    expect(screen.getByText('1 of 2 aliases')).toBeInTheDocument()
  })

  it('pre-fills the display name and adds the selected alias', () => {
    const onAdd = renderDialog()
    fireEvent.click(screen.getByText('michel@weesky.be'))
    expect(screen.getByLabelText('Display name')).toHaveValue('Mick Dubois')
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'Michel D.' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add' }))
    expect(onAdd).toHaveBeenCalledWith('michel@weesky.be', 'Michel D.')
  })

  it('disables Add until an alias is selected and a name is present', () => {
    renderDialog()
    expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled()
    fireEvent.click(screen.getByText('michel@weesky.be'))
    fireEvent.change(screen.getByLabelText('Display name'), { target: { value: '  ' } })
    expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled()
  })
})
```

`IdentitiesPage.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import IdentitiesPage from './IdentitiesPage'
import { useIdentities, useReplaceIdentities, useAliases } from '../../mail/queries'
import type { SendingIdentity } from '../../mail/api/mailTypes'

vi.mock('../../mail/queries', () => ({
  useIdentities: vi.fn(), useReplaceIdentities: vi.fn(), useAliases: vi.fn(),
}))
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({ identity: { displayName: 'Mick Dubois', email: 'mick@weesky.be' } }),
}))

const identities: SendingIdentity[] = [
  { address: 'mick@weesky.be', displayName: 'Mick Dubois', isDefault: true, isPrimary: true, stale: false, labelIsCustom: false },
  { address: 'michel@weesky.be', displayName: 'Michel', isDefault: false, isPrimary: false, stale: false, labelIsCustom: true },
  { address: 'gone@weesky.be', displayName: 'Ancien', isDefault: false, isPrimary: false, stale: true, labelIsCustom: true },
]

describe('IdentitiesPage', () => {
  const mutate = vi.fn()

  beforeEach(() => {
    mutate.mockClear()
    vi.mocked(useIdentities).mockReturnValue({ data: identities, isLoading: false, isError: false } as never)
    vi.mocked(useReplaceIdentities).mockReturnValue({ mutate, isPending: false } as never)
    vi.mocked(useAliases).mockReturnValue({ data: [], isLoading: false } as never)
  })

  it('renders each identity with its address and tags', () => {
    render(<IdentitiesPage />)
    expect(screen.getByText('mick@weesky.be')).toBeInTheDocument()
    expect(screen.getByText('primary')).toBeInTheDocument()
    expect(screen.getByText('unavailable')).toBeInTheDocument()
  })

  it('moving the default saves the whole list', () => {
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Make michel@weesky.be the default' }))
    expect(mutate).toHaveBeenCalledWith(
      [{ address: 'michel@weesky.be', displayName: 'Michel', isDefault: true },
       { address: 'gone@weesky.be', displayName: 'Ancien', isDefault: false }],
      expect.anything())
  })

  it('renaming an identity commits on Enter', () => {
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Rename michel@weesky.be' }))
    const input = screen.getByLabelText('Display name for michel@weesky.be')
    fireEvent.change(input, { target: { value: 'Michel D.' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(mutate).toHaveBeenCalledWith(
      [{ address: 'michel@weesky.be', displayName: 'Michel D.', isDefault: false },
       { address: 'gone@weesky.be', displayName: 'Ancien', isDefault: false }],
      expect.anything())
  })

  it('removing an identity keeps the others', () => {
    render(<IdentitiesPage />)
    fireEvent.click(screen.getByRole('button', { name: 'Remove gone@weesky.be' }))
    expect(mutate).toHaveBeenCalledWith(
      [{ address: 'michel@weesky.be', displayName: 'Michel', isDefault: false }],
      expect.anything())
  })

  it('a stale identity offers no star and no rename', () => {
    render(<IdentitiesPage />)
    expect(screen.queryByRole('button', { name: 'Make gone@weesky.be the default' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Rename gone@weesky.be' })).toBeNull()
  })

  it('the primary has no remove button', () => {
    render(<IdentitiesPage />)
    expect(screen.queryByRole('button', { name: 'Remove mick@weesky.be' })).toBeNull()
  })
})
```

- [ ] **Step 7: Run to verify failure**

Run: `npx vitest run src/modules/settings/identities/`
Expected: the two component files FAIL (components missing), identityRows PASSES.

- [ ] **Step 8: Implement the dialog and the page**

`AddIdentityDialog.tsx`:

```tsx
import { useState } from 'react'
import { useAliases } from '../../mail/queries'

interface Props {
  taken: string[]
  defaultName: string
  onAdd: (address: string, displayName: string) => void
  onClose: () => void
}

/** Where the hundred aliases live — a filterable picker, never the From menu. */
export default function AddIdentityDialog({ taken, defaultName, onAdd, onClose }: Props) {
  const { data: aliases, isLoading } = useAliases()
  const [query, setQuery] = useState('')
  const [selected, setSelected] = useState<string | null>(null)
  const [name, setName] = useState(defaultName)

  const available = (aliases ?? [])
    .map(a => `${a.name}@${a.domain}`.toLowerCase())
    .filter(address => !taken.includes(address))
  const needle = query.trim().toLowerCase()
  const matches = available.filter(address => address.includes(needle))

  return (
    <div className="modal-overlay">
      <div className="modal identity-add-modal">
        <div className="modal-header">
          <span className="modal-title">Add identity</span>
          <button className="modal-close" aria-label="Close" onClick={onClose}>✕</button>
        </div>
        <label className="identity-add-label" htmlFor="identity-search">Search your aliases</label>
        <input
          id="identity-search" type="text" autoFocus value={query}
          onChange={e => setQuery(e.target.value)}
        />
        <div className="identity-add-count">{isLoading ? 'Loading…' : `${matches.length} of ${available.length} aliases`}</div>
        <ul className="identity-add-list">
          {matches.map(address => (
            <li key={address}>
              <button
                type="button"
                className={`identity-add-option${selected === address ? ' is-selected' : ''}`}
                onClick={() => setSelected(address)}
              >
                {address}
              </button>
            </li>
          ))}
        </ul>
        <label className="identity-add-label" htmlFor="identity-name">Display name</label>
        <input id="identity-name" type="text" value={name} onChange={e => setName(e.target.value)} />
        <div className="modal-actions">
          <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
          <button
            type="button" className="btn btn-primary"
            disabled={!selected || name.trim() === ''}
            onClick={() => onAdd(selected!, name.trim())}
          >
            Add
          </button>
        </div>
      </div>
    </div>
  )
}
```

Note: `.modal-actions` currently lives in `mail.css`; move that one-liner to `index.css` if the dialog renders without it, rather than duplicating the rule.

`IdentitiesPage.tsx`:

```tsx
import { useState } from 'react'
import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import { useAuth } from '../../../contexts/AuthContext'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import StarIcon from '../../../icons/StarIcon'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import { useIdentities, useReplaceIdentities } from '../../mail/queries'
import AddIdentityDialog from './AddIdentityDialog'
import { withAdded, withDefault, withLabel, without, type IdentityRow } from './identityRows'

/**
 * The curated From list. Every action saves the whole set (PUT semantics); on failure the
 * query invalidation falls back to the server's state, so the page never keeps a refused edit.
 */
export default function IdentitiesPage() {
  const { identity } = useAuth()
  const { data: identities, isLoading, isError } = useIdentities()
  const replace = useReplaceIdentities()
  const { toasts, addToast, removeToast } = useToasts()
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<string | null>(null)
  const [draft, setDraft] = useState('')

  function save(rows: IdentityRow[]) {
    replace.mutate(rows, {
      onError: (error: Error) => addToast(error.message || 'Could not save your identities', 'error'),
    })
  }

  function commitLabel(address: string) {
    setEditing(null)
    if (identities) save(withLabel(identities, address, draft))
  }

  return (
    <div className="settings-page">
      <h1>Identities</h1>
      <p className="identities-hint">
        The addresses you can write from, each with its own name. Add one from your aliases —
        removing an identity never touches the alias itself.
      </p>

      {isLoading && <p>Loading…</p>}
      {!isLoading && (isError || !identities) && <p>Could not load your identities.</p>}
      {!isLoading && !isError && identities && (
        <>
          <ul className="identity-list">
            {identities.map(i => (
              <li key={i.address} className={`identity-row${i.stale ? ' is-stale' : ''}`}>
                {i.stale ? (
                  <span className="identity-star" aria-hidden="true" />
                ) : (
                  <button
                    type="button" className="identity-star"
                    aria-label={i.isDefault ? `${i.address} is the default` : `Make ${i.address} the default`}
                    aria-pressed={i.isDefault}
                    disabled={i.isDefault}
                    onClick={() => save(withDefault(identities, i.address))}
                  >
                    <StarIcon size={18} filled={i.isDefault} />
                  </button>
                )}
                {editing === i.address ? (
                  <input
                    autoFocus type="text" className="identity-name-input" value={draft}
                    aria-label={`Display name for ${i.address}`}
                    onChange={e => setDraft(e.target.value)}
                    onBlur={() => commitLabel(i.address)}
                    onKeyDown={e => {
                      if (e.key === 'Enter') commitLabel(i.address)
                      if (e.key === 'Escape') setEditing(null)
                    }}
                  />
                ) : (
                  <span className="identity-name">{i.displayName}</span>
                )}
                <span className="identity-address">{i.address}</span>
                {i.isPrimary && <span className="identity-tag">primary</span>}
                {i.stale && <span className="identity-tag">unavailable</span>}
                {!i.stale && (
                  <button
                    type="button" className="identity-action" aria-label={`Rename ${i.address}`}
                    onClick={() => { setEditing(i.address); setDraft(i.displayName) }}
                  >
                    <PencilIcon size={15} />
                  </button>
                )}
                {!i.isPrimary && (
                  <button
                    type="button" className="identity-action is-danger" aria-label={`Remove ${i.address}`}
                    title="Removes the identity only — the alias itself is kept"
                    onClick={() => save(without(identities, i.address))}
                  >
                    <TrashIcon size={15} />
                  </button>
                )}
              </li>
            ))}
          </ul>
          <button type="button" className="btn btn-ghost" onClick={() => setAdding(true)}>+ Add identity</button>
          {adding && (
            <AddIdentityDialog
              taken={identities.map(i => i.address)}
              defaultName={identity?.displayName ?? ''}
              onClose={() => setAdding(false)}
              onAdd={(address, name) => { save(withAdded(identities, address, name)); setAdding(false) }}
            />
          )}
        </>
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
```

Check `PencilIcon`/`TrashIcon` props (`.jsx` files — read them; if they take `size`, keep as written, else match their actual API). Note the star: the **default identity's star is disabled** (clicking the current default is a no-op, not a demarcation — demarcating happens by choosing another identity); for the primary the button stays enabled when it is not default.

Wait — test `moving the default saves the whole list` clicks the alias's star while primary is default; consistent. The primary's star when default: disabled, label "mick@weesky.be is the default". A test asserting the primary-click path (`choosing the primary produces no marked row`) is covered by the pure `withDefault` test; the page exposes it when an alias holds the default (primary's star enabled then).

- [ ] **Step 9: Navigation, route, styles**

`SettingsLayout.tsx` — between Aliases and Rules:

```tsx
<NavLink to="/settings/identities" className={paneClass}>Identities</NavLink>
```

`routes.tsx` — beside the AliasesPage lazy import:

```tsx
const IdentitiesPage = lazy(() => import('./modules/settings/identities/IdentitiesPage'))
```

and the route between `aliases` and `rules`:

```tsx
{ path: 'identities', element: <Suspense fallback={null}><IdentitiesPage /></Suspense> },
```

`index.css` — after the aliases-page block, using existing role tokens only:

```css
/* ── Identities page ─────────────────────────────────────── */

.identities-hint { color: var(--text-muted); font-size: 13px; max-width: 560px; margin-bottom: 16px; }

.identity-list { list-style: none; margin: 0 0 16px; padding: 0; max-width: 640px; }

.identity-row {
  display: flex; align-items: center; gap: 10px;
  padding: 8px 10px; border-bottom: 1px solid var(--border-subtle);
}

.identity-row.is-stale .identity-name,
.identity-row.is-stale .identity-address { color: var(--text-muted); }

.identity-star {
  background: none; border: none; padding: 2px; cursor: pointer;
  color: var(--badge-count-bg); display: inline-flex; width: 24px; justify-content: center;
}
.identity-star:disabled { cursor: default; }

.identity-name { font-weight: 600; }
.identity-name-input { max-width: 200px; }
.identity-address { color: var(--text-muted); font-size: 13px; flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; }

.identity-tag {
  font-size: 11px; text-transform: uppercase; letter-spacing: 0.04em;
  color: var(--text-muted); border: 1px solid var(--border-subtle);
  border-radius: 999px; padding: 1px 8px;
}

.identity-action {
  background: none; border: none; padding: 4px; cursor: pointer;
  color: var(--text-muted); border-radius: 6px; display: inline-flex;
}
.identity-action:hover { background: var(--pane-item-hover); color: var(--text-primary); }
.identity-action.is-danger:hover { color: var(--danger-fg, #c0392b); }

.identity-add-modal { max-width: 440px; }
.identity-add-label { display: block; font-size: 13px; color: var(--text-muted); margin: 12px 0 4px; }
.identity-add-count { font-size: 12px; color: var(--text-muted); margin: 6px 0; }
.identity-add-list {
  list-style: none; margin: 0; padding: 0;
  max-height: 220px; overflow-y: auto; border: 1px solid var(--border-subtle); border-radius: 8px;
}
.identity-add-option {
  display: block; width: 100%; text-align: left; background: none; border: none;
  padding: 7px 10px; cursor: pointer; font-size: 13px; color: var(--text-primary);
}
.identity-add-option:hover { background: var(--pane-item-hover); }
.identity-add-option.is-selected { background: var(--pane-item-active, var(--pane-item-hover)); font-weight: 600; }
```

Before using any `var(--…)` above, verify each token exists (`grep -o "\-\-[a-z-]*" src/frontend/src/styles/theme-day.css | sort -u`); replace any missing one with the nearest existing role token — do not invent new palette tokens.

- [ ] **Step 10: Run everything and commit**

Run: `npx vitest run src/modules/settings/identities/` then `npm test`, `npm run lint`, `npm run build`
Expected: all green.

```bash
git add -A && git commit -F - <<'EOF'
Frontend 2c2a: Settings > Identities

Curated list with default star, inline rename and removal; adding
filters the aliases in a dialog. PUT replaces the whole set.
EOF
```

---

### Task 6: `IdentitySelect` in the composer

**Files:**
- Create: `src/frontend/src/modules/mail/compose/IdentitySelect.tsx`
- Modify: `src/frontend/src/modules/mail/compose/ComposeView.tsx`
- Modify: `src/frontend/src/styles/mail.css` (compose styles live there)
- Test: `IdentitySelect.test.tsx` (new), `ComposeView.test.tsx` (modify)

**Interfaces:**
- Consumes: `useIdentities()` (Task 5), `SendingIdentity` (mailTypes), `DropdownMenu {ariaLabel, trigger, items, className}` with `MenuItem {label, onSelect}`, `ChevronRightIcon`, existing ComposeView internals (`markDirty`, `dirty`, `submit` payload).
- Produces: `IdentitySelect({ identities: SendingIdentity[]; value: string | null; onChange: (address: string) => void })`; `sendMessage` payload gains `fromAddress`.

- [ ] **Step 1: Write the failing `IdentitySelect` tests**

`IdentitySelect.test.tsx`:

```tsx
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import IdentitySelect from './IdentitySelect'
import type { SendingIdentity } from '../api/mailTypes'

function identity(over: Partial<SendingIdentity>): SendingIdentity {
  return {
    address: 'mick@weesky.be', displayName: 'Mick', isDefault: true,
    isPrimary: true, stale: false, labelIsCustom: false, ...over,
  }
}
const primary = identity({})
const alias = identity({ address: 'michel@weesky.be', displayName: 'Michel', isDefault: false, isPrimary: false })

describe('IdentitySelect', () => {
  it('renders plain text with a single identity — the 2c1 look', () => {
    render(<IdentitySelect identities={[primary]} value="mick@weesky.be" onChange={vi.fn()} />)
    expect(screen.getByText('Mick (mick@weesky.be)')).toBeInTheDocument()
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('offers a menu with several identities and reports the pick', () => {
    const onChange = vi.fn()
    render(<IdentitySelect identities={[primary, alias]} value="mick@weesky.be" onChange={onChange} />)
    fireEvent.click(screen.getByRole('button', { name: 'From identity' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Michel <michel@weesky.be>' }))
    expect(onChange).toHaveBeenCalledWith('michel@weesky.be')
  })

  it('shows the selected identity on the trigger', () => {
    render(<IdentitySelect identities={[primary, alias]} value="michel@weesky.be" onChange={vi.fn()} />)
    expect(screen.getByRole('button', { name: 'From identity' })).toHaveTextContent('Michel (michel@weesky.be)')
  })

  it('never proposes a stale identity', () => {
    const stale = identity({ address: 'gone@weesky.be', displayName: 'Ancien', isDefault: false, isPrimary: false, stale: true })
    render(<IdentitySelect identities={[primary, alias, stale]} value="mick@weesky.be" onChange={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: 'From identity' }))
    expect(screen.queryByRole('menuitem', { name: /gone@weesky.be/ })).toBeNull()
  })
})
```

- [ ] **Step 2: Run to verify failure**

Run: `npx vitest run src/modules/mail/compose/IdentitySelect.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `IdentitySelect.tsx`**

```tsx
import DropdownMenu from '../../../components/DropdownMenu'
import ChevronRightIcon from '../../../icons/ChevronRightIcon'
import type { SendingIdentity } from '../api/mailTypes'

interface Props {
  identities: SendingIdentity[]
  value: string | null
  onChange: (address: string) => void
}

/** The From line. One identity renders as plain text — the 2c1 look for whoever curated
    nothing; several become a menu. Stale identities are filtered by the caller's hook data. */
export default function IdentitySelect({ identities, value, onChange }: Props) {
  const usable = identities.filter(i => !i.stale)
  const current = usable.find(i => i.address === value) ?? usable.find(i => i.isDefault) ?? usable[0]
  if (!current) return <span className="compose-from-value" />
  const caption = `${current.displayName} (${current.address})`

  if (usable.length <= 1) return <span className="compose-from-value">{caption}</span>

  return (
    <DropdownMenu
      ariaLabel="From identity"
      className="compose-from-select"
      trigger={<>{caption} <ChevronRightIcon size={13} /></>}
      items={usable.map(i => ({ label: `${i.displayName} <${i.address}>`, onSelect: () => onChange(i.address) }))}
    />
  )
}
```

- [ ] **Step 4: Run the select tests**

Run: `npx vitest run src/modules/mail/compose/IdentitySelect.test.tsx`
Expected: PASS. If DropdownMenu's trigger/menu roles differ from the assumptions (read `DropdownMenu.tsx` first), fix the **test selectors**, not the component contract.

- [ ] **Step 5: Wire ComposeView (failing tests first)**

In `ComposeView.test.tsx`, extend the existing `vi.mock` of `../queries` with `useIdentities` (returning `{ data: undefined }` by default so every existing test keeps the 2c1 fallback), then add:

```tsx
const identityList = [
  { address: 'mick@weesky.be', displayName: 'Mick', isDefault: false, isPrimary: true, stale: false, labelIsCustom: false },
  { address: 'michel@weesky.be', displayName: 'Michel', isDefault: true, isPrimary: false, stale: false, labelIsCustom: true },
]

it('preselects the default identity and sends its address', async () => {
  vi.mocked(useIdentities).mockReturnValue({ data: identityList } as never)
  // render, fill a recipient, send — follow the file's existing send test helpers
  // then assert the mutate payload:
  expect(mutateMock).toHaveBeenCalledWith(
    expect.objectContaining({ fromAddress: 'michel@weesky.be' }), expect.anything())
})

it('changing the identity dirties the draft', async () => {
  vi.mocked(useIdentities).mockReturnValue({ data: identityList } as never)
  // render pristine, pick the other identity via the From menu, then navigate away
  // and assert the Discard confirm appears — follow the file's existing guard test.
})

it('keeps the 2c1 plain From while identities are still loading', () => {
  vi.mocked(useIdentities).mockReturnValue({ data: undefined } as never)
  // render and assert the auth identity text is shown, as today
})
```

Adapt to the file's existing helpers (it already mocks `useSendMessage` and renders under a router); the three behaviours above are the contract.

- [ ] **Step 6: Implement the wiring**

`ComposeView.tsx`:

1. Imports: `useIdentities` from `../queries`, `IdentitySelect` from `./IdentitySelect`.
2. State and derivation (after `const send = useSendMessage()`):

```tsx
const { data: identityList } = useIdentities()
const [fromAddress, setFromAddress] = useState<string | null>(null)
const usableIdentities = (identityList ?? []).filter(i => !i.stale)
const effectiveFrom = fromAddress
  ?? usableIdentities.find(i => i.isDefault)?.address
  ?? usableIdentities[0]?.address ?? null
const changeFrom = useCallback((address: string) => { markDirty(); setFromAddress(address) }, [markDirty])
```

3. Include the explicit choice in the dirty computation (the preselected default does not dirty):

```tsx
const dirty = to.length > 0 || cc.length > 0 || bcc.length > 0
  || subject !== '' || bodyTouched || attachments.items.length > 0 || fromAddress !== null
```

4. Replace the From value span:

```tsx
<div className="compose-from">
  <span className="compose-from-label">From</span>
  {usableIdentities.length > 0 ? (
    <IdentitySelect identities={usableIdentities} value={effectiveFrom} onChange={changeFrom} />
  ) : (
    <span className="compose-from-value">
      {identity ? `${identity.displayName} (${identity.email})` : ''}
    </span>
  )}
</div>
```

5. In `submit()`, add to the payload: `fromAddress: effectiveFrom ?? undefined`.

`mail.css` — beside the `.compose-from-*` rules:

```css
.compose-from-select {
  background: none; border: none; padding: 2px 6px; cursor: pointer;
  display: inline-flex; align-items: center; gap: 4px;
  font: inherit; color: var(--text-primary); border-radius: 6px;
}
.compose-from-select:hover { background: var(--pane-item-hover); }
.compose-from-select svg { transform: rotate(90deg); }
```

- [ ] **Step 7: Run everything and commit**

Run: `npx vitest run src/modules/mail/compose/` then `npm test`, `npm run lint`, `npm run build`
Expected: all green.

```bash
git add -A && git commit -F - <<'EOF'
Frontend 2c2a: pick the From identity in the composer

Default preselected, stale identities excluded; a single identity
keeps the 2c1 plain-text From.
EOF
```

---

## Final Verification

1. Backend: `dotnet test` — full suite green.
2. Frontend: `npm test`, `npm run lint`, `npm run build` — green/clean.
3. Manual, after applying `docs/superpowers/webmail-identities-table.md` on **dev** (and checking the Postfix prerequisite): add an identity in Settings, star it, compose → the default is preselected, send from the alias, check the received From and the Sent copy.
4. A virgin account (no curated identity): composer identical to 2c1.
