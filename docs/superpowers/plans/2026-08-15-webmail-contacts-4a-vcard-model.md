# Contacts 4a — modèle complet et moteur vCard : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Les rapports de sous-agents dans le scratchpad sont préfixés `4a-task-N-…`.

**Spec :** [`docs/superpowers/specs/2026-08-14-webmail-contacts-4a-vcard-model-design.md`](../specs/2026-08-14-webmail-contacts-4a-vcard-model-design.md) — toute décision citée ici (« décision N ») y renvoie. En cas de doute, la spec fait foi.

**Goal :** rendre `vcard_raw` souverain — colonnes et tables filles deviennent une projection recalculée à chaque écriture — avec un moteur vCard complet (découpeur, projecteur, composeur), l'import `.vcf`, et le rattrapage des fiches existantes.

**Architecture :** quatre composants purs dans `Services/Contacts/` (`VCardSplitter`, `VCardProjector`, `VCardComposer`, `VCardImportMapper`), un `ContactStore` réécrit autour du cycle « composer → hasher → projeter », trois tables filles nouvelles ou refondues clés sur `(contact_id, position)`, et un endpoint admin de rattrapage par lots.

**Tech stack :** .NET 10, EF Core (Pomelo MySQL, InMemory pour les tests), xUnit 2.9.3, Moq, **FolkerKinzel.VCards 8.2.0** (nouvelle dépendance, MIT).

## Global constraints

- `cd src && dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés) ; build : `cd src && dotnet build`.
- Les tests repository tournent sur EF InMemory (`TestDbContext`), qui n'applique **aucune** FK ni longueur de colonne : toute règle d'intégrité doit être portée par le code, pas par le schéma.
- `Assert.IsType<T>` vérifie le type exact : `BadRequestObjectResult` pour `BadRequest(body)`, jamais `ObjectResult`.
- `ApiDocumentation.xml` : ne committer que les membres réellement touchés ; réverter la dérive massive que `dotnet test` régénère.
- Style : file-scoped namespaces, un type par fichier, records pour les DTO, `sealed`, `internal` par défaut, primary constructors, cancellation tokens partout, ILogger structuré.
- Commits : concis (2 lignes max), jamais commencer/finir par `@`, terminer par `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Aucun écran ne change : le frontend actuel continue d'envoyer `addresses: string[]` — la compatibilité du contrat `POST`/`PUT` est une exigence, pas une option (tâche 2).
- Les blocs SQL sont **joués à la main** par l'utilisateur sur `snoopy_webmail` et `snoopy_webmail_dev` — l'ingénieur amende le document de prérequis, il n'exécute pas de SQL.
- API FolkerKinzel : les noms de membres exacts (v8) se vérifient sur le paquet installé / la doc du dépôt avant usage ; les tests de comportement (entrée `.vcf` → sortie attendue) sont le contrat, jamais un nom de membre supposé.

---

### Task 1 : socle — dépendance, entités EF, DbContext, document de prérequis SQL

**Files :**
- Modify : `src/snoopy.microservice/snoopy.microservice.core.csproj` (PackageReference)
- Modify : `src/snoopy.microservice/Data/Preferences/Contact.cs`
- Modify : `src/snoopy.microservice/Data/Preferences/ContactEmail.cs`
- Create : `src/snoopy.microservice/Data/Preferences/ContactPhone.cs`
- Create : `src/snoopy.microservice/Data/Preferences/ContactAddress.cs`
- Create : `src/snoopy.microservice/Data/Preferences/ContactPhoto.cs`
- Modify : `src/snoopy.microservice/Data/Preferences/PreferencesDbContext.cs`
- Modify : `docs/superpowers/webmail-contacts-tables.md`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Data/ContactEntitiesTests.cs`

**Interfaces (produit pour les tâches suivantes) :**
- `Contact` gagne : `DisplayName`, `MiddleName`, `NamePrefix`, `NameSuffix`, `Organization`, `Department`, `JobTitle`, `Birthday`, `Website`, `Notes` (tous `string?`) et `CardHash` (`string`, défaut `""`).
- `ContactEmail` gagne : `Type` (`string`, défaut `""`), `Pref` (`int`, défaut `101`), `Params` (`string`, `""`), `GroupName` (`string`, `""`). Sa clé EF devient `(ContactId, Position)`.
- `ContactPhone(ContactId, Position, Number, Type, Pref, Params, GroupName)`, `ContactAddress(ContactId, Position, Type, Pref, Params, GroupName, PoBox, Extended, Street, Locality, Region, PostalCode, Country)`, `ContactPhoto(ContactId, MediaType, Bytes)` — mêmes conventions `[Table]`/`[Column]` snake_case que `ContactEmail`.
- `PreferencesDbContext` expose `ContactPhones`, `ContactAddresses`, `ContactPhotos`.

- [ ] **Step 1 : ajouter la dépendance**

Dans le csproj, bloc `<ItemGroup>` des PackageReference :

```xml
<PackageReference Include="FolkerKinzel.VCards" Version="8.2.0" />
```

`cd src && dotnet build` doit passer.

- [ ] **Step 2 : écrire les tests d'entités qui échouent**

Étendre `ContactEntitiesTests` (suivre le style existant du fichier) :

```csharp
[Fact]
public void Contact_CarriesTheProjectionColumns()
{
    var contact = new Contact { DisplayName = "Dr. John Smith Jr.", Birthday = "--0315", CardHash = "" };
    Assert.Equal("--0315", contact.Birthday);
    Assert.Equal(string.Empty, contact.CardHash); // '' = pas encore calculé, jamais null
}

[Fact]
public async Task ContactEmail_KeyIsContactIdAndPosition()
{
    using var db = TestDbContext.Create();
    var id = Guid.NewGuid();
    // La même adresse deux fois sous deux TYPE : légal sous la nouvelle clé (spec, § Schéma).
    db.ContactEmails.Add(new ContactEmail { ContactId = id, Position = 0, Address = "a@b.c", Type = "HOME" });
    db.ContactEmails.Add(new ContactEmail { ContactId = id, Position = 1, Address = "a@b.c", Type = "WORK" });
    await db.SaveChangesAsync();
    Assert.Equal(2, await db.ContactEmails.CountAsync());
}

