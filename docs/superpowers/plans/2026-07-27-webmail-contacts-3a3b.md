# Module Contacts (tranches 3a + 3b) — plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Doter le webmail d'un carnet de contacts — deux tables dans `snoopy_webmail`, un CRUD REST, un module frontend à trois colonnes avec éditeur pleine largeur — et brancher ces contacts sur l'autocomplétion des champs To/Cc/Bcc du composeur.

**Architecture :** Le backend suit le patron `TrustedSenderStore` / `IdentitiesController` — données webmail, donc `PreferencesDbContext`, aucune session IMAP, aucun cookie de credentials, `Result<T>` de bout en bout. Le frontend suit le patron `MailLayout` : trois colonnes en pile de bandes dans l'unique outlet de la coquille, avec `useMatch` échangeant les colonnes centrales contre un éditeur pleine largeur, exactement comme `/mail/compose`. Recherche et tri sont côté client sur la liste complète mise en cache.

**Tech Stack :** ASP.NET Core .NET 10, EF Core (Pomelo MySQL), CSharpFunctionalExtensions, xUnit + Moq + EF InMemory · React 18 + Vite, TypeScript, TanStack Query, react-router-dom v6, Vitest + jsdom + @testing-library/react.

**Spec :** `docs/superpowers/specs/2026-07-27-webmail-contacts-3a3b-design.md`

## Global Constraints

- **Périmètre** — tranches 3a (socle + page) et 3b (autocomplétion). La capture automatique (3c) et l'import CSV/vCard (3d) sont **hors périmètre** : ne rien écrire pour elles.
- **`vcard_raw` n'est jamais servi au client** — écrit par le chemin d'import (3d), lu par un éventuel serveur CardDAV. Aucun DTO ne le porte.
- **Adresses canonicalisées via `IdentityResolver.Canonical`** (`Trim().ToLowerInvariant()`) — réutilisé, jamais réimplémenté.
- **Une adresse peut appartenir à plusieurs contacts** du même utilisateur. Aucun index unique global sur l'adresse.
- **Pas de champ téléphone.** Aucune colonne, aucun champ de formulaire.
- **Plafonds :** 5 000 contacts par utilisateur, 50 adresses par contact.
- **Portée :** tout est filtré sur `AuthenticatedUser.WebmailUid`. Un `id` appartenant à un autre utilisateur répond **404, jamais 403**.
- **Création de schéma manuelle**, aucune migration EF.
- **Style C# :** namespaces file-scoped, `sealed`, constructeurs primaires pour l'injection, `record` pour les DTO, `CancellationToken` partout, `ILogger` structuré sans interpolation.
- **Style tests C# :** `Assert.IsType<BadRequestObjectResult>` pour un `BadRequest(body)` — jamais `ObjectResult`. `dotnet test` (pas `--no-build`) dès qu'un fichier de test est ajouté.
- **Frontend :** un token nomme un rôle, jamais une couleur. Chaque champ porte une paire `htmlFor`/`id` explicite. Aucun test perdu sans remplaçant.
- **Commits :** messages en anglais, deux lignes maximum, jamais un `@` en début ou fin de message.

---

### Task 1 : Schéma, entités EF et document de prérequis

**Files:**
- Create: `docs/superpowers/webmail-contacts-tables.md`
- Create: `src/snoopy.microservice/Data/Preferences/Contact.cs`
- Create: `src/snoopy.microservice/Data/Preferences/ContactEmail.cs`
- Modify: `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Data/ContactEntitiesTests.cs`

**Interfaces:**
- Consumes: `PreferencesDbContext`, `PreferencesTestDbContext(string dbName)`.
- Produces: entités `Contact` (`Id`, `UserId`, `Uid`, `FirstName`, `LastName`, `Nickname`, `IsFavorite`, `VCardRaw`, `UpdatedAt`) et `ContactEmail` (`ContactId`, `Address`, `Position`) ; `context.Contacts` et `context.ContactEmails`.

- [ ] **Step 1 : Écrire le document de prérequis base de données**

Créer `docs/superpowers/webmail-contacts-tables.md` :

