# Contacts 4e — les groupes : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Les rapports de sous-agents dans le scratchpad sont préfixés `4e-task-N-…`.

**Spec :** [`docs/superpowers/specs/2026-08-31-webmail-contacts-4e-groups-design.md`](../specs/2026-08-31-webmail-contacts-4e-groups-design.md) — toute « décision N » citée ici y renvoie. En cas de doute, la spec fait foi.

**Goal :** un groupe de contacts est une carte du carnet — créé, renommé, supprimé et peuplé au webmail, synchronisé tel quel par CardDAV (DAVx⁵, Apple), et développé en destinataires dans le composeur.

**Architecture :** une colonne `kind` sur `contacts` et une table fille `contact_group_members` projetée depuis `MEMBER`/`X-ADDRESSBOOKSERVER-MEMBER` ; le moteur vCard apprend les deux dialectes en lecture, trois éditions de lignes en écriture (ajout, retrait, renommage) et une traduction bilatérale dans le convertisseur de version ; un `ContactGroupStore` et six routes `api/ContactGroups` ; côté écran, un scope `group:<guid>` dans la bande, des puces sur la fiche et une espèce de ligne dans le composeur. **Le côté DAV ne filtre rien.**

**Tech stack :** .NET 10, EF Core (Pomelo MySQL, InMemory pour les tests), xUnit, Moq, FolkerKinzel.VCards 8.2.0 ; frontend React + TypeScript, Vitest, @tanstack/react-query.

## Global constraints

- `cd src && dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés) ; frontend : `cd src/frontend && npx vitest run`.
- Les tests repository tournent sur EF InMemory (`TestDbContext`) : aucune FK, aucune largeur de colonne, aucun `ENUM` n'y est appliqué — toute règle d'intégrité est portée par le code.
- Les blocs SQL sont **joués à la main par l'utilisateur** sur `snoopy_webmail` et `snoopy_webmail_dev`, avant tout déploiement backend (leçon du `MODIFY COLUMN source` de 4c-ii) ; l'ingénieur amende `docs/superpowers/webmail-contacts-tables.md`, il n'exécute pas de SQL.
- `Assert.IsType<T>` vérifie le type exact : `BadRequestObjectResult` pour `BadRequest(body)`.
- `ApiDocumentation.xml` : ne committer que les membres réellement touchés ; réverter la dérive massive que `dotnet test` régénère.
- Style C# : file-scoped namespaces, un type par fichier, records pour les DTO, `sealed`, `internal` par défaut, primary constructors, cancellation tokens, ILogger structuré.
- Commits : concis (2 lignes max), jamais commencer/finir par `@`, terminer par `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`. Messages multi-lignes via heredoc `git commit -F -`, jamais de here-string PowerShell.
- L'UI du site est en **anglais** ; `locales/fr` porte la traduction française. Pas d'assertion dépendante de l'hôte (fins de ligne, valeurs observées).
- Le côté DAV (`DavContactReader`, `DavContactWriter`, rapports, filtres) ne reçoit **aucun** filtre `kind` : la collection sert les deux espèces (décision 4).
- Toute écriture de carte de groupe prend le chemin complet d'`UpdateAsync` : `NextSequenceAsync` **dans** la transaction, relecture sous ce verrou, `card_hash` recalculé, révision archivée, re-projection (décision 20).
- **Avant l'implémentation** (décision 8) : l'utilisateur vérifie sur l'appareil DAVx⁵ de la campagne 4d le réglage *Contact group method* proposé **à la création d'un compte**, et consigne le résultat dans `docs/superpowers/carddav-4d-conformance.md` (tâche 14). Si le défaut est *categories*, la note de version doit dire qu'il faut passer en *separate vCards*.

---

### Task 1 : socle — colonne `kind`, entité `ContactGroupMember`, DbContext, document SQL

**Files :**
- Modify : `src/snoopy.microservice/Data/Preferences/Contact.cs`
- Create : `src/snoopy.microservice/Data/Preferences/ContactGroupMember.cs`
- Create : `src/snoopy.microservice/Repositories/ContactKind.cs`
- Modify : `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs`
- Modify : `docs/superpowers/webmail-contacts-tables.md`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Data/ContactEntitiesTests.cs`

**Interfaces (produit pour les tâches suivantes) :**
- `Contact.Kind` (`string`, défaut `ContactKinds.Individual`), colonne `kind`.
- `ContactGroupMember { Guid GroupId; string MemberUid; int Position }`, table `contact_group_members`, clé EF `(GroupId, Position)`, index unique `(GroupId, MemberUid)`.
- `ContactKinds.Individual == "individual"`, `ContactKinds.Group == "group"` ; extensions `IQueryable<Contact>.Individuals()` / `.GroupCards()` (la clause partagée de la décision 4 — posée ici, appliquée en tâche 4).

- [ ] **Step 1 : tests d'entités qui échouent**

Dans `ContactEntitiesTests` (style du fichier) :

```csharp
[Fact]
public void Contact_DefaultsToIndividualKind()
{
    // La leçon du MODIFY COLUMN source de 4c-ii : la valeur est épinglée, l'ENUM MariaDB la
    // refuse en mode strict si elle diverge du DDL.
    Assert.Equal("individual", new Contact().Kind);
    Assert.Equal("group", ContactKinds.Group);
}

[Fact]
public async Task ContactGroupMember_KeyIsGroupIdAndPosition_AndMemberIsUnique()
{
    using var db = TestDbContext.Create();
    var groupId = Guid.NewGuid();
    db.Contacts.Add(new Contact { Id = groupId, UserId = Guid.NewGuid(), Kind = ContactKinds.Group });
    db.ContactGroupMembers.Add(new ContactGroupMember { GroupId = groupId, MemberUid = "a", Position = 0 });
    db.ContactGroupMembers.Add(new ContactGroupMember { GroupId = groupId, MemberUid = "b", Position = 2 });
    await db.SaveChangesAsync(); // les trous de position sont légaux (décision 3)
    Assert.Equal(2, await db.ContactGroupMembers.CountAsync());
}
```

- [ ] **Step 2 : vérifier l'échec** — `cd src && dotnet test` : compilation en échec (types absents).

- [ ] **Step 3 : implémenter**

`Contact.cs`, après `Source` :

```csharp
/// <summary>
/// The card's species: "group" when it carries KIND:group / X-ADDRESSBOOKSERVER-KIND:group.
/// Written by the projection, like every other card-derived column.
/// </summary>
[Column("kind")]
public string Kind { get; set; } = ContactKinds.Individual;
```

`ContactGroupMember.cs` (mêmes conventions que `ContactEmail`) :

```csharp
[Table("contact_group_members")]
public sealed class ContactGroupMember
{
    [Column("group_id")]
    public Guid GroupId { get; set; }

    /// <summary>The member's UID, its urn:uuid: prefix stripped — never its id: a client may PUT
    /// the group before its members, so the reference is allowed to dangle (décision 2).</summary>
    [Column("member_uid")]
    public string MemberUid { get; set; } = string.Empty;

    /// <summary>Rank of the MEMBER property in the card; holes are legal (décision 3).</summary>
    [Column("position")]
    public int Position { get; set; }
}
```

`Repositories/ContactKind.cs` :

```csharp
using weesky.Snoopy.Microservice.Data.Preferences;

namespace weesky.Snoopy.Microservice.Repositories;

/// <summary>The card's two species. Pinned strings: the MariaDB ENUM refuses anything else.</summary>
public static class ContactKinds
{
    public const string Individual = "individual";
    public const string Group = "group";
}

/// <summary>
/// The kind clause, written once — ContactVisibility's twin sister. Every product read filters
/// through one of these; the DAV side filters through NEITHER: the collection serves both
/// species, and that is what makes it conform (décision 4). "GroupCards", not "Groups": that
/// word already names the vCard property group in this code.
/// </summary>
internal static class ContactKindQueries
{
    internal static IQueryable<Contact> Individuals(this IQueryable<Contact> contacts) =>
        contacts.Where(c => c.Kind == ContactKinds.Individual);

    internal static IQueryable<Contact> GroupCards(this IQueryable<Contact> contacts) =>
        contacts.Where(c => c.Kind == ContactKinds.Group);
}
```

`PreferencesDbContext.OnModelCreating`, sous les quatre sœurs — la forme des trois à clé composite, ligne pour ligne, plus l'index unique :

```csharp
modelBuilder.Entity<ContactGroupMember>().HasKey(m => new { m.GroupId, m.Position });
modelBuilder.Entity<ContactGroupMember>().HasIndex(m => new { m.GroupId, m.MemberUid }).IsUnique();
modelBuilder.Entity<ContactGroupMember>()
    .HasOne<Contact>().WithMany().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
```

et le `DbSet` : `public DbSet<ContactGroupMember> ContactGroupMembers => Set<ContactGroupMember>();` (à côté des quatre existants).

- [ ] **Step 4 : vérifier le vert** — `cd src && dotnet test`.

- [ ] **Step 5 : amender le document SQL**

Dans `docs/superpowers/webmail-contacts-tables.md`, ajouter le bloc de la spec (§ Schéma) **tel quel** : `ALTER TABLE contacts ADD COLUMN kind ENUM('individual','group') NOT NULL DEFAULT 'individual' … AFTER source;` et le `CREATE TABLE contact_group_members` complet (PK `(group_id, position)`, `UNIQUE uq_group_member (group_id, member_uid)`, `INDEX ix_group_members_uid`, FK cascade, `utf8mb4_bin`). Rappeler : à jouer sur les **deux** bases avant tout déploiement. Aucun rattrapage de données (le sondage du 2026-08-31 ne trouve aucune carte de groupe).