[Fact]
public async Task ChildTables_RoundTrip()
{
    using var db = TestDbContext.Create();
    var id = Guid.NewGuid();
    db.ContactPhones.Add(new ContactPhone { ContactId = id, Position = 0, Number = "+3221234567", Pref = 101 });
    db.ContactAddresses.Add(new ContactAddress { ContactId = id, Position = 0, Street = "Rue Haute 1", Locality = "Bruxelles" });
    db.ContactPhotos.Add(new ContactPhoto { ContactId = id, MediaType = "image/jpeg", Bytes = [1, 2, 3] });
    await db.SaveChangesAsync();
    Assert.Single(await db.ContactPhones.ToListAsync());
}
```

Le premier test précis sur la clé : insérer deux `ContactEmail` de même `(ContactId, Address)` mais positions distinctes doit **réussir** — sous l'ancienne clé EF `(ContactId, Address)` l'InMemory le refuse, c'est le rouge attendu.

- [ ] **Step 3 : vérifier l'échec** — `cd src && dotnet test` : compilation en échec (propriétés absentes), puis clé.

- [ ] **Step 4 : implémenter les entités et le DbContext**

Colonnes `Contact` (attributs `[Column("display_name")]` etc., dans l'ordre du bloc SQL de la spec). `ContactEmail` : retirer le commentaire « position 0 is the primary address by definition » du doc-comment (faux depuis la décision 5 bis — l'ordre d'affichage sort de `(pref, position)`). Dans `PreferencesDbContext.OnModelCreating` :

```csharp
modelBuilder.Entity<ContactEmail>().HasKey(e => new { e.ContactId, e.Position });
modelBuilder.Entity<ContactPhone>().HasKey(p => new { p.ContactId, p.Position });
modelBuilder.Entity<ContactAddress>().HasKey(a => new { a.ContactId, a.Position });
modelBuilder.Entity<ContactPhoto>().HasKey(p => p.ContactId);
```

et pour chacune des trois nouvelles tables, l'arête sans navigation vers `Contact`, sur le modèle exact du bloc `ContactEmail -> Contact` existant (même commentaire d'intention : sans arête, EF ordonne les INSERT par nom de table et l'InMemory ne peut pas l'attraper) :

```csharp
modelBuilder.Entity<ContactPhone>()
    .HasOne<Contact>().WithMany().HasForeignKey(p => p.ContactId).OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 5 : vérifier le vert** — `cd src && dotnet test`.

- [ ] **Step 6 : amender le document de prérequis**

Dans `docs/superpowers/webmail-contacts-tables.md` :
1. Ajouter une section « Ajout de la tranche 4a » portant **verbatim** les blocs SQL du § Schéma de la spec (l'`ALTER contacts`, la requête de contrôle, l'`ALTER contact_emails`, les trois `CREATE TABLE`), avec la phrase d'ordre : tables d'abord, backend ensuite, rattrapage en dernier.
2. Amender « Pourquoi `updated_at` est géré par le schéma » : la décision 9 la détrône — `card_hash` est la base de l'ETag ; `updated_at` reste un simple témoin.
3. Réécrire le commentaire de `contact_emails.position` : rang de la propriété dans la carte, l'ordre d'affichage sort de `(pref, position)`.

- [ ] **Step 7 : commit**

```bash
git add -A && git commit -m "feat(contacts): 4a schema groundwork - entities, keys, FolkerKinzel dependency"
```

---

### Task 2 : modèles étendus et `ContactValidator`

**Files :**
- Modify : `src/snoopy.microservice/Models/Contacts/ContactWrite.cs`
- Modify : `src/snoopy.microservice/Models/Contacts/ContactRequest.cs`
- Modify : `src/snoopy.microservice/Models/Contacts/ContactView.cs`
- Create : `src/snoopy.microservice/Models/Contacts/ContactDetail.cs`
- Create : `src/snoopy.microservice/Models/Contacts/ContactLine.cs`
- Create : `src/snoopy.microservice/Models/Contacts/ContactLineJsonConverter.cs`
- Modify : `src/snoopy.microservice/Services/ContactValidator.cs`
- Modify (appelants cassés par les records étendus) : `Controllers/ContactsController.cs`, `Repositories/ContactStore.cs`, `Services/Contacts/ContactCsvExporter.cs` — adaptation minimale pour compiler, la refonte réelle vient aux tâches 5–7.
- Test : `snoopy.microservice.Tests/Services/ContactValidatorTests.cs`, `snoopy.microservice.Tests/Models/ContactLineJsonConverterTests.cs` (create)

**Interfaces (produit) :**

```csharp
// Une ligne fille en écriture. Position null = ligne neuve (décision 4).
public sealed record ContactWriteEmail(int? Position, string Address, string Type);
public sealed record ContactWritePhone(int? Position, string Number, string Type);
public sealed record ContactWriteAddress(
    int? Position, string Type, string? PoBox, string? Extended, string? Street,
    string? Locality, string? Region, string? PostalCode, string? Country);

public sealed record ContactWrite(
    string? FirstName, string? LastName, string? Nickname,
    string? DisplayName, string? MiddleName, string? NamePrefix, string? NameSuffix,
    string? Organization, string? Department, string? JobTitle,
    string? Birthday, string? Website, string? Notes,
    bool IsFavorite,
    IReadOnlyList<ContactWriteEmail> Addresses,
    IReadOnlyList<ContactWritePhone> Phones,
    IReadOnlyList<ContactWriteAddress> PostalAddresses,
    string Source);

// Une ligne fille en lecture (GET /{id}) — params et group_name sortent, n'entrent jamais.
public sealed record ContactDetailEmail(int Position, string Address, string Type, int Pref, string Params, string GroupName);
public sealed record ContactDetailPhone(int Position, string Number, string Type, int Pref, string Params, string GroupName);
public sealed record ContactDetailAddress(
    int Position, string Type, int Pref, string Params, string GroupName,
    string? PoBox, string? Extended, string? Street, string? Locality, string? Region,
    string? PostalCode, string? Country);

public sealed record ContactDetail(
    Guid Id, string? FirstName, string? LastName, string? Nickname,
    string? DisplayName, string? MiddleName, string? NamePrefix, string? NameSuffix,
    string? Organization, string? Department, string? JobTitle,
    string? Birthday, string? Website, string? Notes,
    bool IsFavorite, bool HasPhoto,
    IReadOnlyList<ContactDetailEmail> Addresses,
    IReadOnlyList<ContactDetailPhone> Phones,
    IReadOnlyList<ContactDetailAddress> PostalAddresses);
```

`ContactView` (liste) gagne `string? DisplayName` et `bool HasPhoto` ; ses `Addresses` restent `IReadOnlyList<string>` (dédoublonnées, triées `(pref, position)` — tâche 5). `ContactValidator` expose en plus : `MaxPhonesPerContact = 10`, `MaxPostalAddressesPerContact = 10`, `MaxTypeLength = 64`, `IsValidTypeToken(string)`.

- [ ] **Step 1 : tests du validateur qui échouent**

```csharp
[Fact]
public void Validate_AcceptsTheLegacyStringAddressShape()
{
    // Le frontend actuel envoie ["a@b.c"] ; aucun écran ne change en 4a.
    var result = ContactValidator.Validate(FromJson("""{"firstName":"Ana","addresses":["a@b.c"]}"""));
    Assert.True(result.IsSuccess);
    var line = Assert.Single(result.Value.Addresses);
    Assert.Null(line.Position);
    Assert.Equal("a@b.c", line.Address);
}

[Fact]
public void Validate_RefusesATypeThatIsNotAToken()
{
    // Jeton : lettres ASCII, chiffres, tiret, virgule, <= 64 (spec, § Limites). Un ';' ou un CR
    // est le vecteur d'injection que params fermé a déjà clos.
    var result = ContactValidator.Validate(Request(phones: [new("+322", "WORK;PREF=1")]));
    Assert.True(result.IsFailure);
}

[Fact]
public void Validate_CapsPhonesAtTen()
{
    var result = ContactValidator.Validate(Request(phones: Enumerable.Range(0, 11)
        .Select(i => new PhonePayload($"+32{i}", "CELL")).ToList()));
    Assert.True(result.IsFailure);
}

[Fact]
public void Validate_MirrorsTheNewColumnWidths()
{
    // birthday VARCHAR(64), website VARCHAR(512), organization 255, number 64… — non borné,
    // une valeur trop longue atteint MariaDB en mode strict et revient en 500.
    var result = ContactValidator.Validate(Request(birthday: new string('x', 65)));
    Assert.True(result.IsFailure);
}
```