```markdown
# Prérequis base de données — tables du module Contacts

À rejouer **avant** le déploiement du backend, sur `snoopy_webmail` **et** `snoopy_webmail_dev`.
Création manuelle : ce projet n'utilise pas les migrations EF.

Les FK exigent que `users` existe déjà (voir le prérequis de la table `users`).

```sql
CREATE TABLE `contacts` (
  `id`          CHAR(36)     NOT NULL COMMENT 'GUID généré côté application',
  `user_id`     CHAR(36)     NOT NULL,
  `uid`         VARCHAR(255) NOT NULL COMMENT 'UID vCard d''origine ; = id quand la source n''en portait pas',
  `first_name`  VARCHAR(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `last_name`   VARCHAR(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `nickname`    VARCHAR(100) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `is_favorite` TINYINT(1)   NOT NULL DEFAULT 0,
  `vcard_raw`   MEDIUMTEXT   DEFAULT NULL COMMENT 'vCard source tel quel ; jamais servi à l''UI',
  `updated_at`  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`id`),
  UNIQUE KEY `uq_contacts_user_uid` (`user_id`, `uid`),
  KEY `ix_contacts_user` (`user_id`),
  CONSTRAINT `fk_contacts_user`
    FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE `contact_emails` (
  `contact_id` CHAR(36)          NOT NULL,
  `address`    VARCHAR(320)      NOT NULL COMMENT 'Forme canonique minuscule ; 320 = max RFC 5321',
  `position`   SMALLINT UNSIGNED NOT NULL DEFAULT 0 COMMENT '0 = adresse principale',
  PRIMARY KEY (`contact_id`, `address`),
  CONSTRAINT `fk_contact_emails_contact`
    FOREIGN KEY (`contact_id`) REFERENCES `contacts`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

## Pourquoi la collation est mixte

La table est en `utf8mb4_bin` comme ses sœurs : `uid` est opaque et sensible à la casse, `address`
est stockée canonique — une collation insensible y fusionnerait deux valeurs que le code traite
comme distinctes. Les trois colonnes de nom portent `utf8mb4_unicode_ci` : c'est du texte humain, et
un `LIKE` binaire y serait inutilisable si une recherche serveur apparaissait. Aujourd'hui tri et
filtre sont côté client, donc cette collation ne sert encore à rien — elle évite d'avoir tort plus
tard. `utf8mb4_unicode_ci` et non `utf8mb4_0900_ai_ci` : la base est MariaDB.

## Pourquoi `updated_at` est géré par le schéma

À l'inverse des dates de `users`, que le code pose explicitement pour que `creation_date` ne bouge
jamais, `contacts.updated_at` doit suivre **toute** écriture : il est la base d'un futur ETag
CardDAV. D'où `DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP`.
```

- [ ] **Step 2 : Écrire le test qui échoue**

Créer `src/snoopy.microservice/snoopy.microservice.Tests/Data/ContactEntitiesTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Data;

public sealed class ContactEntitiesTests
{
    [Fact]
    public async Task Contact_RoundTripsThroughTheContext()
    {
        var context = new PreferencesTestDbContext(nameof(Contact_RoundTripsThroughTheContext));
        var id = Guid.NewGuid();
        var user = Guid.NewGuid();

        context.Contacts.Add(new Contact
        {
            Id = id, UserId = user, Uid = id.ToString(), FirstName = "Bruno",
            LastName = "Mertens", Nickname = "bru", IsFavorite = true, UpdatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(CancellationToken.None);

        var stored = Assert.Single(context.Contacts);
        Assert.Equal("Bruno", stored.FirstName);
        Assert.True(stored.IsFavorite);
        Assert.Null(stored.VCardRaw);
    }

    // The composite key is what stops one address being stored twice on the same contact; a
    // second Add under the same pair must be the same tracked entity, not a new row.
    [Fact]
    public async Task ContactEmail_KeyIsContactPlusAddress()
    {
        var context = new PreferencesTestDbContext(nameof(ContactEmail_KeyIsContactPlusAddress));
        var contact = Guid.NewGuid();

        context.ContactEmails.Add(new ContactEmail
        {
            ContactId = contact, Address = "bruno@example.com", Position = 0
        });
        await context.SaveChangesAsync(CancellationToken.None);

        Assert.NotNull(await context.ContactEmails.FindAsync([contact, "bruno@example.com"],
            CancellationToken.None));
    }
}
```

- [ ] **Step 3 : Lancer le test pour vérifier qu'il échoue**

Run: `cd src/snoopy.microservice && dotnet test --filter ContactEntitiesTests`
Expected: FAIL — compilation, `Contact` et `ContactEmail` n'existent pas.

- [ ] **Step 4 : Créer les deux entités**

`src/snoopy.microservice/Data/Preferences/Contact.cs` :

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One contact of one webmail user. Flat like its sibling entities — no navigation property to
/// the addresses: the store joins them, which keeps every read one explicit query.
/// </summary>
[Table("contacts")]
public sealed class Contact
{
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    /// <summary>
    /// The source vCard's own UID, kept distinct from <see cref="Id"/>: a client that PUTs UID X
    /// and reads back UID Y sees a different card and duplicates it on the next sync. Set to
    /// <see cref="Id"/> for a contact born here.
    /// </summary>
    [Column("uid")]
    public string Uid { get; set; } = string.Empty;

    [Column("first_name")]
    public string? FirstName { get; set; }

    [Column("last_name")]
    public string? LastName { get; set; }

    [Column("nickname")]
    public string? Nickname { get; set; }

    [Column("is_favorite")]
    public bool IsFavorite { get; set; }

    /// <summary>
    /// The source vCard verbatim, written by the import path only and never served to the UI. It
    /// is what stops a property we do not model from being destroyed on a future CardDAV sync.
    /// </summary>
    [Column("vcard_raw")]
    public string? VCardRaw { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
```

`src/snoopy.microservice/Data/Preferences/ContactEmail.cs` :

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace weesky.Snoopy.Microservice.Data.Preferences;

/// <summary>
/// One address of one contact. Stored canonical (trimmed, lower-case): the table collates in
/// binary, so a casing difference would split one address into two rows. <see cref="Position"/>
/// carries the order, and position 0 is the primary address by definition — there is no flag to
/// keep in step with it.
/// </summary>
[Table("contact_emails")]
public sealed class ContactEmail
{
    [Column("contact_id")]
    public Guid ContactId { get; set; }

    [Column("address")]
    public string Address { get; set; } = string.Empty;

    [Column("position")]
    public int Position { get; set; }
}
```

- [ ] **Step 5 : Déclarer les deux entités dans le contexte**

Dans `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs`, ajouter dans `OnModelCreating` après la ligne `TrustedSender` :

```csharp
        modelBuilder.Entity<Contact>().HasKey(c => c.Id);
        modelBuilder.Entity<Contact>().HasIndex(c => new { c.UserId, c.Uid }).IsUnique();
        modelBuilder.Entity<ContactEmail>().HasKey(e => new { e.ContactId, e.Address });
```

et les deux `DbSet` après `TrustedSenders` :

```csharp
    public DbSet<Contact> Contacts { get; set; }

    public DbSet<ContactEmail> ContactEmails { get; set; }
```

- [ ] **Step 6 : Lancer le test pour vérifier qu'il passe**

Run: `cd src/snoopy.microservice && dotnet test --filter ContactEntitiesTests`
Expected: PASS — 2 tests.

- [ ] **Step 7 : Commit**

```bash
git add docs/superpowers/webmail-contacts-tables.md \
        src/snoopy.microservice/Data/Preferences/Contact.cs \
        src/snoopy.microservice/Data/Preferences/ContactEmail.cs \
        src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Data/ContactEntitiesTests.cs
git commit -F - <<'EOF'
Add contacts and contact_emails entities

Schema DDL ships as a manual prerequisite doc, no EF migrations.
EOF
```

---

### Task 2 : Le validateur pur `ContactValidator`

**Files:**
- Create: `src/snoopy.microservice/Models/Contacts/ContactRequest.cs`
- Create: `src/snoopy.microservice/Models/Contacts/ContactWrite.cs`
- Create: `src/snoopy.microservice/Services/ContactValidator.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ContactValidatorTests.cs`

**Interfaces:**
- Consumes: rien (classe pure).
- Produces:
  - `ContactRequest` — `{ string? FirstName, string? LastName, string? Nickname, bool IsFavorite, List<string>? Addresses }`, classe mutable à propriétés settables (c'est un corps de requête lié par le modèle binder).
  - `ContactWrite(string? FirstName, string? LastName, string? Nickname, bool IsFavorite, IReadOnlyList<string> Addresses)` — record, la forme normalisée que le store consomme.
  - `ContactValidator.MaxAddressesPerContact` = `50`.
  - `ContactValidator.Validate(ContactRequest request) : Result<ContactWrite>`.

- [ ] **Step 1 : Écrire le test qui échoue**

Créer `src/snoopy.microservice/snoopy.microservice.Tests/Services/ContactValidatorTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ContactValidatorTests
{
    private static ContactRequest Request(
        string? first = null, string? last = null, string? nick = null, params string[] addresses) =>
        new() { FirstName = first, LastName = last, Nickname = nick, Addresses = [.. addresses] };

    [Fact]
    public void Validate_WithANameOnly_Succeeds()
    {
        var result = ContactValidator.Validate(Request(first: "Bruno"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Bruno", result.Value.FirstName);
        Assert.Empty(result.Value.Addresses);
    }

    [Fact]
    public void Validate_WithAnAddressOnly_Succeeds()
    {
        var result = ContactValidator.Validate(Request(addresses: "bruno@example.com"));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.FirstName);
        Assert.Equal("bruno@example.com", Assert.Single(result.Value.Addresses));
    }

    [Fact]
    public void Validate_WithANicknameOnly_Succeeds()
    {
        Assert.True(ContactValidator.Validate(Request(nick: "bru")).IsSuccess);
    }

    // The gate the spec sets: a contact must carry at least one human identifier or one address.
    // Blank strings are not identifiers — they would produce a tile with no label at all.
    [Fact]
    public void Validate_WithNothing_Fails()
    {
        var result = ContactValidator.Validate(Request(first: "   ", last: ""));

        Assert.True(result.IsFailure);
        Assert.Contains("name", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_TrimsNamesAndNullsTheEmptyOnes()
    {
        var result = ContactValidator.Validate(Request(first: "  Bruno  ", last: "   ", nick: ""));

        Assert.Equal("Bruno", result.Value.FirstName);
        Assert.Null(result.Value.LastName);
        Assert.Null(result.Value.Nickname);
    }

    [Fact]
    public void Validate_WithAnUnparsableAddress_FailsNamingIt()
    {
        var result = ContactValidator.Validate(Request(first: "Bruno", addresses: "not-an-address"));

        Assert.True(result.IsFailure);
        Assert.Contains("not-an-address", result.Error);
    }

    // Blank entries come from an editor row the user opened and left empty; dropping them is what
    // the user meant, and refusing the save would be unexplainable next to an empty box.
    [Fact]
    public void Validate_DropsBlankAddressRows()
    {
        var result = ContactValidator.Validate(Request(first: "Bruno", addresses: ["bruno@example.com", "  ", ""]));

        Assert.Equal("bruno@example.com", Assert.Single(result.Value.Addresses));
    }

    [Fact]
    public void Validate_PastTheAddressCap_Fails()
    {
        var many = Enumerable.Range(0, ContactValidator.MaxAddressesPerContact + 1)
            .Select(i => $"a{i}@example.com").ToArray();

        var result = ContactValidator.Validate(Request(first: "Bruno", addresses: many));

        Assert.True(result.IsFailure);
        Assert.Contains(ContactValidator.MaxAddressesPerContact.ToString(), result.Error);
    }

    [Fact]
    public void Validate_KeepsTheAddressOrderGiven()
    {
        var result = ContactValidator.Validate(
            Request(addresses: ["second@example.com", "first@example.com"]));

        Assert.Equal(["second@example.com", "first@example.com"], result.Value.Addresses);
    }

    [Fact]
    public void Validate_WithANullRequest_Fails()
    {
        Assert.True(ContactValidator.Validate(null!).IsFailure);
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `cd src/snoopy.microservice && dotnet test --filter ContactValidatorTests`
Expected: FAIL — compilation, `ContactRequest`, `ContactWrite` et `ContactValidator` n'existent pas.

- [ ] **Step 3 : Créer les deux modèles**

`src/snoopy.microservice/Models/Contacts/ContactRequest.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// The body of POST /api/Contacts and PUT /api/Contacts/{id}. A settable class rather than a
/// record: it is bound from JSON, and every field is optional at the wire level so
/// <see cref="Services.ContactValidator"/> can answer one clear message instead of the binder
/// answering several unclear ones.
/// </summary>
public sealed class ContactRequest
{
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Nickname { get; set; }

    public bool IsFavorite { get; set; }

    /// <summary>Ordered; the first surviving entry becomes the primary address.</summary>
    public List<string>? Addresses { get; set; }
}
```

`src/snoopy.microservice/Models/Contacts/ContactWrite.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// A validated, normalised contact on its way to the store: names trimmed and nulled when blank,
/// addresses non-blank and in the order they must be stored. Only
/// <see cref="Services.ContactValidator"/> produces one, so the store never re-checks the rules.
/// </summary>
public sealed record ContactWrite(
    string? FirstName,
    string? LastName,
    string? Nickname,
    bool IsFavorite,
    IReadOnlyList<string> Addresses);
```

- [ ] **Step 4 : Créer le validateur**

`src/snoopy.microservice/Services/ContactValidator.cs` :

```csharp
using CSharpFunctionalExtensions;
using MimeKit;
using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// The single place the contact rules are written — the role <see cref="IdentityResolver"/> plays
/// for sending identities. Pure, no external call, so POST and PUT read the same rule instead of
/// two that could drift apart.
/// </summary>
internal static class ContactValidator
{
    /// <summary>
    /// What bounds one contact's address list. The whole book is fetched into the browser, so a
    /// contact carrying thousands of addresses is a payload problem, not just an odd fixture.
    /// </summary>
    internal const int MaxAddressesPerContact = 50;

    internal static Result<ContactWrite> Validate(ContactRequest request)
    {
        if (request == null) return Result.Failure<ContactWrite>("Request body is required");

        var first = Blank(request.FirstName);
        var last = Blank(request.LastName);
        var nick = Blank(request.Nickname);

        // Blank rows are what an editor leaves behind when the user opens an address line and
        // changes their mind; they are dropped, never refused.
        var addresses = (request.Addresses ?? [])
            .Select(a => a?.Trim() ?? string.Empty)
            .Where(a => a.Length > 0)
            .ToList();

        if (first == null && last == null && nick == null && addresses.Count == 0)
            return Result.Failure<ContactWrite>(
                "A contact needs a first name, last name or nickname, or at least one address");

        if (addresses.Count > MaxAddressesPerContact)
            return Result.Failure<ContactWrite>(
                $"A contact cannot carry more than {MaxAddressesPerContact} addresses");

        foreach (var address in addresses)
            if (!Parses(address))
                return Result.Failure<ContactWrite>($"'{address}' is not a valid email address");

        return Result.Success(new ContactWrite(first, last, nick, request.IsFavorite, addresses));
    }

    private static string? Blank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    // MimeKit is the authority here as it is on the send path: a hand-rolled regex accepts and
    // rejects a different set than the library that will actually address the mail.
    private static bool Parses(string address) =>
        MailboxAddress.TryParse(address, out var parsed) && parsed.Address == address;
}
```

- [ ] **Step 5 : Lancer le test pour vérifier qu'il passe**

Run: `cd src/snoopy.microservice && dotnet test --filter ContactValidatorTests`
Expected: PASS — 10 tests.

- [ ] **Step 6 : Commit**

```bash
git add src/snoopy.microservice/Models/Contacts/ \
        src/snoopy.microservice/Services/ContactValidator.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/ContactValidatorTests.cs
git commit -F - <<'EOF'
Add ContactValidator, the single place contact rules live

Names trimmed, blank address rows dropped, MimeKit parses each address.
EOF
```

---

### Task 3 : `ContactStore` — lecture et création

**Files:**
- Create: `src/snoopy.microservice/Models/Contacts/ContactView.cs`
- Create: `src/snoopy.microservice/Repositories/IContactStore.cs`
- Create: `src/snoopy.microservice/Repositories/ContactStore.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreTests.cs`

**Interfaces:**
- Consumes: `PreferencesDbContext`, `Contact`, `ContactEmail`, `ContactWrite`, `IdentityResolver.Canonical`.
- Produces:
  - `ContactView(Guid Id, string? FirstName, string? LastName, string? Nickname, bool IsFavorite, IReadOnlyList<string> Addresses)` — record, le DTO de lecture. **Ne porte ni `Uid` ni `VCardRaw`.**
  - `ContactStore.MaxPerUser` = `5000`.
  - `ContactStore.CapReached` — le message d'erreur, chiffre interpolé une seule fois.
  - `IContactStore.ListAsync(Guid userId, CancellationToken) : Task<IReadOnlyList<ContactView>>`
  - `IContactStore.CreateAsync(Guid userId, ContactWrite contact, CancellationToken) : Task<Result<Guid>>`
  - Les trois méthodes d'écriture restantes sont déclarées dans l'interface dès cette tâche mais implémentées en Task 4 : `UpdateAsync(Guid userId, Guid contactId, ContactWrite contact, CancellationToken) : Task<Result>`, `DeleteAsync(Guid userId, Guid contactId, CancellationToken) : Task<Result>`, `SetFavoriteAsync(Guid userId, Guid contactId, bool isFavorite, CancellationToken) : Task<Result>`.

- [ ] **Step 1 : Écrire le test qui échoue**

Créer `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class ContactStoreTests
{
    private static ContactStore CreateStore(string dbName) =>
        new(new PreferencesTestDbContext(dbName));

    private static ContactWrite Write(
        string? first = "Bruno", string? last = "Mertens", string? nick = null,
        bool favorite = false, params string[] addresses) =>
        new(first, last, nick, favorite, addresses);

    [Fact]
    public async Task Create_ThenList_ReturnsTheContact()
    {
        var db = nameof(Create_ThenList_ReturnsTheContact);
        var user = Guid.NewGuid();

        var created = await CreateStore(db)
            .CreateAsync(user, Write(addresses: "bruno@example.com"), CancellationToken.None);

        Assert.True(created.IsSuccess);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal(created.Value, stored.Id);
        Assert.Equal("Bruno", stored.FirstName);
        Assert.Equal("bruno@example.com", Assert.Single(stored.Addresses));
    }

    // The table collates binary, so folding on the way in is the only thing stopping one address
    // from becoming two rows the client can never reconcile.
    [Fact]
    public async Task Create_FoldsAddressCaseAndSpace()
    {
        var db = nameof(Create_FoldsAddressCaseAndSpace);
        var user = Guid.NewGuid();

        await CreateStore(db).CreateAsync(user, Write(addresses: " Bruno@Example.COM "), CancellationToken.None);

        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal("bruno@example.com", Assert.Single(stored.Addresses));
    }

    [Fact]
    public async Task Create_KeepsAddressOrder_PositionZeroIsPrimary()
    {
        var db = nameof(Create_KeepsAddressOrder_PositionZeroIsPrimary);
        var user = Guid.NewGuid();

        await CreateStore(db).CreateAsync(
            user, Write(addresses: ["second@example.com", "first@example.com"]), CancellationToken.None);

        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal(["second@example.com", "first@example.com"], stored.Addresses);
    }

    // Two rows differing only by case fold onto one address. Left as two, the composite key would
    // throw; resequencing after the fold is what keeps position 0 unambiguous.
    [Fact]
    public async Task Create_DedupesAddressesThatFoldTogether()
    {
        var db = nameof(Create_DedupesAddressesThatFoldTogether);
        var user = Guid.NewGuid();

        var created = await CreateStore(db).CreateAsync(
            user, Write(addresses: ["Bruno@example.com", "bruno@example.com", "other@example.com"]),
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal(["bruno@example.com", "other@example.com"], stored.Addresses);
    }

    [Fact]
    public async Task Create_SetsUidToTheGeneratedId()
    {
        var db = nameof(Create_SetsUidToTheGeneratedId);
        var user = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);

        var created = await new ContactStore(context).CreateAsync(user, Write(), CancellationToken.None);

        var row = Assert.Single(new PreferencesTestDbContext(db).Contacts);
        Assert.Equal(created.Value.ToString(), row.Uid);
    }

    [Fact]
    public async Task Create_LeavesVCardRawNull()
    {
        var db = nameof(Create_LeavesVCardRawNull);

        await CreateStore(db).CreateAsync(Guid.NewGuid(), Write(), CancellationToken.None);

        Assert.Null(Assert.Single(new PreferencesTestDbContext(db).Contacts).VCardRaw);
    }

    // Same address on two contacts is allowed by decision: shared mailboxes are real. Nothing in
    // the schema or the store may refuse it.
    [Fact]
    public async Task Create_AllowsTheSameAddressOnTwoContacts()
    {
        var db = nameof(Create_AllowsTheSameAddressOnTwoContacts);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(user, Write(first: "Alice", addresses: "info@example.com"),
            CancellationToken.None);

        var second = await CreateStore(db).CreateAsync(
            user, Write(first: "Compta", addresses: "info@example.com"), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(2, (await CreateStore(db).ListAsync(user, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task List_IsScopedToItsUser()
    {
        var db = nameof(List_IsScopedToItsUser);
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await CreateStore(db).CreateAsync(mine, Write(first: "Mine"), CancellationToken.None);
        await CreateStore(db).CreateAsync(theirs, Write(first: "Theirs"), CancellationToken.None);

        var listed = await CreateStore(db).ListAsync(mine, CancellationToken.None);

        Assert.Equal("Mine", Assert.Single(listed).FirstName);
    }

    [Fact]
    public async Task List_WithNoContacts_IsEmptyNotNull()
    {
        var listed = await CreateStore(nameof(List_WithNoContacts_IsEmptyNotNull))
            .ListAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(listed);
    }

    // Counted only on the branch that adds a row, and it is what bounds the payload the whole
    // book becomes in the browser.
    [Fact]
    public async Task Create_AtTheCap_IsRefused()
    {
        var db = nameof(Create_AtTheCap_IsRefused);
        var user = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        for (var i = 0; i < ContactStore.MaxPerUser; i++)
        {
            var id = Guid.NewGuid();
            context.Contacts.Add(new Contact
            {
                Id = id, UserId = user, Uid = id.ToString(), FirstName = $"C{i}",
                UpdatedAt = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync(CancellationToken.None);

        var result = await new ContactStore(new PreferencesTestDbContext(db))
            .CreateAsync(user, Write(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContactStore.CapReached, result.Error);
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `cd src/snoopy.microservice && dotnet test --filter ContactStoreTests`
Expected: FAIL — compilation, `ContactStore` et `ContactView` n'existent pas.

- [ ] **Step 3 : Créer le DTO de lecture**

`src/snoopy.microservice/Models/Contacts/ContactView.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// One contact as the client reads it. Carries neither <c>Uid</c> nor <c>VCardRaw</c>: no screen
/// reads either, and the raw card would multiply the payload of a list already fetched whole.
/// </summary>
/// <param name="Addresses">Ordered; the first entry is the primary address.</param>
public sealed record ContactView(
    Guid Id,
    string? FirstName,
    string? LastName,
    string? Nickname,
    bool IsFavorite,
    IReadOnlyList<string> Addresses);
```

- [ ] **Step 4 : Créer l'interface du store**

`src/snoopy.microservice/Repositories/IContactStore.cs` :

```csharp
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Models.Contacts;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>
/// A user's contacts. Addresses go in as the caller typed them and come back canonical; callers
/// never fold them themselves. Every method is scoped by <paramref name="userId"/>, so a contact
/// belonging to somebody else is simply not found.
/// </summary>
public interface IContactStore
{
    Task<IReadOnlyList<ContactView>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Creates it and answers its new id. Fails only when the per-user cap is reached.</summary>
    Task<Result<Guid>> CreateAsync(Guid userId, ContactWrite contact, CancellationToken cancellationToken);

    /// <summary>Replaces names, favourite flag and the whole address list. Fails when not found.</summary>
    Task<Result> UpdateAsync(Guid userId, Guid contactId, ContactWrite contact, CancellationToken cancellationToken);

    /// <summary>Removes it and its addresses. Fails when not found.</summary>
    Task<Result> DeleteAsync(Guid userId, Guid contactId, CancellationToken cancellationToken);

    /// <summary>
    /// Flips the favourite flag alone. Its own method because the star is toggled from a tile
    /// that holds a possibly stale copy of the contact — a whole-object write would clobber it.
    /// </summary>
    Task<Result> SetFavoriteAsync(Guid userId, Guid contactId, bool isFavorite, CancellationToken cancellationToken);
}
```

- [ ] **Step 5 : Implémenter la lecture et la création**

`src/snoopy.microservice/Repositories/ContactStore.cs` :

```csharp
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Repositories;

internal sealed class ContactStore(PreferencesDbContext context) : IContactStore
{
    /// <summary>
    /// What bounds the table, and what bounds the payload: the whole book is fetched into the
    /// browser, so this is a transfer ceiling as much as a storage one. Far above real use — it
    /// guards against a runaway import, not against a user.
    /// </summary>
    internal const int MaxPerUser = 5000;

    // Interpolated, not spelled out, so the ceiling is written once.
    internal static readonly string CapReached =
        $"You have reached the maximum of {MaxPerUser} contacts";

    internal const string NotFound = "Contact not found";

    public async Task<IReadOnlyList<ContactView>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var contacts = await context.Contacts.AsNoTracking()
            .Where(c => c.UserId == userId)
            .ToListAsync(cancellationToken);
        if (contacts.Count == 0) return [];

        // One query for every address rather than one per contact: the list is read whole on
        // every page load, so an N+1 here is N+1 round trips on the hot path.
        var ids = contacts.Select(c => c.Id).ToList();
        var addresses = await context.ContactEmails.AsNoTracking()
            .Where(e => ids.Contains(e.ContactId))
            .OrderBy(e => e.Position)
            .ToListAsync(cancellationToken);

        var byContact = addresses
            .GroupBy(e => e.ContactId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(e => e.Address).ToList());

        return [.. contacts.Select(c => new ContactView(
            c.Id, c.FirstName, c.LastName, c.Nickname, c.IsFavorite,
            byContact.TryGetValue(c.Id, out var found) ? found : []))];
    }

    public async Task<Result<Guid>> CreateAsync(
        Guid userId, ContactWrite contact, CancellationToken cancellationToken)
    {
        var stored = await context.Contacts.CountAsync(c => c.UserId == userId, cancellationToken);
        if (stored >= MaxPerUser) return Result.Failure<Guid>(CapReached);

        var id = Guid.NewGuid();
        context.Contacts.Add(new Contact
        {
            Id = id,
            UserId = userId,
            // A contact born here has no foreign UID, so its own id serves. The column stays
            // distinct from the key because an imported card brings a UID we must not overwrite.
            Uid = id.ToString(),
            FirstName = contact.FirstName,
            LastName = contact.LastName,
            Nickname = contact.Nickname,
            IsFavorite = contact.IsFavorite,
            UpdatedAt = DateTime.UtcNow
        });
        AddAddresses(id, contact.Addresses);

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success(id);
    }

    public Task<Result> UpdateAsync(
        Guid userId, Guid contactId, ContactWrite contact, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<Result> DeleteAsync(Guid userId, Guid contactId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<Result> SetFavoriteAsync(
        Guid userId, Guid contactId, bool isFavorite, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    /// <summary>
    /// Folds every address, drops what folds together, and numbers what survives from 0. The
    /// position is reassigned here rather than taken from the caller: a gap or a repeat coming
    /// off the wire would leave two rows claiming to be the primary.
    /// </summary>
    private void AddAddresses(Guid contactId, IReadOnlyList<string> addresses)
    {
        var seen = new HashSet<string>();
        var position = 0;

        foreach (var address in addresses)
        {
            var canonical = IdentityResolver.Canonical(address);
            if (!seen.Add(canonical)) continue;

            context.ContactEmails.Add(new ContactEmail
            {
                ContactId = contactId, Address = canonical, Position = position++
            });
        }
    }
}
```

`IdentityResolver.Canonical` est `internal` : `ContactStore` est dans le même assembly, l'appel compile sans changement de visibilité.

- [ ] **Step 6 : Lancer le test pour vérifier qu'il passe**

Run: `cd src/snoopy.microservice && dotnet test --filter ContactStoreTests`
Expected: PASS — 10 tests.

- [ ] **Step 7 : Commit**

```bash
git add src/snoopy.microservice/Models/Contacts/ContactView.cs \
        src/snoopy.microservice/Repositories/IContactStore.cs \
        src/snoopy.microservice/Repositories/ContactStore.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreTests.cs
git commit -F - <<'EOF'
Add ContactStore read and create paths

Addresses fold canonical, positions are reassigned from zero, per-user cap.
EOF
```

---

### Task 4 : `ContactStore` — mise à jour, suppression, favori

**Files:**
- Modify: `src/snoopy.microservice/Repositories/ContactStore.cs`
- Modify: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreTests.cs`

**Interfaces:**
- Consumes: tout de Task 3.
- Produces: les trois méthodes d'écriture réellement implémentées. `UpdateAsync` remplace **l'ensemble** des adresses ; les trois échouent sur `ContactStore.NotFound` quand le contact n'existe pas ou appartient à un autre utilisateur.

- [ ] **Step 1 : Écrire les tests qui échouent**

Ajouter à `ContactStoreTests.cs`, avant l'accolade fermante de la classe :

```csharp
    private static async Task<Guid> Seed(string db, Guid user, params string[] addresses) =>
        (await CreateStore(db).CreateAsync(user, Write(addresses: addresses), CancellationToken.None)).Value;

    [Fact]
    public async Task Update_ReplacesNamesAndAddresses()
    {
        var db = nameof(Update_ReplacesNamesAndAddresses);
        var user = Guid.NewGuid();
        var id = await Seed(db, user, "old@example.com");

        var result = await CreateStore(db).UpdateAsync(user, id,
            new ContactWrite("Chloé", "Vermeulen", "chlo", true, ["new@example.com"]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal("Chloé", stored.FirstName);
        Assert.True(stored.IsFavorite);
        Assert.Equal("new@example.com", Assert.Single(stored.Addresses));
    }

    // Replace, not merge: the editor sends the list it shows, so an address the user removed has
    // to disappear. Merging would make removal impossible from the only screen that offers it.
    [Fact]
    public async Task Update_DropsAddressesNoLongerListed()
    {
        var db = nameof(Update_DropsAddressesNoLongerListed);
        var user = Guid.NewGuid();
        var id = await Seed(db, user, "a@example.com", "b@example.com");

        await CreateStore(db).UpdateAsync(user, id,
            new ContactWrite("Bruno", null, null, false, ["b@example.com"]), CancellationToken.None);

        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal("b@example.com", Assert.Single(stored.Addresses));
    }

    [Fact]
    public async Task Update_ReorderingChangesThePrimary()
    {
        var db = nameof(Update_ReorderingChangesThePrimary);
        var user = Guid.NewGuid();
        var id = await Seed(db, user, "a@example.com", "b@example.com");

        await CreateStore(db).UpdateAsync(user, id,
            new ContactWrite("Bruno", null, null, false, ["b@example.com", "a@example.com"]),
            CancellationToken.None);

        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal(["b@example.com", "a@example.com"], stored.Addresses);
    }

    [Fact]
    public async Task Update_TouchesUpdatedAt()
    {
        var db = nameof(Update_TouchesUpdatedAt);
        var user = Guid.NewGuid();
        var id = await Seed(db, user);
        var before = Assert.Single(new PreferencesTestDbContext(db).Contacts).UpdatedAt;

        await CreateStore(db).UpdateAsync(user, id,
            new ContactWrite("Bruno", null, null, false, []), CancellationToken.None);

        Assert.True(Assert.Single(new PreferencesTestDbContext(db).Contacts).UpdatedAt >= before);
    }

    // The uid must survive an edit: it is the identity a CardDAV client syncs on, and rewriting
    // it would duplicate the card on that client's next pass.
    [Fact]
    public async Task Update_LeavesUidAlone()
    {
        var db = nameof(Update_LeavesUidAlone);
        var user = Guid.NewGuid();
        var id = await Seed(db, user);
        var before = Assert.Single(new PreferencesTestDbContext(db).Contacts).Uid;

        await CreateStore(db).UpdateAsync(user, id,
            new ContactWrite("Bruno", null, null, false, []), CancellationToken.None);

        Assert.Equal(before, Assert.Single(new PreferencesTestDbContext(db).Contacts).Uid);
    }

    [Fact]
    public async Task Update_AnotherUsersContact_IsNotFound()
    {
        var db = nameof(Update_AnotherUsersContact_IsNotFound);
        var id = await Seed(db, Guid.NewGuid());

        var result = await CreateStore(db).UpdateAsync(Guid.NewGuid(), id,
            new ContactWrite("Hijack", null, null, false, []), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContactStore.NotFound, result.Error);
    }

    [Fact]
    public async Task Delete_RemovesTheContactAndItsAddresses()
    {
        var db = nameof(Delete_RemovesTheContactAndItsAddresses);
        var user = Guid.NewGuid();
        var id = await Seed(db, user, "a@example.com", "b@example.com");

        var result = await CreateStore(db).DeleteAsync(user, id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Empty(new PreferencesTestDbContext(db).ContactEmails);
    }

    [Fact]
    public async Task Delete_AnUnknownId_IsNotFound()
    {
        var result = await CreateStore(nameof(Delete_AnUnknownId_IsNotFound))
            .DeleteAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContactStore.NotFound, result.Error);
    }

    [Fact]
    public async Task SetFavorite_FlipsTheFlagAndNothingElse()
    {
        var db = nameof(SetFavorite_FlipsTheFlagAndNothingElse);
        var user = Guid.NewGuid();
        var id = await Seed(db, user, "a@example.com");

        var result = await CreateStore(db).SetFavoriteAsync(user, id, true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.True(stored.IsFavorite);
        Assert.Equal("Bruno", stored.FirstName);
        Assert.Equal("a@example.com", Assert.Single(stored.Addresses));
    }

    [Fact]
    public async Task SetFavorite_AnotherUsersContact_IsNotFound()
    {
        var db = nameof(SetFavorite_AnotherUsersContact_IsNotFound);
        var id = await Seed(db, Guid.NewGuid());

        var result = await CreateStore(db)
            .SetFavoriteAsync(Guid.NewGuid(), id, true, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContactStore.NotFound, result.Error);
    }
```

- [ ] **Step 2 : Lancer les tests pour vérifier qu'ils échouent**

Run: `cd src/snoopy.microservice && dotnet test --filter ContactStoreTests`
Expected: FAIL — 10 nouveaux tests lèvent `NotImplementedException`.

- [ ] **Step 3 : Implémenter les trois méthodes**

Dans `ContactStore.cs`, remplacer les trois `throw new NotImplementedException()` par :

```csharp
    public async Task<Result> UpdateAsync(
        Guid userId, Guid contactId, ContactWrite contact, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, contactId, cancellationToken);
        if (row == null) return Result.Failure(NotFound);

        row.FirstName = contact.FirstName;
        row.LastName = contact.LastName;
        row.Nickname = contact.Nickname;
        row.IsFavorite = contact.IsFavorite;
        row.UpdatedAt = DateTime.UtcNow;
        // Uid and VCardRaw are deliberately untouched: the first is the identity a CardDAV client
        // syncs on, the second holds properties this UI cannot show and must not erase.

        // Replace rather than merge: the editor submits the list it displays, so an address the
        // user removed has to go. Removed then re-added, because a position is not a key and
        // reordering has to be able to move an address that stays.
        var existing = await context.ContactEmails
            .Where(e => e.ContactId == contactId)
            .ToListAsync(cancellationToken);
        context.ContactEmails.RemoveRange(existing);
        await context.SaveChangesAsync(cancellationToken);

        AddAddresses(contactId, contact.Addresses);
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid userId, Guid contactId, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, contactId, cancellationToken);
        if (row == null) return Result.Failure(NotFound);

        // The FK cascades in MariaDB, but the InMemory provider the tests run on enforces no FK
        // at all: deleting the children here is what makes the behaviour the same in both.
        var addresses = await context.ContactEmails
            .Where(e => e.ContactId == contactId)
            .ToListAsync(cancellationToken);
        context.ContactEmails.RemoveRange(addresses);
        context.Contacts.Remove(row);

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> SetFavoriteAsync(
        Guid userId, Guid contactId, bool isFavorite, CancellationToken cancellationToken)
    {
        var row = await FindAsync(userId, contactId, cancellationToken);
        if (row == null) return Result.Failure(NotFound);

        row.IsFavorite = isFavorite;
        row.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Scoped by user on purpose: a contact belonging to somebody else must be indistinguishable
    /// from one that does not exist, so the controller can answer 404 without leaking it.
    /// </summary>
    private async Task<Contact?> FindAsync(Guid userId, Guid contactId, CancellationToken cancellationToken) =>
        await context.Contacts.FirstOrDefaultAsync(
            c => c.Id == contactId && c.UserId == userId, cancellationToken);
```

- [ ] **Step 4 : Lancer les tests pour vérifier qu'ils passent**

Run: `cd src/snoopy.microservice && dotnet test --filter ContactStoreTests`
Expected: PASS — 20 tests.

- [ ] **Step 5 : Commit**

```bash
git add src/snoopy.microservice/Repositories/ContactStore.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreTests.cs
git commit -F - <<'EOF'
Add ContactStore update, delete and favourite paths

Update replaces the whole address list; uid and vcard_raw are never touched.
EOF
```

---

### Task 5 : `ContactsController` et enregistrement DI

**Files:**
- Create: `src/snoopy.microservice/Models/Contacts/ContactListResponse.cs`
- Create: `src/snoopy.microservice/Models/Contacts/FavoriteRequest.cs`
- Create: `src/snoopy.microservice/Controllers/ContactsController.cs`
- Modify: `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs:89`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/ContactsControllerTests.cs`

**Interfaces:**
- Consumes: `IContactStore`, `ContactValidator.Validate`, `ApiBaseController` (`AuthenticatedUser.WebmailUid`, `BadRequestEnveloppe`, `NotFoundEnveloppe`).
- Produces: les cinq actions `List`, `Create`, `Update`, `Delete`, `SetFavorite` ; `ContactListResponse(IReadOnlyList<ContactView> Contacts)` ; `FavoriteRequest { bool IsFavorite }`.

- [ ] **Step 1 : Écrire le test qui échoue**

Créer `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/ContactsControllerTests.cs` :

```csharp
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class ContactsControllerTests
{
    // Fixed rather than a fresh Guid per call: the uid the controller hands the store is what
    // scopes an action to one user's book, so a test has to be able to name it.
    private static readonly Guid Uid = Guid.NewGuid();

    private readonly Mock<IContactStore> _store = new();

    private ContactsController CreateController()
    {
        var controller = new ContactsController(_store.Object);
        controller.ControllerContext =
            ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", Uid);
        return controller;
    }

    private static ContactRequest Valid() =>
        new() { FirstName = "Bruno", Addresses = ["bruno@example.com"] };

    [Fact]
    public async Task List_Returns200WithTheContacts()
    {
        var view = new ContactView(Guid.NewGuid(), "Bruno", "Mertens", null, false, ["bruno@example.com"]);
        _store.Setup(s => s.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync([view]);

        var result = await CreateController().List(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<ContactListResponse>(ok.Value);
        Assert.Equal("Bruno", Assert.Single(body.Contacts).FirstName);
    }

    [Fact]
    public async Task Create_WhenAccepted_Returns200WithTheId()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<ContactWrite>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success(id));

        var result = await CreateController().Create(Valid(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(id, Assert.IsType<ContactView>(ok.Value).Id);
    }

    // The validator's message must reach the client verbatim: it is what the form prints in its
    // error banner, so a generic 400 would leave the user with nothing to act on.
    [Fact]
    public async Task Create_WithNeitherNameNorAddress_Returns400()
    {
        var result = await CreateController().Create(new ContactRequest(), CancellationToken.None);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        // Compared against the validator's own output rather than a copy of the sentence: a
        // generic "Invalid request" here would be a 400 the banner cannot act on.
        Assert.Equal(ContactValidator.Validate(new ContactRequest()).Error,
            Assert.IsType<ResultEnveloppe>(bad.Value).Message);
        _store.Verify(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<ContactWrite>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_WithAnUnparsableAddress_Returns400()
    {
        var request = new ContactRequest { FirstName = "Bruno", Addresses = ["nope"] };

        Assert.IsType<BadRequestObjectResult>((await CreateController().Create(request, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Create_AtTheCap_Returns400()
    {
        _store.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<ContactWrite>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure<Guid>(ContactStore.CapReached));

        Assert.IsType<BadRequestObjectResult>((await CreateController().Create(Valid(), CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Update_WhenAccepted_Returns204()
    {
        _store.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ContactWrite>(),
                  It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success());

        var result = await CreateController().Update(Guid.NewGuid(), Valid(), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    // Not found and belonging to another user are the same answer on purpose: a 403 would confirm
    // the contact exists, and the namespace is sealed per user. That sealing is the uid the
    // controller hands down, so the call is verified on its arguments, not merely on its result.
    [Fact]
    public async Task Update_WhenNotFound_Returns404()
    {
        var id = Guid.NewGuid();
        _store.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ContactWrite>(),
                  It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(ContactStore.NotFound));

        var result = await CreateController().Update(id, Valid(), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        _store.Verify(s => s.UpdateAsync(Uid, id, It.IsAny<ContactWrite>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_WithAnInvalidBody_Returns400()
    {
        var result = await CreateController().Update(Guid.NewGuid(), new ContactRequest(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Delete_WhenAccepted_Returns204()
    {
        _store.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success());

        Assert.IsType<NoContentResult>(await CreateController().Delete(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_WhenNotFound_Returns404()
    {
        _store.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Failure(ContactStore.NotFound));

        Assert.IsType<NotFoundObjectResult>(await CreateController().Delete(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task SetFavorite_WhenAccepted_Returns204()
    {
        _store.Setup(s => s.SetFavoriteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), true,
                  It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success());

        var result = await CreateController()
            .SetFavorite(Guid.NewGuid(), new FavoriteRequest { IsFavorite = true }, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task SetFavorite_WithNoBody_Returns400()
    {
        var result = await CreateController().SetFavorite(Guid.NewGuid(), null!, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `cd src/snoopy.microservice && dotnet test --filter ContactsControllerTests`
Expected: FAIL — compilation, `ContactsController` n'existe pas.

- [ ] **Step 3 : Créer les deux modèles restants**

`src/snoopy.microservice/Models/Contacts/ContactListResponse.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>The whole book. Wrapped in an object rather than answered as a bare array, so a later
/// field — a sync token, a count — can be added without changing the response's shape.</summary>
public sealed record ContactListResponse(IReadOnlyList<ContactView> Contacts);
```

`src/snoopy.microservice/Models/Contacts/FavoriteRequest.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>The body of PUT /api/Contacts/{id}/Favorite. A settable class, bound from JSON.</summary>
public sealed class FavoriteRequest
{
    public bool IsFavorite { get; set; }
}
```

- [ ] **Step 4 : Créer le contrôleur**

`src/snoopy.microservice/Controllers/ContactsController.cs` :

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Controllers;

/// <summary>
/// The user's contacts — webmail data, not mail-server data. No IMAP session and no credentials
/// cookie: every action is a database read or write, the same shape as IdentitiesController.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public sealed class ContactsController(IContactStore store) : ApiBaseController
{
    /// <summary>
    /// The whole book in one answer. Search and sort are the client's job, over this cached list.
    /// </summary>
    /// <response code="200">The contacts</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ContactListResponse>> List(CancellationToken cancellationToken)
    {
        var contacts = await store.ListAsync(AuthenticatedUser.WebmailUid, cancellationToken);
        return Ok(new ContactListResponse(contacts));
    }

    /// <summary>Creates a contact and answers it, id included.</summary>
    /// <response code="200">Created</response>
    /// <response code="400">Neither name nor address, an unparsable address, or the cap reached</response>
    /// <response code="401">Not authenticated</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ContactView>> Create(
        ContactRequest request, CancellationToken cancellationToken)
    {
        var validated = ContactValidator.Validate(request);
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var created = await store.CreateAsync(
            AuthenticatedUser.WebmailUid, validated.Value, cancellationToken);
        if (created.IsFailure) return BadRequestEnveloppe(created.Error);

        // Answered from the validated write rather than re-read: the store folded the addresses,
        // so echoing the request's spelling would hand back a form the next save would change.
        var write = validated.Value;
        return Ok(new ContactView(created.Value, write.FirstName, write.LastName, write.Nickname,
            write.IsFavorite, [.. write.Addresses.Select(IdentityResolver.Canonical).Distinct()]));
    }

    /// <summary>Replaces the contact whole — names, favourite flag, and the entire address list.</summary>
    /// <response code="204">Saved</response>
    /// <response code="400">Neither name nor address, or an unparsable address</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such contact for this user</response>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Update(
        Guid id, ContactRequest request, CancellationToken cancellationToken)
    {
        var validated = ContactValidator.Validate(request);
        if (validated.IsFailure) return BadRequestEnveloppe(validated.Error);

        var saved = await store.UpdateAsync(
            AuthenticatedUser.WebmailUid, id, validated.Value, cancellationToken);
        return saved.IsSuccess ? NoContent() : NotFoundEnveloppe(saved.Error);
    }

    /// <summary>Deletes the contact and its addresses.</summary>
    /// <response code="204">Deleted</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such contact for this user</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await store.DeleteAsync(AuthenticatedUser.WebmailUid, id, cancellationToken);
        return deleted.IsSuccess ? NoContent() : NotFoundEnveloppe(deleted.Error);
    }

    /// <summary>
    /// Flips the favourite flag alone. Its own route because the star is toggled from a tile
    /// holding a possibly stale copy — a whole-contact PUT from there would clobber a concurrent
    /// edit, the same reason message flags have their own endpoint.
    /// </summary>
    /// <response code="204">Saved</response>
    /// <response code="400">No body</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="404">No such contact for this user</response>
    [HttpPut("{id:guid}/Favorite")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SetFavorite(
        Guid id, FavoriteRequest request, CancellationToken cancellationToken)
    {
        if (request == null) return BadRequestEnveloppe("Request body is required");

        var saved = await store.SetFavoriteAsync(
            AuthenticatedUser.WebmailUid, id, request.IsFavorite, cancellationToken);
        return saved.IsSuccess ? NoContent() : NotFoundEnveloppe(saved.Error);
    }
}
```

- [ ] **Step 5 : Enregistrer le store dans le conteneur**

Dans `src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs`, après la ligne 89 (`services.AddScoped<ITrustedSenderStore, TrustedSenderStore>();`) :

```csharp
        services.AddScoped<IContactStore, ContactStore>();
```

- [ ] **Step 6 : Lancer les tests pour vérifier qu'ils passent**

Run: `cd src/snoopy.microservice && dotnet test --filter ContactsControllerTests`
Expected: PASS — 12 tests.

- [ ] **Step 7 : Lancer toute la suite backend**

Run: `cd src/snoopy.microservice && dotnet test`
Expected: PASS — aucune régression.

- [ ] **Step 8 : Commit**

```bash
git add src/snoopy.microservice/Models/Contacts/ContactListResponse.cs \
        src/snoopy.microservice/Models/Contacts/FavoriteRequest.cs \
        src/snoopy.microservice/Controllers/ContactsController.cs \
        src/snoopy.microservice/Configuration/ApplicationServicesConfiguration.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Controllers/ContactsControllerTests.cs
git commit -F - <<'EOF'
Add ContactsController with the five contact routes

A contact owned by another user answers 404, never 403.
EOF
```

---

### Task 6 : `useAccountId` partagé, types et `contactName`

**Files:**
- Create: `src/frontend/src/hooks/useAccountId.ts`
- Modify: `src/frontend/src/modules/mail/queries.ts:65-67`
- Create: `src/frontend/src/modules/contacts/contactTypes.ts`
- Create: `src/frontend/src/modules/contacts/contactName.ts`
- Test: `src/frontend/src/modules/contacts/contactName.test.ts`

**Interfaces:**
- Consumes: `useAuth()` depuis `contexts/AuthContext`.
- Produces:
  - `useAccountId(): string` depuis `src/hooks/useAccountId.ts`, **ré-exporté** par `modules/mail/queries.ts` pour que ses importateurs actuels ne bougent pas.
  - `interface Contact { id: string; firstName: string | null; lastName: string | null; nickname: string | null; isFavorite: boolean; addresses: string[] }` — `addresses` ordonné, `[0]` = principale.
  - `interface ContactListResponse { contacts: Contact[] }`
  - `displayNameOf(contact: Contact): string`
  - `primaryAddressOf(contact: Contact): string | null`

**Pourquoi déplacer `useAccountId` :** il vit dans `modules/mail/queries.ts` alors que le module Contacts en a besoin pour scoper ses propres clés de cache. Un module qui importe la couche de données d'un autre module pour obtenir l'identité du compte est un couplage que rien ne justifie. Le hook n'appartient à aucun module : il rejoint `src/hooks/`.

- [ ] **Step 1 : Écrire le test qui échoue**

Créer `src/frontend/src/modules/contacts/contactName.test.ts` :

```ts
import { describe, expect, it } from 'vitest'
import { displayNameOf, primaryAddressOf } from './contactName'
import type { Contact } from './contactTypes'

function contact(fields: Partial<Contact> = {}): Contact {
  return {
    id: 'c1', firstName: null, lastName: null, nickname: null,
    isFavorite: false, addresses: [], ...fields,
  }
}

describe('displayNameOf', () => {
  it('joins first and last name', () => {
    expect(displayNameOf(contact({ firstName: 'Bruno', lastName: 'Mertens' }))).toBe('Bruno Mertens')
  })

  it('accepts a first name alone', () => {
    expect(displayNameOf(contact({ firstName: 'Bruno' }))).toBe('Bruno')
  })

  it('accepts a last name alone', () => {
    expect(displayNameOf(contact({ lastName: 'Mertens' }))).toBe('Mertens')
  })

  // The three fallbacks in order. A tile with no label at all is what this prevents, and every
  // screen has to fall back the same way or one contact reads under two different names.
  it('falls back to the nickname when there is no name', () => {
    expect(displayNameOf(contact({ nickname: 'bru' }))).toBe('bru')
  })

  it('falls back to the primary address when there is neither', () => {
    expect(displayNameOf(contact({ addresses: ['bruno@example.com', 'other@example.com'] })))
      .toBe('bruno@example.com')
  })

  it('prefers a name over a nickname', () => {
    expect(displayNameOf(contact({ firstName: 'Bruno', nickname: 'bru' }))).toBe('Bruno')
  })

  it('returns an empty string when the contact carries nothing', () => {
    expect(displayNameOf(contact())).toBe('')
  })
})

describe('primaryAddressOf', () => {
  it('is the first address', () => {
    expect(primaryAddressOf(contact({ addresses: ['a@x.be', 'b@x.be'] }))).toBe('a@x.be')
  })

  it('is null without any address', () => {
    expect(primaryAddressOf(contact())).toBeNull()
  })
})
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `cd src/frontend && npx vitest run src/modules/contacts/contactName.test.ts`
Expected: FAIL — les modules `./contactName` et `./contactTypes` n'existent pas.

- [ ] **Step 3 : Créer les types**

`src/frontend/src/modules/contacts/contactTypes.ts` :

```ts
/** One contact as `GET /api/Contacts` answers it. The API sends neither the vCard UID nor the raw
    card: no screen reads either. */
export interface Contact {
  id: string
  firstName: string | null
  lastName: string | null
  nickname: string | null
  isFavorite: boolean
  /** Ordered; `[0]` is the primary address. There is no separate flag to keep in step with it. */
  addresses: string[]
}

export interface ContactListResponse {
  contacts: Contact[]
}
```

- [ ] **Step 4 : Créer `contactName.ts`**

`src/frontend/src/modules/contacts/contactName.ts` :

```ts
import type { Contact } from './contactTypes'

/** The one place a contact is named. The tile, the card, the editor's heading and the composer's
    suggestion list all call this — four screens naming one contact four ways is the bug it
    prevents. */
export function displayNameOf(contact: Contact): string {
  const full = [contact.firstName, contact.lastName].filter(Boolean).join(' ')
  return full || contact.nickname || contact.addresses[0] || ''
}

export function primaryAddressOf(contact: Contact): string | null {
  return contact.addresses[0] ?? null
}
```

- [ ] **Step 5 : Lancer le test pour vérifier qu'il passe**

Run: `cd src/frontend && npx vitest run src/modules/contacts/contactName.test.ts`
Expected: PASS — 9 tests.

- [ ] **Step 6 : Extraire `useAccountId` vers `src/hooks/`**

Créer `src/frontend/src/hooks/useAccountId.ts` :

```ts
import { useAuth } from '../contexts/AuthContext'

/** The active account's id, the scope every module's query keys carry. Shared rather than owned
    by the mail module: contacts key their cache on it too, and a module reaching into another
    module's data layer for the account identity is a coupling nothing justifies. */
export function useAccountId(): string {
  return useAuth().activeAccount?.id ?? 'primary'
}
```

Dans `src/frontend/src/modules/mail/queries.ts`, remplacer la définition (lignes 65-67) :

```ts
export function useAccountId(): string {
  return useAuth().activeAccount?.id ?? 'primary'
}
```

par une ré-exportation, de sorte qu'aucun importateur actuel ne change :

```ts
// Moved to src/hooks: contacts scope their keys on it too. Re-exported so the mail module's own
// importers keep working from here.
export { useAccountId }
```

et ajouter l'import en tête du fichier, après la ligne `import { useAuth } ...` :

```ts
import { useAccountId } from '../../hooks/useAccountId'
```

- [ ] **Step 7 : Retirer l'import devenu inutile s'il l'est**

Run: `cd src/frontend && npx grep -n "useAuth" src/modules/mail/queries.ts || grep -n "useAuth" src/modules/mail/queries.ts`

Si `useAuth` n'apparaît plus qu'à sa ligne d'import, supprimer cette ligne. Puis vérifier :

Run: `cd src/frontend && npm run typecheck && npm run lint`
Expected: PASS — aucune erreur, aucun import inutilisé.

- [ ] **Step 8 : Lancer toute la suite frontend**

Run: `cd src/frontend && npx vitest run`
Expected: PASS — aucune régression ; les importateurs de `useAccountId` (dont `MailLayout`) compilent inchangés.

- [ ] **Step 9 : Commit**

```bash
git add src/frontend/src/hooks/useAccountId.ts \
        src/frontend/src/modules/mail/queries.ts \
        src/frontend/src/modules/contacts/contactTypes.ts \
        src/frontend/src/modules/contacts/contactName.ts \
        src/frontend/src/modules/contacts/contactName.test.ts
git commit -F - <<'EOF'
Add contact types and naming helper, share useAccountId

useAccountId moves to src/hooks so contacts can scope their keys without importing mail.
EOF
```

---

### Task 7 : `contactSearch` — repliement, filtre, tri, suggestions

**Files:**
- Create: `src/frontend/src/modules/contacts/contactSearch.ts`
- Test: `src/frontend/src/modules/contacts/contactSearch.test.ts`

**Interfaces:**
- Consumes: `Contact` (Task 6), `displayNameOf`, `primaryAddressOf`.
- Produces:
  - `fold(value: string): string` — sans accents, en minuscules.
  - `matches(contact: Contact, query: string): boolean` — sur prénom, nom, pseudo et adresses.
  - `filterContacts(contacts: Contact[], query: string): Contact[]`
  - `compareContacts(a: Contact, b: Contact): number` — favoris d'abord, puis `displayNameOf` en `localeCompare` (`sensitivity: 'base'`).
  - `interface AddressSuggestion { address: string; names: string[] }`
  - `suggestionsFor(contacts: Contact[], query: string, options?: { exclude?: Set<string>; limit?: number }): AddressSuggestion[]`

**Le cœur de la tranche 3b est `suggestionsFor`.** Une ligne par **adresse**, pas par contact — on choisit une adresse. Une adresse portée par plusieurs contacts donne **une seule ligne** dont `names` porte tous les noms : deux lignes produisant le même destinataire seraient du bruit, et n'en retenir qu'un nom serait l'arbitrage arbitraire que la décision d'autoriser les doublons nous interdit de trancher à l'aveugle.

- [ ] **Step 1 : Écrire le test qui échoue**

Créer `src/frontend/src/modules/contacts/contactSearch.test.ts` :

```ts
import { describe, expect, it } from 'vitest'
import { compareContacts, filterContacts, fold, matches, suggestionsFor } from './contactSearch'
import type { Contact } from './contactTypes'

function contact(fields: Partial<Contact> & { id: string }): Contact {
  return {
    firstName: null, lastName: null, nickname: null, isFavorite: false, addresses: [], ...fields,
  }
}

const bruno = contact({
  id: 'b', firstName: 'Bruno', lastName: 'Mertens', nickname: 'bru',
  addresses: ['bruno@example.com', 'b.mertens@wk.be'],
})
const chloe = contact({
  id: 'c', firstName: 'Chloé', lastName: 'Vermeulen', addresses: ['chloe@example.com'],
})
const alice = contact({
  id: 'a', firstName: 'Alice', lastName: 'Dupont', isFavorite: true,
  addresses: ['alice@example.com'],
})

describe('fold', () => {
  it('strips diacritics and lowercases', () => {
    expect(fold('Chloé VERMEULEN')).toBe('chloe vermeulen')
  })

  it('leaves plain text alone', () => {
    expect(fold('bruno')).toBe('bruno')
  })
})

describe('matches', () => {
  it('matches on the first name', () => {
    expect(matches(bruno, 'bru')).toBe(true)
  })

  it('matches on the last name', () => {
    expect(matches(bruno, 'mert')).toBe(true)
  })

  // A needle no other field carries: 'bru' prefixes the first name too, so it would match with
  // the nickname left out of the searched fields entirely.
  it('matches on the nickname', () => {
    expect(matches(contact({ id: 'n', firstName: 'Bruno', nickname: 'chef' }), 'chef')).toBe(true)
  })

  it('matches on any address, not only the primary', () => {
    expect(matches(bruno, 'wk.be')).toBe(true)
  })

  // Typing without accents has to find an accented contact: nobody reaches for the é key to
  // look somebody up. Neither fixture carries an address — chloe@example.com spells the name
  // unaccented, so it would answer both queries with the folding stripping nothing at all.
  it('ignores accents in both directions', () => {
    expect(matches(contact({ id: 'x', firstName: 'Chloé' }), 'chloe')).toBe(true)
    expect(matches(contact({ id: 'y', firstName: 'Chloe' }), 'chloé')).toBe(true)
  })

  it('ignores case', () => {
    expect(matches(bruno, 'BRUNO')).toBe(true)
  })

  it('matches anywhere in the field, not just at the start', () => {
    expect(matches(bruno, 'ertens')).toBe(true)
  })

  it('does not match unrelated text', () => {
    expect(matches(bruno, 'zzz')).toBe(false)
  })

  it('matches everything on an empty query', () => {
    expect(matches(bruno, '   ')).toBe(true)
  })
})

describe('filterContacts', () => {
  it('keeps only the matching contacts', () => {
    expect(filterContacts([bruno, chloe, alice], 'chlo').map(c => c.id)).toEqual(['c'])
  })

  it('returns everything on an empty query', () => {
    expect(filterContacts([bruno, chloe, alice], '')).toHaveLength(3)
  })
})

describe('compareContacts', () => {
  // The favourite is the one that sorts last by name: with Alice against Bruno the expected order
  // is also the alphabetical one, so a comparator ignoring the flag would pass.
  it('puts favourites first', () => {
    const zoe = contact({ id: 'z', firstName: 'Zoé', isFavorite: true })

    expect([bruno, zoe].sort(compareContacts).map(c => c.id)).toEqual(['z', 'b'])
  })

  it('sorts the rest by display name', () => {
    expect([chloe, bruno].sort(compareContacts).map(c => c.id)).toEqual(['b', 'c'])
  })

  // A codepoint sort files every accented name after Z, and a case-sensitive one exiles
  // 'e-commerce' past every capitalised name. localeCompare with base sensitivity is what the
  // folder list already uses.
  it('files an accented name where a reader expects it', () => {
    const eric = contact({ id: 'e', firstName: 'Éric' })
    const frank = contact({ id: 'f', firstName: 'Frank' })
    const dora = contact({ id: 'd', firstName: 'Dora' })

    expect([frank, eric, dora].sort(compareContacts).map(c => c.id)).toEqual(['d', 'e', 'f'])
  })
})

describe('suggestionsFor', () => {
  it('answers one row per address, not per contact', () => {
    const rows = suggestionsFor([bruno], 'bru')

    expect(rows.map(r => r.address)).toEqual(['bruno@example.com', 'b.mertens@wk.be'])
  })

  it('names each row with its contact', () => {
    expect(suggestionsFor([chloe], 'chlo')[0].names).toEqual(['Chloé Vermeulen'])
  })

  // The decision to allow a shared address lands here: one row, every owner named. Two rows would
  // produce the identical recipient, and picking one name would be an arbitrary arbitration.
  it('collapses an address shared by two contacts into one row naming both', () => {
    const shared = 'info@example.com'
    const first = contact({ id: '1', firstName: 'Alice', lastName: 'Dupont', addresses: [shared] })
    const second = contact({ id: '2', firstName: 'Compta', lastName: 'Weesky', addresses: [shared] })

    const rows = suggestionsFor([first, second], 'info')

    expect(rows).toHaveLength(1)
    expect(rows[0].address).toBe(shared)
    expect(rows[0].names).toEqual(['Alice Dupont', 'Compta Weesky'])
  })

  // The favourite's address sorts last alphabetically and is nobody's primary but its own, so it
  // reaches the top on the favourite rule alone — alice@example.com would have won the address
  // tiebreak without it.
  it('puts a favourite contact’s address first', () => {
    const zoe = contact({
      id: 'z', firstName: 'Zoé', isFavorite: true, addresses: ['zoe@example.com'],
    })

    const rows = suggestionsFor([bruno, zoe], 'e')

    expect(rows[0].address).toBe('zoe@example.com')
  })

  it('puts a primary address before a secondary one', () => {
    const rows = suggestionsFor([bruno], 'e')
    const primary = rows.findIndex(r => r.address === 'bruno@example.com')
    const secondary = rows.findIndex(r => r.address === 'b.mertens@wk.be')

    expect(primary).toBeLessThan(secondary)
  })

  it('finds a contact by name and offers its addresses', () => {
    expect(suggestionsFor([chloe], 'vermeulen').map(r => r.address)).toEqual(['chloe@example.com'])
  })

  // An excluded address must not eat a slot, or a field with nine tokens would show one option.
  it('drops excluded addresses without spending the cap', () => {
    const many = Array.from({ length: 12 }, (_, i) =>
      contact({ id: `c${i}`, firstName: `C${i}`, addresses: [`c${i}@example.com`] }))

    const rows = suggestionsFor(many, 'example', { exclude: new Set(['c0@example.com']), limit: 10 })

    expect(rows).toHaveLength(10)
    expect(rows.map(r => r.address)).not.toContain('c0@example.com')
  })

  it('caps the list at ten rows by default', () => {
    const many = Array.from({ length: 30 }, (_, i) =>
      contact({ id: `c${i}`, firstName: `C${i}`, addresses: [`c${i}@example.com`] }))

    expect(suggestionsFor(many, 'example')).toHaveLength(10)
  })

  it('answers nothing on an empty query', () => {
    expect(suggestionsFor([bruno], '   ')).toEqual([])
  })

  it('ignores a contact carrying no address', () => {
    expect(suggestionsFor([contact({ id: 'n', firstName: 'Nobody' })], 'nobody')).toEqual([])
  })
})
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `cd src/frontend && npx vitest run src/modules/contacts/contactSearch.test.ts`
Expected: FAIL — le module `./contactSearch` n'existe pas.

- [ ] **Step 3 : Écrire `contactSearch.ts`**

`src/frontend/src/modules/contacts/contactSearch.ts` :

```ts
import { displayNameOf, primaryAddressOf } from './contactName'
import type { Contact } from './contactTypes'

const DEFAULT_LIMIT = 10

/** Diacritics stripped and lower-cased. Nobody reaches for the é key to look somebody up, so a
    query has to match an accented contact and the reverse. */
export function fold(value: string): string {
  return value.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()
}

/** Case- and accent-insensitive substring across every field a user would search by. Shared by
    the page's filter and the composer's dropdown — one rule, so the two can never disagree about
    what "matching" means. */
export function matches(contact: Contact, query: string): boolean {
  const needle = fold(query.trim())
  if (needle === '') return true

  return [contact.firstName, contact.lastName, contact.nickname, ...contact.addresses]
    .some(field => field != null && fold(field).includes(needle))
}

export function filterContacts(contacts: Contact[], query: string): Contact[] {
  return contacts.filter(contact => matches(contact, query))
}

/** Favourites first, then by display name. localeCompare with base sensitivity for the reason the
    folder list uses it: a codepoint sort files every accented name after 'Z'. */
export function compareContacts(a: Contact, b: Contact): number {
  if (a.isFavorite !== b.isFavorite) return a.isFavorite ? -1 : 1
  return displayNameOf(a).localeCompare(displayNameOf(b), undefined, { sensitivity: 'base' })
}

export interface AddressSuggestion {
  address: string
  /** Every contact carrying this address, in contact order. Length > 1 for a shared mailbox. */
  names: string[]
}

/**
 * The composer's dropdown. Rows are keyed by **address**, since an address is what gets inserted:
 * one address carried by several contacts is one row naming all of them, never several rows
 * producing the identical recipient.
 */
export function suggestionsFor(
  contacts: Contact[],
  query: string,
  options: { exclude?: Set<string>; limit?: number } = {},
): AddressSuggestion[] {
  const { exclude, limit = DEFAULT_LIMIT } = options
  if (fold(query.trim()) === '') return []

  const rows = new Map<string, { names: string[]; favorite: boolean; primary: boolean }>()

  for (const contact of [...contacts].sort(compareContacts)) {
    if (!matches(contact, query)) continue

    const primary = primaryAddressOf(contact)
    for (const address of contact.addresses) {
      if (exclude?.has(address)) continue

      const existing = rows.get(address)
      if (existing) {
        existing.names.push(displayNameOf(contact))
        existing.favorite ||= contact.isFavorite
        existing.primary ||= address === primary
        continue
      }
      rows.set(address, {
        names: [displayNameOf(contact)],
        favorite: contact.isFavorite,
        primary: address === primary,
      })
    }
  }

  return [...rows.entries()]
    .sort(([leftAddress, left], [rightAddress, right]) =>
      Number(right.favorite) - Number(left.favorite)
      || Number(right.primary) - Number(left.primary)
      || leftAddress.localeCompare(rightAddress, undefined, { sensitivity: 'base' }))
    .slice(0, limit)
    .map(([address, row]) => ({ address, names: row.names }))
}
```

- [ ] **Step 4 : Lancer le test pour vérifier qu'il passe**

Run: `cd src/frontend && npx vitest run src/modules/contacts/contactSearch.test.ts`
Expected: PASS — 26 tests.

- [ ] **Step 5 : Commit**

```bash
git add src/frontend/src/modules/contacts/contactSearch.ts \
        src/frontend/src/modules/contacts/contactSearch.test.ts
git commit -F - <<'EOF'
Add contactSearch: folding, filter, sort and address suggestions

A shared address collapses to one suggestion row naming every contact that carries it.
EOF
```

---

### Task 8 : Client API et hooks TanStack Query

**Files:**
- Modify: `src/frontend/src/api.js` (après `putIdentities`, ligne ~143)
- Modify: `src/frontend/src/api.test.js` (après le bloc `putIdentities`, ligne ~150)
- Modify: `src/frontend/src/modules/contacts/contactTypes.ts`
- Create: `src/frontend/src/modules/contacts/queries.ts`

**Interfaces:**
- Consumes: `request()` via `api`, `useAccountId` (Task 6), `compareContacts` (Task 7).
- Produces:
  - `api.getContacts()`, `api.createContact(contact)`, `api.updateContact(id, contact)`, `api.deleteContact(id)`, `api.setContactFavorite(id, isFavorite)`.
  - `interface ContactDraft { firstName: string | null; lastName: string | null; nickname: string | null; isFavorite: boolean; addresses: string[] }`
  - `contactKeys.all(accountId)` → `['contacts', accountId]`
  - `useContacts()` → `UseQueryResult<Contact[]>`, **déjà trié** par `compareContacts`.
  - `useCreateContact()`, `useUpdateContact()`, `useDeleteContact()`, `useSetContactFavorite()`.

- [ ] **Step 1 : Écrire les tests API qui échouent**

Dans `src/frontend/src/api.test.js`, insérer après le bloc `putIdentities` (ligne ~150) :

```js
  it('getContacts calls GET /api/Contacts', async () => {
    const { api } = await import('./api.js')
    await api.getContacts()
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Contacts'),
      expect.objectContaining({ method: 'GET' })
    )
  })

  it('createContact POSTs the contact', async () => {
    const { api } = await import('./api.js')
    const draft = {
      firstName: 'Bruno', lastName: 'Mertens', nickname: null,
      isFavorite: false, addresses: ['bruno@example.com'],
    }
    await api.createContact(draft)
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Contacts'),
      expect.objectContaining({ method: 'POST', body: JSON.stringify(draft) })
    )
  })

  it('updateContact PUTs to the contact id', async () => {
    const { api } = await import('./api.js')
    await api.updateContact('11111111-1111-1111-1111-111111111111', { firstName: 'B' })
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Contacts/11111111-1111-1111-1111-111111111111'),
      expect.objectContaining({ method: 'PUT' })
    )
  })

  it('deleteContact DELETEs the contact id', async () => {
    const { api } = await import('./api.js')
    await api.deleteContact('22222222-2222-2222-2222-222222222222')
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Contacts/22222222-2222-2222-2222-222222222222'),
      expect.objectContaining({ method: 'DELETE' })
    )
  })

  it('setContactFavorite PUTs the flag to the Favorite sub-route', async () => {
    const { api } = await import('./api.js')
    await api.setContactFavorite('33333333-3333-3333-3333-333333333333', true)
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/api/Contacts/33333333-3333-3333-3333-333333333333/Favorite'),
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ isFavorite: true }) })
    )
  })
```

- [ ] **Step 2 : Lancer les tests pour vérifier qu'ils échouent**

Run: `cd src/frontend && npx vitest run src/api.test.js`
Expected: FAIL — `api.getContacts is not a function`.

- [ ] **Step 3 : Ajouter les cinq méthodes au client**

Dans `src/frontend/src/api.js`, après `putIdentities` :

```js
  getContacts: () =>
    request('GET', '/api/Contacts'),

  createContact: (contact) =>
    request('POST', '/api/Contacts', contact),

  // Replaces the contact whole — names, favourite flag and the entire address list.
  updateContact: (id, contact) =>
    request('PUT', `/api/Contacts/${id}`, contact),

  deleteContact: (id) =>
    request('DELETE', `/api/Contacts/${id}`),

  // Its own route: the star is toggled from a tile holding a possibly stale copy, so a whole
  // contact PUT from there would clobber a concurrent edit.
  setContactFavorite: (id, isFavorite) =>
    request('PUT', `/api/Contacts/${id}/Favorite`, { isFavorite }),
```

Un id de contact est un GUID : il ne contient ni `/` ni `&`, donc il voyage sans risque dans un segment de route — contrairement à un chemin de dossier IMAP, qui est précisément pourquoi ceux-là passent par la query string.

- [ ] **Step 4 : Lancer les tests pour vérifier qu'ils passent**

Run: `cd src/frontend && npx vitest run src/api.test.js`
Expected: PASS — les 5 nouveaux tests, plus les existants inchangés.

- [ ] **Step 5 : Ajouter le type d'écriture**

Dans `src/frontend/src/modules/contacts/contactTypes.ts`, ajouter :

```ts
/** What the editor submits. Same shape as `Contact` minus its id: the API assigns that. */
export interface ContactDraft {
  firstName: string | null
  lastName: string | null
  nickname: string | null
  isFavorite: boolean
  addresses: string[]
}
```

- [ ] **Step 6 : Créer les hooks**

`src/frontend/src/modules/contacts/queries.ts` :

```ts
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api.js'
import { useAccountId } from '../../hooks/useAccountId'
import { compareContacts } from './contactSearch'
import type { Contact, ContactDraft, ContactListResponse } from './contactTypes'

/** Scoped by account from the outset, like the mail keys: linking a second account later isolates
    its book instead of mixing two. */
export const contactKeys = {
  all: (accountId: string) => ['contacts', accountId] as const,
}

/**
 * The whole book, cached. Long staleTime: it changes only from this module, which invalidates it.
 * Sorted in `select`, so the page and the composer read one already-ordered list rather than each
 * sorting its own copy.
 */
export function useContacts() {
  const accountId = useAccountId()

  return useQuery({
    queryKey: contactKeys.all(accountId),
    queryFn: () => api.getContacts() as Promise<ContactListResponse>,
    staleTime: 5 * 60_000,
    select: (data): Contact[] => [...data.contacts].sort(compareContacts),
  })
}

// Settled, not success: after a refused write the screen must fall back to the server's state
// rather than keep an optimistic list nobody stored.
function useContactMutation<TArgs>(mutationFn: (args: TArgs) => Promise<unknown>) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn,
    onSettled: () => queryClient.invalidateQueries({ queryKey: contactKeys.all(accountId) }),
  })
}

export function useCreateContact() {
  return useContactMutation((contact: ContactDraft) => api.createContact(contact))
}

export function useUpdateContact() {
  return useContactMutation(
    ({ id, contact }: { id: string; contact: ContactDraft }) => api.updateContact(id, contact))
}

export function useDeleteContact() {
  return useContactMutation((id: string) => api.deleteContact(id))
}

export function useSetContactFavorite() {
  return useContactMutation(
    ({ id, isFavorite }: { id: string; isFavorite: boolean }) =>
      api.setContactFavorite(id, isFavorite))
}
```

Pas de test dédié pour `queries.ts` : le projet exerce ses hooks à travers les composants qui les consomment, ce que font les tâches 9 à 13.

- [ ] **Step 7 : Vérifier types et lint**

Run: `cd src/frontend && npm run typecheck && npm run lint`
Expected: PASS.

- [ ] **Step 8 : Commit**

```bash
git add src/frontend/src/api.js src/frontend/src/api.test.js \
        src/frontend/src/modules/contacts/contactTypes.ts \
        src/frontend/src/modules/contacts/queries.ts
git commit -F - <<'EOF'
Add contacts api client methods and query hooks

The cached list is sorted in select, so page and composer share one ordering.
EOF
```

---

### Task 9 : Routes, `ContactsLayout` et `ContactScopes`

**Files:**
- Create: `src/frontend/src/modules/contacts/ContactsLayout.tsx`
- Create: `src/frontend/src/modules/contacts/ContactScopes.tsx`
- Create: `src/frontend/src/modules/contacts/ContactScopes.test.tsx`
- Create: `src/frontend/src/modules/contacts/ContactsLayout.test.tsx`
- Modify: `src/frontend/src/routes.tsx:33`
- Modify: `src/frontend/src/index.css`

**Interfaces:**
- Consumes: `useContacts` (Task 8), `filterContacts` (Task 7), `useToasts`, `Toasts`, `ContactsIcon`, `PersonPlusIcon`, `usePaneSize`, `PaneSplitter`.
- Produces:
  - `ContactScopes` props : `{ scope: ContactScope; total: number; favorites: number; onScope: (scope: ContactScope) => void }`
  - `type ContactScope = 'all' | 'favorites'` exporté depuis `ContactScopes.tsx`.
  - `ContactsLayout` — export par défaut, la coquille du module ; les tâches 10 à 12 y branchent leurs colonnes.

**La sélection vit dans les search params, l'édition dans un segment de route.** `?scope=` et `?id=` pour ce qui est consulté — même choix que `/mail?folder=&uid=` ; `/contacts/new` et `/contacts/:id/edit` pour l'éditeur — même choix que `/mail/compose`. Un id de contact est un GUID, donc un segment de route le porte sans encodage, contrairement à un chemin de dossier.

- [ ] **Step 1 : Écrire les tests qui échouent**

Créer `src/frontend/src/modules/contacts/ContactScopes.test.tsx` :

```tsx
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ContactScopes from './ContactScopes'

describe('ContactScopes', () => {
  it('shows both scopes with their counts', () => {
    render(<ContactScopes scope="all" total={42} favorites={5} onScope={vi.fn()} />)

    expect(screen.getByRole('button', { name: /all contacts/i })).toHaveTextContent('42')
    expect(screen.getByRole('button', { name: /favourites/i })).toHaveTextContent('5')
  })

  // `is-active` is the hook the navigation paint hangs on, and it must land on the active row
  // alone. Whether that paint is a fill rather than an accent bar is a CSS fact jsdom computes
  // nothing about — it is measured in the browser pass, Task 15.
  it('marks the active scope, and only the active one', () => {
    render(<ContactScopes scope="favorites" total={42} favorites={5} onScope={vi.fn()} />)

    expect(screen.getByRole('button', { name: /favourites/i })).toHaveClass('is-active')
    expect(screen.getByRole('button', { name: /all contacts/i })).not.toHaveClass('is-active')
  })

  it('reports a scope change', async () => {
    const onScope = vi.fn()
    render(<ContactScopes scope="all" total={42} favorites={5} onScope={onScope} />)

    await userEvent.click(screen.getByRole('button', { name: /favourites/i }))

    expect(onScope).toHaveBeenCalledWith('favorites')
  })

  // Zero is printed, not hidden: an absent count reads as a rendering fault next to a row that
  // has one.
  it('prints a zero count', () => {
    render(<ContactScopes scope="all" total={0} favorites={0} onScope={vi.fn()} />)

    expect(screen.getByRole('button', { name: /all contacts/i })).toHaveTextContent('0')
  })
})
```

Créer `src/frontend/src/modules/contacts/ContactsLayout.test.tsx` :

```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi, beforeEach } from 'vitest'
import ContactsLayout from './ContactsLayout'
import type { Contact } from './contactTypes'

vi.mock('../../api.js', () => ({
  api: { getContacts: vi.fn() },
  ApiError: class extends Error {},
}))
vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'primary' }))

const { api } = await import('../../api.js') as unknown as
  { api: { getContacts: ReturnType<typeof vi.fn> } }

function contact(fields: Partial<Contact> & { id: string }): Contact {
  return {
    firstName: null, lastName: null, nickname: null, isFavorite: false, addresses: [], ...fields,
  }
}

function renderAt(path: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/contacts" element={<ContactsLayout />} />
          <Route path="/contacts/new" element={<ContactsLayout />} />
          <Route path="/contacts/:id/edit" element={<ContactsLayout />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('ContactsLayout', () => {
  beforeEach(() => {
    api.getContacts.mockResolvedValue({
      contacts: [
        contact({ id: 'a', firstName: 'Alice', isFavorite: true, addresses: ['alice@x.be'] }),
        contact({ id: 'b', firstName: 'Bruno', addresses: ['bruno@x.be'] }),
      ],
    })
  })

  it('counts the whole book and its favourites in the band', async () => {
    renderAt('/contacts')

    await waitFor(() =>
      expect(screen.getByRole('button', { name: /all contacts/i })).toHaveTextContent('2'))
    expect(screen.getByRole('button', { name: /favourites/i })).toHaveTextContent('1')
  })

  // The editor swaps the two content columns and leaves the band standing — the mechanism
  // /mail/compose uses, so a scope stays one click away while a contact is being written.
  it('swaps the content columns for the editor and keeps the band', async () => {
    renderAt('/contacts/new')

    await waitFor(() => expect(screen.getByRole('button', { name: /all contacts/i })).toBeInTheDocument())
    expect(screen.queryByTestId('contact-list')).not.toBeInTheDocument()
    expect(screen.getByTestId('contact-editor')).toBeInTheDocument()
  })

  it('shows list and card outside the editor routes', async () => {
    renderAt('/contacts')

    await waitFor(() => expect(screen.getByTestId('contact-list')).toBeInTheDocument())
    expect(screen.getByTestId('contact-card')).toBeInTheDocument()
    expect(screen.queryByTestId('contact-editor')).not.toBeInTheDocument()
  })
})
```

- [ ] **Step 2 : Lancer les tests pour vérifier qu'ils échouent**

Run: `cd src/frontend && npx vitest run src/modules/contacts/`
Expected: FAIL — `ContactsLayout` et `ContactScopes` n'existent pas.

- [ ] **Step 3 : Créer `ContactScopes.tsx`**

```tsx
import ContactsIcon from '../../icons/ContactsIcon'
import StarIcon from '../../icons/StarIcon'

export type ContactScope = 'all' | 'favorites'

interface Props {
  scope: ContactScope
  total: number
  favorites: number
  onScope: (scope: ContactScope) => void
}

/**
 * The module's navigation band, on the same surface as the mail folder tree and the settings
 * context pane. It marks its active row with a fill and heavier weight and **no accent bar**: the
 * bar belongs to content lists, and keeping the two languages apart is how a reader tells a
 * navigation pane from a list of rows at a glance.
 *
 * Two scopes today. It is also where import will land in slice 3d and where CardDAV address books
 * would go — the reason the module has a band at all rather than starting flush against the rail.
 */
export default function ContactScopes({ scope, total, favorites, onScope }: Props) {
  return (
    <nav className="contact-scopes">
      <button type="button" className={`contact-scope${scope === 'all' ? ' is-active' : ''}`}
        onClick={() => onScope('all')}>
        <ContactsIcon size={15} />
        <span className="contact-scope-label">All contacts</span>
        <span className="contact-scope-count">{total}</span>
      </button>
      <button type="button" className={`contact-scope${scope === 'favorites' ? ' is-active' : ''}`}
        onClick={() => onScope('favorites')}>
        <StarIcon size={15} />
        <span className="contact-scope-label">Favourites</span>
        <span className="contact-scope-count">{favorites}</span>
      </button>
    </nav>
  )
}
```

- [ ] **Step 4 : Créer `ContactsLayout.tsx`**

Les colonnes centrales sont des espaces réservés portant les `data-testid` que les tâches 10 à 12 remplaceront par les vrais composants. C'est ce qui permet à cette tâche d'être testable seule.

```tsx
import { useState } from 'react'
import { useMatch, useNavigate, useSearchParams } from 'react-router-dom'
import Toasts from '../../components/Toasts.jsx'
import { useToasts } from '../../hooks/useToasts.js'
import PaneSplitter from '../mail/split/PaneSplitter'
import { usePaneSize } from '../mail/split/usePaneSize'
import ContactScopes, { type ContactScope } from './ContactScopes'
import { useContacts } from './queries'

/**
 * The contacts module's three columns. The shell hands a module one outlet, so the module builds
 * its own columns inside it — the same way the mail module and the settings section do.
 *
 * Each column is a band stack: `min-height: 0` on the one scrolling band is the load-bearing
 * part, without which the scroll escapes to the whole column and the pinned heading drifts away.
 */
export default function ContactsLayout() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const { toasts, addToast, removeToast } = useToasts()
  const { data: contacts, isLoading, isError } = useContacts()

  // The editor takes the two content columns and leaves the band standing, exactly as the
  // composer does inside the mail module. Two routes, one layout — not a layout of its own.
  const creating = useMatch('/contacts/new') != null
  const editing = useMatch('/contacts/:id/edit') != null
  const inEditor = creating || editing

  const scope: ContactScope = params.get('scope') === 'favorites' ? 'favorites' : 'all'
  const selectedId = params.get('id')

  const [listWidth, setListWidth] = usePaneSize('contacts.split.right', 380, 240)

  const total = contacts?.length ?? 0
  const favorites = contacts?.filter(contact => contact.isFavorite).length ?? 0

  function changeScope(next: ContactScope) {
    // Dropping the selected id: a contact filtered out of the new scope must not stay open, the
    // same reason choosing a folder drops the open message's uid.
    setParams(next === 'favorites' ? { scope: next } : {})
  }

  return (
    <div className="contacts-layout">
      <div className="contacts-scopes-column">
        <div className="contacts-scopes-add">
          <button type="button" className="btn btn-primary contacts-add-btn"
            onClick={() => navigate('/contacts/new')}>
            + Add contact
          </button>
        </div>
        <div className="contacts-scopes-scroll">
          <ContactScopes scope={scope} total={total} favorites={favorites} onScope={changeScope} />
        </div>
      </div>

      {inEditor ? (
        <div className="contacts-editor" data-testid="contact-editor">
          {/* Task 12 mounts ContactEditView here. */}
        </div>
      ) : (
        <div className="contacts-row">
          <div className="contacts-list" style={{ width: listWidth }} data-testid="contact-list">
            {isLoading && <p className="contacts-empty">Loading contacts…</p>}
            {isError && <p className="contacts-empty">Could not load contacts.</p>}
            {/* Task 10 mounts ContactList here. */}
          </div>
          <PaneSplitter orientation="vertical" size={listWidth} defaultSize={380} min={240}
            reserve={320} onResize={setListWidth} />
          <div className="contacts-card" data-testid="contact-card">
            {/* Task 11 mounts ContactCard here, for selectedId. */}
          </div>
        </div>
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
```

`selectedId` et `addToast` sont déclarés ici et consommés par les tâches 11 et 12 ; le lint peut les signaler comme inutilisés à cette étape — les brancher est le travail de ces tâches, ne pas les supprimer.

- [ ] **Step 5 : Brancher les trois routes**

Dans `src/frontend/src/routes.tsx`, ajouter l'import paresseux après celui de `MailLayout` (ligne 14) :

```tsx
const ContactsLayout = lazy(() => import('./modules/contacts/ContactsLayout'))
```

et remplacer la ligne 33 (`{ path: 'contacts', element: <ComingSoon module="Contacts" /> },`) par :

```tsx
          { path: 'contacts', element: <Suspense fallback={null}><ContactsLayout /></Suspense> },
          // The editor lives inside the contacts module: same layout, the two content columns
          // replaced. A contact id is a GUID, so it travels safely in a route segment.
          { path: 'contacts/new', element: <Suspense fallback={null}><ContactsLayout /></Suspense> },
          { path: 'contacts/:id/edit', element: <Suspense fallback={null}><ContactsLayout /></Suspense> },
```

- [ ] **Step 6 : Ajouter les styles du module**

Dans `src/frontend/src/index.css`, à la fin du fichier :

```css
/* ── Contacts module ───────────────────────────────────────────────────────────
   Three band-stack columns inside the shell's single content area, the same
   arrangement the mail module uses. min-height: 0 on the scrolling band is what
   keeps the pinned heading pinned. */
.contacts-layout { display: flex; height: 100%; min-height: 0; overflow: hidden; }

.contacts-scopes-column {
  width: 240px; flex: none; display: flex; flex-direction: column; min-height: 0;
  overflow: hidden; background: var(--folders-bg); border-right: 1px solid var(--border);
}
.contacts-scopes-add { flex: none; padding: 12px; }
.contacts-add-btn { width: 100%; justify-content: center; }
.contacts-scopes-scroll { flex: 1; min-height: 0; overflow-y: auto; padding: 0 8px 12px; }

.contact-scopes { display: flex; flex-direction: column; gap: 2px; }
.contact-scope {
  display: flex; align-items: center; gap: 8px; width: 100%; padding: 7px 10px;
  border: 0; border-radius: var(--radius-sm); background: transparent; cursor: pointer;
  color: var(--text); font: inherit; font-size: 13px; text-align: left;
}
.contact-scope:hover { background: var(--folders-item-hover); }
/* Fill and weight, no accent bar: the navigation language. */
.contact-scope.is-active {
  background: var(--pane-item-active-bg); color: var(--pane-item-active-fg); font-weight: 600;
}
.contact-scope-label { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; }
.contact-scope-count { flex: none; color: var(--text-muted); font-size: 12px; }
.contact-scope.is-active .contact-scope-count { color: inherit; }

.contacts-row { flex: 1; display: flex; min-width: 0; min-height: 0; overflow: hidden; }
.contacts-list, .contacts-card, .contacts-editor {
  display: flex; flex-direction: column; min-height: 0; overflow: hidden; background: var(--surface);
}
.contacts-list { flex: none; }
.contacts-card { flex: 1; min-width: 0; }
.contacts-editor { flex: 1; min-width: 0; }
.contacts-empty { margin: 0; padding: 20px; color: var(--text-muted); font-size: 13px; text-align: center; }
```

- [ ] **Step 7 : Lancer les tests pour vérifier qu'ils passent**

Run: `cd src/frontend && npx vitest run src/modules/contacts/`
Expected: PASS — 7 tests (4 `ContactScopes`, 3 `ContactsLayout`).

- [ ] **Step 8 : Vérifier types, lint et suite complète**

Run: `cd src/frontend && npm run typecheck && npm run lint && npx vitest run`
Expected: PASS.

- [ ] **Step 9 : Commit**

```bash
git add src/frontend/src/modules/contacts/ContactsLayout.tsx \
        src/frontend/src/modules/contacts/ContactsLayout.test.tsx \
        src/frontend/src/modules/contacts/ContactScopes.tsx \
        src/frontend/src/modules/contacts/ContactScopes.test.tsx \
        src/frontend/src/routes.tsx src/frontend/src/index.css
git commit -F - <<'EOF'
Add contacts module shell: three columns, scopes band, three routes

The editor swaps the content columns and leaves the band standing, as compose does.
EOF
```

---

### Task 10 : `ContactList` — les tuiles et le filtre

**Files:**
- Create: `src/frontend/src/modules/contacts/ContactList.tsx`
- Create: `src/frontend/src/modules/contacts/ContactList.test.tsx`
- Modify: `src/frontend/src/index.css`

**Interfaces:**
- Consumes: `Contact`, `displayNameOf`, `primaryAddressOf`, `filterContacts`, `StarIcon` (prop `filled`), `PencilIcon`, `TrashIcon`, `SearchIcon`.
- Produces: `ContactList` — export par défaut. Props :
  `{ contacts: Contact[]; selectedId: string | null; onSelect: (id: string) => void; onToggleFavorite: (contact: Contact) => void; onEdit: (id: string) => void; onDelete: (contact: Contact) => void }`
  Les contacts reçus sont **déjà filtrés par portée** ; le texte cherché est un état interne.

**Correction de la spec, à consigner.** La spec annonce « deux peaux comme la liste de messages ». C'est excessif : dans le mail les deux peaux existent parce que trois arrangements de volet existent, alors qu'ici la liste est **toujours** à côté de la fiche. Une seconde peau serait du code inatteignable, donc la tuile n'en a qu'une, **celle sur deux lignes** — qui était de toute façon la réponse aux ~348px de la colonne au plancher de 1024px. La tâche 15 corrige la formulation de la spec.

- [ ] **Step 1 : Écrire le test qui échoue**

Créer `src/frontend/src/modules/contacts/ContactList.test.tsx` :

```tsx
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ContactList from './ContactList'
import type { Contact } from './contactTypes'

function contact(fields: Partial<Contact> & { id: string }): Contact {
  return {
    firstName: null, lastName: null, nickname: null, isFavorite: false, addresses: [], ...fields,
  }
}

const alice = contact({
  id: 'a', firstName: 'Alice', lastName: 'Dupont', isFavorite: true, addresses: ['alice@x.be'],
})
const bruno = contact({
  id: 'b', firstName: 'Bruno', lastName: 'Mertens', addresses: ['bruno@x.be', 'b@wk.be', 'c@wk.be'],
})

function setup(overrides: Partial<Parameters<typeof ContactList>[0]> = {}) {
  const props = {
    contacts: [alice, bruno], selectedId: null,
    onSelect: vi.fn(), onToggleFavorite: vi.fn(), onEdit: vi.fn(), onDelete: vi.fn(),
    ...overrides,
  }
  render(<ContactList {...props} />)
  return props
}

describe('ContactList', () => {
  it('renders one tile per contact, named by displayNameOf', () => {
    setup()

    expect(screen.getByText('Alice Dupont')).toBeInTheDocument()
    expect(screen.getByText('Bruno Mertens')).toBeInTheDocument()
  })

  it('shows the primary address and counts the others', () => {
    setup()

    expect(screen.getByText(/bruno@x\.be/)).toHaveTextContent('+2')
  })

  it('shows the address alone when there is only one', () => {
    setup()

    expect(screen.getByText(/alice@x\.be/)).not.toHaveTextContent('+')
  })

  // Anchored: "2 / 2" contains a 2 as well, and printing it while nothing is filtered is exactly
  // the reading — something is hidden — the bare count exists to avoid.
  it('shows the bare total while nothing is being filtered', () => {
    setup()

    expect(screen.getByTestId('contact-count')).toHaveTextContent(/^2$/)
  })

  it('filters live as the user types, and updates the count', async () => {
    setup()

    await userEvent.type(screen.getByRole('searchbox'), 'dupont')

    expect(screen.getByText('Alice Dupont')).toBeInTheDocument()
    expect(screen.queryByText('Bruno Mertens')).not.toBeInTheDocument()
    expect(screen.getByTestId('contact-count')).toHaveTextContent('1 / 2')
  })

  it('finds a contact by an address that is not the primary', async () => {
    setup()

    await userEvent.type(screen.getByRole('searchbox'), 'wk.be')

    expect(screen.getByText('Bruno Mertens')).toBeInTheDocument()
  })

  it('reports the picked contact', async () => {
    const props = setup()

    await userEvent.click(screen.getByText('Bruno Mertens'))

    expect(props.onSelect).toHaveBeenCalledWith('b')
  })

  // `is-selected` is the hook the content-row paint hangs on — the selected fill plus an inset
  // accent bar, the opposite language from the navigation band. The paint itself is a CSS fact
  // jsdom computes nothing about; it is measured in the browser pass, Task 15.
  it('marks the selected tile with the content-row class', () => {
    setup({ selectedId: 'b' })

    expect(screen.getByTestId('contact-tile-b')).toHaveClass('is-selected')
    expect(screen.getByTestId('contact-tile-a')).not.toHaveClass('is-selected')
  })

  // Two things at once, and the label alone proves neither: it names the action to come, while
  // `is-on` is what actually lights the star.
  it('shows a lit star for a favourite and an unlit one otherwise', () => {
    setup()

    expect(screen.getByRole('button', { name: /remove alice dupont from favourites/i }))
      .toHaveClass('is-on')
    expect(screen.getByRole('button', { name: /add bruno mertens to favourites/i }))
      .not.toHaveClass('is-on')
  })

  // The star must not open the contact underneath it: two things would happen on one click.
  it('toggling the star does not select the contact', async () => {
    const props = setup()

    await userEvent.click(screen.getByRole('button', { name: /add bruno mertens to favourites/i }))

    expect(props.onToggleFavorite).toHaveBeenCalledWith(bruno)
    expect(props.onSelect).not.toHaveBeenCalled()
  })

  it('reports edit and delete without selecting', async () => {
    const props = setup()

    await userEvent.click(screen.getByRole('button', { name: /edit bruno mertens/i }))
    await userEvent.click(screen.getByRole('button', { name: /delete bruno mertens/i }))

    expect(props.onEdit).toHaveBeenCalledWith('b')
    expect(props.onDelete).toHaveBeenCalledWith(bruno)
    expect(props.onSelect).not.toHaveBeenCalled()
  })

  it('shows a muted line rather than a blank area when empty', () => {
    setup({ contacts: [] })

    expect(screen.getByText(/no contacts/i)).toBeInTheDocument()
  })

  it('says so when the filter matches nothing', async () => {
    setup()

    await userEvent.type(screen.getByRole('searchbox'), 'zzz')

    expect(screen.getByText(/no matching contacts/i)).toBeInTheDocument()
  })
})
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `cd src/frontend && npx vitest run src/modules/contacts/ContactList.test.tsx`
Expected: FAIL — le module `./ContactList` n'existe pas.

- [ ] **Step 3 : Créer `ContactList.tsx`**

```tsx
import { useMemo, useState } from 'react'
import PencilIcon from '../../icons/PencilIcon.jsx'
import SearchIcon from '../../icons/SearchIcon'
import StarIcon from '../../icons/StarIcon'
import TrashIcon from '../../icons/TrashIcon.jsx'
import { displayNameOf, primaryAddressOf } from './contactName'
import { filterContacts } from './contactSearch'
import type { Contact } from './contactTypes'

interface Props {
  /** Already scoped by the layout; the text query is this component's own state. */
  contacts: Contact[]
  selectedId: string | null
  onSelect: (id: string) => void
  onToggleFavorite: (contact: Contact) => void
  onEdit: (id: string) => void
  onDelete: (contact: Contact) => void
}

/**
 * The tiles, between a pinned heading band and nothing else — there is no pager: the whole book
 * is one cached list, so there is no page to go to.
 *
 * One tile skin, on two lines. The mail list carries two because three pane arrangements exist
 * there; here the list always sits beside the card, so a wide skin would be unreachable code.
 */
export default function ContactList({
  contacts, selectedId, onSelect, onToggleFavorite, onEdit, onDelete,
}: Props) {
  const [query, setQuery] = useState('')
  const shown = useMemo(() => filterContacts(contacts, query), [contacts, query])
  const filtering = query.trim() !== ''

  return (
    <>
      <div className="contacts-list-heading">
        <span className="contacts-search">
          <SearchIcon size={14} />
          <input type="search" className="search-input" aria-label="Search contacts"
            placeholder="Search contacts…" value={query}
            onChange={event => setQuery(event.target.value)} />
        </span>
        {/* Matching over total while filtering, the bare count otherwise: "2 / 2" reads as though
            something were hidden. */}
        <span className="contacts-count" data-testid="contact-count">
          {filtering ? `${shown.length} / ${contacts.length}` : contacts.length}
        </span>
      </div>

      <div className="contacts-list-scroll">
        {contacts.length === 0 && <p className="contacts-empty">No contacts yet</p>}
        {contacts.length > 0 && shown.length === 0 && (
          <p className="contacts-empty">No matching contacts</p>
        )}

        <div className="contact-tiles">
          {shown.map(contact => {
            const name = displayNameOf(contact)
            const primary = primaryAddressOf(contact)
            const extra = contact.addresses.length - 1

            return (
              <div key={contact.id} data-testid={`contact-tile-${contact.id}`}
                className={`contact-tile${contact.id === selectedId ? ' is-selected' : ''}`}
                role="button" tabIndex={0} onClick={() => onSelect(contact.id)}
                onKeyDown={event => {
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault()
                    onSelect(contact.id)
                  }
                }}>
                <div className="contact-tile-line">
                  {/* The star leads on the far left and the actions close on the far right — the
                      tile anatomy, identical to the admin and identities lists. */}
                  <button type="button" className={`contact-star${contact.isFavorite ? ' is-on' : ''}`}
                    title={contact.isFavorite ? 'Remove from favourites' : 'Add to favourites'}
                    aria-label={contact.isFavorite
                      ? `Remove ${name} from favourites` : `Add ${name} to favourites`}
                    onClick={event => { event.stopPropagation(); onToggleFavorite(contact) }}>
                    <StarIcon size={14} filled={contact.isFavorite} />
                  </button>

                  <span className="contact-tile-name">{name}</span>

                  <span className="contact-tile-actions">
                    <button type="button" className="admin-icon-btn" title="Edit"
                      aria-label={`Edit ${name}`}
                      onClick={event => { event.stopPropagation(); onEdit(contact.id) }}>
                      <PencilIcon size={14} />
                    </button>
                    <button type="button" className="admin-icon-btn is-danger" title="Delete"
                      aria-label={`Delete ${name}`}
                      onClick={event => { event.stopPropagation(); onDelete(contact) }}>
                      <TrashIcon size={14} />
                    </button>
                  </span>
                </div>

                {/* Always rendered, even empty, so a contact with no address is not a shorter tile
                    than its neighbours. */}
                <div className="contact-tile-address">
                  {primary ?? ''}{extra > 0 ? ` · +${extra}` : ''}
                </div>
              </div>
            )
          })}
        </div>
      </div>
    </>
  )
}
```

- [ ] **Step 4 : Ajouter les styles de la liste**

À la fin de `src/frontend/src/index.css` :

```css
.contacts-list-heading {
  flex: none; display: flex; align-items: center; gap: 8px; padding: 10px 12px;
  border-bottom: 1px solid var(--border);
}
.contacts-search { flex: 1; min-width: 0; display: flex; align-items: center; gap: 6px; }
.contacts-search .search-input { flex: 1; min-width: 0; }
.contacts-search svg { flex: none; color: var(--text-muted); }
.contacts-count { flex: none; color: var(--text-muted); font-size: 12px; }

.contacts-list-scroll { flex: 1; min-height: 0; overflow-y: auto; padding: 8px; }
.contact-tiles { display: flex; flex-direction: column; gap: 6px; }

.contact-tile {
  border: 1px solid var(--border); border-radius: var(--radius-sm); background: var(--surface);
  padding: 7px 9px; cursor: pointer;
}
.contact-tile:hover { border-color: var(--action-primary); box-shadow: 0 0 0 3px var(--action-primary-ring, transparent); }
/* Content-row language: selected fill plus an inset accent bar. Never a bare fill — that is what
   a navigation pane wears. */
.contact-tile.is-selected {
  background: var(--list-row-selected-bg); color: var(--list-row-selected-fg);
  border-color: var(--action-primary); box-shadow: inset 3px 0 0 var(--accent-unread);
}
.contact-tile-line { display: flex; align-items: center; gap: 7px; min-width: 0; }
.contact-tile-name {
  flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
  font-weight: 700; font-size: 13px;
}
/* Reserved, not conjured: revealing the cluster on hover must not shove the name sideways. */
.contact-tile-actions { flex: none; display: flex; gap: 2px; visibility: hidden; }
.contact-tile:hover .contact-tile-actions,
.contact-tile:focus-within .contact-tile-actions { visibility: visible; }

.contact-star {
  flex: none; display: inline-flex; padding: 2px; border: 0; background: transparent;
  cursor: pointer; color: var(--text-muted);
}
.contact-star:hover { color: var(--icon-hover-accent); }
.contact-star.is-on { color: var(--badge-count-bg); }

.contact-tile-address {
  padding-left: 25px; min-height: 1.2em; color: var(--text-muted); font-size: 12px;
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.contact-tile.is-selected .contact-tile-address { color: inherit; opacity: .8; }
```

- [ ] **Step 5 : Lancer le test pour vérifier qu'il passe**

Run: `cd src/frontend && npx vitest run src/modules/contacts/ContactList.test.tsx`
Expected: PASS — 13 tests.

- [ ] **Step 6 : Commit**

```bash
git add src/frontend/src/modules/contacts/ContactList.tsx \
        src/frontend/src/modules/contacts/ContactList.test.tsx \
        src/frontend/src/index.css
git commit -F - <<'EOF'
Add ContactList tiles with live filter and star toggle

One two-line tile skin: the list always sits beside the card, so a wide skin is unreachable.
EOF
```

---

### Task 11 : `ContactCard` — la fiche en lecture

**Files:**
- Create: `src/frontend/src/modules/contacts/ContactCard.tsx`
- Create: `src/frontend/src/modules/contacts/ContactCard.test.tsx`
- Modify: `src/frontend/src/index.css`

**Interfaces:**
- Consumes: `Contact`, `displayNameOf`, `StarIcon`, `PencilIcon`, `TrashIcon`.
- Produces: `ContactCard` — export par défaut. Props :
  `{ contact: Contact | null; onEdit: (id: string) => void; onDelete: (contact: Contact) => void; onToggleFavorite: (contact: Contact) => void }`

- [ ] **Step 1 : Écrire le test qui échoue**

Créer `src/frontend/src/modules/contacts/ContactCard.test.tsx` :

```tsx
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ContactCard from './ContactCard'
import type { Contact } from './contactTypes'

function contact(fields: Partial<Contact> & { id: string }): Contact {
  return {
    firstName: null, lastName: null, nickname: null, isFavorite: false, addresses: [], ...fields,
  }
}

const bruno = contact({
  id: 'b', firstName: 'Bruno', lastName: 'Mertens', nickname: 'bru',
  addresses: ['bruno@x.be', 'b.mertens@wk.be'],
})

function setup(overrides: Partial<Parameters<typeof ContactCard>[0]> = {}) {
  const props = {
    contact: bruno, onEdit: vi.fn(), onDelete: vi.fn(), onToggleFavorite: vi.fn(), ...overrides,
  }
  render(<ContactCard {...props} />)
  return props
}

describe('ContactCard', () => {
  it('heads the card with the display name', () => {
    setup()

    expect(screen.getByRole('heading', { name: 'Bruno Mertens' })).toBeInTheDocument()
  })

  it('lists every address in order', () => {
    setup()

    const addresses = screen.getAllByTestId('card-address').map(node => node.textContent)
    expect(addresses?.[0]).toContain('bruno@x.be')
    expect(addresses?.[1]).toContain('b.mertens@wk.be')
  })

  // Position 0 is the primary by definition, so the card has to say which one it is: it is the
  // address a reply or a new message will use.
  it('marks the first address as the primary', () => {
    setup()

    expect(screen.getAllByTestId('card-address')[0]).toHaveTextContent(/primary/i)
    expect(screen.getAllByTestId('card-address')[1]).not.toHaveTextContent(/primary/i)
  })

  it('shows the nickname', () => {
    setup()

    expect(screen.getByText('bru')).toBeInTheDocument()
  })

  // A field that does not exist renders nothing at all — an empty labelled row reads as data lost.
  it('renders no nickname row when there is none', () => {
    setup({ contact: contact({ id: 'n', firstName: 'Alice', addresses: ['a@x.be'] }) })

    expect(screen.queryByText(/nickname/i)).not.toBeInTheDocument()
  })

  it('renders no address section when the contact carries none', () => {
    setup({ contact: contact({ id: 'n', firstName: 'Alice' }) })

    expect(screen.queryByTestId('card-address')).not.toBeInTheDocument()
  })

  it('offers edit, delete and the favourite toggle', async () => {
    const props = setup()

    await userEvent.click(screen.getByRole('button', { name: /^edit$/i }))
    await userEvent.click(screen.getByRole('button', { name: /^delete$/i }))
    await userEvent.click(screen.getByRole('button', { name: /add to favourites/i }))

    expect(props.onEdit).toHaveBeenCalledWith('b')
    expect(props.onDelete).toHaveBeenCalledWith(bruno)
    expect(props.onToggleFavorite).toHaveBeenCalledWith(bruno)
  })

  it('names the action to come on the favourite toggle', () => {
    setup({ contact: { ...bruno, isFavorite: true } })

    expect(screen.getByRole('button', { name: /remove from favourites/i })).toBeInTheDocument()
  })

  it('invites a pick when nothing is selected', () => {
    setup({ contact: null })

    expect(screen.getByText(/select a contact/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^edit$/i })).not.toBeInTheDocument()
  })
})
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `cd src/frontend && npx vitest run src/modules/contacts/ContactCard.test.tsx`
Expected: FAIL — le module `./ContactCard` n'existe pas.

- [ ] **Step 3 : Créer `ContactCard.tsx`**

```tsx
import PencilIcon from '../../icons/PencilIcon.jsx'
import StarIcon from '../../icons/StarIcon'
import TrashIcon from '../../icons/TrashIcon.jsx'
import { displayNameOf } from './contactName'
import type { Contact } from './contactTypes'

interface Props {
  contact: Contact | null
  onEdit: (id: string) => void
  onDelete: (contact: Contact) => void
  onToggleFavorite: (contact: Contact) => void
}

/**
 * The contact in reading mode — the column the mail module gives its reader. Editing happens on
 * its own route, in a full-width editor, so this stays a viewer.
 *
 * Every row renders only when its datum exists: an empty labelled row reads as data that went
 * missing rather than data that was never entered.
 */
export default function ContactCard({ contact, onEdit, onDelete, onToggleFavorite }: Props) {
  if (contact == null) {
    return <p className="contacts-empty contacts-card-invite">Select a contact to see its details</p>
  }

  return (
    <div className="contact-card">
      <div className="contact-card-head">
        <h2 className="contact-card-name">{displayNameOf(contact)}</h2>
      </div>

      <div className="contact-card-body">
        {contact.nickname && (
          <div className="contact-card-row">
            <span className="contact-card-label">Nickname</span>
            <span className="contact-card-value">{contact.nickname}</span>
          </div>
        )}

        {contact.addresses.length > 0 && (
          <div className="contact-card-row">
            <span className="contact-card-label">Addresses</span>
            <span className="contact-card-values">
              {contact.addresses.map((address, index) => (
                <span key={address} className="contact-card-value" data-testid="card-address">
                  <a href={`mailto:${address}`}>{address}</a>
                  {index === 0 && <span className="contact-card-primary">primary</span>}
                </span>
              ))}
            </span>
          </div>
        )}
      </div>

      {/* Bottom-right of whatever rows are actually present, like the reader's action cluster. */}
      <div className="contact-card-actions">
        <button type="button" className="btn contact-card-btn"
          aria-label={contact.isFavorite ? 'Remove from favourites' : 'Add to favourites'}
          onClick={() => onToggleFavorite(contact)}>
          <StarIcon size={15} filled={contact.isFavorite} />
          {contact.isFavorite ? 'Remove from favourites' : 'Add to favourites'}
        </button>
        <span className="actions-rule" />
        <button type="button" className="btn contact-card-btn" onClick={() => onEdit(contact.id)}>
          <PencilIcon size={15} /> Edit
        </button>
        <button type="button" className="btn contact-card-btn is-danger"
          onClick={() => onDelete(contact)}>
          <TrashIcon size={15} /> Delete
        </button>
      </div>
    </div>
  )
}
```

- [ ] **Step 4 : Ajouter les styles de la fiche**

À la fin de `src/frontend/src/index.css` :

```css
.contact-card { display: flex; flex-direction: column; min-height: 0; height: 100%; }
.contacts-card-invite { margin: auto; }
.contact-card-head { flex: none; padding: 16px 18px 12px; border-bottom: 1px solid var(--border); }
.contact-card-name { margin: 0; font-size: 18px; font-weight: 700; color: var(--text); }

.contact-card-body { flex: 1; min-height: 0; overflow-y: auto; padding: 16px 18px; }
.contact-card-row { display: flex; gap: 14px; margin-bottom: 14px; }
.contact-card-label {
  flex: none; width: 96px; color: var(--text-muted); font-size: 11px; text-transform: uppercase;
  letter-spacing: .04em; padding-top: 2px;
}
.contact-card-values { display: flex; flex-direction: column; gap: 5px; min-width: 0; }
.contact-card-value { font-size: 13px; color: var(--text); min-width: 0; overflow-wrap: break-word; }
.contact-card-value a { color: var(--action-primary); text-decoration: none; }
.contact-card-value a:hover { text-decoration: underline; }
.contact-card-primary {
  margin-left: 8px; color: var(--text-muted); font-size: 10px; text-transform: uppercase;
  letter-spacing: .04em;
}

.contact-card-actions {
  flex: none; display: flex; align-items: center; gap: 6px; justify-content: flex-end;
  padding: 12px 18px; border-top: 1px solid var(--border);
}
.contact-card-btn { display: inline-flex; align-items: center; gap: 6px; font-size: 13px; }
.contact-card-btn.is-danger { color: var(--danger); }
```

- [ ] **Step 5 : Lancer le test pour vérifier qu'il passe**

Run: `cd src/frontend && npx vitest run src/modules/contacts/ContactCard.test.tsx`
Expected: PASS — 9 tests.

- [ ] **Step 6 : Commit**

```bash
git add src/frontend/src/modules/contacts/ContactCard.tsx \
        src/frontend/src/modules/contacts/ContactCard.test.tsx \
        src/frontend/src/index.css
git commit -F - <<'EOF'
Add ContactCard, the reading pane of the contacts module

A row renders only when its datum exists; the first address is marked primary.
EOF
```

---

### Task 12 : `ContactEditView` — le formulaire pleine largeur

**Files:**
- Create: `src/frontend/src/modules/contacts/ContactEditView.tsx`
- Create: `src/frontend/src/modules/contacts/ContactEditView.test.tsx`
- Modify: `src/frontend/src/index.css`

**Interfaces:**
- Consumes: `Contact`, `ContactDraft`, `PersonPlusIcon`, `PencilIcon`, `TrashIcon`, `StarIcon`.
- Produces: `ContactEditView` — export par défaut. Props :
  `{ contact: Contact | null; saving: boolean; error: string | null; onSave: (draft: ContactDraft) => void; onCancel: () => void }`
  `contact === null` signifie **mode création**.

**Un seul composant pour les deux modes.** C'est ce qui justifiait le choix de surface : « Ajouter » n'a aucun contact sélectionné, donc si l'édition vivait dans la fiche et l'ajout dans un modal, on entretiendrait deux dialectes pour un même formulaire.

- [ ] **Step 1 : Écrire le test qui échoue**

Créer `src/frontend/src/modules/contacts/ContactEditView.test.tsx` :

```tsx
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ContactEditView from './ContactEditView'
import type { Contact } from './contactTypes'

const bruno: Contact = {
  id: 'b', firstName: 'Bruno', lastName: 'Mertens', nickname: 'bru',
  isFavorite: false, addresses: ['bruno@x.be', 'b.mertens@wk.be'],
}

function setup(overrides: Partial<Parameters<typeof ContactEditView>[0]> = {}) {
  const props = {
    contact: null as Contact | null, saving: false, error: null as string | null,
    onSave: vi.fn(), onCancel: vi.fn(), ...overrides,
  }
  render(<ContactEditView {...props} />)
  return props
}

describe('ContactEditView', () => {
  // Both halves, side by side in the document: one component serves the two modes, so the heading
  // is the only thing telling the user which one they are in.
  it('heads a create as New contact and an edit as Edit contact', () => {
    setup()
    setup({ contact: bruno })

    expect(screen.getByRole('heading', { name: /new contact/i })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /edit contact/i })).toBeInTheDocument()
  })

  it('seeds every field from the contact being edited', () => {
    setup({ contact: bruno })

    expect(screen.getByLabelText(/first name/i)).toHaveValue('Bruno')
    expect(screen.getByLabelText(/last name/i)).toHaveValue('Mertens')
    expect(screen.getByLabelText(/nickname/i)).toHaveValue('bru')
    expect(screen.getByLabelText(/address 1/i)).toHaveValue('bruno@x.be')
    expect(screen.getByLabelText(/address 2/i)).toHaveValue('b.mertens@wk.be')
  })

  it('starts a create with one empty address row', () => {
    setup()

    expect(screen.getByLabelText(/address 1/i)).toHaveValue('')
    expect(screen.queryByLabelText(/address 2/i)).not.toBeInTheDocument()
  })

  // Position 0 is the primary by definition: the badge is on the first row, and it moves when
  // the rows are reordered rather than being a flag of its own.
  it('badges the first address row as the primary', () => {
    setup({ contact: bruno })

    expect(screen.getByTestId('address-row-0')).toHaveTextContent(/primary/i)
    expect(screen.getByTestId('address-row-1')).not.toHaveTextContent(/primary/i)
  })

  it('adds an address row on demand', async () => {
    setup()

    await userEvent.click(screen.getByRole('button', { name: /add an address/i }))

    expect(screen.getByLabelText(/address 2/i)).toBeInTheDocument()
  })

  it('removes an address row', async () => {
    setup({ contact: bruno })

    await userEvent.click(screen.getByRole('button', { name: /remove address 2/i }))

    expect(screen.queryByLabelText(/address 2/i)).not.toBeInTheDocument()
    expect(screen.getByLabelText(/address 1/i)).toHaveValue('bruno@x.be')
  })

  it('moves an address up, which makes it the primary', async () => {
    setup({ contact: bruno })

    await userEvent.click(screen.getByRole('button', { name: /move address 2 up/i }))

    expect(screen.getByLabelText(/address 1/i)).toHaveValue('b.mertens@wk.be')
    expect(screen.getByTestId('address-row-0')).toHaveTextContent(/primary/i)
  })

  it('offers no move up on the first row', () => {
    setup({ contact: bruno })

    expect(screen.queryByRole('button', { name: /move address 1 up/i })).not.toBeInTheDocument()
  })

  // The gate the backend also enforces. Refusing here is what keeps the user from a round trip
  // whose only outcome is an error banner.
  it('keeps save disabled while neither a name nor an address is filled', () => {
    setup()

    expect(screen.getByRole('button', { name: /save contact/i })).toBeDisabled()
  })

  it('enables save on a name alone', async () => {
    setup()

    await userEvent.type(screen.getByLabelText(/first name/i), 'Bruno')

    expect(screen.getByRole('button', { name: /save contact/i })).toBeEnabled()
  })

  it('enables save on an address alone', async () => {
    setup()

    await userEvent.type(screen.getByLabelText(/address 1/i), 'bruno@x.be')

    expect(screen.getByRole('button', { name: /save contact/i })).toBeEnabled()
  })

  it('submits the draft, blank address rows dropped', async () => {
    const props = setup()
    await userEvent.type(screen.getByLabelText(/first name/i), 'Bruno')
    await userEvent.click(screen.getByRole('button', { name: /add an address/i }))
    await userEvent.type(screen.getByLabelText(/address 1/i), 'bruno@x.be')

    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(props.onSave).toHaveBeenCalledWith({
      firstName: 'Bruno', lastName: null, nickname: null, isFavorite: false,
      addresses: ['bruno@x.be'],
    })
  })

  it('sends null rather than an empty string for a blank name', async () => {
    const props = setup()
    await userEvent.type(screen.getByLabelText(/address 1/i), 'a@x.be')

    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(props.onSave).toHaveBeenCalledWith(expect.objectContaining({ firstName: null, nickname: null }))
  })

  it('carries the favourite flag through', async () => {
    const props = setup({ contact: { ...bruno, isFavorite: true } })

    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    expect(props.onSave).toHaveBeenCalledWith(expect.objectContaining({ isFavorite: true }))
  })

  it('surfaces a server error at the top of the form', () => {
    setup({ error: "'nope' is not a valid email address" })

    expect(screen.getByRole('alert')).toHaveTextContent('not a valid email address')
  })

  it('disables save and shows a spinner while saving', () => {
    setup({ contact: bruno, saving: true })

    expect(screen.getByRole('button', { name: /save contact/i })).toBeDisabled()
    expect(screen.getByTestId('editor-spinner')).toBeInTheDocument()
  })

  it('cancels through the ✕', async () => {
    const props = setup()

    await userEvent.click(screen.getByRole('button', { name: /close the editor/i }))

    expect(props.onCancel).toHaveBeenCalled()
  })
})
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `cd src/frontend && npx vitest run src/modules/contacts/ContactEditView.test.tsx`
Expected: FAIL — le module `./ContactEditView` n'existe pas.

- [ ] **Step 3 : Créer `ContactEditView.tsx`**

```tsx
import { useState, type FormEvent } from 'react'
import PencilIcon from '../../icons/PencilIcon.jsx'
import PersonPlusIcon from '../../icons/PersonPlusIcon.jsx'
import StarIcon from '../../icons/StarIcon'
import TrashIcon from '../../icons/TrashIcon.jsx'
import type { Contact, ContactDraft } from './contactTypes'

interface Props {
  /** null in create mode. One component for both, because "Add" has no selected contact and a
      second surface for the same form would be a second dialect of it. */
  contact: Contact | null
  saving: boolean
  error: string | null
  onSave: (draft: ContactDraft) => void
  onCancel: () => void
}

/** Blank rows are dropped on submit; the trailing empty row exists so a create has something to
    type into without clicking "add" first. */
function blank(value: string): string | null {
  const trimmed = value.trim()
  return trimmed === '' ? null : trimmed
}

export default function ContactEditView({ contact, saving, error, onSave, onCancel }: Props) {
  const [firstName, setFirstName] = useState(contact?.firstName ?? '')
  const [lastName, setLastName] = useState(contact?.lastName ?? '')
  const [nickname, setNickname] = useState(contact?.nickname ?? '')
  const [isFavorite, setIsFavorite] = useState(contact?.isFavorite ?? false)
  const [addresses, setAddresses] = useState<string[]>(
    contact && contact.addresses.length > 0 ? [...contact.addresses] : [''])

  const filled = addresses.map(blank).filter((a): a is string => a != null)
  // The same gate the backend enforces, so the user never spends a round trip to be told.
  const valid = blank(firstName) != null || blank(lastName) != null
    || blank(nickname) != null || filled.length > 0

  function change(index: number, value: string) {
    setAddresses(previous => previous.map((address, i) => (i === index ? value : address)))
  }

  function remove(index: number) {
    setAddresses(previous => {
      const next = previous.filter((_, i) => i !== index)
      // Never zero rows: an address list with no box to type in offers no way back.
      return next.length > 0 ? next : ['']
    })
  }

  // Reordering is how the primary changes — position 0 is the primary by definition, so there is
  // no flag that could fall out of step with the order.
  function moveUp(index: number) {
    setAddresses(previous => {
      if (index === 0) return previous
      const next = [...previous]
      ;[next[index - 1], next[index]] = [next[index], next[index - 1]]
      return next
    })
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    if (!valid || saving) return

    onSave({
      firstName: blank(firstName),
      lastName: blank(lastName),
      nickname: blank(nickname),
      isFavorite,
      addresses: filled,
    })
  }

  return (
    <form className="contact-editor-form" onSubmit={submit}>
      <div className="contact-editor-head">
        <h2 className="contact-editor-title">
          {contact ? <PencilIcon size={16} /> : <PersonPlusIcon size={16} />}
          {contact ? 'Edit contact' : 'New contact'}
        </h2>
        <button type="submit" className="btn btn-primary" disabled={!valid || saving}>
          {saving && <span className="spinner" data-testid="editor-spinner" />}
          Save contact
        </button>
        {/* The ✕ is the only dismissal, as in every dialog of this app — no Cancel beside Save. */}
        <button type="button" className="modal-close" aria-label="Close the editor" onClick={onCancel}>✕</button>
      </div>

      <div className="contact-editor-body">
        {error && <div className="alert alert-error" role="alert">{error}</div>}

        {/* Full width is what lets these be .field-h rows at all: at the card's 380px, and worse
            at its 240px floor, a 110px label column leaves nothing for the control. */}
        <div className="field-h">
          <label htmlFor="contact-first-name">First name</label>
          <input id="contact-first-name" type="text" value={firstName}
            onChange={event => setFirstName(event.target.value)} autoFocus />
        </div>
        <div className="field-h">
          <label htmlFor="contact-last-name">Last name</label>
          <input id="contact-last-name" type="text" value={lastName}
            onChange={event => setLastName(event.target.value)} />
        </div>
        <div className="field-h">
          <label htmlFor="contact-nickname">Nickname</label>
          <input id="contact-nickname" type="text" value={nickname}
            onChange={event => setNickname(event.target.value)} />
        </div>

        <div className="field-h contact-editor-addresses">
          <span className="field-h-label">Addresses</span>
          <div className="contact-address-list">
            {addresses.map((address, index) => (
              <div key={index} className="contact-address-row" data-testid={`address-row-${index}`}>
                <label className="sr-only" htmlFor={`contact-address-${index}`}>
                  Address {index + 1}
                </label>
                <input id={`contact-address-${index}`} type="email" value={address}
                  placeholder="name@example.com"
                  onChange={event => change(index, event.target.value)} />
                {index === 0
                  ? <span className="contact-address-primary">primary</span>
                  : (
                    <button type="button" className="admin-icon-btn"
                      title="Make this the primary address"
                      aria-label={`Move address ${index + 1} up`} onClick={() => moveUp(index)}>↑</button>
                  )}
                <button type="button" className="admin-icon-btn is-danger" title="Remove"
                  aria-label={`Remove address ${index + 1}`} onClick={() => remove(index)}>
                  <TrashIcon size={14} />
                </button>
              </div>
            ))}
            <button type="button" className="contact-address-add"
              onClick={() => setAddresses(previous => [...previous, ''])}>
              + Add an address
            </button>
          </div>
        </div>

        <div className="field-h">
          <label htmlFor="contact-favorite">Favourite</label>
          <button type="button" id="contact-favorite"
            className={`contact-star${isFavorite ? ' is-on' : ''}`}
            aria-pressed={isFavorite}
            onClick={() => setIsFavorite(previous => !previous)}>
            <StarIcon size={16} filled={isFavorite} />
          </button>
        </div>
      </div>
    </form>
  )
}
```

- [ ] **Step 4 : Ajouter les styles de l'éditeur**

À la fin de `src/frontend/src/index.css` :

```css
.contact-editor-form { display: flex; flex-direction: column; min-height: 0; height: 100%; }
.contact-editor-head {
  flex: none; display: flex; align-items: center; gap: 10px; padding: 14px 18px;
  border-bottom: 1px solid var(--border);
}
.contact-editor-title {
  margin: 0; flex: 1; display: flex; align-items: center; gap: 8px; font-size: 16px;
  font-weight: 700; color: var(--text);
}
/* The form is bounded rather than full-bleed: four fields spread across a 900px column read as a
   page that failed to load, not as a form. */
.contact-editor-body { flex: 1; min-height: 0; overflow-y: auto; padding: 18px; max-width: 560px; }
.contact-editor-addresses { align-items: flex-start; }
.contact-editor-addresses .field-h-label {
  width: 110px; flex: none; color: var(--text-muted); font-size: 11px; text-transform: uppercase;
  letter-spacing: .04em; padding-top: 8px;
}
.contact-address-list { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 6px; }
.contact-address-row { display: flex; align-items: center; gap: 6px; min-width: 0; }
.contact-address-row input { flex: 1; min-width: 0; }
.contact-address-primary {
  flex: none; color: var(--action-primary); font-size: 10px; text-transform: uppercase;
  letter-spacing: .04em;
}
.contact-address-add {
  align-self: flex-start; border: 0; background: transparent; padding: 2px 0; cursor: pointer;
  color: var(--action-primary); font: inherit; font-size: 12px;
}
.contact-address-add:hover { text-decoration: underline; }
.sr-only {
  position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden;
  clip: rect(0 0 0 0); white-space: nowrap; border: 0;
}
```

- [ ] **Step 5 : Lancer le test pour vérifier qu'il passe**

Run: `cd src/frontend && npx vitest run src/modules/contacts/ContactEditView.test.tsx`
Expected: PASS — 17 tests.

- [ ] **Step 6 : Commit**

```bash
git add src/frontend/src/modules/contacts/ContactEditView.tsx \
        src/frontend/src/modules/contacts/ContactEditView.test.tsx \
        src/frontend/src/index.css
git commit -F - <<'EOF'
Add ContactEditView, one form for create and edit

Reordering the address list is what changes the primary; there is no separate flag.
EOF
```

---

### Task 13 : Assembler le module dans `ContactsLayout`

**Files:**
- Modify: `src/frontend/src/modules/contacts/ContactsLayout.tsx`
- Modify: `src/frontend/src/modules/contacts/ContactsLayout.test.tsx`

**Interfaces:**
- Consumes: `ContactList` (10), `ContactCard` (11), `ContactEditView` (12), `DeleteConfirmModal`, les quatre mutations (Task 8), `filterContacts` n'est **pas** utilisé ici — la portée filtre sur `isFavorite`, le texte est interne à `ContactList`.
- Produces: le module complet et navigable. Aucune nouvelle interface publique.

- [ ] **Step 1 : Écrire les tests d'intégration qui échouent**

Ajouter à `ContactsLayout.test.tsx`, dans le `describe`, et compléter le mock d'`api.js` en tête du fichier :

```tsx
vi.mock('../../api.js', () => ({
  api: {
    getContacts: vi.fn(), createContact: vi.fn(), updateContact: vi.fn(),
    deleteContact: vi.fn(), setContactFavorite: vi.fn(),
  },
  ApiError: class extends Error {},
}))
```

et le déstructurer avec les cinq méthodes :

```tsx
const { api } = await import('../../api.js') as unknown as {
  api: Record<'getContacts' | 'createContact' | 'updateContact' | 'deleteContact'
    | 'setContactFavorite', ReturnType<typeof vi.fn>>
}
```

puis les cas :

```tsx
  it('narrows the list to favourites when that scope is picked', async () => {
    renderAt('/contacts')
    await waitFor(() => expect(screen.getByText('Alice')).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: /favourites/i }))

    expect(screen.getByText('Alice')).toBeInTheDocument()
    expect(screen.queryByText('Bruno')).not.toBeInTheDocument()
  })

  it('opens the picked contact in the card', async () => {
    renderAt('/contacts')
    await waitFor(() => expect(screen.getByText('Bruno')).toBeInTheDocument())

    await userEvent.click(screen.getByText('Bruno'))

    expect(screen.getByRole('heading', { name: 'Bruno' })).toBeInTheDocument()
  })

  it('toggles a favourite through the API and keeps the card open', async () => {
    api.setContactFavorite.mockResolvedValue(undefined)
    renderAt('/contacts')
    await waitFor(() => expect(screen.getByText('Bruno')).toBeInTheDocument())
    await userEvent.click(screen.getByText('Bruno'))

    await userEvent.click(screen.getByRole('button', { name: /add bruno to favourites/i }))

    await waitFor(() => expect(api.setContactFavorite).toHaveBeenCalledWith('b', true))
    // The star is not a navigation: the contact it belongs to stays open behind it.
    expect(screen.getByRole('heading', { name: 'Bruno' })).toBeInTheDocument()
  })

  // Deleting never happens on the first click anywhere in this app.
  it('confirms before deleting', async () => {
    api.deleteContact.mockResolvedValue(undefined)
    renderAt('/contacts')
    await waitFor(() => expect(screen.getByText('Bruno')).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: /delete bruno/i }))
    expect(api.deleteContact).not.toHaveBeenCalled()

    await userEvent.click(screen.getByRole('button', { name: /^delete$/i }))

    await waitFor(() => expect(api.deleteContact).toHaveBeenCalledWith('b'))
  })

  it('creates a contact and returns to the list', async () => {
    api.createContact.mockResolvedValue({ id: 'n' })
    renderAt('/contacts/new')
    await waitFor(() => expect(screen.getByRole('heading', { name: /new contact/i })).toBeInTheDocument())

    await userEvent.type(screen.getByLabelText(/first name/i), 'Chloé')
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    await waitFor(() => expect(api.createContact).toHaveBeenCalledWith(
      expect.objectContaining({ firstName: 'Chloé' })))
    // Saved is only half of it: a save that left the editor standing would strand the user in a
    // form whose contact already exists.
    await waitFor(() => expect(screen.getByTestId('contact-list')).toBeInTheDocument())
    expect(screen.queryByTestId('contact-editor')).not.toBeInTheDocument()
  })

  it('seeds the editor from the contact named in the route', async () => {
    renderAt('/contacts/b/edit')

    await waitFor(() => expect(screen.getByLabelText(/first name/i)).toHaveValue('Bruno'))
  })

  // A refused save has to leave the user in the form with the reason, never bounce them back to
  // a list that silently kept nothing.
  it('keeps the editor open and shows the reason when a save is refused', async () => {
    api.createContact.mockRejectedValue(new Error("'nope' is not a valid email address"))
    renderAt('/contacts/new')
    await waitFor(() => expect(screen.getByLabelText(/first name/i)).toBeInTheDocument())

    await userEvent.type(screen.getByLabelText(/first name/i), 'Bruno')
    await userEvent.click(screen.getByRole('button', { name: /save contact/i }))

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(/not a valid email/i))
    expect(screen.getByRole('heading', { name: /new contact/i })).toBeInTheDocument()
  })
```

Ajouter `import userEvent from '@testing-library/user-event'` en tête du fichier de test.

- [ ] **Step 2 : Lancer les tests pour vérifier qu'ils échouent**

Run: `cd src/frontend && npx vitest run src/modules/contacts/ContactsLayout.test.tsx`
Expected: FAIL — les colonnes sont encore des espaces réservés vides.

- [ ] **Step 3 : Monter les trois composants dans le layout**

Dans `ContactsLayout.tsx`, ajouter les imports :

```tsx
import { useParams } from 'react-router-dom'
import { DeleteConfirmModal } from '../../components/DeleteConfirmModal.jsx'
import ContactCard from './ContactCard'
import ContactEditView from './ContactEditView'
import ContactList from './ContactList'
import { displayNameOf } from './contactName'
import type { Contact, ContactDraft } from './contactTypes'
import {
  useContacts, useCreateContact, useDeleteContact, useSetContactFavorite, useUpdateContact,
} from './queries'
```

puis, dans le corps du composant, après `const [listWidth, setListWidth] = …` :

```tsx
  const { id: routeId } = useParams()
  const createContact = useCreateContact()
  const updateContact = useUpdateContact()
  const deleteContact = useDeleteContact()
  const setFavorite = useSetContactFavorite()

  const [pendingDelete, setPendingDelete] = useState<Contact | null>(null)
  const [saveError, setSaveError] = useState<string | null>(null)

  const scoped = (contacts ?? []).filter(contact => scope !== 'favorites' || contact.isFavorite)
  const selected = contacts?.find(contact => contact.id === selectedId) ?? null
  const editing_ = routeId ? contacts?.find(contact => contact.id === routeId) ?? null : null

  function select(id: string) {
    setParams(previous => {
      const next: Record<string, string> = { id }
      const currentScope = previous.get('scope')
      if (currentScope) next.scope = currentScope
      return next
    })
  }

  async function save(draft: ContactDraft) {
    setSaveError(null)
    try {
      if (editing_) await updateContact.mutateAsync({ id: editing_.id, contact: draft })
      else await createContact.mutateAsync(draft)
      navigate('/contacts')
      addToast('Contact saved', 'success')
    } catch (error) {
      // Stay in the form carrying the reason: bouncing back to a list that kept nothing is how a
      // user loses what they typed without being told why.
      setSaveError((error as Error).message || 'Could not save the contact')
    }
  }

  async function confirmDelete() {
    if (!pendingDelete) return
    const name = displayNameOf(pendingDelete)
    try {
      await deleteContact.mutateAsync(pendingDelete.id)
      // The open card must not survive its contact.
      if (selectedId === pendingDelete.id) setParams(scope === 'favorites' ? { scope } : {})
      addToast(`${name} deleted`, 'success')
    } catch (error) {
      addToast((error as Error).message || 'Could not delete the contact', 'error')
    } finally {
      setPendingDelete(null)
    }
  }

  function toggleFavorite(contact: Contact) {
    setFavorite.mutate({ id: contact.id, isFavorite: !contact.isFavorite }, {
      onError: error => addToast((error as Error).message || 'Could not save the favourite', 'error'),
    })
  }
```

Remplacer le contenu de `.contacts-editor` par :

```tsx
        <div className="contacts-editor" data-testid="contact-editor">
          {/* Keyed on the contact so switching from one edit to another reseeds the form rather
              than carrying the previous contact's values into it. */}
          <ContactEditView key={routeId ?? 'new'} contact={editing_} error={saveError}
            saving={createContact.isPending || updateContact.isPending}
            onSave={save} onCancel={() => navigate('/contacts')} />
        </div>
```

celui de `.contacts-list` par :

```tsx
          <div className="contacts-list" style={{ width: listWidth }} data-testid="contact-list">
            {isLoading && <p className="contacts-empty">Loading contacts…</p>}
            {isError && <p className="contacts-empty">Could not load contacts.</p>}
            {contacts && (
              <ContactList contacts={scoped} selectedId={selectedId} onSelect={select}
                onToggleFavorite={toggleFavorite} onDelete={setPendingDelete}
                onEdit={id => navigate(`/contacts/${id}/edit`)} />
            )}
          </div>
```

celui de `.contacts-card` par :

```tsx
          <div className="contacts-card" data-testid="contact-card">
            <ContactCard contact={selected} onToggleFavorite={toggleFavorite}
              onDelete={setPendingDelete} onEdit={id => navigate(`/contacts/${id}/edit`)} />
          </div>
```

et ajouter avant `<Toasts …>` :

```tsx
      {pendingDelete && (
        <DeleteConfirmModal entityLabel={displayNameOf(pendingDelete)}
          loading={deleteContact.isPending}
          onConfirm={confirmDelete} onClose={() => setPendingDelete(null)} />
      )}
```

- [ ] **Step 4 : Lancer les tests pour vérifier qu'ils passent**

Run: `cd src/frontend && npx vitest run src/modules/contacts/`
Expected: PASS — l'ensemble du module, dont les 7 nouveaux cas d'intégration.

- [ ] **Step 5 : Vérifier types, lint et suite complète**

Run: `cd src/frontend && npm run typecheck && npm run lint && npx vitest run`
Expected: PASS.

- [ ] **Step 6 : Commit**

```bash
git add src/frontend/src/modules/contacts/ContactsLayout.tsx \
        src/frontend/src/modules/contacts/ContactsLayout.test.tsx
git commit -F - <<'EOF'
Wire list, card and editor into the contacts module

A refused save keeps the editor open carrying the reason.
EOF
```

---

### Task 14 : Tranche 3b — autocomplétion dans `RecipientsField`

**Files:**
- Modify: `src/frontend/src/modules/mail/compose/RecipientsField.tsx`
- Modify: `src/frontend/src/modules/mail/compose/RecipientsField.test.tsx`
- Modify: `src/frontend/src/modules/mail/compose/ComposeView.tsx:233,239,240`
- Modify: `src/frontend/src/index.css`

**Interfaces:**
- Consumes: `suggestionsFor` et `AddressSuggestion` (Task 7), `Contact` (Task 6), `useContacts` (Task 8).
- Produces: `RecipientsField` gagne **une** prop optionnelle, `contacts?: Contact[]` (défaut `[]`). Le composant reste présentationnel — aucun hook de données à l'intérieur, donc ses tests existants continuent de passer sans modification, ce qu'exige la règle « aucun test perdu sans remplaçant ».

**`ComposeView` appelle `useContacts()` une seule fois** et passe la liste aux trois champs. Trois appels du hook partageraient de toute façon le même cache, mais un seul site de lecture est plus facile à suivre.

- [ ] **Step 1 : Écrire les tests qui échouent**

Ajouter à `RecipientsField.test.tsx` un nouveau `describe`, et importer `Contact` :

```tsx
import type { Contact } from '../../contacts/contactTypes'

function contact(fields: Partial<Contact> & { id: string }): Contact {
  return {
    firstName: null, lastName: null, nickname: null, isFavorite: false, addresses: [], ...fields,
  }
}

describe('RecipientsField — contact suggestions', () => {
  const bruno = contact({
    id: 'b', firstName: 'Bruno', lastName: 'Mertens', addresses: ['bruno@x.be', 'b@wk.be'],
  })

  it('offers no dropdown before anything is typed', () => {
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={vi.fn()} contacts={[bruno]} />)

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('opens the dropdown as the user types and lists one row per address', async () => {
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={vi.fn()} contacts={[bruno]} />)

    await userEvent.type(screen.getByLabelText('To'), 'bru')

    expect(screen.getAllByRole('option')).toHaveLength(2)
    expect(screen.getByRole('option', { name: /bruno@x\.be/ })).toHaveTextContent('Bruno Mertens')
  })

  // One row, every owner named: the decision to allow a shared address lands here. Two rows would
  // produce the identical recipient and one name would be an arbitrary pick.
  it('shows a shared address once, naming both contacts', async () => {
    const shared = 'info@x.be'
    const contacts = [
      contact({ id: '1', firstName: 'Alice', lastName: 'Dupont', addresses: [shared] }),
      contact({ id: '2', firstName: 'Compta', addresses: [shared] }),
    ]
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={vi.fn()} contacts={contacts} />)

    await userEvent.type(screen.getByLabelText('To'), 'info')

    const rows = screen.getAllByRole('option')
    expect(rows).toHaveLength(1)
    expect(rows[0]).toHaveTextContent('Alice Dupont')
    expect(rows[0]).toHaveTextContent('Compta')
  })

  it('commits the picked address as a token', async () => {
    const onChange = vi.fn()
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={onChange} contacts={[bruno]} />)
    await userEvent.type(screen.getByLabelText('To'), 'bru')

    await userEvent.click(screen.getByRole('option', { name: /bruno@x\.be/ }))

    expect(onChange).toHaveBeenCalledWith(['bruno@x.be'])
  })

  it('drops an address already tokenised from the options', async () => {
    render(<RecipientsField id="to" label="To" tokens={['bruno@x.be']} onChange={vi.fn()}
      contacts={[bruno]} />)

    await userEvent.type(screen.getByLabelText('To'), 'b')

    expect(screen.queryByRole('option', { name: /bruno@x\.be/ })).not.toBeInTheDocument()
    expect(screen.getByRole('option', { name: /b@wk\.be/ })).toBeInTheDocument()
  })

  it('walks the list with the arrow keys and commits with Enter', async () => {
    const onChange = vi.fn()
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={onChange} contacts={[bruno]} />)
    await userEvent.type(screen.getByLabelText('To'), 'bru')

    await userEvent.keyboard('{ArrowDown}{ArrowDown}{Enter}')

    expect(onChange).toHaveBeenCalledWith(['b@wk.be'])
  })

  // Free typing is what keeps the field usable with zero contacts: the list accelerates, it never
  // gates. Nothing is highlighted until an arrow key says so, so Enter commits what was typed.
  // The query has to match a contact, or the dropdown is shut and Enter has nothing it could have
  // substituted — a field highlighting its first row by default would pass on a query matching
  // nobody.
  it('commits the typed text on Enter when no row is highlighted', async () => {
    const onChange = vi.fn()
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={onChange} contacts={[bruno]} />)
    await userEvent.type(screen.getByLabelText('To'), 'bru')
    expect(screen.getByRole('listbox')).toBeInTheDocument()

    await userEvent.keyboard('{Enter}')

    expect(onChange).toHaveBeenCalledWith(['bru'])
  })

  it('closes on Escape without clearing what was typed', async () => {
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={vi.fn()} contacts={[bruno]} />)
    await userEvent.type(screen.getByLabelText('To'), 'bru')

    await userEvent.keyboard('{Escape}')

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect(screen.getByLabelText('To')).toHaveValue('bru')
  })

  it('works exactly as before when no contacts are supplied', async () => {
    const onChange = vi.fn()
    render(<RecipientsField id="to" label="To" tokens={[]} onChange={onChange} />)

    await userEvent.type(screen.getByLabelText('To'), 'a@x.be{Enter}')

    expect(onChange).toHaveBeenCalledWith(['a@x.be'])
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })
})
```

Si les tests existants n'attribuent pas déjà de nom accessible au champ, ajouter `htmlFor`/`id` est le travail de l'étape 3 — `.field-h` place le label à côté du contrôle, donc sans la paire le champ n'a aucun nom et `getByLabelText` ne l'atteint pas.

- [ ] **Step 2 : Lancer les tests pour vérifier qu'ils échouent**

Run: `cd src/frontend && npx vitest run src/modules/mail/compose/RecipientsField.test.tsx`
Expected: FAIL — la prop `contacts` n'existe pas, aucun `listbox` n'est rendu.

- [ ] **Step 3 : Ajouter la liste déroulante au champ**

Réécrire `src/frontend/src/modules/mail/compose/RecipientsField.tsx` :

```tsx
import { useMemo, useState, type ClipboardEvent, type KeyboardEvent } from 'react'
import { suggestionsFor } from '../../contacts/contactSearch'
import type { Contact } from '../../contacts/contactTypes'

/** Paint-and-gate check only; the backend's MimeKit parse is the authority. */
export function isValidAddress(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)
}

interface Props {
  id: string
  label: string
  tokens: string[]
  onChange: (tokens: string[]) => void
  autoFocus?: boolean
  /** The user's book, handed in by ComposeView. Empty by default, so the field stays fully usable
      — and its existing behaviour unchanged — for an account with no contacts. */
  contacts?: Contact[]
}

export default function RecipientsField({
  id, label, tokens, onChange, autoFocus, contacts = [],
}: Props) {
  const [draft, setDraft] = useState('')
  const [closed, setClosed] = useState(false)
  // -1 means "nothing highlighted", and it is the default on purpose: Enter must commit the
  // address the user typed, not substitute a suggestion they never looked at.
  const [active, setActive] = useState(-1)

  const suggestions = useMemo(
    () => suggestionsFor(contacts, draft, { exclude: new Set(tokens) }),
    [contacts, draft, tokens])
  const open = !closed && suggestions.length > 0

  function commit(raw: string) {
    const parts = raw.split(/[,;]/).map(p => p.trim()).filter(Boolean)
    if (parts.length > 0) onChange([...tokens, ...parts])
    reset()
  }

  function reset() {
    setDraft('')
    setActive(-1)
    setClosed(false)
  }

  function type(value: string) {
    setDraft(value)
    setActive(-1)
    setClosed(false)
  }

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (open && event.key === 'ArrowDown') {
      event.preventDefault()
      setActive(previous => Math.min(previous + 1, suggestions.length - 1))
    } else if (open && event.key === 'ArrowUp') {
      event.preventDefault()
      setActive(previous => Math.max(previous - 1, -1))
    } else if (event.key === 'Escape') {
      if (open) { event.preventDefault(); setClosed(true) }
    } else if (event.key === 'Enter' || event.key === ',' || event.key === ';') {
      event.preventDefault()
      if (open && active >= 0) commit(suggestions[active].address)
      else if (draft.trim()) commit(draft)
    } else if (event.key === 'Backspace' && draft === '' && tokens.length > 0) {
      onChange(tokens.slice(0, -1))
    }
  }

  function onPaste(event: ClipboardEvent<HTMLInputElement>) {
    const text = event.clipboardData.getData('text')
    if (!/[,;]/.test(text)) return
    event.preventDefault()
    commit(text)
  }

  return (
    <div className="field-h recipients-field">
      <label htmlFor={id}>{label}</label>
      <div className="recipients-box">
        {tokens.map((token, index) => (
          <span key={`${token}-${index}`} className={`recipient-token${isValidAddress(token) ? '' : ' is-invalid'}`}>
            {token}
            <button type="button" aria-label={`Remove ${token}`}
              onClick={() => onChange(tokens.filter((_, i) => i !== index))}>✕</button>
          </span>
        ))}
        <input id={id} type="text" value={draft} autoFocus={autoFocus}
          role="combobox" aria-expanded={open} aria-autocomplete="list"
          onChange={e => type(e.target.value)}
          onKeyDown={onKeyDown} onPaste={onPaste}
          onBlur={() => { if (draft.trim()) commit(draft) }} />

        {open && (
          <ul className="ownership-dropdown" role="listbox" aria-label={`${label} suggestions`}>
            {suggestions.map((suggestion, index) => (
              <li key={suggestion.address} role="option" aria-selected={index === active}
                className={`ownership-dropdown-option${index === active ? ' is-active' : ''}`}
                // mouseDown with preventDefault: the input's blur would otherwise commit the draft
                // and unmount this list before the click ever landed.
                onMouseDown={event => { event.preventDefault(); commit(suggestion.address) }}>
                <span className="suggestion-names">{suggestion.names.join(', ')}</span>
                <span className="suggestion-address">{suggestion.address}</span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  )
}
```

- [ ] **Step 4 : Passer les contacts depuis `ComposeView`**

Dans `src/frontend/src/modules/mail/compose/ComposeView.tsx`, ajouter l'import :

```tsx
import { useContacts } from '../../contacts/queries'
```

lire la liste une fois, près des autres hooks :

```tsx
  // One read for the three fields: they would share the cache anyway, but a single call site is
  // easier to follow than three.
  const { data: contacts } = useContacts()
```

et ajouter la prop aux trois champs (lignes 233, 239, 240) :

```tsx
          <RecipientsField id="compose-to" label="To" tokens={to} onChange={changeTo}
            autoFocus={!seed} contacts={contacts} />
...
        {showCc && <RecipientsField id="compose-cc" label="Cc" tokens={cc} onChange={changeCc}
          contacts={contacts} />}
        {showBcc && <RecipientsField id="compose-bcc" label="Bcc" tokens={bcc} onChange={changeBcc}
          contacts={contacts} />}
```

- [ ] **Step 5 : Styler la liste déroulante**

À la fin de `src/frontend/src/index.css` :

```css
/* The dropdown is anchored beneath the box, the shape website-design.md prescribes for a
   combobox. The tokens stay inline in the box rather than becoming chips above it: that is what
   this field already did, and what every mail client does. */
.recipients-field .recipients-box { position: relative; }
.recipients-field .ownership-dropdown { max-height: 260px; overflow-y: auto; }
.ownership-dropdown-option .suggestion-names { font-weight: 600; }
.ownership-dropdown-option .suggestion-address {
  margin-left: 8px; color: var(--text-muted); font-size: 12px;
}
```

Si `.ownership-dropdown` n'est pas déjà positionné en absolu sous son ancre dans `index.css`, ajouter :

```css
.recipients-field .ownership-dropdown {
  position: absolute; top: 100%; left: 0; right: 0; z-index: 20; margin: 2px 0 0; padding: 4px;
  list-style: none; background: var(--surface); border: 1px solid var(--border);
  border-radius: var(--radius-sm); box-shadow: 0 6px 18px rgb(0 0 0 / 18%);
}
```

- [ ] **Step 6 : Lancer les tests pour vérifier qu'ils passent**

Run: `cd src/frontend && npx vitest run src/modules/mail/compose/`
Expected: PASS — les 9 nouveaux cas **et** tous les cas préexistants de `RecipientsField.test.tsx` et `ComposeView.test.tsx`, inchangés.

- [ ] **Step 7 : Vérifier types, lint et suite complète**

Run: `cd src/frontend && npm run typecheck && npm run lint && npx vitest run`
Expected: PASS.

- [ ] **Step 8 : Commit**

```bash
git add src/frontend/src/modules/mail/compose/RecipientsField.tsx \
        src/frontend/src/modules/mail/compose/RecipientsField.test.tsx \
        src/frontend/src/modules/mail/compose/ComposeView.tsx \
        src/frontend/src/index.css
git commit -F - <<'EOF'
Suggest contacts in the composer recipient fields

Nothing is highlighted until an arrow key says so, so Enter still commits typed text.
EOF
```

---

### Task 15 : Vérification navigateur

**Files:** aucun fichier modifié sauf correctif éventuel de `src/frontend/src/index.css`.

**Pourquoi cette tâche existe.** jsdom ne calcule aucune mise en page : tous les tests ci-dessus peuvent passer alors que la colonne des tuiles est illisible, que le volet déborde ou que la fiche est invisible en thème sombre. La géométrie se **mesure** dans un navigateur, elle ne se raisonne pas.

- [ ] **Step 1 : Lancer l'application**

Run: `cd src/frontend && npm run dev`

Se connecter, puis ouvrir `/contacts`. Créer au moins **cinq** contacts de fixture, dont : un favori, un à trois adresses, un sans aucune adresse (pseudo seul), un au nom très long (« Marie-Charlotte Vandenbroucke-Delacroix »), et un accentué (« Éric Ötztal »).

- [ ] **Step 2 : Mesurer au plancher de 1024px**

Réduire la fenêtre à exactement 1024px de large. Vérifier, **mesuré et non supposé** :

- la page ne défile pas horizontalement ;
- la colonne des tuiles reste utilisable ; le nom long se termine par une ellipse et **ne passe pas sous** les icônes d'action ;
- l'espace des actions est réservé : survoler une tuile ne décale pas son texte d'un pixel ;
- la fiche à droite affiche ses lignes sans débordement.

Noter la largeur effective de la colonne des tuiles. Si elle tombe sous ~300px, réduire `reserve` du `PaneSplitter` dans `ContactsLayout` plutôt que de toucher aux tuiles.

- [ ] **Step 3 : Mesurer le séparateur à ses deux bouts**

Tirer le séparateur jusqu'à son minimum (240px) puis très large. À 240px : la tuile reste lisible sur ses deux lignes. Au maximum : la fiche garde sa place et ne se réduit pas à néant.

- [ ] **Step 4 : Vérifier l'échange plein cadre**

Cliquer « Add contact ». La bande de gauche **reste** en place, les deux colonnes centrales laissent la place à l'éditeur. Le formulaire est borné et ne s'étale pas sur toute la largeur. Ajouter trois adresses, en réordonner une, en supprimer une, enregistrer, revenir. Puis « Edit » sur un contact existant : les champs sont préremplis.

- [ ] **Step 5 : Vérifier les deux thèmes et deux palettes**

Basculer en thème sombre, puis changer de palette dans Réglages → Apparence. Vérifier qu'aucune couleur n'est codée en dur : l'étoile allumée, la tuile sélectionnée, le badge « primary » et le survol de tuile doivent tous suivre la palette. Une couleur qui ne bouge pas est un token manquant.

- [ ] **Step 6 : Vérifier l'autocomplétion dans le composeur**

Ouvrir `/mail/compose`. Dans « To », taper trois lettres d'un contact : la liste s'ouvre sous le champ. Vérifier au clavier (↓ ↓ Entrée), à la souris (clic sur une ligne), Escape, puis taper une adresse complète absente du carnet et valider par Entrée — elle doit devenir un jeton. Créer deux contacts partageant une adresse et confirmer **une seule ligne portant les deux noms**.

- [ ] **Step 7 : Corriger et committer si nécessaire**

Toute correction porte sur `index.css` ou sur `reserve`. Si rien n'est à corriger, ne rien committer et le noter.

```bash
git add src/frontend/src/index.css
git commit -F - <<'EOF'
Fix contacts module layout measured at the 1024px floor

Measured in a browser: jsdom computes no layout, so the suite could not have caught this.
EOF
```

---

### Task 16 : Documentation

**Files:**
- Modify: `src/frontend/CLAUDE.md`
- Modify: `src/snoopy.microservice/CLAUDE.md`
- Modify: `src/snoopy.microservice/DESIGN.md`
- Modify: `src/frontend/website-design.md`
- Modify: `docs/superpowers/specs/2026-07-27-webmail-contacts-3a3b-design.md`

- [ ] **Step 1 : Corriger la formulation de la spec**

Dans la spec, la ligne décrivant `ContactList` annonce « **deux peaux** comme la liste de messages ». Remplacer par la formulation exacte de ce qui a été construit :

> **`ContactList.tsx`** — les tuiles sur **une seule peau, à deux lignes** (étoile + nom + actions, puis l'adresse dessous). La liste de messages en porte deux parce que trois arrangements de volet existent dans le mail ; ici la liste est toujours à côté de la fiche, donc une peau large serait du code inatteignable. Les deux lignes sont de toute façon la réponse aux ~348px de la colonne au plancher de 1024px.

- [ ] **Step 2 : Documenter le module côté frontend**

Dans `src/frontend/CLAUDE.md` : remplacer « Calendar and Contacts are still placeholder (`ComingSoon`) pages » par une mention de Calendar seul, puis ajouter une section décrivant le module — les trois colonnes et l'échange plein cadre sur `/contacts/new` et `/contacts/:id/edit` (le mécanisme de `/mail/compose`) ; la bande de portées et pourquoi elle existe (l'import de 3d, les carnets CardDAV) ; `contactName.ts` et `contactSearch.ts` comme les deux points de partage entre la page et le composeur ; la peau unique de tuile et sa raison ; et le fait que `useAccountId` a quitté `modules/mail/queries.ts` pour `src/hooks/`.

- [ ] **Step 3 : Documenter l'API côté backend**

Dans `src/snoopy.microservice/CLAUDE.md`, ajouter `ContactsController` à la liste des contrôleurs avec ses cinq routes et leurs codes, en notant les deux règles qui se lisent mal dans le code : **404 et non 403** pour le contact d'autrui, et la route `Favorite` séparée parce que l'étoile part d'une tuile potentiellement périmée.

Dans `DESIGN.md`, ajouter `ContactStore` à la section Repositories et les deux tables à la table des entités, en précisant que `vcard_raw` et `uid` existent pour un CardDAV futur et que **rien ne les lit aujourd'hui** — sans quoi le prochain lecteur les prendra pour du code mort.

- [ ] **Step 4 : Consigner la convention de tuile**

Dans `website-design.md`, section *Lists & tiles*, ajouter que le module Contacts est la première liste tuilée dont la tuile s'étale sur deux lignes, que l'ordre de l'anatomie (étoile à l'extrême gauche, actions à l'extrême droite) y est conservé, et que la deuxième ligne porte l'adresse principale avec un « · +N » quand le contact en porte d'autres.

- [ ] **Step 5 : Commit**

```bash
git add src/frontend/CLAUDE.md src/snoopy.microservice/CLAUDE.md \
        src/snoopy.microservice/DESIGN.md src/frontend/website-design.md \
        docs/superpowers/specs/2026-07-27-webmail-contacts-3a3b-design.md
git commit -F - <<'EOF'
Document the contacts module

Spec corrected: the tile has one two-line skin, not two.
EOF
```

---

## Auto-revue du plan

**1. Couverture de la spec** — chaque section a sa tâche : données → 1 ; validation → 2 ; store → 3-4 ; API et portée → 5 ; helpers partagés → 6-7 ; client et cache → 8 ; surfaces et routes → 9-13 ; tranche 3b → 14 ; thèmes et géométrie → 15 ; documentation → 16. Les plafonds (5 000 / 50) sont posés en 2 et 3 ; le 404-jamais-403 en 4 et 5 ; `uid` et `vcard_raw` en 1, 3 et 4 ; les doublons d'adresse en 3, 7 et 14.

**2. Écarts assumés, tous nommés dans le plan** — la spec disait « deux peaux » de tuile, le plan n'en construit qu'une (Task 10), et Task 16 corrige la spec. La spec plaçait « Importer… » dans mes maquettes mais pas dans le périmètre : la bande n'a que deux entrées (Task 9). `useAccountId` change de dossier (Task 6), ce que la spec ne prévoyait pas — la raison est écrite dans la tâche.

**3. Cohérence des types** — `ContactWrite` est produit par `ContactValidator` (2) et consommé par `IContactStore` (3) et `ContactsController` (5) ; `ContactView` est produit par le store (3) et sérialisé tel quel vers `Contact` du frontend (6) ; `ContactDraft` (8) est la forme envoyée par `ContactEditView` (12) et acceptée par `ContactRequest` (2). `displayNameOf` / `primaryAddressOf` (6) sont appelés par 7, 10, 11 et 13 ; `suggestionsFor` (7) par 14 seul. `ContactScope` (9) reste interne au module.

**4. Placeholders** — aucun « TBD ». Les deux seuls emplacements provisoires sont les colonnes vides de `ContactsLayout` en Task 9, explicitement décrites comme telles et remplies en Task 13, ce qui est ce qui rend la tâche 9 testable seule.