- [ ] **Step 6 : commit** — `feat(contacts): kind column and group members table`

---

### Task 2 : `VCardProjector` — lecture des deux dialectes

**Files :**
- Modify : `src/snoopy.microservice/Models/Contacts/ContactProjection.cs`
- Modify : `src/snoopy.microservice/Services/Contacts/VCardProjector.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/VCardProjectorTests.cs`

**Interfaces :**
- `ContactProjection` gagne, en fin de record : `string Kind` (valeurs `ContactKinds.*`) et `IReadOnlyList<ProjectedMember> Members`.
- `public sealed record ProjectedMember(string MemberUid, int Position);` dans `ContactProjection.cs`.
- `VCardProjector.StripUrnUuid(string value)` (internal) : retire un préfixe `urn:uuid:` insensible à la casse — partagé avec le retrait de la tâche 8.

- [ ] **Step 1 : tests qui échouent**

```csharp
[Theory]
[InlineData("KIND:group")]
[InlineData("X-ADDRESSBOOKSERVER-KIND:group")]
[InlineData("X-ADDRESSBOOKSERVER-KIND:GROUP")] // la valeur se lit insensible à la casse
public void Project_ReadsBothGroupDialects(string kindLine)
{
    var card = $"BEGIN:VCARD\r\nVERSION:3.0\r\nUID:g1\r\nFN:Amis\r\nN:;;;;\r\n{kindLine}\r\n" +
        "X-ADDRESSBOOKSERVER-MEMBER:urn:uuid:m1\r\nEND:VCARD\r\n";
    var p = VCardProjector.Project(card);
    Assert.Equal(ContactKinds.Group, p.Kind);
    Assert.Equal([new ProjectedMember("m1", 0)], p.Members);
}

[Fact]
public void Project_MemberValueFormsReadWide()
{
    var card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:g\r\nFN:G\r\nX-ADDRESSBOOKSERVER-KIND:group\r\n"
        + "X-ADDRESSBOOKSERVER-MEMBER:URN:UUID:a\r\n"   // préfixe retiré quelle que soit sa casse
        + "X-ADDRESSBOOKSERVER-MEMBER:b\r\n"            // UID nu accepté
        + "X-ADDRESSBOOKSERVER-MEMBER:mailto:c@d.e\r\n" // autre schéma : stocké tel quel, pendant
        + "X-ADDRESSBOOKSERVER-MEMBER:urn:uuid:a\r\n"   // doublon : une seule ligne (décision 3)
        + $"X-ADDRESSBOOKSERVER-MEMBER:urn:uuid:{new string('x', 300)}\r\n" // > 255 : écarté
        + "END:VCARD\r\n";
    var p = VCardProjector.Project(card);
    Assert.Equal([new ProjectedMember("a", 0), new ProjectedMember("b", 1),
        new ProjectedMember("mailto:c@d.e", 2)], p.Members);
}

[Fact]
public void Project_IndividualCardHasNoMembers()
{
    var p = VCardProjector.Project("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:X\r\nEND:VCARD\r\n");
    Assert.Equal(ContactKinds.Individual, p.Kind);
    Assert.Empty(p.Members);
}
```

- [ ] **Step 2 : vérifier l'échec** — compilation (record).

- [ ] **Step 3 : implémenter**

`ContactProjection.cs` : ajouter `string Kind` et `IReadOnlyList<ProjectedMember> Members` en fin de record, et le record `ProjectedMember`. Corriger `VCardProjector.Empty` (`ContactKinds.Individual`, `[]`).

Dans `VCardProjector`, étendre `RawCard` : trois familles de plus (`KIND`, `X-ADDRESSBOOKSERVER-KIND` d'une part, `MEMBER` et `X-ADDRESSBOOKSERVER-MEMBER` d'autre part) dont on garde cette fois la **valeur** (après le `:` hors guillemets), dans l'ordre du document — même mécanique que `Birthday`. Les deux noms de membre alimentent **une seule** liste `MemberValues` (une carte n'emploie qu'un dialecte ; l'ordre du document fait foi), et `KindValue` retient la première valeur de l'un des deux noms de kind. Puis dans `Project` :

```csharp
var kind = raw.KindValue?.Trim().Equals("group", StringComparison.OrdinalIgnoreCase) == true
    ? ContactKinds.Group : ContactKinds.Individual;
var members = Members(raw.MemberValues);
```

```csharp
// La projection est un ensemble (décision 3) : le doublon s'écarte ici, avant l'UNIQUE de la
// table. Une valeur plus longue que la colonne s'écarte entière, jamais tronquée — le régime de
// l'adresse e-mail : un UID coupé désignerait le mauvais contact (décision 2). Les rangs de la
// carte survivent tels quels.
private static List<ProjectedMember> Members(IReadOnlyList<string> values)
{
    var members = new List<ProjectedMember>();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    for (var rank = 0; rank < values.Count; rank++)
    {
        var uid = StripUrnUuid(values[rank].Trim());
        if (uid.Length == 0 || uid.Length > MaxUidLength || !seen.Add(uid)) continue;
        members.Add(new ProjectedMember(uid, rank));
    }
    return members;
}

internal static string StripUrnUuid(string value) =>
    value.StartsWith("urn:uuid:", StringComparison.OrdinalIgnoreCase) ? value[9..] : value;
```

- [ ] **Step 4 : vert** — `cd src && dotnet test` (les constructions positionnelles de `ContactProjection` dans les tests existants sont à compléter des deux champs).

- [ ] **Step 5 : commit** — `feat(contacts): projector reads KIND and MEMBER in both dialects`

---

### Task 3 : le cycle de projection passe à cinq tables

**Files :**
- Modify : `src/snoopy.microservice/Repositories/ContactStore.cs` (`ReplaceProjectionAsync`, `LoadProjectionAsync`, `ClearProjectionAsync`, `ProjectionCache`)
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreTests.cs` (ou le fichier de tests DAV write existant pour le second PUT)

**Interfaces :** `ProjectionCache` gagne `ILookup<Guid, ContactGroupMember> Members` ; `ReplaceProjectionAsync` écrit `row.Kind` et les lignes de membres.

- [ ] **Step 1 : le test du second PUT**

Le test qui rougit si `ProjectionCache` reste à quatre tables (décision 2) — via `DavContactWriter.PutAsync` joué deux fois avec la même carte de groupe **modifiée entre les deux** (même membre, FN changé), sinon le garde byte-identique court-circuite le cycle :

```csharp
[Fact]
public async Task SecondPutOfAGroupCard_LeavesOneRowPerMember()
{
    // Arrange : writer + store sur TestDbContext, une carte X-ADDRESSBOOKSERVER-KIND:group
    // avec deux X-ADDRESSBOOKSERVER-MEMBER.
    var first = await writer.PutAsync(userId, "g.vcf", GroupCard("Amis"), CancellationToken.None);
    Assert.Equal(DavWriteStatus.Created, first.Status);
    var second = await writer.PutAsync(userId, "g.vcf", GroupCard("Amis renommés"), CancellationToken.None);
    Assert.Equal(DavWriteStatus.Replaced, second.Status); // pas un 500 sur UNIQUE
    Assert.Equal(2, await db.ContactGroupMembers.CountAsync());
    Assert.Equal(ContactKinds.Group, (await db.Contacts.SingleAsync()).Kind);
}
```

- [ ] **Step 2 : vérifier l'échec** — le second PUT casse (lignes doublées / InMemory unique index).

- [ ] **Step 3 : implémenter**

- `ReplaceProjectionAsync` : `row.Kind = projection.Kind;` avec les autres scalaires, puis :

```csharp
foreach (var member in projection.Members)
    context.ContactGroupMembers.Add(new ContactGroupMember
    {
        GroupId = row.Id, MemberUid = member.MemberUid, Position = member.Position
    });