Et pour le convertisseur (`ContactLineJsonConverterTests`) : un élément JSON **chaîne** (`"a@b.c"`) et un élément **objet** (`{"position":0,"address":"a@b.c","type":"HOME"}`) désérialisent tous deux en `ContactEmailPayload` ; tout autre token JSON → `JsonException`.

- [ ] **Step 2 : vérifier l'échec** — `cd src && dotnet test`.

- [ ] **Step 3 : implémenter**

`ContactRequest` gagne les scalaires (`DisplayName`, `MiddleName`, `NamePrefix`, `NameSuffix`, `Organization`, `Department`, `JobTitle`, `Birthday`, `Website`, `Notes` — tous `string?`) et :

```csharp
[JsonConverter(typeof(ContactLineJsonConverter))]
public List<ContactEmailPayload>? Addresses { get; set; }   // chaîne nue OU objet — compat 4a
public List<ContactPhonePayload>? Phones { get; set; }
public List<ContactAddressPayload>? PostalAddresses { get; set; }
```

où les payloads sont des classes settables (`Position`, `Address`/`Number`, `Type`, composantes ADR). Le convertisseur lit `JsonTokenType.String` → payload sans position ni type, `StartObject` → désérialisation normale. `ContactValidator.Validate` : règles existantes inchangées, plus — trim/null des nouveaux scalaires, miroir des longueurs (`100` middle, `50` prefix/suffix, `255` organization/department/job_title, `64` birthday/number/type, `512` website), `IsValidTypeToken` = `^[A-Za-z0-9,-]{0,64}$` (vide accepté = sans type), plafonds 10/10, et pour chaque adresse la règle existante (`IsValidAddress`, 320). `notes` : non borné (TEXT).

Adapter les appelants au nouveau record **sans rien refondre** : `ContactStore`/`ContactsController`/`ContactCsvExporter` construisent le `ContactWrite` étendu avec des listes vides et des scalaires null là où ils passaient l'ancien — les tests existants restent verts.

- [ ] **Step 4 : vérifier le vert** — `cd src && dotnet test`.

- [ ] **Step 5 : commit** — `git add -A && git commit -m "feat(contacts): extended write/detail models and validator rules"`

---

### Task 3 : `VCardProjector`

**Files :**
- Create : `src/snoopy.microservice/Services/Contacts/VCardProjector.cs`
- Create : `src/snoopy.microservice/Models/Contacts/ContactProjection.cs`
- Test : `snoopy.microservice.Tests/Services/VCardProjectorTests.cs` (create)

**Interfaces (produit) :**

```csharp
public sealed record ProjectedLine(int Position, string Type, int Pref, string Params, string GroupName);
public sealed record ProjectedEmail(string Address, ProjectedLine Line);
public sealed record ProjectedPhone(string Number, ProjectedLine Line);
public sealed record ProjectedAddress(
    string? PoBox, string? Extended, string? Street, string? Locality, string? Region,
    string? PostalCode, string? Country, ProjectedLine Line);
public sealed record ProjectedPhoto(string MediaType, byte[] Bytes);

public sealed record ContactProjection(
    string? FirstName, string? LastName, string? Nickname,
    string? DisplayName, string? MiddleName, string? NamePrefix, string? NameSuffix,
    string? Organization, string? Department, string? JobTitle,
    string? Birthday, string? Website, string? Notes, string? Uid,
    IReadOnlyList<ProjectedEmail> Addresses,
    IReadOnlyList<ProjectedPhone> Phones,
    IReadOnlyList<ProjectedAddress> PostalAddresses,
    ProjectedPhoto? Photo);

internal static class VCardProjector
{
    internal static ContactProjection Project(string vcardRaw);
}
```

**Consomme :** FolkerKinzel (`Vcf.Parse`) pour les valeurs décodées ; le **texte brut** pour `Params` verbatim (voir step 3). `ContactValidator.IsValidAddress` pour la règle d'abandon.

- [ ] **Step 1 : tests qui échouent** — chaque règle de la spec est un test nommé :

```csharp
private const string Card30 = "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Smith;John;Q.;Dr.;Jr.\r\n" +
    "FN:Dr. John Smith Jr.\r\nEMAIL;TYPE=INTERNET:john@work.example\r\n" +
    "item1.EMAIL;TYPE=INTERNET,HOME,PREF:john@home.example\r\n" +
    "item1.X-ABLabel:Perso\r\nTEL;TYPE=CELL:+32470000000\r\n" +
    "ADR;TYPE=HOME:PO 12;;Rue Haute 1;Bruxelles;;1000;Belgique\r\n" +
    "ORG:Acme;R&D;Lab\r\nBDAY:--03-15\r\nEND:VCARD\r\n";

[Fact] // position = rang dans la carte ; pref extrait (TYPE=PREF vaut 1, absent vaut 101)
public void Project_NumbersByCardOrderAndNormalisesPref()
{
    var p = VCardProjector.Project(Card30);
    Assert.Equal([0, 1], p.Addresses.Select(a => a.Line.Position));
    Assert.Equal(101, p.Addresses[0].Line.Pref);
    Assert.Equal(1, p.Addresses[1].Line.Pref);
    Assert.Equal("item1", p.Addresses[1].Line.GroupName);
    Assert.Equal("TYPE=INTERNET,HOME,PREF", p.Addresses[1].Line.Params); // verbatim
}

[Fact] // N complet, ORG scindé en organization / department (composantes 2..n jointes par ;)
public void Project_ReadsNamesAndOrg()
{
    var p = VCardProjector.Project(Card30);
    Assert.Equal(("John", "Smith", "Q.", "Dr.", "Jr."),
        (p.FirstName, p.LastName, p.MiddleName, p.NamePrefix, p.NameSuffix));
    Assert.Equal("Acme", p.Organization);
    Assert.Equal("R&D;Lab", p.Department);
    Assert.Equal("--03-15", p.Birthday); // forme vCard telle quelle (décision 11)
}

[Fact] // décision 8, exception nommée : EMAIL invalide → ligne abandonnée, pas tronquée
public void Project_DropsAnUnparsableEmail()
{
    var p = VCardProjector.Project(Card("EMAIL:not-an-address", "EMAIL:ok@example.com"));
    var kept = Assert.Single(p.Addresses);
    Assert.Equal("ok@example.com", kept.Address);
    Assert.Equal(1, kept.Line.Position); // le rang carte est conservé, pas renuméroté
}

[Fact] // décision 4 : ADR portant une composante RFC 9554 → street ignorée (doublon)
public void Project_IgnoresStreetWhenExtendedComponentsArePresent() { /* ADR à 12 composantes */ }

[Fact] // décision 12 : data: projeté avec type MIME traduit ; http(s) jamais ; SVG jamais
public void Project_TakesOnlyARasterDataPhoto()
{
    var withData = VCardProjector.Project(CardWithPhoto("PHOTO;ENCODING=b;TYPE=JPEG:" + Base64Jpeg));
    Assert.Equal("image/jpeg", withData.Photo!.MediaType);
    Assert.Null(VCardProjector.Project(CardWithPhoto("PHOTO;VALUE=URI:https://example.com/a.jpg")).Photo);
    Assert.Null(VCardProjector.Project(CardWithPhoto("PHOTO;ENCODING=b;TYPE=SVG:" + Base64Svg)).Photo);
}

[Fact] // décision 8 : un TYPE interminable est tronqué à 64, jamais fatal
public void Project_TruncatesAnOversizeType() { /* TYPE de 200 chars → .Type.Length == 64 */ }

[Fact] // multi-valué : jamais réduit à la première valeur (décision 4)
public void Project_KeepsCommaJoinedComponents() { /* ADR;…:;;Rue A\,Rue B;… → street "Rue A,Rue B" */ }
```

- [ ] **Step 2 : vérifier l'échec.**

- [ ] **Step 3 : implémenter**

Structure de `Project` :
1. `Vcf.Parse(vcardRaw)` → première carte (une carte vide/illisible → projection vide, jamais d'exception : décision 8, envelopper le parse dans un try qui rend une projection nue).
2. Scalaires depuis le modèle : `N` (5 composantes, multi-valeurs jointes par `,` telles que la bibliothèque les rend), `FN` → `DisplayName`, `NICKNAME`, `ORG` (composante 0 → `Organization`, 2..n jointes `;` → `Department`), `TITLE`, `NOTE`, `BDAY` (**forme texte de la carte** — si la bibliothèque typise la date, reprendre la valeur au texte brut par le scanner ci-dessous), première `URL`, `UID`.
3. Lignes filles : pour chaque `EMAIL`/`TEL`/`ADR` **dans l'ordre du document**, `Position` = rang, `GroupName` = `prop.Group ?? ""`, `Type` = valeurs TYPE jointes par `,` (tronqué à 64), `Pref` = `Parameters.Preference` ∈ [1,100] sinon `TYPE` contient `PREF` → 1, sinon 101.
4. `Params` **verbatim** : un scanner de texte brut privé dans le même fichier —

```csharp
// Déplie (CRLF + espace/tab), puis pour la Nième ligne dont le nom est `name` :
// params = ce qui sépare le premier ';' du premier ':' hors guillemets. Verbatim : aucun décodage.
private static string RawParamsOf(string raw, string name, int rank)
```

(itération caractère par caractère, bascule `inQuotes` sur `"` ; le nom matche `^(group\.)?NAME` insensible à la casse). Aligné par rang avec la collection de la bibliothèque — même ordre document des deux côtés, épinglé par les tests.
5. Adresses : composantes 0–6 seules ; si l'une des composantes 7+ est non vide, `Street = null` (règle RFC 9554). E-mail : `ContactValidator.IsValidAddress` et ≤ 320 sinon ligne absente de la projection (le rang carte des survivantes est conservé). Troncatures : chaque champ à la largeur de sa colonne ; `Params` à 255/512 **sur une frontière `;` hors guillemets** (le paramètre qui ne tient pas entier est abandonné — colonne d'affichage, spec décision 8).
6. Photo : première `PHOTO` embarquée (`data:` en 4.0, `ENCODING=b` en 3.0 — la bibliothèque expose les octets et le type) dont le type MIME ∈ {image/jpeg, image/png, image/gif, image/webp} ; mots vCard 3.0 traduits (`JPEG` → `image/jpeg`…). `http(s):` et tout autre schéma → pas de photo.

- [ ] **Step 4 : vert.** — [ ] **Step 5 : commit** — `feat(contacts): VCardProjector - card to projection`

---

### Task 4 : `VCardComposer`

**Files :**
- Create : `src/snoopy.microservice/Services/Contacts/VCardComposer.cs`
- Test : `snoopy.microservice.Tests/Services/VCardComposerTests.cs` (create)

**Interfaces (produit) :**

```csharp
internal static class VCardComposer
{
    // Carte neuve : VERSION:3.0, UID = uid, REV posé. (création manuelle, ligne CSV, rattrapage)
    internal static string ComposeNew(string uid, ContactWrite write);

    // Édition : version de la carte préservée (2.1 promu 3.0), remplacement en place (décision 4).
    internal static string Compose(string existingCard, string uid, ContactWrite write);

    // Rattrapage : ne repose QUE N, FN (chaîne de repli), NICKNAME, EMAILs en bloc. Rien d'autre.
    internal static string Reconcile(string existingCard, string uid, ReconcileWrite write);

    // Fusion d'import : ne pose que les champs non null, ajoute les adresses, FN seulement s'il manque.
    internal static string MergeFill(string existingCard, string uid, MergeWrite write);
}

public sealed record ReconcileWrite(
    string? FirstName, string? LastName, string? Nickname, IReadOnlyList<string> Addresses);
public sealed record MergeWrite(
    string? FirstName, string? LastName, string? Nickname, IReadOnlyList<string> AddedAddresses);
```

**Consomme :** `ContactWrite` étendu (tâche 2). **Réglages non négociables** (décision 6) : la sérialisation passe par une méthode unique privée `Serialize(vCard, version)` avec `VcfOpts.Default.Set(VcfOpts.WriteNonStandardProperties).Set(VcfOpts.WriteNonStandardParameters)` — et **jamais** `SetPropertyIDs`.

- [ ] **Step 1 : tests qui échouent**