```

- `LoadProjectionAsync` : cinquième requête sur `ContactGroupMembers` (clé `GroupId`) ; `ProjectionCache.Of` et le record gagnent le lookup ; `Clear` fait le cinquième `RemoveRange`.
- Corriger les commentaires : « four queries » → « five queries » sur `LoadProjectionAsync`, « the four families » → « the five families » sur `ClearProjectionAsync` (décision 2 : les commentaires se corrigent avec le code).

- [ ] **Step 4 : vert** — `cd src && dotnet test`.

- [ ] **Step 5 : commit** — `feat(contacts): group members join the projection cycle`

---

### Task 4 : la clause `kind` sur toutes les surfaces produit

**Files :**
- Modify : `src/snoopy.microservice/Repositories/ContactStore.cs`
- Modify : `src/snoopy.microservice/Repositories/IContactStore.cs`
- Modify : `src/snoopy.microservice/Repositories/DavContactWriter.cs` (`DeleteAllAsync` seulement)
- Test : `snoopy.microservice.Tests/Repositories/ContactStoreTests.cs`, `Controllers/ContactsControllerTests.cs`, tests DAV read existants

**Interfaces :** `IContactStore.DeleteManyAsync` gagne `bool includeGroups = false` (seul `DavContactWriter.DeleteAllAsync` passe `true`).

Audit des vingt-cinq accès `context.Contacts` de `ContactStore` — la règle : **une lecture produit filtre `Individuals()`, un compte de plafond et le côté DAV ne filtrent rien** (décisions 4 et 18).

- [ ] **Step 1 : tests qui échouent** — un groupe (ligne `Kind = ContactKinds.Group` + carte) :
  - n'apparaît pas dans `ListAsync` ni `ExportAsync` ;
  - `GetAsync`, `GetPhotoAsync`, `UpdateAsync`, `DeleteAsync`, `SetFavoriteAsync` répondent null/`NotFound` sur son id (→ 404 contrôleur) ;
  - `DeleteManyAsync` et `SetFavoriteManyAsync` l'ignorent en silence ; `DeleteManyAsync(includeGroups: true)` l'emporte ;
  - il **compte** dans le plafond : avec `MaxPerUser` atteint groupes compris, `CreateAsync` refuse (`CapReached`) ;
  - l'index par nom de l'import ne le contient pas : un CSV « Amis » sans adresse crée une fiche au lieu de fusionner dans le groupe « Amis » ;
  - il reste servi par les lectures DAV (le test `DavContactReader` existant qui liste la collection, joué avec une carte de groupe en base).

- [ ] **Step 2 : vérifier l'échec.**

- [ ] **Step 3 : implémenter** — poser `.Individuals()` sur :
  - `ListAsync` (la requête projetée) ; `ExportAsync` (`Scalars(...)`) ; `GetAsync` ; `GetPhotoAsync` ; `FindAsync` (ce qui couvre Update/Delete/SetFavorite d'un coup) ;
  - `DeleteManyAsync` : la requête du lot devient `context.Contacts.Individuals()…` sauf si `includeGroups` ; `SetFavoriteManyAsync` : `Individuals()` sans option ;
  - `ImportAsync` : la requête `addressless` gagne `Individuals()` (l'index par nom n'a jamais de groupe pour cible — décision 4) ; `uidOwners` reste **toutes espèces** (la frontière des espèces se juge en tâche 9).
  - Les comptes de plafond (`CreateAsync`, `ImportBatchAsync`, `DavContactWriter.GateAsync`) restent non filtrés — un groupe compte (décision 18).
  - `DavContactWriter.DeleteAllAsync` : `store.DeleteManyAsync(userId, ids, includeGroups: true, ct)`.
  - `BackfillAsync` et le cycle interne (`ReloadAsync`, `ApplyCardAsync`…) ne filtrent pas : ils suivent la carte, pas l'écran.

- [ ] **Step 4 : vert**, **Step 5 : commit** — `feat(contacts): kind clause on every product surface`

---

### Task 5 : `VCardComposer` — carte neuve, ajout, retrait, renommage

**Files :**
- Modify : `src/snoopy.microservice/Services/Contacts/VCardComposer.cs`
- Test : `snoopy.microservice.Tests/Services/VCardComposerTests.cs`

**Interfaces (internal static, sur `VCardComposer`) :**
- `ComposeNewGroup(string uid, string name)` → carte 3.0 neuve : `X-ADDRESSBOOKSERVER-KIND:group`, `FN` = nom, `N:;;;;`, UID posé. La seule des trois à passer par le sérialiseur (décision 6).
- `AddGroupMember(string card, string memberUid)` → édition de lignes ; la ligne insérée porte le dialecte de la carte (`MEMBER` si la carte dit `KIND`, `X-ADDRESSBOOKSERVER-MEMBER` sinon), valeur `urn:uuid:` + uid sans condition (décision 5), insérée avant `END:VCARD`, pliée.
- `RemoveGroupMember(string card, string memberUid)` → retire **toute** ligne des deux noms dont la valeur, préfixe `urn:uuid:` retiré insensible à la casse, vaut `memberUid` (décision 7 : le retrait matche toutes les formes que la lecture accepte).
- `RenameGroup(string card, string name)` → remplace la **valeur** du premier `FN` et rien d'autre ; la valeur `text` est échappée (`\` → `\\`, `;` → `\;`, `,` → `\,`, sauts de ligne → `\n` — l'antislash en premier), pliée à 75.

- [ ] **Step 1 : tests qui échouent**

```csharp
[Fact]
public void ComposeNewGroup_CarriesKindNameAndEmptyN()
{
    var card = VCardComposer.ComposeNewGroup("g1", "Amis");
    Assert.Contains("X-ADDRESSBOOKSERVER-KIND:group", card);
    Assert.Contains("FN:Amis", card);
    Assert.Contains("N:;;;;", card);      // pas le ? de la bibliothèque (décision 17)
    Assert.Contains("UID:g1", card);
    Assert.DoesNotContain("KIND:group\r\n", card.Replace("X-ADDRESSBOOKSERVER-KIND:group", ""));
}

[Fact]
public void AddGroupMember_FollowsTheCardsDialect_AndTouchesNothingElse()
{
    // Carte 4.0 stockée : la ligne écrite est MEMBER, jamais X-ADDRESSBOOKSERVER-MEMBER.
    var v4 = "BEGIN:VCARD\r\nVERSION:4.0\r\nUID:g\r\nFN:G\r\nKIND:group\r\nX-FOO;X-BAR=1:v\r\nEND:VCARD\r\n";
    var added = VCardComposer.AddGroupMember(v4, "m1");
    Assert.Contains("MEMBER:urn:uuid:m1", added);
    Assert.DoesNotContain("X-ADDRESSBOOKSERVER-MEMBER", added);
    // Le reste octet pour octet : la famille X- étrangère intacte — le test qui rougit si
    // l'écriture repasse par le modèle (l'écrivain 3.0 n'émet jamais VCard.Members, et le splice
    // réverte une famille X- déjà présente).
    Assert.Equal(v4.Replace("END:VCARD", "MEMBER:urn:uuid:m1\r\nEND:VCARD"), added);
}

[Fact]
public void RemoveGroupMember_MatchesEveryValueForm()
{
    var card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:g\r\nFN:G\r\nX-ADDRESSBOOKSERVER-KIND:group\r\n"
        + "X-ADDRESSBOOKSERVER-MEMBER:m1\r\n"           // nu (DAVx⁵)
        + "X-ADDRESSBOOKSERVER-MEMBER:URN:UUID:m1\r\n"  // préfixé, casse quelconque (Apple)
        + "X-ADDRESSBOOKSERVER-MEMBER:urn:uuid:m2\r\nEND:VCARD\r\n";
    var removed = VCardComposer.RemoveGroupMember(card, "m1");
    Assert.DoesNotContain("m1", removed);
    Assert.Contains("urn:uuid:m2", removed);
}

[Fact]
public void RenameGroup_TouchesOnlyTheFnAndEscapes()
{
    var card = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:g\r\nFN:G\r\nN:;;;;\r\n"
        + "X-ADDRESSBOOKSERVER-KIND:group\r\nEMAIL:a@b.c\r\nEND:VCARD\r\n";
    var renamed = VCardComposer.RenameGroup(card, "Amis, Famille");
    Assert.Contains(@"FN:Amis\, Famille", renamed);
    Assert.Contains("EMAIL:a@b.c", renamed);   // les EMAIL sont là après (décision 6)
    Assert.Contains("N:;;;;", renamed);        // pas de N rempli
    // Et l'aller-retour par le projecteur rend le nom déséchappé.
    Assert.Equal("Amis, Famille", VCardProjector.Project(renamed).DisplayName);
}
```

- [ ] **Step 2 : vérifier l'échec.**

- [ ] **Step 3 : implémenter**

```csharp
/// <summary>A new group card — the only group write that goes through the serializer: a card
/// that does not exist yet has nothing to preserve (décision 6). Born in 3.0, Apple's dialect.</summary>
internal static string ComposeNewGroup(string uid, string name)
{
    var card = new VCard { DisplayNames = [new TextProperty(name)] };
    card.NonStandards = [new NonStandardProperty("X-ADDRESSBOOKSERVER-KIND", "group")];
    var source = new SourceCard(card, VCdVersion.V3_0, [], [], null);
    return Emit(source, uid, null);
}
```

(`Emit` pose l'UID, `StripNamePlaceholders` rend le `N:;;;;` ; le sérialiseur échappe le FN — déjà épinglé par `ComposeNew_EscapesTheSeparators`.)

Les trois éditions de lignes travaillent sur `LogicalLines(CanonicalLineBreaks(card))` et rendent `string.Join("\r\n", lines) + "\r\n"` :

```csharp
internal static string AddGroupMember(string card, string memberUid)
{
    var lines = LogicalLines(CanonicalLineBreaks(card));
    // Le dialecte de la carte, pas le nôtre (décision 6) : une carte mixte serait un groupe
    // sans membre pour un lecteur 4.0 strict.
    var v4 = lines.Any(l => NameOf(Unfold(l)).Equals("KIND", StringComparison.OrdinalIgnoreCase));
    var name = v4 ? "MEMBER" : "X-ADDRESSBOOKSERVER-MEMBER";
    var end = lines.FindLastIndex(l =>
        NameOf(Unfold(l)).Equals("END", StringComparison.OrdinalIgnoreCase));
    lines.Insert(end < 0 ? lines.Count : end, Fold($"{name}:urn:uuid:{memberUid}"));
    return string.Join("\r\n", lines) + "\r\n";
}

internal static string RemoveGroupMember(string card, string memberUid)
{
    var lines = LogicalLines(CanonicalLineBreaks(card));
    lines.RemoveAll(l =>
    {
        var unfolded = Unfold(l);
        var name = NameOf(unfolded);
        if (!name.Equals("MEMBER", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("X-ADDRESSBOOKSERVER-MEMBER", StringComparison.OrdinalIgnoreCase))
            return false;
        var colon = IndexOutsideQuotes(unfolded, ':');
        return colon >= 0 && VCardProjector.StripUrnUuid(unfolded[(colon + 1)..].Trim()) == memberUid;
    });
    return string.Join("\r\n", lines) + "\r\n";
}

internal static string RenameGroup(string card, string name)
{
    var lines = LogicalLines(CanonicalLineBreaks(card));
    var index = lines.FindIndex(l =>
        NameOf(Unfold(l)).Equals("FN", StringComparison.OrdinalIgnoreCase));
    var escaped = EscapeText(name);
    if (index < 0)
    {
        var end = lines.FindLastIndex(l =>
            NameOf(Unfold(l)).Equals("END", StringComparison.OrdinalIgnoreCase));
        lines.Insert(end < 0 ? lines.Count : end, Fold("FN:" + escaped));
    }
    else
    {
        var unfolded = Unfold(lines[index]);
        var colon = IndexOutsideQuotes(unfolded, ':');
        lines[index] = Fold(unfolded[..(colon + 1)] + escaped);
    }
    return string.Join("\r\n", lines) + "\r\n";
}

// Le prix d'écrire une ligne à la main est de l'échapper à la main (décision 6) : l'antislash
// d'abord, sans quoi il ré-échappe les échappements qu'il vient de poser.
internal static string EscapeText(string value) => value
    .Replace("\\", "\\\\").Replace("\r\n", "\\n").Replace("\r", "\\n").Replace("\n", "\\n")
    .Replace(";", "\\;").Replace(",", "\\,");
```

(`SourceCard` est privé : soit rendre son constructeur accessible à `ComposeNewGroup` — même fichier, aucun changement de visibilité — soit répliquer l'appel `Emit` ; même fichier, donc direct.)

- [ ] **Step 4 : vert**, **Step 5 : commit** — `feat(contacts): composer group card, member and rename line edits`

---

### Task 6 : `VCardVersionConverter` — les deux propriétés, dans les deux sens

**Files :**
- Modify : `src/snoopy.microservice/Services/CardDav/VCardVersionConverter.cs`
- Test : `snoopy.microservice.Tests/Services/CardDav/VCardVersionConverterTests.cs`

- [ ] **Step 1 : tests qui échouent**

```csharp
[Fact]
public void ServingAGroupCardIn40_TranslatesKindAndMembers()
{
    var v3 = "BEGIN:VCARD\r\nVERSION:3.0\r\nUID:g\r\nFN:G\r\nN:;;;;\r\n"
        + "X-ADDRESSBOOKSERVER-KIND:group\r\nX-ADDRESSBOOKSERVER-MEMBER:urn:uuid:m1\r\nEND:VCARD\r\n";
    var served = VCardVersionConverter.To(v3, "4.0");
    Assert.Contains("KIND:group", served);
    Assert.Contains("MEMBER:urn:uuid:m1", served);
    Assert.DoesNotContain("X-ADDRESSBOOKSERVER", served);
}

[Fact]
public void ServingAGroupCardIn30_RebuildsFromTheStoredCard()
{
    // Le writer 3.0 a DÉJÀ supprimé KIND et MEMBER (propriétés 4.0-only) : rien à renommer,
    // les deux lignes se rebâtissent depuis la carte stockée — le test rougit si on l'a écrit
    // comme un renommage (décision 5).
    var v4 = "BEGIN:VCARD\r\nVERSION:4.0\r\nUID:g\r\nFN:G\r\nKIND:group\r\n"
        + "MEMBER:urn:uuid:m1\r\nMEMBER:m2\r\nEND:VCARD\r\n";
    var served = VCardVersionConverter.To(v4, "3.0");
    Assert.Contains("X-ADDRESSBOOKSERVER-KIND:group", served);
    Assert.Contains("X-ADDRESSBOOKSERVER-MEMBER:urn:uuid:m1", served);
    Assert.Contains("X-ADDRESSBOOKSERVER-MEMBER:m2", served); // valeur verbatim, jamais réécrite
}
```

- [ ] **Step 2 : vérifier l'échec.**

- [ ] **Step 3 : implémenter**

Dans `To`, après `RestoreUid(lines, card)` :

```csharp
TranslateGroupProperties(lines, wanted, card);
```

```csharp
/// <summary>
/// The third of this class's assumed exceptions, after DropEmbeddedCards and RestoreUid, and for
/// RestoreUid's reason: without it a strictly-4.0 client reads a group as an empty sheet — the
/// defect 4e repairs, moved one step over (décision 5 de 4e). Going to 4.0 the library copied the
/// X- lines through: renaming them is enough. Going to 3.0 it already dropped KIND and MEMBER
/// before we looked, so the two lines are rebuilt from the STORED card, values verbatim.
/// </summary>
private static void TranslateGroupProperties(List<string> lines, VCdVersion wanted, string stored)
{
    if (wanted == VCdVersion.V4_0)
    {
        Rename(lines, "X-ADDRESSBOOKSERVER-KIND", "KIND");
        Rename(lines, "X-ADDRESSBOOKSERVER-MEMBER", "MEMBER");
        return;
    }

    // 3.0 : rebâtir depuis la carte stockée, comme RestoreUid va y relire son UID.
    if (VCardComposer.FirstRawValue(stored, "KIND") is not { } kind
        || !kind.Trim().Equals("group", StringComparison.OrdinalIgnoreCase))
        return;
    var rebuilt = new List<string> { "X-ADDRESSBOOKSERVER-KIND:" + kind };
    rebuilt.AddRange(RawValuesOf(stored, "MEMBER")
        .Select(v => VCardComposer.Fold("X-ADDRESSBOOKSERVER-MEMBER:" + v)));
    var end = lines.FindLastIndex(l => VCardComposer
        .NameOf(VCardComposer.Unfold(l)).Equals("END", StringComparison.OrdinalIgnoreCase));
    lines.InsertRange(end < 0 ? lines.Count : end, rebuilt);
}
```

`Rename(lines, from, to)` remplace le préfixe de nom (groupe de propriété conservé) sur chaque ligne dont `NameOf` vaut `from` ; `RawValuesOf(card, name)` est un petit parcours de `LogicalLines` rendant **toutes** les valeurs brutes d'une famille (le `FirstRawValue` existant n'en rend qu'une) — à poser en interne dans `VCardComposer` à côté de `FirstRawValue`.

Corriger le **commentaire de tête** de la classe : la règle « never a textual rewrite of ours » énumère désormais ses trois exceptions (`RestoreUid`, `StripNamePlaceholders`, `TranslateGroupProperties`) — sans quoi il énonce une règle que trois méthodes enfreignent (décision 5).

- [ ] **Step 4 : vert**, **Step 5 : commit** — `feat(carddav): version converter translates group properties both ways`

---

### Task 7 : `ContactGroupStore`, validateur, contrôleur — les six routes

**Files :**
- Create : `src/snoopy.microservice/Repositories/IContactGroupStore.cs`
- Create : `src/snoopy.microservice/Repositories/ContactGroupStore.cs`
- Create : `src/snoopy.microservice/Models/Contacts/ContactGroupView.cs`, `ContactGroupsResponse.cs`, `ContactGroupRequest.cs`, `ContactGroupMembersRequest.cs`
- Modify : `src/snoopy.microservice/Services/ContactValidator.cs` (borne du nom)
- Create : `src/snoopy.microservice/Controllers/ContactGroupsController.cs`
- Modify : `src/snoopy.microservice.host/Program.cs` (ou l'endroit où `IContactStore` est enregistré) : `AddScoped<IContactGroupStore, ContactGroupStore>()`
- Test : `snoopy.microservice.Tests/Repositories/ContactGroupStoreTests.cs`, `Controllers/ContactGroupsControllerTests.cs`

**Interfaces :**

```csharp
public sealed record ContactGroupView(Guid Id, string Name, IReadOnlyList<Guid> MemberIds);
public sealed record ContactGroupsResponse(IReadOnlyList<ContactGroupView> Groups);
public sealed record ContactGroupRequest(string? Name);
public sealed record ContactGroupMembersRequest(IReadOnlyList<Guid>? ContactIds);

public interface IContactGroupStore
{
    Task<IReadOnlyList<ContactGroupView>> ListAsync(Guid userId, CancellationToken cancellationToken);
    Task<Result<ContactGroupView>> CreateAsync(Guid userId, string name, CancellationToken cancellationToken);
    Task<Result> RenameAsync(Guid userId, Guid groupId, string name, CancellationToken cancellationToken);
    Task<Result> DeleteAsync(Guid userId, Guid groupId, CancellationToken cancellationToken);
    Task<Result> AddMembersAsync(Guid userId, Guid groupId, IReadOnlyList<Guid> contactIds, CancellationToken cancellationToken);
    Task<Result> RemoveMembersAsync(Guid userId, Guid groupId, IReadOnlyList<Guid> contactIds, CancellationToken cancellationToken);
}
```

`ContactValidator` : `internal const int MaxGroupNameLength = 255;` (la largeur de `display_name`, à côté des autres largeurs) et

```csharp
/// <summary>The group-name rule, one place: trimmed; refused empty or over the column.</summary>
internal static Result<string> ValidateGroupName(string? name)
{
    var trimmed = name?.Trim();
    if (string.IsNullOrEmpty(trimmed)) return Result.Failure<string>("A group needs a name");
    if (trimmed.Length > MaxGroupNameLength)
        return Result.Failure<string>($"The group name must be at most {MaxGroupNameLength} characters");
    return Result.Success(trimmed);
}
```

- [ ] **Step 1 : tests du store qui échouent** (InMemory, `ContactStore` + `ContactSyncStore` réels comme les tests DAV) :
  - `CreateAsync` : rend un `ContactGroupView` (memberIds vides), la ligne porte `Kind = group`, une carte `X-ADDRESSBOOKSERVER-KIND:group`, un `DavName`, un rang > 0 ; refuse au plafond (`CapReached`, groupes comptés — décision 18 : le quatrième endroit qui le contrôle).
  - `ListAsync` : `memberIds` ne contient que les membres **résolus** — un `MEMBER` pendant n'en sort pas, un membre d'un autre carnet non plus (la jointure porte le `user_id`), un membre-groupe non plus (décision 9), et un membre dont l'UID stocké est lui-même `urn:uuid:…` **résout** (les deux formes).
  - `RenameAsync` : seul le `FN` bouge (les autres lignes de la carte identiques), `SyncSequence` avance, une révision est archivée, `display_name` re-projeté ; `NotFound` sur un id de fiche ou d'autrui.
  - `AddMembersAsync` / `RemoveMembersAsync` : lot ; id inconnu, id d'autrui, id de groupe (le sien compris) = no-op silencieux ; un ajout déjà membre ne double pas la ligne `MEMBER` ; **un lot qui ne change rien ne prend ni rang ni révision** ; un lot qui change prend un rang et une révision et re-projette (`contact_group_members` à jour).
  - `DeleteAsync` : cascade table des membres, tombe DAV, révision, les fiches restent (décision 7 fin).

- [ ] **Step 2 : vérifier l'échec.**

- [ ] **Step 3 : implémenter le store**

`ContactGroupStore(PreferencesDbContext context, ContactStore store, IContactSyncStore sync)` — le concret, comme `DavContactWriter`, pour atteindre `InTransactionAsync`/`ApplyCardAsync`/`ClearProjectionAsync`.

`ListAsync` — une requête pour les groupes, une pour la résolution (les quatre besoins de la spec sortent de celle-ci) :

```csharp
var groups = await context.Contacts.AsNoTracking().GroupCards()
    .Where(c => c.UserId == userId)
    .Select(c => new { c.Id, c.DisplayName })
    .ToListAsync(cancellationToken);
if (groups.Count == 0) return [];

// La frontière entre deux carnets vit entièrement dans cette jointure (décision 2) ; et l'UID
// s'essaie sous ses deux formes : uid == member_uid, ou uid == 'urn:uuid:' + member_uid.
var resolved = await context.ContactGroupMembers.AsNoTracking()
    .Join(context.Contacts.AsNoTracking().Individuals().Where(c => c.UserId == userId),
        m => 1, c => 1, (m, c) => new { m, c })
    .Where(x => x.c.Uid == x.m.MemberUid || x.c.Uid == "urn:uuid:" + x.m.MemberUid)
    .Select(x => new { x.m.GroupId, MemberId = x.c.Id, x.m.Position })
    .ToListAsync(cancellationToken);
```

(EF traduit mal un join constant — écrire plutôt la forme `from m in context.ContactGroupMembers join c in … on … equals …` n'acceptant pas de OR : utiliser `SelectMany` + `Where` comme ci-dessus mais en `from m in Members from c in Contacts where …`, qui se traduit en CROSS JOIN filtré ; sur MariaDB l'index `ix_group_members_uid` et `uq_contacts_user_uid` portent la jointure. Restreindre d'abord `Members` aux `GroupId` de l'utilisateur via `groups`-ids pour ne pas balayer la table.) Ordonner les `memberIds` par `Position`.

`CreateAsync` — le squelette de `ContactStore.CreateAsync` :

```csharp
var validated = ContactValidator.ValidateGroupName(name); // le contrôleur l'a déjà fait ; sûreté
var stored = await context.Contacts.CountAsync(c => c.UserId == userId, cancellationToken);
if (stored >= ContactStore.MaxPerUser) return Result.Failure<ContactGroupView>(ContactStore.CapReached);
// puis InTransactionAsync : rank → row (Uid = id, Source = "manual", DavName, SyncSequence = rank)
// → store.ApplyCardAsync(row, VCardComposer.ComposeNewGroup(row.Uid, validated.Value), null, ct)
// → SaveChanges → LiftTombstone → Result.Success(new ContactGroupView(id, validated.Value, []))
```

(l'homonymie est permise — aucune unicité de nom, décision 17.)

`RenameAsync` / `AddMembersAsync` / `RemoveMembersAsync` — un seul gabarit privé, le chemin complet d'une écriture de carte (décision 20) :

```csharp
private async Task<Result> EditCardAsync(
    Guid userId, Guid groupId, Func<Contact, string?> edit, CancellationToken cancellationToken)
{
    var row = await context.Contacts.GroupCards()
        .FirstOrDefaultAsync(c => c.Id == groupId && c.UserId == userId, cancellationToken);
    if (row?.VCardRaw is null) return Result.Failure(ContactStore.NotFound);

    return await store.InTransactionAsync<Result>(async () =>
    {
        var rank = await sync.NextSequenceAsync(userId, cancellationToken);
        // Relu sous le verrou : c'est lui, pas un cardHash, qui fait du lire-modifier-écrire une
        // section critique — la course ne touche alors que la ligne en jeu (décision 20).
        if (!await ReloadAsync(row, cancellationToken) || row.VCardRaw is null)
            return Result.Failure(ContactStore.NotFound);

        var card = edit(row);
        if (card is null || card == row.VCardRaw) return Result.Success(); // rien à écrire : le
        // commit-prédicat d'InTransactionAsync committe un Success — le rollback n'est pas requis
        // ici, mais un no-op décidé AVANT la transaction est préférable : voir Add/Remove ci-dessous.

        await sync.ArchiveAsync(new ContactRevision
        {
            UserId = userId, ContactId = row.Id, Uid = row.Uid, DavName = row.DavName,
            CardHash = row.CardHash, VCardRaw = row.VCardRaw,
            Cause = RevisionCause.Webmail, ReplacedAt = DateTime.UtcNow
        }, cancellationToken);

        await store.ApplyCardAsync(row, card, null, cancellationToken);
        row.UpdatedAt = DateTime.UtcNow;
        row.SyncSequence = rank;
        await context.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }, cancellationToken);
}
```

- `RenameAsync` : `EditCardAsync(userId, groupId, r => VCardComposer.RenameGroup(r.VCardRaw!, validated.Value), ct)`.
- `AddMembersAsync` : résoudre d'abord les cibles **hors transaction** — `context.Contacts.Individuals().Where(c => c.UserId == userId && contactIds.Contains(c.Id)).Select(c => new { c.Id, c.Uid })` (l'id inconnu/groupe/autrui tombe ici : no-op) ; lire les `member_uid` déjà en table pour ce groupe ; le delta vide → `Result.Success()` sans transaction (pas de rang pour rien). Sinon `EditCardAsync` avec `edit = r => uids.Aggregate(r.VCardRaw!, VCardComposer.AddGroupMember)` en re-filtrant sous le verrou les uids que la carte relue porte déjà (projection de `r.VCardRaw` via `VCardProjector.Project`).
- `RemoveMembersAsync` : symétrique avec `RemoveGroupMember`.
- `DeleteAsync` : le squelette de `ContactStore.DeleteAsync` (find `GroupCards()`, transaction : rang → reload → révision `Delete` (`ContactId = null`) → `store.ClearProjectionAsync([id])` → `Remove` → tombe). La cascade `contact_group_members` (clé `group_id`) est dans le cycle depuis la tâche 3.

- [ ] **Step 4 : vert sur le store.**

- [ ] **Step 5 : tests contrôleur qui échouent** — les six routes (style `ContactsControllerTests`) :
  - `GET` → 200 `ContactGroupsResponse` (l'enveloppe, pas un tableau nu) ; `POST` → **200** + groupe entier ; `PUT {id}` / `DELETE {id}` / `POST|DELETE {id}/Members` → 204 ;
  - 400 : nom vide, nom > 255, plafond atteint (l'enveloppe de `POST /api/Contacts`), lot vide ou > 200 ;
  - 404 : id que le carnet ne porte pas (groupe d'autrui compris) — `NotFoundObjectResult` ;
  - **jamais** 409 (décision 20).

- [ ] **Step 6 : implémenter le contrôleur**

`ContactGroupsController(IContactGroupStore store) : ApiBaseController`, `[Route("api/[controller]")]`, `[Authorize]` — un contrôleur à part (454 lignes suffisent à l'autre). Six actions, `AuthenticatedUser.WebmailUid`, `MaxBatch = 200` (la constante de `ContactsController` : la déplacer en visibilité partagée ou la dupliquer avec un commentaire de renvoi — préférer une constante `internal const int MaxBatch` sur `ContactsController` référencée). Le lot de membres passe par le même gabarit `Refuse` (null/0/>200 → 400). `CreateAsync` en échec : `CapReached` → 400 ; `RenameAsync`/`DeleteAsync`/membres en échec `NotFound` → 404.

- [ ] **Step 7 : vert**, enregistrer le service, **Step 8 : commit** — `feat(contacts): group store and the six ContactGroups routes`

---

### Task 8 : supprimer un contact le retire de chaque groupe

**Files :**
- Modify : `src/snoopy.microservice/Repositories/ContactStore.cs` (`DeleteAsync`, `DeleteManyAsync`, + helper)
- Modify : `src/snoopy.microservice/Repositories/DavContactWriter.cs` (`DeleteAsync`)
- Test : `ContactStoreTests`, tests DAV write

**Interfaces :** `ContactStore.StripFromGroupsAsync(Guid userId, IReadOnlyList<string> uids, IReadOnlyCollection<Guid> dyingIds, ulong rank, CancellationToken)` (internal) — appelé **dans** la transaction des trois chemins, après l'archivage des contacts et avant `SaveChanges`.

- [ ] **Step 1 : tests qui échouent**
  - `DeleteAsync` : le `MEMBER` du contact sort de la carte du groupe (les deux formes de valeur : nu et préfixé), `contact_group_members` n'a plus la ligne, la carte du groupe prend le rang de la transaction et une révision.
  - `DavContactWriter.DeleteAsync` : même chose depuis le chemin DAV.
  - `DeleteManyAsync` **au-delà de cent contacts** (le test tient plus d'une tranche, sans quoi il ne prouve rien) : le vidage n'écrit jamais dans la carte d'un groupe que la liste emporte — l'exclusion se calcule sur les `ids` remis à la méthode, pas sur la tranche ; un groupe survivant dont deux tranches retirent des membres prend deux rangs.

- [ ] **Step 2 : vérifier l'échec.**

- [ ] **Step 3 : implémenter**

```csharp
/// <summary>
/// Décision 7: a deleted contact leaves every group that carries it, inside the deleting
/// transaction — the card must say what the book knows. Matches every value form the reader
/// accepts: bare UID or urn:uuid:-prefixed, either property name (RemoveGroupMember's rule).
/// Groups in <paramref name="dyingIds"/> are dying with this delete and are left alone: ranks
/// and revisions on condemned cards.
/// </summary>
internal async Task StripFromGroupsAsync(
    Guid userId, IReadOnlyList<string> uids, IReadOnlyCollection<Guid> dyingIds, ulong rank,
    CancellationToken cancellationToken)
{
    if (uids.Count == 0) return;

    var touched = await context.Contacts.GroupCards()
        .Where(g => g.UserId == userId && !dyingIds.Contains(g.Id)
            && context.ContactGroupMembers.Any(m => m.GroupId == g.Id && uids.Contains(m.MemberUid)))
        .ToListAsync(cancellationToken);

    foreach (var group in touched)
    {
        if (group.VCardRaw is null) continue;
        await sync.ArchiveAsync(new ContactRevision
        {
            UserId = userId, ContactId = group.Id, Uid = group.Uid, DavName = group.DavName,
            CardHash = group.CardHash, VCardRaw = group.VCardRaw,
            Cause = RevisionCause.Webmail, ReplacedAt = DateTime.UtcNow
        }, cancellationToken);

        var card = uids.Aggregate(group.VCardRaw, VCardComposer.RemoveGroupMember);
        await ApplyCardAsync(group, card, null, cancellationToken);
        group.UpdatedAt = DateTime.UtcNow;
        group.SyncSequence = rank;
    }
}
```

(Note : `uids.Contains(m.MemberUid)` attrape le membre stocké strippé — la projection strippe toujours (tâche 2), donc `member_uid` est la forme nue ; mais un contact dont `contacts.uid` vaut `urn:uuid:X` est référencé par `member_uid = X` : passer alors les **deux** formes dans `uids` — l'uid brut et sa forme strippée `VCardProjector.StripUrnUuid(uid)` — pour que la requête comme le retrait de lignes matchent, symétrique de la jointure de `ListAsync`.)

Appels :
- `DeleteAsync` : après `ClearProjectionAsync`/`Remove`, avant `SaveChanges` : `await StripFromGroupsAsync(userId, Forms(before.Uid), [contactId], rank, ct);` où `Forms(uid)` rend `[uid, StripUrnUuid(uid)]` distincts.
- `DeleteManyAsync` : dans chaque tranche, `dyingIds` = **la liste `ids` entière** (jamais la tranche) ; `uids` = les uids des lignes de la tranche.
- `DavContactWriter.DeleteAsync` : dans sa transaction, après `Remove`, `await store.StripFromGroupsAsync(userId, Forms(row.Uid), [row.Id], rank, ct);`.

- [ ] **Step 4 : vert**, **Step 5 : commit** — `feat(contacts): deleting a contact strips it from its groups`

---

### Task 9 : l'import `.vcf` projette les groupes comme tels

**Files :**
- Modify : `src/snoopy.microservice/Models/Contacts/ContactImportRow.cs` (champ `bool IsGroup = false`)
- Modify : `src/snoopy.microservice/Services/Contacts/VCardImportMapper.cs`
- Modify : `src/snoopy.microservice/Repositories/ContactStore.cs` (`ImportAsync`/`ImportBatchAsync`)
- Test : `ContactStoreImportTests` (ou équivalent existant), `VCardImportMapperTests`

- [ ] **Step 1 : tests qui échouent** (décision 19) :
  - une carte de groupe importée par `.vcf` arrive en `Kind = group`, membres projetés ; un `MEMBER` dont la carte du membre suit **dans le même fichier** se résout (l'ordre du fichier est indifférent — la résolution est une jointure, pas un état d'import) ;
  - une carte **sans adresse** nommée « Amis » placée **après** la carte du groupe « Amis » dans le même `.vcf` crée une fiche au lieu d'entrer dans le groupe (l'index tenu en cours de lecture écarte les groupes) ;
  - une carte de groupe dont l'UID appartient déjà à une **fiche** — et l'inverse — est comptée `failed` avec une raison, jamais fusionnée ;
  - une carte de groupe à l'UID inconnu se crée, toujours — jamais résolue par nom.

- [ ] **Step 2 : vérifier l'échec.**

- [ ] **Step 3 : implémenter**

- `VCardImportMapper.Map` : `IsGroup = projection.Kind == ContactKinds.Group` (dernier argument) ; pour un groupe, ne pas dériver le nickname du DisplayName (le nom du groupe n'est pas un surnom de fiche) — laisser `nickname = projection.Nickname`.
- `ImportAsync` : la requête `uidOwners` sélectionne aussi `c.Kind` → `Dictionary<string, (Guid Id, string Kind)>` ; adapter `Adopt` et les lectures existantes (`byUid` devient `.Id`).
- `ImportBatchAsync`, dans la boucle des lignes, **avant** la résolution actuelle :

```csharp
if (row.IsGroup)
{
    // Une ligne de groupe entrante ne se résout que par UID, jamais par nom ni par adresse
    // (décision 19). L'UID de l'autre espèce ne tranche pas : refusée et comptée.
    if (row.Uid != null && uidOwners.TryGetValue(row.Uid, out var owner))
    {
        if (owner.Kind != ContactKinds.Group)
        { failed++; errors.Add(new ContactImportError(row.Line, CrossSpeciesUid)); continue; }

        // Groupe déjà connu par UID : la carte entrante le remplace via pending — l'idempotence
        // de la re-importation ; la fusion de fiches ne s'applique jamais aux groupes.
        var known = await context.Contacts
            .FirstOrDefaultAsync(c => c.Id == owner.Id, cancellationToken);
        if (known != null) { pending[known.Id] = new PendingCard(known, row.Line, row.VCard!); merged++; }
        continue;
    }

    if (stored + created >= MaxPerUser)
    { skipped++; errors.Add(new ContactImportError(row.Line, CapReached)); continue; }

    // Création : la mécanique de la fiche née d'un .vcf, SANS Register ni Index — un groupe
    // n'entre dans aucun index de fusion, ni comme cible ni comme entrant (décision 19).
    var groupId = Guid.NewGuid();
    var group = new Contact
    {
        Id = groupId, UserId = userId, Uid = row.Uid ?? groupId.ToString(),
        Source = "imported", UpdatedAt = DateTime.UtcNow
    };
    context.Contacts.Add(group);
    born[groupId] = group;
    pending[groupId] = new PendingCard(group, row.Line, row.VCard!); // toujours non-null : un CSV
    uidOwners[group.Uid] = (groupId, ContactKinds.Group);            // ne décrit jamais un groupe
    created++;
    continue;
}
// Une fiche dont l'UID appartient à un groupe : le miroir du refus ci-dessus.
if (row.Uid != null && uidOwners.TryGetValue(row.Uid, out var held2) && held2.Kind == ContactKinds.Group)
{ failed++; errors.Add(new ContactImportError(row.Line, CrossSpeciesUid)); continue; }
```

avec `internal const string CrossSpeciesUid = "This row's UID already belongs to the other kind of card";` sur `ContactStore`. Et la seconde moitié du mécanisme (décision 19) : le `if (kept.Count == 0) Index(named, …)` de la boucle des fiches n'est jamais atteint par un groupe (le `continue` ci-dessus), et `uidOwners` enregistre l'espèce — sa valeur devient `(Guid Id, string Kind)` partout (les fiches y entrent avec `ContactKinds.Individual`).

- [ ] **Step 4 : vert**, **Step 5 : commit** — `feat(contacts): .vcf import projects group cards as groups`

---

### Task 10 : frontend — API, modèle, hooks, invalidations croisées

**Files :**
- Modify : `src/frontend/src/api.js`
- Create : `src/frontend/src/modules/contacts/contactGroupTypes.ts`
- Modify : `src/frontend/src/modules/contacts/queries.ts`
- Test : `src/frontend/src/modules/contacts/queries.test.ts` (à créer si absent — sinon tester via les composants des tâches 11-13)

**Interfaces :**

```ts
// contactGroupTypes.ts
/** One group as GET /api/ContactGroups answers it. memberIds only carries resolved members, so
    counters, list filtering, chips and the composer's expansion all read one truth. */
export interface ContactGroup {
  id: string
  name: string
  memberIds: string[]
}
export interface ContactGroupsResponse { groups: ContactGroup[] }
```

- [ ] **Step 1 : api.js** — six appels à côté des onze de Contacts :

```js
getContactGroups: () => request('GET', '/api/ContactGroups'),
createContactGroup: (name) => request('POST', '/api/ContactGroups', { name }),
renameContactGroup: (id, name) => request('PUT', `/api/ContactGroups/${id}`, { name }),
deleteContactGroup: (id) => request('DELETE', `/api/ContactGroups/${id}`),
addContactGroupMembers: (id, contactIds) =>
  request('POST', `/api/ContactGroups/${id}/Members`, { contactIds }),
removeContactGroupMembers: (id, contactIds) =>
  request('DELETE', `/api/ContactGroups/${id}/Members`, { contactIds }),
```

- [ ] **Step 2 : queries.ts**

```ts
export const contactGroupKeys = {
  all: (accountId: string) => ['contactGroups', accountId] as const,
}

export function useContactGroups(enabled = true) {
  const accountId = useAccountId()
  return useQuery({
    queryKey: contactGroupKeys.all(accountId),
    queryFn: () => api.getContactGroups() as Promise<ContactGroupsResponse>,
    staleTime: 5 * 60_000,
    select: data => data.groups,
    enabled,
  })
}

/** Group writes invalidate both keys — the group count on a contact changes its chips.
    onSettled, never onSuccess. */
function useContactGroupMutation<TArgs, TResult = unknown>(
  mutationFn: (args: TArgs) => Promise<TResult>,
) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: contactGroupKeys.all(accountId) })
      queryClient.invalidateQueries({ queryKey: contactKeys.all(accountId) })
    },
  })
}