```csharp
[Fact] // les deux drapeaux, et SetPropertyIDs dehors — le pin de la décision 6
public void Options_PinTheNonNegotiableFlags()
{
    Assert.True(VCardComposer.SerializationOptions.HasFlag(VcfOpts.WriteNonStandardProperties));
    Assert.True(VCardComposer.SerializationOptions.HasFlag(VcfOpts.WriteNonStandardParameters));
    Assert.False(VCardComposer.SerializationOptions.HasFlag(VcfOpts.SetPropertyIDs));
}

[Fact] // décision 4 : la valeur change, groupe + params + X- restent
public void Compose_ReplacesAValueInPlace()
{
    var card = Card("item1.TEL;TYPE=WORK;X-FOO=bar:+3221111111");
    var write = WriteWith(phones: [new ContactWritePhone(0, "+3229999999", "WORK")]);
    var output = VCardComposer.Compose(card, Uid, write);
    Assert.Contains("item1.TEL", output);
    Assert.Contains("X-FOO=bar", output);
    Assert.Contains("+3229999999", output);
    Assert.DoesNotContain("+3221111111", output);
}

[Fact] // type changé sur ligne existante : seul TYPE bouge, le jeton PREF du 3.0 survit
public void Compose_ReplacesOnlyTheTypeParameterKeepingPref()
{
    var card = Card("TEL;TYPE=HOME,PREF;X-A=1:+321");
    var output = VCardComposer.Compose(card, Uid, WriteWith(phones: [new(0, "+321", "WORK")]));
    Assert.Contains("PREF", ParamsOfFirstTel(output));
    Assert.Contains("WORK", ParamsOfFirstTel(output));
    Assert.DoesNotContain("HOME", ParamsOfFirstTel(output));
    Assert.Contains("X-A=1", ParamsOfFirstTel(output));
}

[Fact] // position absente de la fiche = propriété supprimée ; position hors carte = ligne ajoutée en fin
public void Compose_DeletesRemovedAndAppendsUnmatched() { /* 2 TEL -> write n'en rend qu'une + une neuve */ }

[Fact] // ADR : 7 premières composantes remplacées, les composantes RFC 9554 (8..18) intactes
public void Compose_LeavesExtendedAdrComponentsAlone() { /* ADR à 12 composantes, street éditée */ }

[Fact] // versions : 3.0 reste 3.0, 4.0 reste 4.0, 2.1 promu 3.0 (décision 7)
[InlineData("3.0", "3.0")] [InlineData("4.0", "4.0")] [InlineData("2.1", "3.0")]
public void Compose_EmitsInTheCardsVersion(string input, string expected) { }

[Fact] // invariants : UID = colonne (ajouté s'il manque), REV rafraîchi
public void Compose_EnforcesUidAndRev()
{
    var output = VCardComposer.Compose(CardWithoutUid, "the-uid", MinimalWrite);
    Assert.Contains("UID:the-uid", output);
    Assert.Contains("REV:", output);
}

[Fact] // ComposeNew : VERSION:3.0, FN par la chaîne de repli, toute fiche a une carte (même nom seul)
public void ComposeNew_AlwaysProducesACard()
{
    var output = VCardComposer.ComposeNew("u1", WriteWith(firstName: "Ana")); // rien d'autre
    Assert.Contains("VERSION:3.0", output);
    Assert.Contains("FN:Ana", output);
}

[Fact] // Reconcile : borné — les TEL/ADR/ORG/BDAY/NOTE du brut survivent à des colonnes vides
public void Reconcile_NeverTouchesWhatOnlyTheCardCarries()
{
    var card = LegacyWriterCard; // TEL, ADR, ORG, BDAY, NOTE, pas d'UID — la forme ContactVCardWriter
    var output = VCardComposer.Reconcile(card, "u1", new ReconcileWrite("Jean", "Nouveau", null, ["j@n.be"]));
    Assert.Contains("TEL", output);
    Assert.Contains("ORG", output);
    Assert.Contains("BDAY", output);
    Assert.Contains("UID:u1", output);
    Assert.Contains("N:Nouveau;Jean", output);
}

[Fact] // MergeFill : FN existant jamais écrasé, adresses ajoutées en fin
public void MergeFill_KeepsAnExistingFn() { /* carte avec FN:Dr. X — MergeFill(first:"Y") le laisse */ }

[Fact] // survie : BDAY texte ré-émis tel quel même en 3.0 (décision 11) — épingle la bibliothèque
public void Compose_EmitsAPartialBdayVerbatim() { /* write.Birthday = "--0315" sur carte 3.0 */ }
```

- [ ] **Step 2 : vérifier l'échec.**

- [ ] **Step 3 : implémenter**

Exposer `internal static VcfOpts SerializationOptions` (le pin du test). Mécanique commune : parser la carte, opérer sur le modèle, sérialiser via `Serialize`. Remplacement en place d'une ligne : construire la propriété neuve avec la **même** valeur de `Group` et la **même** `ParameterSection` que l'ancienne (l'API v8 permet de copier/assigner les paramètres — vérifier le membre exact sur le paquet ; le test `Compose_ReplacesAValueInPlace` est l'arbitre). `type` changé : retirer de TYPE toutes les valeurs sauf `PREF`, poser les valeurs du champ (`,`-séparées) — les autres paramètres du bloc intacts. Appariement : indexer les propriétés du nom par rang document ; write.Position → propriété ; positions non représentées → suppression ; `Position null` ou hors bornes → ajout en fin avec pour seuls paramètres le TYPE du champ. Scalaires : remplacement en place (première occurrence, décision 5) ; valeur vide → suppression de la première occurrence. `ADR`/`N` : recopier les composantes 7+/5+ de l'existante dans la nouvelle valeur. `BDAY` : poser la forme texte telle quelle (si la bibliothèque refuse un texte partiel en 3.0, passer par une propriété non standard du modèle — le test décide). `UID`/`REV` : à la fin, sur toute sortie. `ComposeNew` : carte vide + `Compose`-mécanique en mode ajout, `FN` = repli (prénom+nom → pseudo → première adresse → "").

- [ ] **Step 4 : vert.** — [ ] **Step 5 : commit** — `feat(contacts): VCardComposer - in-place vCard writes`

---

### Task 5 : `ContactStore` — composer, hasher, projeter

**Files :**
- Modify : `src/snoopy.microservice/Repositories/IContactStore.cs`
- Modify : `src/snoopy.microservice/Repositories/ContactStore.cs`
- Test : `snoopy.microservice.Tests/Repositories/ContactStoreTests.cs`

**Interfaces (produit) :**

```csharp
public interface IContactStore // ajouts
{
    Task<ContactDetail?> GetAsync(Guid userId, Guid contactId, CancellationToken ct);
    Task<(byte[] Bytes, string MediaType, string CardHash)?> GetPhotoAsync(Guid userId, Guid contactId, CancellationToken ct);
    Task<IReadOnlyList<ContactDetail>> ExportAsync(Guid userId, CancellationToken ct); // l'export CSV puise ici
}
```

`CreateAsync`/`UpdateAsync` : compose (`ComposeNew`/`Compose`) → plafond 1 Mo → hash → écrit `vcard_raw`+`card_hash` → projette (total, destructeur). Constante `internal const int MaxCardBytes = 1024 * 1024;` et message `CardTooLarge`.

- [ ] **Step 1 : tests qui échouent**

```csharp
[Fact] // le cycle complet : créer -> la carte existe, le hash aussi, les colonnes sont la projection
public async Task Create_ComposesHashesAndProjects()
{
    var id = await Create(WriteWith(firstName: "Ana", phones: [new(null, "+321", "CELL")]));
    var row = await Db.Contacts.SingleAsync(c => c.Id == id.Value);
    Assert.Contains("BEGIN:VCARD", row.VCardRaw);
    Assert.Equal(64, row.CardHash.Length);
    Assert.Single(Db.ContactPhones.Where(p => p.ContactId == id.Value));
    Assert.Equal("Ana", row.DisplayName); // FN projeté, pas recopié du write
}

[Fact] // décision 3 : projection totale — l'update efface et réécrit les lignes filles
public async Task Update_RewritesChildrenFromTheCard() { }

[Fact] // une écriture qui ne change rien ne change pas le hash (ETag stable)
public async Task Update_SameContentKeepsTheHash() { }

[Fact] // 1 Mo : le store refuse, même hors import (spec, § Limites)
public async Task Update_RefusesACardOverOneMegabyte()
{
    var result = await Update(id, WriteWith(notes: new string('x', 1_100_000)));
    Assert.True(result.IsFailure);
}

[Fact] // liste : displayName + hasPhoto, adresses dédoublonnées, ordre (pref, position)
public async Task List_OrdersByPrefAndDeduplicates() { /* seconde EMAIL PREF=1 -> première de la liste */ }

[Fact] // fiche : GetAsync rend positions, type, pref, params, group ; l'id d'autrui rend null
public async Task Get_IsScopedByUser() { }

[Fact] // photo : Bytes + MediaType + CardHash ; absente -> null
public async Task GetPhoto_AnswersTheProjection() { }
```