export function useCreateContactGroup() {
  return useContactGroupMutation((name: string) =>
    api.createContactGroup(name) as Promise<ContactGroup>)
}
export function useRenameContactGroup() {
  return useContactGroupMutation(({ id, name }: { id: string; name: string }) =>
    api.renameContactGroup(id, name))
}
export function useDeleteContactGroup() {
  return useContactGroupMutation((id: string) => api.deleteContactGroup(id))
}
export function useAddContactGroupMembers() {
  return useContactGroupMutation(({ id, contactIds }: { id: string; contactIds: string[] }) =>
    api.addContactGroupMembers(id, contactIds))
}
export function useRemoveContactGroupMembers() {
  return useContactGroupMutation(({ id, contactIds }: { id: string; contactIds: string[] }) =>
    api.removeContactGroupMembers(id, contactIds))
}
```

Et la symétrie (spec, § API) : `useDeleteContact`, `useDeleteContacts` et `useImportContacts` invalident **aussi** `contactGroupKeys.all` — leur `onSettled` passe par un helper commun `invalidateBook(queryClient, accountId)` qui invalide les deux clés ; les autres mutations de contact gardent la seule clé `contacts`.

- [ ] **Step 3 : vert** — `cd src/frontend && npx vitest run` (typecheck via le build des tâches suivantes).

- [ ] **Step 4 : commit** — `feat(contacts): group API calls, model and hooks`

---

### Task 11 : frontend — la bande (section Groupes), le scope `group:`, le drop

**Files :**
- Modify : `src/frontend/src/modules/contacts/ContactScopes.tsx`
- Create : `src/frontend/src/modules/contacts/GroupNameModal.tsx`
- Modify : `src/frontend/src/modules/contacts/ContactsLayout.tsx`
- Modify : `src/frontend/src/index.css` (rangées de groupe, en réutilisant les classes `contact-scope`)
- Modify : `src/frontend/src/locales/{en,fr}/contacts.json`
- Test : `ContactScopes.test.tsx`, `ContactsLayout.test.tsx`, `GroupNameModal.test.tsx`

**Interfaces :**
- `export type ContactScope = 'all' | 'favorites' | \`group:${string}\`` (dans `ContactScopes.tsx`) ; helper `export function groupIdOf(scope: ContactScope): string | null` (le GUID après `group:`, sinon null).
- `ContactScopes` gagne : `groups: ContactGroup[]`, `onCreateGroup: () => void`, `onGroupMenu`-callbacks (`onRenameGroup(g)`, `onDeleteGroup(g)`, `onWriteToGroup(g)` — désactivé quand le groupe n'offre aucune adresse, la valeur vient du parent).
- `GroupNameModal({ title, initialName, saving, onSubmit, onClose })` — création et renommage, même champ, même validation (nom non vide, ≤ 255), deux titres (décision 13).

- [ ] **Step 1 : tests qui échouent**
  - `canDropIntoScope('group:abc')` est vrai (elle l'est déjà par construction — le test l'épingle) ;
  - la bande rend une section *Groups* avec un « + » sur son en-tête (**pas** dans `.column-actions` — décision 13), une rangée par groupe avec son compteur de membres, et un `DropdownMenu` (Rename / Delete / Write to group) ;
  - le drop d'un payload sur une rangée de groupe appelle `onDropContacts('group:<id>', payload)` ;
  - `ContactsLayout` : un scope `group:<guid>` filtre la liste sur `memberIds` ; un scope qui ne résout plus (groupe supprimé, GUID étranger) se replie sur `all` ; le drop sur un groupe **ajoute et ne retire jamais** (mutation `addMembers`, jamais `removeMembers`) ;
  - la suppression d'un groupe passe par `DeleteConfirmModal` avec un message disant que les contacts restent.

- [ ] **Step 2 : vérifier l'échec.**

- [ ] **Step 3 : implémenter**

`ContactScopes` : sous les deux rangées existantes, une section :

```tsx
<div className="contact-scopes-groups-header">
  <span>{t('groups.title')}</span>
  <button type="button" className="contact-scopes-add" aria-label={t('groups.add')}
    onClick={onCreateGroup}>+</button>
</div>
{groups.map(group => (
  <div key={group.id} className="contact-scope-row">
    <ScopeRow scope={`group:${group.id}`} active={scope === `group:${group.id}`}
      icon={<PeopleIcon size={15} />} label={group.name} count={group.memberIds.length}
      dropLabel={t('scopes.dropHere')} onScope={onScope} onDropContacts={onDropContacts} />
    <DropdownMenu ariaLabel={t('groups.menu', { name: group.name })} trigger={<KebabIcon size={14} />}
      items={[
        { label: t('groups.rename'), onSelect: () => onRenameGroup(group) },
        { label: t('groups.write'), onSelect: () => onWriteToGroup(group),
          disabled: !groupHasAddresses(group) },
        'separator',
        { label: t('groups.delete'), onSelect: () => onDeleteGroup(group) },
      ]} />
  </div>
))}
```

(`groupHasAddresses` est passé par le parent — voir tâche 13 pour la source ; les icônes réutilisent celles du projet, en créer une `PeopleIcon` seulement si aucune ne convient. `ScopeRow` inchangée : `canDropIntoScope` accepte déjà tout sauf `all`.)

`ContactsLayout` :
- parsing : `const raw = params.get('scope'); const scope: ContactScope = raw === 'favorites' ? 'favorites' : raw?.startsWith('group:') ? (raw as ContactScope) : 'all'` ;
- les sept `'favorites'` en dur : partout où le code demande « le scope se conserve-t-il dans l'URL ? », la règle devient `scope !== 'all'` — écrire un helper `paramsForScope(scope, extra?)` utilisé par `changeScope`, `select`, `backToList`, `confirmDelete` ;
- `const groups = useContactGroups()` ; `const openGroup = groupIdOf(scope) ? groups.data?.find(g => g.id === groupIdOf(scope)) ?? null : null` ;
- repli : `useEffect` — si `groupIdOf(scope) != null && groups.data != null && openGroup == null`, `setParams({}, { replace: true })` (le repli sur `all`) ;
- filtre : `const scoped = (contacts ?? []).filter(c => scope === 'favorites' ? c.isFavorite : openGroup ? openGroup.memberIds.includes(c.id) : true)` (un `Set` si le groupe est grand) ;
- drop : `dropOnScope` route sur la cible — `favorites` inchangé ; `group:` → `addMembers.mutate({ id, contactIds: payload.ids }, { onError: … })` ;
- `writeTo` s'élargit : `(addresses: string | string[]) => navigate('/mail/compose', { state: { seed: newMessageSeed(Array.isArray(addresses) ? addresses : [addresses]), backTo: backToHere() } })` où `backToHere()` rend `/contacts?scope=group:<id>` dans un scope de groupe, `?id=` sinon (décision 16) ;
- état des modales : `groupModal: { mode: 'create' } | { mode: 'rename'; group: ContactGroup } | null` → `GroupNameModal` ; `pendingGroupDelete: ContactGroup | null` → `DeleteConfirmModal` avec `entityLabel={group.name}` et un corps `t('groups.deleteBody')` (« les contacts, eux, restent »).

`GroupNameModal` : le gabarit du modal de conflit du fichier (overlay + `modal`), un champ, submit désactivé si vide/inchangé, `maxLength={255}`.

Locales : section `groups` dans `contacts.json` (en/fr) — `title`, `add`, `menu`, `rename`, `write`, `delete`, `deleteBody`, `created`, `renamed`, `deleted`, `renameTitle`, `createTitle`, `nameLabel`, plus les erreurs. **Ne pas toucher `.column-actions`** ni sa mesure (décision 13).

- [ ] **Step 4 : vert**, **Step 5 : commit** — `feat(contacts): groups band section, group scope and drop`

---

### Task 12 : frontend — « Retirer du groupe » et les puces de la fiche

**Files :**
- Modify : `src/frontend/src/modules/contacts/ContactList.tsx`
- Modify : `src/frontend/src/modules/contacts/ContactCard.tsx`
- Modify : `src/frontend/src/modules/contacts/ContactsLayout.tsx` (câblage)
- Modify : `src/frontend/src/locales/{en,fr}/contacts.json`, `src/frontend/src/index.css`
- Test : `ContactList.test.tsx`, `ContactCard.test.tsx`

- [ ] **Step 1 : tests qui échouent**
  - dans un scope de groupe, la bande de sélection porte **« Remove from group »** à côté de **« Delete »**, qui garde son dialogue — les deux libellés distincts (décision 14) ; hors scope de groupe, pas de bouton de retrait ;
  - « Retirer » appelle `onRemoveFromGroup(selectedIds)` et vide la sélection, sans dialogue (l'appartenance se remet d'un drop — pas une perte de données) ;
  - la fiche liste les groupes du contact en puces avec un ×, chaque × appelant `onRemoveFromGroup(groupId)`.

- [ ] **Step 2 : vérifier l'échec.**

- [ ] **Step 3 : implémenter**

`ContactList` : props `+ onRemoveFromGroup?: (ids: string[]) => void` ; dans la `SelectionBand`, avant le bouton Delete :

```tsx
{onRemoveFromGroup && (
  <button type="button" className="selection-btn"
    aria-label={t('list.removeFromGroup')} title={t('list.removeFromGroup')}
    disabled={count === 0}
    onClick={() => { onRemoveFromGroup(selectedIds); selection.clear() }}>
    <PersonMinusIcon size={20} />
  </button>
)}
```

`ContactCard` : props `+ groups?: { id: string; name: string }[]` et `+ onRemoveFromGroup?: (groupId: string) => void` ; sous l'en-tête, quand `groups` est non vide :

```tsx
<div className="contact-card-groups">
  {groups.map(g => (
    <span key={g.id} className="contact-group-chip">
      {g.name}
      <button type="button" aria-label={t('card.removeFromGroup', { name: g.name })}
        onClick={() => onRemoveFromGroup?.(g.id)}>✕</button>
    </span>
  ))}
</div>
```

`ContactsLayout` : `groupsOf(contactId)` = `groups.data?.filter(g => g.memberIds.includes(contactId))` ; câbler `onRemoveFromGroup` de la liste (`removeMembers.mutate({ id: openGroup.id, contactIds })`) et de la fiche (`removeMembers.mutate({ id: groupId, contactIds: [selected.id] })`), toasts d'erreur via `addToast`.

- [ ] **Step 4 : vert**, **Step 5 : commit** — `feat(contacts): remove-from-group band action and card chips`

---

### Task 13 : frontend — la ligne de groupe du composeur et « Écrire au groupe »

**Files :**
- Modify : `src/frontend/src/modules/contacts/contactSearch.ts`
- Modify : `src/frontend/src/modules/mail/compose/RecipientsField.tsx`
- Modify : `src/frontend/src/modules/mail/compose/ComposeView.tsx`
- Modify : `src/frontend/src/modules/contacts/ContactsLayout.tsx` (« Écrire au groupe »)
- Modify : `src/frontend/src/locales/{en,fr}/compose.json`
- Test : `contactSearch.test.ts`, `RecipientsField.test.tsx` (créer si absent), `ContactsLayout.test.tsx`

**Interfaces (contactSearch.ts) :**

```ts
export interface GroupOption { id: string; name: string; memberCount: number; addresses: string[] }
export type ComposerSuggestion =
  | ({ kind: 'address' } & AddressSuggestion)
  | ({ kind: 'group' } & GroupOption)
const GROUP_LIMIT = 3

/** Resolved member primary addresses of every group, deduplicated — computed once by the caller
    so the field and the band read one truth. */
export function groupOptionsOf(groups: ContactGroup[], contacts: Contact[]): GroupOption[]
```

`suggestionsFor` prend un paramètre optionnel `groups: GroupOption[] = []` et rend `ComposerSuggestion[]` : les groupes matchés sur le nom replié, plafonnés à `GROUP_LIMIT` **avant** la fusion, rangés **avant** les adresses ; un groupe dont `addresses` non vide est entièrement couvert par `exclude` **ne paraît pas** ; un groupe dont `addresses` est vide paraît (c'est le cas du toast). Les dix places des adresses restent les dix places des adresses (décision 15).

- [ ] **Step 1 : tests qui échouent** (contactSearch)
  - une requête matchant un groupe le range en tête, avec `memberCount` ;
  - budget : 4 groupes matchés → 3 lignes de groupe **et toujours** jusqu'à 10 adresses ;
  - un groupe dont tous les membres sont dans `exclude` ne paraît pas — donc ne déclenchera pas le toast ;
  - un groupe sans adresse à offrir (aucun membre résolu, ou aucun membre porteur d'adresse) paraît.

- [ ] **Step 2 : implémenter contactSearch** — `groupOptionsOf` résout `memberIds → contacts → primaryAddressOf`, dédoublonne (clé repliée) ; `suggestionsFor` construit les lignes d'adresses comme aujourd'hui puis :

```ts
const groupRows: ComposerSuggestion[] = groups
  .filter(g => fold(g.name).includes(needle))
  .filter(g => g.addresses.length === 0
    || g.addresses.some(a => !excludeKeys?.has(fold(a.trim()))))
  .slice(0, GROUP_LIMIT)
  .map(g => ({ kind: 'group' as const, ...g }))
return [...groupRows, ...addressRows.map(r => ({ kind: 'address' as const, ...r }))]
```

- [ ] **Step 3 : RecipientsField** — props `+ groups?: GroupOption[]` et `+ onEmptyGroup?: (name: string) => void` ; `suggestions` devient `ComposerSuggestion[]` ; le rendu d'une ligne de groupe :

```tsx
<span className="suggestion-names">{suggestion.name}</span>
<span className="suggestion-address">{t('recipients.groupMembers', { count: suggestion.memberCount })}</span>
```

`commit` se dédouble : `commitSuggestion(s)` — pour `kind === 'group'`, développer :

```ts
const fresh = s.addresses.filter(a => !tokens.some(tk => fold(tk.trim()) === fold(a.trim())))
if (fresh.length > 0) onChange([...tokens, ...fresh])
else onEmptyGroup?.(s.name)  // jamais inséré en silence (décision 15)
reset()
```

la flèche et Entrée parcourent la liste unifiée (`suggestions[active]` quel que soit son `kind`) ; le clavier existant ne change pas de forme.

- [ ] **Step 4 : ComposeView** — `useContactGroups()` + `groupOptionsOf(groups, contacts)` (mémoïsé), passés aux trois `RecipientsField` (To/Cc/Bcc), avec `onEmptyGroup={name => onNotify(t('toast.emptyGroup', { name }), 'error')}` — la voie que toutes les annonces empruntent (décision 15). Locales `compose.json` : `recipients.groupMembers` (« {{count}} members » / « {{count}} membres »), `toast.emptyGroup`.

- [ ] **Step 5 : « Écrire au groupe »** (décision 16) — dans `ContactsLayout`, `onWriteToGroup(group)` appelle `writeTo(groupOptionsOf([group], contacts)[0].addresses)` ; l'entrée du menu est désactivée quand `addresses.length === 0` (le cas vide se traite en amont). `groupHasAddresses` de la tâche 11 lit la même source.

- [ ] **Step 6 : tests RecipientsField/Layout** — la ligne de groupe rangée avant les adresses, atteinte à la flèche, développée en N jetons dédoublonnés contre ceux posés, le groupe sans adresse qui n'en pose aucun mais fait remonter le toast, et « Écrire au groupe » qui navigue avec les adresses principales + `backTo` du scope.

- [ ] **Step 7 : vert**, **Step 8 : commit** — `feat(compose): group suggestion row and write-to-group`

---

### Task 14 : conformité, mesure DAVx⁵, documents

**Files :**
- Modify : `docs/superpowers/carddav-4d-conformance.md` (section 5)
- Modify : `docs/superpowers/webmail-contacts-tables.md` (si un écart est apparu en route)

- [ ] **Step 1 : le scénario client** — ajouter à la section 5 le tableau de la spec (§ Tests, fin) : groupe créé au webmail vu sur le téléphone ; créé sur le téléphone vu au webmail ; membre ajouté dans chaque sens ; supprimé dans chaque sens. **Le tableau nomme son client par ligne** : DAVx⁵ joue tout ; Thunderbird ne mappe pas les groupes (limite du client, pas un défaut) ; l'app Contacts d'iOS crée des listes depuis iOS 16 — « créé sur le téléphone » se joue sur DAVx⁵ comme sur un iPhone ; rien d'Apple n'ayant encore été observé sur ce serveur, la ligne Apple **reste ouverte** plutôt que cochée.

- [ ] **Step 2 : la mesure de la décision 8** — consigner le réglage *Contact group method* que DAVx⁵ propose par défaut à la création du compte (relevé par l'utilisateur — voir Global constraints), et la conséquence : si *separate vCards*, la tranche marche d'origine ; si *categories*, la note de version doit dire le réglage à changer.

- [ ] **Step 3 : commit** — `docs(carddav): group conformance scenario and DAVx5 default`

---

## Self-review (fait à l'écriture du plan)

- **Couverture** : décisions 1→4 (tâches 1-4), 5 (2 et 6), 6 (5), 7 (7-fin et 8), 8 (Global constraints + 14), 9 (2, 7), 10-11 (7), 12 (11), 13 (11, 12), 14 (12), 15 (13), 16 (13), 17 (5, 7), 18 (4, 7), 19 (9), 20 (7) ; schéma (1) ; API (7, 10) ; tests de la spec répartis dans les tâches correspondantes ; scénario client (14).
- **Types** : `ContactKinds`/`Individuals()`/`GroupCards()` (tâche 1) sont consommés tels quels en 4, 7, 8, 9 ; `ProjectedMember`/`StripUrnUuid` (2) en 3, 7, 8 ; `ComposeNewGroup`/`AddGroupMember`/`RemoveGroupMember`/`RenameGroup` (5) en 7, 8 ; `ContactGroupView` (7) en 10 (`ContactGroup` TS) ; `GroupOption`/`groupOptionsOf` (13) en 11 via le parent.
- **Hors périmètre** (ne pas implémenter) : mode `CATEGORIES`, résolution des groupes imbriqués, carnets multiples, appartenance dans le CSV et l'export, groupe destinataire persistant.