- [ ] **Step 2 : échec.**

- [ ] **Step 3 : implémenter**

Un seul point d'écriture privé :

```csharp
// L'endroit unique où vcard_raw s'écrit : composer est fait par l'appelant, hasher et projeter ici.
private Result WriteCard(Contact row, string card)
{
    if (Encoding.UTF8.GetByteCount(card) > MaxCardBytes) return Result.Failure(CardTooLarge);
    row.VCardRaw = card;
    row.CardHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(card)));
    ReplaceProjection(row, VCardProjector.Project(card));
    return Result.Success();
}
```

`ReplaceProjection` : supprime les `ContactEmails`/`ContactPhones`/`ContactAddresses`/`ContactPhotos` du contact (chargés puis `RemoveRange`, comme `DeleteAsync` le fait déjà — l'InMemory n'a pas de cascade), réécrit depuis la projection (adresses passées par `IdentityResolver.Canonical` — le contrat de la colonne), pose les scalaires (`DisplayName`, `Birthday`… et `FirstName`/`LastName`/`Nickname` **depuis la projection**, plus le write). `CreateAsync` : garde le plafond 5000 et la logique UID actuelle, puis `ComposeNew` + `WriteCard`. `UpdateAsync` : `Compose(row.VCardRaw ?? ComposeNew…, row.Uid, write)` + `WriteCard` ; `uid`/`source` intouchés (commentaire existant à conserver, amendé : `vcard_raw` cesse d'être intouchable). `ListAsync` : projeter aussi `DisplayName` et l'existence photo (requête `ContactPhotos` par jointure corrélée, même motif anti-N+1 que les adresses) ; adresses ordonnées `(Pref, Position)` puis dédoublonnées (`Distinct` conserve la première). `GetAsync`/`GetPhotoAsync`/`ExportAsync` : lectures projetées, scoping utilisateur via le motif `FindAsync` existant. `SetFavoriteAsync` et les routes bulk : inchangés (l'étoile n'est pas une projection, décision 1).

- [ ] **Step 4 : vert.** — [ ] **Step 5 : commit** — `feat(contacts): store composes, hashes and projects on every write`

---

### Task 6 : import `.vcf`, CSV recomposé, mort de `ContactVCardWriter`

**Files :**
- Create : `src/snoopy.microservice/Services/Contacts/VCardSplitter.cs`
- Create : `src/snoopy.microservice/Services/Contacts/VCardImportMapper.cs`
- Delete : `src/snoopy.microservice/Services/Contacts/ContactVCardWriter.cs`
- Delete : `snoopy.microservice.Tests/Services/ContactVCardWriterTests.cs`
- Modify : `src/snoopy.microservice/Models/Contacts/ContactImportRow.cs`
- Modify : `src/snoopy.microservice/Repositories/ContactStore.cs` (ImportAsync)
- Modify : `src/snoopy.microservice/Controllers/ContactsController.cs` (Import)
- Test : `snoopy.microservice.Tests/Services/VCardSplitterTests.cs` (create), `VCardImportMapperTests.cs` (create), `Repositories/ContactStoreImportTests.cs`, `Controllers/ContactsControllerTests.cs`

**Interfaces :**

```csharp
public sealed record VCardChunk(int Line, string Text); // Line = ligne du BEGIN:VCARD, 1-based
internal static class VCardSplitter
{
    internal static IReadOnlyList<VCardChunk> Split(string fileText); // texte, jamais parsé
}
internal static class VCardImportMapper
{
    internal static ContactImportRow Map(VCardChunk chunk); // via VCardProjector
}
// ContactImportRow gagne : string? Uid — la clé de fusion placée devant l'adresse et le nom.
public sealed record ContactImportRow(
    int Line, string? FirstName, string? LastName, string? Nickname,
    bool IsFavorite, IReadOnlyList<string> Addresses, string? VCard, string? Uid);
```

- [ ] **Step 1 : tests qui échouent**

Splitter : deux cartes → deux chunks **verbatim** (octet pour octet, repliement compris) avec les bons numéros de ligne ; texte hors `BEGIN`/`END` ignoré ; `END:VCARD` manquant → le fragment est un chunk quand même (le lecteur tolérant décidera). Mapper : chunk → row avec `Uid` de la carte, noms, adresses valides, `VCard == chunk.Text` (le verbatim, pas une re-sérialisation — **l'assertion clé de la décision 1**).

`ContactStoreImportTests` (nouveaux cas ; les cas 3d existants restent verts) :

```csharp
[Fact] // UID connu -> fusion, avant l'adresse et le nom ; réimport idempotent
public async Task Import_MergesOnUidFirst() { }

[Fact] // deux cartes neuves de même UID dans un fichier -> la seconde fusionne dans la première
public async Task Import_KeepsTheUidIndexCurrent() { }

[Fact] // carte entrante sur fiche sans carte -> posée verbatim (porte 3)
public async Task Import_StoresTheIncomingCardVerbatimWhenTheTargetHasNone() { }

[Fact] // fusion sur fiche cartée -> carte recomposée par MergeFill, X- existants survivent
public async Task Import_RecomposesTheTargetsCard() { }

[Fact] // une fiche créée par CSV sans colonne hors modèle a une carte quand même (règle null morte)
public async Task Import_EveryCreatedContactHasACard() { }
```

Contrôleur : un `.vcf` (type MIME `text/vcard` ou contenu commençant par `BEGIN:VCARD` après BOM) part dans le chemin vCard ; un CSV inchangé ; carte > 1 Mo → ligne en erreur avec son numéro ; UID > 255 → idem ; plafond de requête à 20 Mo.

- [ ] **Step 2 : échec.**

- [ ] **Step 3 : implémenter**

Splitter : balayage ligne à ligne (index de ligne courant), début de chunk sur `BEGIN:VCARD` (insensible à la casse, ligne entière), fin sur `END:VCARD` — le texte est découpé sur les offsets d'origine, **jamais reconstruit**. `ImportAsync` : troisième index `uidOwners : Dictionary<string, Guid>` chargé depuis les contacts de l'utilisateur (`Uid` → id), consulté **avant** l'index d'adresses quand `row.Uid != null`, tenu à jour à chaque création (motif exact des index 3d, `ContactStore.cs:312-315`). Fusion : champs comme aujourd'hui, puis si quelque chose a bougé — cible avec carte → `contact.VCardRaw = VCardComposer.MergeFill(carte, uid, …)` ; cible sans carte et `row.VCard != null` → verbatim ; dans les deux cas via le même `WriteCard` (hash + projection). Création : `row.VCard` verbatim s'il existe (chemin `.vcf`), sinon `ComposeNew` (chemin CSV — c'est ici que la règle `null` de `ContactVCardWriter` meurt). Contrôleur `Import` : `[RequestSizeLimit(20 * 1024 * 1024)]` (constante renommée, commentaire du `MemoryStream` relu — à 20 Mo l'allocation est LOH ; la garder mais le dire), aiguillage MIME puis contenu, chemin vCard = `Split` → filtre 1 Mo/UID 255 (erreurs avec `Line`) → `Map` → `ImportAsync`. Chemin CSV : `ContactCsvRow` → `ContactWrite` (les `Extras` mappés sur les champs étendus par la table de correspondance qui vivait dans `ContactVCardWriter` — phones par clé `mobilephone`→`CELL` etc., `company`/`department`, adresses `home`/`business`, scalaires) → `ComposeNew` → `row.VCard`. Supprimer `ContactVCardWriter` et ses tests ; les assertions de valeur (chaque famille de propriétés) migrent dans `VCardComposerTests` si elles n'y sont pas déjà couvertes.

- [ ] **Step 4 : vert** (`cd src && dotnet test` — suite entière : la suppression du writer touche des tests 3d).

- [ ] **Step 5 : commit** — `feat(contacts): vcf import with verbatim cards, CSV path through composer`

---

### Task 7 : contrat d'API — fiche, photo, écritures, export CSV étendu

**Files :**
- Modify : `src/snoopy.microservice/Controllers/ContactsController.cs`
- Modify : `src/snoopy.microservice/Services/Contacts/ContactCsvExporter.cs`
- Modify : `src/snoopy.microservice/Models/Contacts/ContactListResponse.cs` (si la forme le demande)
- Test : `snoopy.microservice.Tests/Controllers/ContactsControllerTests.cs`, `Services/ContactCsvExporterTests.cs`, `Services/ContactCsvMapperTests.cs`

- [ ] **Step 1 : tests qui échouent**

```csharp
[Fact] // GET /{id} : 200 avec la fiche ; l'id d'autrui 404, jamais 403
public async Task Get_AnswersTheDetailAndHidesForeignIds() { }

[Fact] // Photo : binaire, nosniff, attachement, ETag = card_hash ; If-None-Match -> 304 sans corps
public async Task GetPhoto_HonoursIfNoneMatch()
{
    // 1er appel : FileContentResult, header ETag "\"<hash>\"", X-Content-Type-Options nosniff
    // 2e appel avec If-None-Match = ce même ETag : StatusCodeResult 304
}

[Fact] // POST/PUT : nouveaux champs acceptés ; params/group_name/pref présents dans le corps -> ignorés
public async Task Put_IgnoresOutputOnlyFields() { }

[Fact] // la réponse du POST reste la fiche validée (jamais re-lue), nouveaux champs compris
public async Task Create_AnswersFromTheValidatedWrite() { }
```

Export (`ContactCsvExporterTests`) : l'en-tête devient le jeu Outlook de 3d — `Title, First Name, Middle Name, Last Name, Nick Name, Display Name, Company, Department, Job Title, E-mail Address[, E-mail N Address…], Notes, Web Page, Birthday, Mobile Phone, Home Phone, Business Phone, Home Fax, Business Fax, Other Phone, Home Street, Home City, Home State, Home Postal Code, Home Country, Business Street, …, Business Country, Favorite` ; mapping type → colonne : `CELL`→Mobile, `HOME`+`FAX`→Home Fax, `WORK`+`FAX`→Business Fax, `HOME`→Home Phone, `WORK`→Business Phone, reste→Other Phone (première occurrence par colonne, l'excédent reste en base — le CSV n'est pas la carte) ; première ADR `HOME`→colonnes Home, première `WORK`→Business ; neutralisation formule inchangée sur **tous** les champs de texte libre (noms + notes + company…) ; ordre des lignes inchangé. Aller-retour : exporter un carnet à téléphones et adresses postales puis réimporter le fichier ne crée rien et ne change rien (le mapper 3d lit déjà ces colonnes — c'est le test de symétrie que la spec exige).

- [ ] **Step 2 : échec.**

- [ ] **Step 3 : implémenter**

`GET /{id}` : `store.GetAsync` → 200 `ContactDetail` / 404 (`NotFoundEnveloppe(ContactStore.NotFound)`). `GET /{id}/Photo` :

```csharp
var photo = await store.GetPhotoAsync(AuthenticatedUser.WebmailUid, id, cancellationToken);
if (photo == null) return NotFoundEnveloppe(ContactStore.NotFound);
var etag = $"\"{photo.Value.CardHash}\"";
Response.Headers.ETag = etag;
Response.Headers.XContentTypeOptions = "nosniff";
if (Request.Headers.IfNoneMatch.Contains(etag)) return StatusCode(StatusCodes.Status304NotModified);
return File(photo.Value.Bytes, photo.Value.MediaType, "photo");
```

`POST`/`PUT` : rien de plus que le validateur étendu — les champs sortants ne figurent pas dans `ContactRequest`, ils sont donc ignorés par construction ; la réponse du POST reconstruit le `ContactView` depuis le write validé comme aujourd'hui, `DisplayName`/`HasPhoto` compris (`HasPhoto` = false : pas de porte d'écriture photo en 4a, décision 12). `Export` : `store.ExportAsync` → exporteur réécrit sur `ContactDetail` (tri actuel conservé, colonnes d'adresse e-mail dynamiques conservées).

- [ ] **Step 4 : vert.** — [ ] **Step 5 : commit** — `feat(contacts): detail and photo routes, extended CSV export`

---

### Task 8 : le rattrapage

**Files :**
- Modify : `src/snoopy.microservice/Controllers/ContactsController.cs` (ou `ContactsBackfillController.cs` — create, si la taille du contrôleur le justifie)
- Modify : `src/snoopy.microservice/Repositories/IContactStore.cs`, `ContactStore.cs`
- Modify : `src/snoopy.microservice/CLAUDE.md`, `src/snoopy.microservice/Configuration/SecurityConfiguration.cs` (commentaires policy)
- Create : `docs/superpowers/contacts-4a-backfill.md`
- Test : `ContactStoreTests` (ou fichier dédié `ContactStoreBackfillTests.cs`), `ContactsControllerTests`

**Interfaces :**

```csharp
public sealed record BackfillOutcome(int Processed, int Remaining);
// Dans IContactStore :
Task<BackfillOutcome> BackfillAsync(int batchSize, CancellationToken ct); // TOUS les utilisateurs
```

Route : `POST /api/Contacts/Backfill`, `[Authorize(Policy = <nom déclaré dans SecurityConfiguration.cs>)]` — reprendre la constante exacte que `SecurityConfiguration` déclare (le handler vit chez le provider ; sur generic la policy est insatisfiable, c'est voulu). Réponse 200 `{ processed, remaining }`.

- [ ] **Step 1 : tests qui échouent**

```csharp
[Fact] // fiche sans carte -> carte neuve depuis les colonnes, hash posé, projection écrite
public async Task Backfill_ComposesTheMissingCard() { }

[Fact] // fiche avec carte ContactVCardWriter ÉDITÉE depuis : les colonnes gagnent sur N/FN/EMAIL,
        // les TEL/ORG/BDAY du brut survivent — la réconciliation bornée de la spec
public async Task Backfill_ReconcilesWithoutDestroyingTheCard()
{
    var row = SeedLegacy(vcard: LegacyWriterCard, firstName: "Jean", lastName: "Édité");
    await Store.BackfillAsync(100, default);
    var after = await Db.Contacts.SingleAsync(c => c.Id == row.Id);
    Assert.Contains("N:Édité;Jean", after.VCardRaw);
    Assert.Contains("TEL", after.VCardRaw);          // seule la carte les portait
    Assert.Contains($"UID:{row.Uid}", after.VCardRaw);
    Assert.NotEqual(string.Empty, after.CardHash);
    Assert.NotEmpty(Db.ContactPhones.Where(p => p.ContactId == row.Id)); // projeté à la fin
}

[Fact] // par lots : batchSize=1 sur 3 fiches -> {1, 2} puis {1, 1} puis {1, 0} ; rejouer -> {0, 0}
public async Task Backfill_WorksInBatchesAndIsIdempotent() { }

[Fact] // la sélection est card_hash = '' : une fiche déjà traitée n'est jamais revisitée
public async Task Backfill_SkipsProcessedRows() { }
```

Contrôleur : 200 avec le compte ; sans le rôle admin → 403 (le framework répond, pas nous).

- [ ] **Step 2 : échec.**

- [ ] **Step 3 : implémenter**

`BackfillAsync` : `Contacts.Where(c => c.CardHash == "").OrderBy(c => c.Id).Take(batchSize)` (**pas** de filtre `user_id` : balayage global), charge les adresses des fiches du lot en une requête ; par fiche — carte absente → `ComposeNew(uid, write-des-colonnes)` ; carte présente → `Reconcile(carte, uid, ReconcileWrite(first, last, nick, adresses))` ; puis le `WriteCard` commun (plafond, hash, projection). `Remaining` = `Count(CardHash == "")` après `SaveChanges`. Journaliser par lot (`logger.LogInformation("Contacts backfill: {Processed} processed, {Remaining} remaining", …)` — structuré, jamais interpolé). Amendements : la phrase du CLAUDE.md (« no route it serves carries it » côté § platform seam) et le commentaire de `SecurityConfiguration.cs:24-26` — les deux disent désormais qu'une route core la porte : le rattrapage. Le document `contacts-4a-backfill.md` : quand (après tables + déploiement), comment (boucle `curl` tant que `remaining > 0`), comment rejouer après un correctif moteur (remettre `card_hash = ''` en SQL sur le périmètre voulu), et le fait que l'opération journalise.

- [ ] **Step 4 : vert.** — [ ] **Step 5 : commit** — `feat(contacts): batched admin backfill with bounded reconciliation`

---

### Task 9 : corpus de cartes réelles et tests de survie

**Files :**
- Create : `snoopy.microservice.Tests/Fixtures/VCards/iphone.vcf`, `google.vcf`, `thunderbird.vcf`, `davx5.vcf`
- Create : `snoopy.microservice.Tests/Services/VCardCorpusTests.cs`
- Modify : csproj de tests (copier les fixtures : `<Content Include="Fixtures\VCards\*.vcf" CopyToOutputDirectory="PreserveNewest" />`)

**Contenu des fixtures :** des cartes **réalistes et anonymes** écrites d'après les formats documentés des quatre clients — iPhone : 3.0, groupes `item1.`/`item2.`, `X-ABLabel`, `X-ABADR`, `X-ABShowAs`, photo base64 ; Google : 3.0, `TYPE=INTERNET`, `item1.EMAIL` ; Thunderbird : 4.0 depuis 102+ ; DAVx⁵ : 4.0, `PREF=1`, `data:` photo, composantes RFC 9554 sur une ADR. Noms, adresses, numéros : fictifs (`Prénom Test`, `+32470000000`, `exemple.example`). Si l'utilisateur fournit de vrais exports anonymisés, ils **remplacent** ces fichiers à l'identique de nom — la structure des tests ne bouge pas.

- [ ] **Step 1 : écrire fixtures + tests**

```csharp
[Theory]
[InlineData("iphone.vcf")] [InlineData("google.vcf")] [InlineData("thunderbird.vcf")] [InlineData("davx5.vcf")]
public void Corpus_ProjectsWithoutLoss(string file)
{
    foreach (var chunk in VCardSplitter.Split(Fixture(file)))
    {
        var p = VCardProjector.Project(chunk.Text);
        Assert.NotNull(p.DisplayName); // FN obligatoire chez les quatre
        Assert.All(p.Addresses, a => Assert.True(ContactValidator.IsValidAddress(a.Address)));
    }
}

[Theory] // LA famille d'assertions de la spec : survie
[InlineData("iphone.vcf")] [InlineData("davx5.vcf")]
public void Corpus_SurvivesASingleFieldEdit(string file)
{
    var card = VCardSplitter.Split(Fixture(file))[0].Text;
    var p = VCardProjector.Project(card);
    var write = WriteFrom(p) with { Notes = "edited" };          // un seul champ bouge
    var output = VCardComposer.Compose(card, p.Uid!, write);
    var reparsed = VCardProjector.Project(output);

    // Toute propriété non modélisée est encore là (comptage brut des noms de propriété X-*)
    Assert.Equal(NonStandardNames(card), NonStandardNames(output));
    // Tout paramètre est encore sur la sienne (PREF, X- params)
    Assert.Equal(p.Addresses.Select(a => a.Line.Params), reparsed.Addresses.Select(a => a.Line.Params));
    // Tout libellé groupé désigne encore sa propriété
    Assert.Equal(GroupOfLabel(card, "X-ABLabel"), GroupOfLabel(output, "X-ABLabel"));
}

[Fact] // les deux comportements « à constater » de la spec, épinglés une fois observés
public void Corpus_PinsLibraryBehaviour()
{
    // 1. Le sort de X-ABLabel (modélisé par la v8) : groupe conservé ou déplacé — observer,
    //    puis écrire l'assertion sur le comportement CONSTATÉ et le commenter.
    // 2. La forme exacte d'un BDAY partiel ré-émis en 3.0.
}
```

Le troisième test se remplit en deux temps : exécuter, observer la sortie réelle, figer l'assertion sur l'observé (avec un commentaire disant que c'est un constat, pas une exigence). Si `X-ABLabel` perd son groupe à la réécriture, **s'arrêter et le signaler** : c'est une menace sur la décision 4 qui mérite l'avis de l'utilisateur (workaround possible : le ré-attacher via `NonStandards` plutôt que la propriété modélisée).

- [ ] **Step 2 : exécuter, observer, figer** — `cd src && dotnet test`.

- [ ] **Step 3 : commit** — `test(contacts): real-world vCard corpus - projection and survival`

---

## Self-review (fait à l'écriture du plan)

- **Couverture spec → tâches** : décisions 1–3 (T5), 4 (T3+T4), 5/5 bis (T3, T4, T5), 6 (T1, T4, T9), 7 (T4), 8 (T3), 9 (T1, T5), 10 (T1, T3, T5), 11 (T3, T4, T9), 12 (T3, T5, T7), 13 (T5, T7), 14 (T6), 15 (T8) ; schéma (T1), moteur (T3–T6), contrat d'API (T7), rattrapage (T8), limites (T2, T5, T6), tests (T9 + chaque tâche), amendements docs (T1, T8).
- **Types** : `ContactWrite`/`ContactDetail`/`ContactProjection` et les signatures `VCardComposer`/`VCardSplitter`/`IContactStore` sont définis une fois (T2–T5) et consommés à l'identique ensuite.
- **Ordre** : chaque tâche ne consomme que ce que les précédentes produisent ; la suite complète reste verte à la fin de chaque tâche (T2 adapte les appelants, T6 absorbe les tests du writer).
