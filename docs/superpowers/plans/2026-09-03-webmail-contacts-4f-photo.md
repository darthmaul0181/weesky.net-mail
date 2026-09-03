# Contacts 4f — la photo s'écrit : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Les rapports de sous-agents dans le scratchpad sont préfixés `4f-task-N-…`.

**Spec :** [`docs/superpowers/specs/2026-09-03-webmail-contacts-4f-photo-design.md`](../specs/2026-09-03-webmail-contacts-4f-photo-design.md) — toute « décision N » citée ici y renvoie. En cas de doute, la spec fait foi.

**Goal :** dans l'éditeur, l'avatar se clique — on choisit une image, elle s'affiche aussitôt, un bouton la retire, et « Save » la fait voyager avec le reste du formulaire jusqu'à une ligne `PHOTO` dans la carte, que le téléphone synchronisé verra au sync suivant.

**Architecture :** un champ `photo` sur `ContactRequest`, traduit par le validateur en `PhotoPayload?` (`null` = garde, `Remove`, `Replace(bytes, mediaType)`) et transporté tel quel par `ContactWrite` jusqu'à `VCardComposer.Emit`, où `PlacePhoto` remplace ou retire **toute** la famille `PHOTO` et pose une ligne dans le dialecte de la carte. Côté navigateur, un module pur réduit l'image avant l'envoi ; l'éditeur tient un état d'action à trois cas plutôt qu'un seed gelé, et la clé du cache photo porte le `cardHash`. En préalable, `LogicalLines` cesse d'être quadratique — sans quoi 512 Ko de photo coûtent des gigaoctets par sauvegarde.

**Tech stack :** .NET 10, EF Core (Pomelo MySQL, InMemory pour les tests), xUnit, Moq, FolkerKinzel.VCards 8.2.0 ; frontend React + TypeScript, Vitest, @tanstack/react-query.

## Global constraints

- `cd src && dotnet test` (jamais `--no-build` quand des fichiers de test sont ajoutés) ; frontend : `cd src/frontend && npx vitest run`.
- **Aucun SQL, aucune migration.** `contacts.vcard_raw` et `contact_revisions.vcard_raw` sont `MEDIUMTEXT`, `contact_photos.bytes` est `MEDIUMBLOB` : les colonnes encaissent déjà une carte d'1 Mo. Rien à jouer à la main pour cette tranche.
- `Assert.IsType<T>` vérifie le type exact : `BadRequestObjectResult` pour `BadRequest(body)`.
- `ApiDocumentation.xml` : ne committer que les membres réellement touchés ; réverter la dérive massive que `dotnet test` régénère.
- Style C# : file-scoped namespaces, un type par fichier (les records satellites d'un DTO restent dans son fichier, comme `ContactWriteEmail` aujourd'hui), `sealed`, `internal` par défaut, primary constructors, cancellation tokens.
- Style commentaire : le codebase commente le *pourquoi*, jamais le *quoi*. Trois lignes maximum, et rien du tout quand le code se lit seul.
- Commits : concis (2 lignes max), jamais commencer/finir par `@`, messages multi-lignes via heredoc `git commit -F -` (jamais de here-string PowerShell). Terminer par :
  ```
  Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01PCtivr8FyYuYHxChFv1hVX
  ```
- L'UI du site est en **anglais** ; `locales/fr` porte la traduction française.
- Pas d'assertion dépendante de l'hôte (fins de ligne, valeurs observées plutôt que spécifiées).
- **Aucun cas `Keep`** : `PhotoPayload?` est nullable et `null` veut dire « la requête ne nomme pas la photo ». Une valeur par défaut d'argument optionnel doit être une constante de compilation ; `= Keep` sur un record ne compile pas (CS1736).
- **Aucun échappement de la ligne `PHOTO`** : `EscapeText` corromprait le `data:image/jpeg;base64,` de la 4.0, dont le `;` et la `,` sont de la syntaxe URI. Base64 n'a rien à échapper.
- Plafonds : `ContactValidator.MaxPhotoBytes = 512 * 1024` octets **décodés** ; `ContactStore.MaxCardBytes = 1 Mo` reste souverain au-dessus ; `RequestSizeLimit(2 * ContactStore.MaxCardBytes)` sur les deux routes d'écriture.

---

## Structure des fichiers

**Backend**

| Fichier | Responsabilité après 4f |
|---|---|
| `Models/Contacts/ContactRequest.cs` | + `Photo` (`string?`), la forme filaire |
| `Models/Contacts/ContactWrite.cs` | + `PhotoPayload` (union fermée) et `Photo` en dernier paramètre |
| `Services/Contacts/VCardProjector.cs` | `SniffRasterType` et `RasterTypeName` rendus `internal static` — la table sniff/nom, écrite une fois |
| `Services/ContactValidator.cs` | la porte : base64, plafond, sniff, message |
| `Services/Contacts/VCardComposer.cs` | `LogicalLines` linéaire, `IsName` sans dépliage, `PlacePhoto`, `SpliceUnmodelledFamilies(skipPhoto)` |
| `Controllers/ContactsController.cs` | `RequestSizeLimit`, `hasPhoto` dans la réponse de `Create` |

`Repositories/ContactStore.cs` **n'est pas touché** : c'est le sens de la décision 7.

**Frontend**

| Fichier | Responsabilité après 4f |
|---|---|
| `modules/contacts/contactPhoto.ts` *(nouveau)* | le réducteur pur : `File` → `{ base64, blob }` ou une clé d'erreur |
| `modules/contacts/queries.ts` | `contactKeys.photo` porte le `cardHash` |
| `modules/contacts/useContactPhotoUrl.ts` | reçoit et transmet le `cardHash` |
| `modules/contacts/ContactCard.tsx`, `ContactsLayout.tsx` | passent le `cardHash` qu'ils lisent déjà du même `detail` |
| `modules/contacts/contactTypes.ts` | `ContactDraft.photo` |
| `modules/contacts/useCaptureContacts.ts` | `photo: null` |
| `modules/contacts/ContactEditView.tsx` | avatar cliquable, état d'action à trois cas, erreur inline |
| `locales/en|fr/contacts.json`, `index.css` | les quatre libellés, les deux blocs de style |

## Ordre des tâches

```
1 (LogicalLines) → 2 (validation) → 3 (composeur puis route)
4 (réducteur) ─────────────────────→ 5 (cache puis éditeur) → 6 (doc + vérification manuelle)
```

La tâche 4 ne dépend de rien et peut partir en parallèle de 1–3. La tâche 5 a besoin de 2 (la forme filaire) et de 4.

Six tâches, dont deux en deux commits : une tâche est une porte de revue, pas un commit. Le découpage suit les **risques** de conception, pas les fichiers — mettre `RequestSizeLimit` sous sa propre revue, ou séparer une clé de cache de l'écran qui s'y appuie, ferait deux gardes là où la conception n'en a qu'une.

---

### Task 1 : `LogicalLines` linéaire et `IsName` sans dépliage

Décision 6 bis. Aucun changement de comportement — c'est un préalable de coût. `lines[^1] += …` réalloue la ligne logique entière à chaque pli : une `PHOTO` de 700 Ko pliée fait ~9 500 plis, soit ~3 G de caractères copiés par appel, et une sauvegarde y passe quatre fois. `IsName` déplie 700 Ko pour lire cinq lettres, des dizaines de fois par `Emit`.

**Files :**
- Modify : `src/snoopy.microservice/Services/Contacts/VCardComposer.cs:821-846` (`LogicalLines`), `:884-886` (`IsName`)
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/VCardComposerTests.cs`

**Interfaces (produit pour les tâches suivantes) :**
- `VCardComposer.LogicalLines(string) → List<string>` — signature et résultat **inchangés**.
- `VCardComposer.IsName(string chunk, string name) → bool` — signature et résultat **inchangés**.

- [ ] **Step 1 : écrire les tests qui échouent**

Dans `VCardComposerTests`, à la suite des tests de pliage existants :

```csharp
// Une borne d'allocations, pas un budget de temps : la première est déterministe, le second est
// un flake par construction.
[Fact]
public void LogicalLines_OnAFoldedPhoto_DoesNotReallocateTheLinePerFold()
{
    var card = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:X\r\n"
        + VCardComposer.Fold("PHOTO;ENCODING=b;TYPE=JPEG:" + new string('A', 699_052))
        + "\r\nEND:VCARD\r\n";

    var before = GC.GetAllocatedBytesForCurrentThread();
    var lines = VCardComposer.LogicalLines(card);
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

    Assert.Equal(5, lines.Count);
    Assert.StartsWith("PHOTO;ENCODING=b;TYPE=JPEG:", lines[3]);
    // Le résultat est le même qu'avant : les lignes recollées rendent la carte moins son \r\n final.
    Assert.Equal(card, string.Join("\r\n", lines) + "\r\n");
    Assert.True(allocated < 20L * card.Length, $"{allocated} octets alloués pour {card.Length} caractères");
}

[Fact]
public void IsName_OnAFoldedPhoto_ReadsTheNameWithoutUnfolding()
{
    var chunk = VCardComposer.Fold("PHOTO;ENCODING=b;TYPE=JPEG:" + new string('A', 699_052));

    var before = GC.GetAllocatedBytesForCurrentThread();
    var found = VCardComposer.IsName(chunk, "PHOTO");
    var missed = VCardComposer.IsName(chunk, "NOTE");
    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

    Assert.True(found);
    Assert.False(missed);
    Assert.True(allocated < 4096, $"{allocated} octets alloués");
}

// Le cas que la première ligne physique ne suffit pas à trancher : le pli a coupé le nom lui-même.
[Fact]
public void IsName_WhenTheFoldCutsTheNameItself_StillUnfolds()
{
    var name = new string('N', 80);

    Assert.True(VCardComposer.IsName(VCardComposer.Fold(name + ":v"), name));
}

// Un préfixe de groupe coupé par le pli : la première ligne ne porte ni ';' ni ':'.
[Fact]
public void IsName_WhenTheFoldCutsAGroupPrefix_StillUnfolds()
{
    var chunk = VCardComposer.Fold(new string('g', 74) + ".PHOTO:v");

    Assert.True(VCardComposer.IsName(chunk, "PHOTO"));
}

[Fact]
public void LogicalLines_OnALeadingContinuation_StillMakesItItsOwnLine()
{
    Assert.Equal([" orphan", "FN:X"], VCardComposer.LogicalLines(" orphan\r\nFN:X"));
}
```

- [ ] **Step 2 : vérifier l'échec**

Run : `cd src && dotnet test --filter "FullyQualifiedName~VCardComposerTests.LogicalLines_OnAFoldedPhoto|FullyQualifiedName~VCardComposerTests.IsName_"`
Expected : `LogicalLines_OnAFoldedPhoto…` et `IsName_OnAFoldedPhoto…` échouent sur la borne d'allocations ; les trois autres passent déjà (ils tiennent le comportement à ne pas casser).

- [ ] **Step 3 : rendre `LogicalLines` linéaire**

Remplacer le corps de `LogicalLines` :

```csharp
    internal static List<string> LogicalLines(string text)
    {
        var lines = new List<string>();
        // Un tampon plutôt que lines[^1] += … : le second réalloue la ligne logique entière à
        // chaque pli, ce qu'une PHOTO de 700 Ko paie 9 500 fois (décision 6 bis).
        var pending = new StringBuilder();
        foreach (var physical in text.Split("\r\n"))
        {
            if (physical.Length == 0) continue;
            if ((physical[0] == ' ' || physical[0] == '\t') && pending.Length > 0)
            {
                pending.Append("\r\n").Append(physical);
                continue;
            }

            if (pending.Length > 0) lines.Add(pending.ToString());
            pending.Clear().Append(physical);
        }

        if (pending.Length > 0) lines.Add(pending.ToString());
        return lines;
    }
```

- [ ] **Step 4 : rendre `IsName` sans dépliage**

Remplacer :

```csharp
    // Le nom d'un chunk plié vit dans sa première ligne physique, sauf si le pli a coupé le nom
    // lui-même — ce que seul un nom de plus de 75 caractères peut faire. Déplier 700 Ko de PHOTO
    // pour lire « PHOTO » est ce qui rendait chaque balayage de famille quadratique.
    internal static bool IsName(string chunk, string name)
    {
        var fold = chunk.IndexOf("\r\n", StringComparison.Ordinal);
        var first = fold < 0 ? chunk : chunk[..fold];
        var readable = first.AsSpan().IndexOfAny(';', ':') >= 0;
        return NameOf(readable ? first : Unfold(chunk)).Equals(name, StringComparison.OrdinalIgnoreCase);
    }
```

- [ ] **Step 5 : vérifier que tout passe**

Run : `cd src && dotnet test`
Expected : PASS, y compris `VCardComposerResidualTests`, `VCardCorpusTests`, `AddressBookFilterTests`, `AddressDataFilterTests`, `DavContactWriterTests`, `VCardVersionConverterTests` — les six suites qui appellent `LogicalLines` ou `IsName` indirectement. Une régression y est le signal que l'équivalence n'est pas tenue.

- [ ] **Step 6 : commit**

```bash
git add src/snoopy.microservice/Services/Contacts/VCardComposer.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/VCardComposerTests.cs
git commit -F - <<'MSG'
perf(contacts): LogicalLines et IsName cessent d'etre quadratiques

Une PHOTO de 700 Ko pliee coutait ~3 G de caracteres copies par appel.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PCtivr8FyYuYHxChFv1hVX
MSG
```

---

### Task 2 : `PhotoPayload`, le champ `photo`, et la validation

Décisions 2, 3, 4. La photo entre dans l'API et devient un type ; personne ne l'écrit encore dans la carte.

**Files :**
- Modify : `src/snoopy.microservice/Models/Contacts/ContactRequest.cs`
- Modify : `src/snoopy.microservice/Models/Contacts/ContactWrite.cs`
- Modify : `src/snoopy.microservice/Services/Contacts/VCardProjector.cs:193-198`
- Modify : `src/snoopy.microservice/Services/ContactValidator.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/ContactValidatorTests.cs`

**Interfaces (produit pour les tâches suivantes) :**
- `ContactRequest.Photo` (`string?`).
- `PhotoPayload` — union fermée dans `ContactWrite.cs` : `PhotoPayload.Remove`, `PhotoPayload.Replace(byte[] Bytes, string MediaType)`. Pas de `Keep` : `null` en tient lieu.
- `ContactWrite.Photo` (`PhotoPayload?`), dernier paramètre, défaut `null`.
- `VCardProjector.SniffRasterType(byte[]) → string?` (rendu `internal static`), `VCardProjector.RasterTypeName(string mediaType) → string`.
- `ContactValidator.MaxPhotoBytes` (`int`, 524 288), `PhotoNotBase64`, `PhotoNotRaster`, `PhotoTooLarge` (`string`).

- [ ] **Step 1 : écrire les tests qui échouent**

Dans `ContactValidatorTests`, ajouter le paramètre `photo` au helper `Request` et les fixtures :

```csharp
    private static ContactRequest Request(
        string? first = null, string? last = null, string? nick = null,
        string? birthday = null, string? notes = null, string? photo = null,
        IReadOnlyList<PhonePayload>? phones = null,
        params string[] addresses) =>
        new()
        {
            FirstName = first,
            LastName = last,
            Nickname = nick,
            Birthday = birthday,
            Notes = notes,
            Photo = photo,
            Addresses = [.. addresses],
            Phones = phones is null
                ? null
                : [.. phones.Select(p => new ContactPhonePayload { Number = p.Number, Type = p.Type })],
        };

    // Le sniff ne lit que les premiers octets : une signature suivie de zéros est un JPEG pour lui,
    // et une vraie image dans une fixture ne prouverait rien de plus.
    private static string Base64Jpeg(int length) =>
        Convert.ToBase64String([0xFF, 0xD8, 0xFF, .. new byte[length - 3]]);

    private static IEnumerable<string> Wrapped(string value, int width)
    {
        for (var i = 0; i < value.Length; i += width)
            yield return value[i..Math.Min(i + width, value.Length)];
    }
```

Puis les tests :

```csharp
    [Fact]
    public void Validate_WithoutAPhoto_LeavesTheCardsOwn()
    {
        var result = ContactValidator.Validate(Request(first: "Bruno"));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Photo);
    }

    [Fact]
    public void Validate_WithAJsonNullPhoto_LeavesTheCardsOwn()
    {
        var result = ContactValidator.Validate(FromJson("""{"firstName":"Bruno","photo":null}"""));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Photo);
    }

    [Fact]
    public void Validate_WithAnEmptyPhoto_Removes()
    {
        var result = ContactValidator.Validate(Request(first: "Bruno", photo: ""));

        Assert.IsType<PhotoPayload.Remove>(result.Value.Photo);
    }

    [Fact]
    public void Validate_WithABase64Jpeg_ReplacesAndSniffsTheType()
    {
        var result = ContactValidator.Validate(Request(first: "Bruno", photo: Base64Jpeg(64)));

        var replace = Assert.IsType<PhotoPayload.Replace>(result.Value.Photo);
        Assert.Equal("image/jpeg", replace.MediaType);
        Assert.Equal(64, replace.Bytes.Length);
    }

    [Fact]
    public void Validate_WithAnSvg_Refuses()
    {
        var svg = Convert.ToBase64String("<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8);

        var result = ContactValidator.Validate(Request(first: "Bruno", photo: svg));

        Assert.Equal("The photo is not a JPEG, PNG, GIF or WebP image", result.Error);
    }

    [Fact]
    public void Validate_WithSomethingThatIsNotBase64_Refuses()
    {
        var result = ContactValidator.Validate(Request(first: "Bruno", photo: "not base64!!"));

        Assert.Equal("The photo is not valid base64", result.Error);
    }

    // 512 Ko et 512 Ko + 1 s'encodent sur la même longueur, le padding absorbant l'octet : seule
    // la taille décodée peut les distinguer (décision 4).
    [Fact]
    public void Validate_WithOneByteOverTheCeiling_RefusesOnTheDecodedLength()
    {
        var admitted = Base64Jpeg(512 * 1024);
        var refused = Base64Jpeg(512 * 1024 + 1);

        Assert.Equal(admitted.Length, refused.Length);
        Assert.True(ContactValidator.Validate(Request(first: "B", photo: admitted)).IsSuccess);
        Assert.Equal("The photo exceeds 512 KB",
            ContactValidator.Validate(Request(first: "B", photo: refused)).Error);
    }

    // Le test qui empêche une garde de longueur de revenir : pliée à 76 colonnes, une image
    // parfaitement admissible dépasse 4 * ceil(MaxPhotoBytes / 3) caractères (décision 4).
    [Fact]
    public void Validate_WithAWrappedBase64UnderTheCeiling_Accepts()
    {
        var wrapped = string.Join("\r\n", Wrapped(Base64Jpeg(500 * 1024), 76));

        Assert.True(wrapped.Length > 4 * ((512 * 1024 + 2) / 3));
        var result = ContactValidator.Validate(Request(first: "Bruno", photo: wrapped));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error : null);
        Assert.IsType<PhotoPayload.Replace>(result.Value.Photo);
    }
```

- [ ] **Step 2 : vérifier l'échec**

Run : `cd src && dotnet test --filter "FullyQualifiedName~ContactValidatorTests"`
Expected : la compilation échoue — `ContactRequest.Photo`, `PhotoPayload` et `ContactWrite.Photo` n'existent pas.

- [ ] **Step 3 : le champ filaire**

`ContactRequest.cs`, après `Notes` :

```csharp
    /// <summary>
    /// Absent or null: the card keeps its photo. Empty: removed. Otherwise base64 with no data:
    /// prefix and no media type — the bytes are what say the format (décision 3).
    /// </summary>
    public string? Photo { get; set; }
```

- [ ] **Step 4 : l'union fermée**

`ContactWrite.cs`, au-dessus de `ContactWrite` :

```csharp
/// <summary>
/// What a validated request says about the photo. Two cases only: null is the third, and it is the
/// "the request did not name this field, the card keeps its own" that <see cref="ContactWrite"/>
/// documents for every field the editor does not own. A <c>Keep</c> case could not be an optional
/// argument's default anyway — that must be a compile-time constant, which no record instance is.
/// </summary>
public abstract record PhotoPayload
{
    private PhotoPayload() { }

    /// <summary>Every PHOTO line leaves the card, not just the first (décision 5).</summary>
    public sealed record Remove : PhotoPayload;

    /// <summary>Every PHOTO line leaves the card and this one takes their place.</summary>
    /// <param name="Bytes">The decoded image.</param>
    /// <param name="MediaType">What the sniff read of those bytes, never what the client claimed.</param>
    public sealed record Replace(byte[] Bytes, string MediaType) : PhotoPayload;
}
```

Puis, sur `ContactWrite`, la doc du paramètre et le paramètre :

```csharp
/// <param name="Photo">
/// Null = the request did not name the photo, so the card keeps its own. The validator is the only
/// producer that ever poses it; the import and <c>WriteOf</c> keep the default.
/// </param>
public sealed record ContactWrite(
    // … inchangé jusqu'à CardHash …
    string? CardHash = null,
    PhotoPayload? Photo = null);
```

- [ ] **Step 5 : le sniff partagé et son nom**

`VCardProjector.cs` : passer `SniffRasterType` de `private` à `internal`, et ajouter juste en dessous :

```csharp
    /// <summary>The vCard TYPE word for a media type <see cref="SniffRasterType"/> answered — the
    /// write side of the same table, kept beside it so the two never drift (décision 3).</summary>
    internal static string RasterTypeName(string mediaType) => mediaType switch
    {
        "image/jpeg" => "JPEG",
        "image/png" => "PNG",
        "image/gif" => "GIF",
        "image/webp" => "WEBP",
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, null),
    };
```

- [ ] **Step 6 : la porte du validateur**

`ContactValidator.cs`, ajouter `using System.Buffers.Text;` en tête, puis à côté des autres plafonds :

```csharp
    /// <summary>
    /// The photo's ceiling, in decoded bytes — what the browser's reducer produces at worst
    /// (décision 8). <see cref="Repositories.ContactStore.MaxCardBytes"/> stays sovereign above it.
    /// </summary>
    internal const int MaxPhotoBytes = 512 * 1024;

    internal const string PhotoNotBase64 = "The photo is not valid base64";

    internal const string PhotoNotRaster = "The photo is not a JPEG, PNG, GIF or WebP image";

    internal static readonly string PhotoTooLarge = $"The photo exceeds {MaxPhotoBytes / 1024} KB";
```

Puis, dans `Validate`, juste avant le `return Result.Success(...)` — après les boucles, parce que c'est le contrôle le plus cher :

```csharp
        var photoError = PhotoOf(request.Photo, out var photo);
        if (photoError != null) return Result.Failure<ContactWrite>(photoError);
```

et passer `photo` en dernier argument du `new ContactWrite(...)`, après `request.CardHash`.

La méthode, à côté de `TooLong` dont elle reprend la forme :

```csharp
    /// <summary>
    /// The photo's refusal message, null when the payload is good — <paramref name="photo"/> is
    /// only meaningful then. Validity and decoded size are read by <c>Base64.IsValid</c>, which
    /// allocates nothing and tolerates the whitespace <c>FromBase64String</c> tolerates; the string
    /// it reads is already bounded by the route's request size limit. No length pre-guard: it would
    /// refuse a wrapped base64 the ceiling admits, and the length cannot decide anyway — 512 KB and
    /// 512 KB + 1 encode to the same 699 052 characters (décision 4). The decode comes last.
    /// </summary>
    private static string? PhotoOf(string? value, out PhotoPayload? photo)
    {
        photo = null;
        if (value == null) return null;
        if (value.Length == 0)
        {
            photo = new PhotoPayload.Remove();
            return null;
        }

        if (!Base64.IsValid(value.AsSpan(), out var decodedLength)) return PhotoNotBase64;
        if (decodedLength > MaxPhotoBytes) return PhotoTooLarge;

        var bytes = Convert.FromBase64String(value);
        if (VCardProjector.SniffRasterType(bytes) is not { } mediaType) return PhotoNotRaster;

        photo = new PhotoPayload.Replace(bytes, mediaType);
        return null;
    }
```

La valeur n'est pas passée par `Given` : le blanc est du remplissage base64 légal, et seule la chaîne exactement vide veut dire « retire ».

- [ ] **Step 7 : vérifier que tout passe**

Run : `cd src && dotnet test`
Expected : PASS.

- [ ] **Step 8 : commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml 2>/dev/null || true
git add src/snoopy.microservice/Models/Contacts/ContactRequest.cs \
        src/snoopy.microservice/Models/Contacts/ContactWrite.cs \
        src/snoopy.microservice/Services/Contacts/VCardProjector.cs \
        src/snoopy.microservice/Services/ContactValidator.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/ContactValidatorTests.cs
git commit -F - <<'MSG'
feat(contacts): la requete porte une photo, le validateur la lit

Base64 valide, 512 Ko decodes, octets sniffes : PhotoPayload ou 400.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PCtivr8FyYuYHxChFv1hVX
MSG
```

---

### Task 3 : la carte porte la photo, la route la fait voyager

Décisions 5 et 6. La famille `PHOTO` entière est remplacée ou retirée, jamais sa première occurrence, et la ligne suit le dialecte de la carte.

> Deux livrables, deux commits, une seule porte de revue : le composeur qui pose la ligne, puis la route qui la laisse passer. La seconde moitié ne porte aucun risque propre — deux attributs et un booléen — et ce sont ses tests de store qui prouvent la première.

**Files :**
- Modify : `src/snoopy.microservice/Services/Contacts/VCardComposer.cs` (`Apply:207-232`, `Emit:506-525`, `SpliceUnmodelledFamilies:712-736`, + `PlacePhoto`)
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/VCardComposerTests.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/VCardCorpusTests.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Services/CardDav/VCardVersionConverterTests.cs`
- Modify : `src/snoopy.microservice/Controllers/ContactsController.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/ContactsControllerTests.cs`
- Test : `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreTests.cs`

**Interfaces (consomme / produit) :**
- Consomme : `PhotoPayload.Remove`, `PhotoPayload.Replace(byte[], string)`, `ContactWrite.Photo`, `VCardProjector.RasterTypeName` (tâche 2).
- Produit : `VCardComposer.Compose` / `ComposeNew` honorent `write.Photo`. `Reconcile`, `MergeFill` et `ComposeNewGroup` sont inchangés — `Emit` reçoit le payload par un paramètre optionnel `null`, comme `Apply` reçoit déjà `rawBirthday`.

- [ ] **Step 1 : écrire les tests qui échouent**

Dans `VCardComposerTests`, ajouter au helper `WriteWith` le paramètre et les fixtures :

```csharp
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0x01, 0x02, 0x03];

    private static readonly PhotoPayload JpegPayload = new PhotoPayload.Replace(JpegBytes, "image/jpeg");

    private static string Card40(params string[] lines) =>
        "BEGIN:VCARD\r\nVERSION:4.0\r\nFN:X\r\n"
        + string.Concat(lines.Select(l => l + "\r\n")) + "END:VCARD\r\n";

    private static IEnumerable<string> PhotoLines(string card) =>
        VCardComposer.LogicalLines(card).Where(l => VCardComposer.IsName(l, "PHOTO"));
```

(`WriteWith` gagne `PhotoPayload? photo = null` en dernier paramètre, passé après `"manual"`.)

Les tests :

```csharp
    [Fact]
    public void Compose_WithAReplacedPhoto_WritesOneLineInThe30Dialect()
    {
        var card = Card("PHOTO;ENCODING=b;TYPE=PNG:AAAA", "PHOTO;ENCODING=b;TYPE=PNG:BBBB");

        var result = VCardComposer.Compose(card, Uid, WriteWith(photo: JpegPayload));

        // Les deux occurrences partent ensemble : n'en retirer qu'une ferait de la seconde l'avatar
        // et l'utilisateur verrait son geste échouer (décision 5).
        var only = Assert.Single(PhotoLines(result));
        Assert.Equal("PHOTO;ENCODING=b;TYPE=JPEG:" + Convert.ToBase64String(JpegBytes),
            VCardComposer.Unfold(only));
    }

    [Fact]
    public void Compose_WithAReplacedPhoto_WritesADataUriInThe40Dialect()
    {
        var result = VCardComposer.Compose(
            Card40("PHOTO:data:image/png;base64,AAAA"), Uid, WriteWith(photo: JpegPayload));

        // Le ';' et la ',' du data: URI survivent : EscapeText ne touche jamais cette ligne.
        var only = Assert.Single(PhotoLines(result));
        Assert.Equal("PHOTO:data:image/jpeg;base64," + Convert.ToBase64String(JpegBytes),
            VCardComposer.Unfold(only));
    }

    [Fact]
    public void Compose_WithARemovedPhoto_DropsEveryOccurrenceAndNothingElse()
    {
        var card = Card("PHOTO;ENCODING=b;TYPE=PNG:AAAA", "NOTE:n", "PHOTO;ENCODING=b;TYPE=PNG:BBBB");

        var result = VCardComposer.Compose(card, Uid, WriteWith(photo: new PhotoPayload.Remove()));

        Assert.Empty(PhotoLines(result));
        Assert.Contains(VCardComposer.LogicalLines(result), l => VCardComposer.IsName(l, "NOTE"));
    }

    [Fact]
    public void Compose_WithoutAPhotoPayload_KeepsApplesCropParameterByteForByte()
    {
        const string apple =
            "PHOTO;X-ABCROP-RECTANGLE=ABClipRect_1&0&0&320&320&abc==;ENCODING=b;TYPE=JPEG:AAAA";

        var result = VCardComposer.Compose(Card(apple), Uid, MinimalWrite);

        Assert.Contains(apple, result);
    }

    [Fact]
    public void ComposeNew_WithAPhoto_PosesItBeforeEndVCard()
    {
        var lines = VCardComposer.LogicalLines(VCardComposer.ComposeNew(Uid, WriteWith(photo: JpegPayload)));

        Assert.Equal("END:VCARD", lines[^1]);
        Assert.True(VCardComposer.IsName(lines[^2], "PHOTO"));
    }

    [Fact]
    public void Compose_WithACeilingSizedPhoto_FoldsTheLine()
    {
        var big = new PhotoPayload.Replace([0xFF, 0xD8, 0xFF, .. new byte[512 * 1024 - 3]], "image/jpeg");

        var chunk = Assert.Single(PhotoLines(VCardComposer.Compose(Card(), Uid, WriteWith(photo: big))));

        Assert.Contains("\r\n ", chunk);
        foreach (var physical in chunk.Split("\r\n"))
            Assert.True(physical.Length <= 75, $"ligne physique de {physical.Length} caractères");
    }
```

Dans `VCardCorpusTests`, le tour complet sur les quatre cartes réelles — calqué sur `Corpus_SurvivesASingleFieldEdit`, dont il reprend `WriteFrom` et l'UID lu comme la production le lit :

```csharp
    // Ce que le composeur écrit, la projection le relit : c'est elle qui remplit contact_photos.
    [Theory]
    [MemberData(nameof(AllClients))]
    public void Corpus_ARewrittenCard_ProjectsTheNewPhoto(string file)
    {
        var card = Card(file, 0);
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0x7F };
        var write = WriteFrom(VCardProjector.Project(card))
            with { Photo = new PhotoPayload.Replace(bytes, "image/jpeg") };

        var output = VCardComposer.Compose(card, VCardImportMapper.UidOf(card) ?? Uid, write);
        var reparsed = VCardProjector.Project(output);

        Assert.Equal("image/jpeg", reparsed.Photo!.MediaType);
        Assert.Equal(bytes, reparsed.Photo.Bytes);
    }
```

Dans `VCardVersionConverterTests`, le chemin par lequel un téléphone lira la photo :

```csharp
    // Le REPORT d'un client 3.0 sur une carte stockée en 4.0 passe par là : une photo écrite par
    // 4f doit en ressortir avec ses octets.
    [Fact]
    public void ConvertingFourToThree_KeepsAnEmbeddedPhoto()
    {
        var value = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0x2A });
        var card = "BEGIN:VCARD\r\nVERSION:4.0\r\nUID:u1\r\nFN:A\r\n"
            + $"PHOTO:data:image/jpeg;base64,{value}\r\nEND:VCARD\r\n";

        var converted = VCardVersionConverter.To(card, "3.0");

        Assert.Contains(value, VCardComposer.Unfold(converted));
    }
```

- [ ] **Step 2 : vérifier l'échec**

Run : `cd src && dotnet test --filter "FullyQualifiedName~VCardComposerTests|FullyQualifiedName~VCardCorpusTests|FullyQualifiedName~VCardVersionConverterTests"`
Expected : compilation en échec (le paramètre `photo` de `WriteWith` n'a pas de destination), puis les six nouveaux tests en échec.

- [ ] **Step 3 : `Apply` transmet le payload**

`VCardComposer.cs`, dernière ligne d'`Apply` :

```csharp
        return Emit(source, uid, write.Birthday ?? rawBirthday, write.Photo);
```

- [ ] **Step 4 : `Emit` le reçoit et vide `card.Photos`**

```csharp
    private static string Emit(SourceCard source, string uid, string? birthday, PhotoPayload? photo = null)
    {
        var card = source.Card;
        var old = card.ContactID;
        var id = new ContactIDProperty(ContactID.Create(uid), old?.Group);
        if (old != null) id.Parameters.Assign(old.Parameters);
        card.ContactID = id;

        // Vidé avant le writer : sur un Replace comme sur un Remove, la bibliothèque écrirait
        // 700 Ko que PlacePhoto retirerait aussitôt.
        if (photo != null) card.Photos = null;

        var lines = LogicalLines(Serialize(card, source.Version));
        if (source.Version == VCdVersion.V3_0)
        {
            RestoreDroppedParameters(lines, card);
            SpliceCollapsedFamilies(lines, card, source);
            SpliceUnmodelledFamilies(lines, source, skipPhoto: photo != null);
        }

        StripNamePlaceholders(lines, card);
        EnforceBirthday(lines, card, birthday);
        PlacePhoto(lines, source.Version, photo);
        RestoreUid(lines, source, uid);
        return Join(lines);
    }
```

Le paramètre est optionnel : `Reconcile`, `MergeFill` et `ComposeNewGroup` ne portent pas de photo et n'ont pas une ligne à changer.

- [ ] **Step 5 : `PlacePhoto`**

À la suite d'`EnforceBirthday` :

```csharp
    /// <summary>
    /// Décision 5: the whole PHOTO family goes, never just its first occurrence — the projection
    /// promotes whatever is left, so a partial removal is one the user watches fail and a partial
    /// replacement leaves an old picture for the next removal to wake up. Décision 6: the line is
    /// built by hand, in the card's dialect, and never escaped — base64 has nothing to escape, and
    /// the 4.0 data: URI's ';' and ',' are URI syntax that <see cref="EscapeText"/> would corrupt.
    /// </summary>
    private static void PlacePhoto(List<string> lines, VCdVersion version, PhotoPayload? photo)
    {
        if (photo == null) return;

        var indices = FamilyIndices(lines, "PHOTO");
        for (var i = indices.Count - 1; i >= 0; i--) lines.RemoveAt(indices[i]);
        if (photo is not PhotoPayload.Replace replace) return;

        var value = Convert.ToBase64String(replace.Bytes);
        lines.Insert(EndIndex(lines), Fold(version == VCdVersion.V4_0
            ? $"PHOTO:data:{replace.MediaType};base64,{value}"
            : $"PHOTO;ENCODING=b;TYPE={VCardProjector.RasterTypeName(replace.MediaType)}:{value}"));
    }
```

Pas de règle de rang : l'ordre des propriétés n'a aucun sens vCard.

- [ ] **Step 6 : la splice saute `PHOTO` hors du cas `null`**

```csharp
    private static void SpliceUnmodelledFamilies(List<string> lines, SourceCard source, bool skipPhoto = false)
    {
        var families = new List<string>();
        var inputLines = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var chunk in source.InputChunks)
        {
            var name = NameOf(Unfold(chunk));
            if (name.Length == 0 || OwnedNames.Contains(name)) continue;
            // L'entrée porte une PHOTO que PlacePhoto s'apprête à remplacer ou retirer : la
            // réinjecter ne lui donnerait qu'une ligne de plus à supprimer, 700 Ko à la fois.
            if (skipPhoto && name.Equals("PHOTO", StringComparison.OrdinalIgnoreCase)) continue;
            // … reste inchangé …
```

- [ ] **Step 7 : vérifier que tout passe**

Run : `cd src && dotnet test`
Expected : PASS. `VCardComposerResidualTests` est le garde-fou du cas `null` : rien n'y bouge.

- [ ] **Step 8 : commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml 2>/dev/null || true
git add src/snoopy.microservice/Services/Contacts/VCardComposer.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/VCardComposerTests.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/VCardCorpusTests.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/CardDav/VCardVersionConverterTests.cs
git commit -F - <<'MSG'
feat(contacts): le composeur pose et retire la famille PHOTO

Toute la famille, jamais la premiere ; la ligne suit le dialecte de la carte.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PCtivr8FyYuYHxChFv1hVX
MSG
```

#### Seconde moitié : la route de bout en bout

Décisions 1 et 7. La photo voyage dans le même `PUT`/`POST` que les autres champs, sous la protection du `cardHash`, et rend une révision et un rang — un seul.

- [ ] **Step 9 : écrire les tests qui échouent**

Dans `ContactsControllerTests` :

```csharp
    [Fact]
    public async Task Create_WithAPhoto_AnswersHasPhoto()
    {
        _store.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<ContactWrite>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success(Guid.NewGuid()));
        var request = Valid();
        request.Photo = Convert.ToBase64String(new byte[] { 0xFF, 0xD8, 0xFF, 0x09 });

        var result = await CreateController().Create(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        // false par construction depuis 4a : c'est la ligne que cette tranche corrige.
        Assert.True(Assert.IsType<ContactView>(ok.Value).HasPhoto);
    }

    [Fact]
    public async Task Create_WithoutAPhoto_StillAnswersFalse()
    {
        _store.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<ContactWrite>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Success(Guid.NewGuid()));

        var result = await CreateController().Create(Valid(), CancellationToken.None);

        Assert.False(Assert.IsType<ContactView>(Assert.IsType<OkObjectResult>(result.Result).Value).HasPhoto);
    }

    [Fact]
    public async Task Update_WithAnOversizedPhoto_Answers400WithoutTouchingTheStore()
    {
        var request = Valid();
        request.Photo = Convert.ToBase64String([0xFF, 0xD8, 0xFF, .. new byte[512 * 1024 - 2]]);

        var result = await CreateController().Update(Guid.NewGuid(), request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        _store.Verify(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ContactWrite>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WriteRoutes_BoundTheirBodyLikeTheDavPut()
    {
        foreach (var name in new[] { nameof(ContactsController.Create), nameof(ContactsController.Update) })
        {
            var limit = typeof(ContactsController).GetMethod(name)!
                .GetCustomAttributes(typeof(RequestSizeLimitAttribute), false)
                .Cast<RequestSizeLimitAttribute>().Single();
            Assert.Equal(2 * ContactStore.MaxCardBytes, limit.Bytes);
        }
    }
```

Dans `ContactStoreTests`, ajouter `PhotoPayload? photo = null` au helper `Write` (passé après `source`), puis :

```csharp
    [Fact]
    public async Task Update_WithAReplacedPhoto_ProjectsItAndArchivesOneRevision()
    {
        var db = nameof(Update_WithAReplacedPhoto_ProjectsItAndArchivesOneRevision);
        var user = Guid.NewGuid();
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0x11 };
        var id = (await CreateStore(db).CreateAsync(user, Write(addresses: "b@x.be"), CancellationToken.None)).Value;

        var updated = await CreateStore(db).UpdateAsync(user, id,
            Write(addresses: "b@x.be", photo: new PhotoPayload.Replace(bytes, "image/jpeg")),
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        var photo = await CreateStore(db).GetPhotoAsync(user, id, CancellationToken.None);
        Assert.Equal(bytes, photo!.Value.Bytes);
        Assert.Equal("image/jpeg", photo.Value.MediaType);
    }

    [Fact]
    public async Task Update_WithARemovedPhoto_DropsItAndHasPhotoFallsBack()
    {
        var db = nameof(Update_WithARemovedPhoto_DropsItAndHasPhotoFallsBack);
        var user = Guid.NewGuid();
        var id = (await CreateStore(db).CreateAsync(user,
            Write(addresses: "b@x.be", photo: new PhotoPayload.Replace([0xFF, 0xD8, 0xFF, 0x11], "image/jpeg")),
            CancellationToken.None)).Value;

        await CreateStore(db).UpdateAsync(user, id,
            Write(addresses: "b@x.be", photo: new PhotoPayload.Remove()), CancellationToken.None);

        Assert.Null(await CreateStore(db).GetPhotoAsync(user, id, CancellationToken.None));
        Assert.False((await CreateStore(db).GetAsync(user, id, CancellationToken.None))!.HasPhoto);
    }

    // Le chemin nominal d'une sauvegarde qui ne change rien : la photo reste, et la carte
    // recomposée est byte-identique, donc PrepareCard n'ouvre aucune transaction.
    [Fact]
    public async Task Update_WithoutAPhotoPayload_KeepsThePhotoAndTakesNoRank()
    {
        var db = nameof(Update_WithoutAPhotoPayload_KeepsThePhotoAndTakesNoRank);
        var user = Guid.NewGuid();
        var sync = ContactStoreTestFactory.NewSync();
        var store = new ContactStore(new PreferencesTestDbContext(db), sync.Object);
        var id = (await store.CreateAsync(user,
            Write(addresses: "b@x.be", photo: new PhotoPayload.Replace([0xFF, 0xD8, 0xFF, 0x11], "image/jpeg")),
            CancellationToken.None)).Value;
        sync.Invocations.Clear();

        await new ContactStore(new PreferencesTestDbContext(db), sync.Object)
            .UpdateAsync(user, id, Write(addresses: "b@x.be"), CancellationToken.None);

        Assert.NotNull(await CreateStore(db).GetPhotoAsync(user, id, CancellationToken.None));
        sync.Verify(s => s.NextSequenceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
```

- [ ] **Step 10 : vérifier l'échec**

Run : `cd src && dotnet test --filter "FullyQualifiedName~ContactsControllerTests|FullyQualifiedName~ContactStoreTests"`
Expected : `Create_WithAPhoto…` échoue (`HasPhoto` est `false` par construction), `WriteRoutes_BoundTheirBodyLikeTheDavPut` échoue (aucun attribut), les trois tests de store passent déjà — ils tiennent le câblage de la tâche 2 et de la première moitié de celle-ci.

- [ ] **Step 11 : borner les deux corps**

`ContactsController.cs`, sur `Create` et sur `Update`, sous les `[ProducesResponseType]` :

```csharp
    // Une photo de 512 Ko fait ~700 Ko de base64 : le corps a désormais une taille qui mérite
    // d'être bornée, et la borne est celle du PUT CardDAV.
    [RequestSizeLimit(2 * ContactStore.MaxCardBytes)]
```

- [ ] **Step 12 : dire la vérité sur `hasPhoto`**

Remplacer les deux dernières lignes de `Create` :

```csharp
        // 4f ouvre la porte d'écriture que 4a avait fermée : la réponse ne peut plus dire false
        // par construction.
        return Ok(new ContactView(created.Value, write.FirstName, write.LastName, write.Nickname,
            write.IsFavorite,
            [.. write.Addresses.Select(a => IdentityResolver.Canonical(a.Address)).Distinct()],
            write.DisplayName, write.Photo is PhotoPayload.Replace));
```

- [ ] **Step 13 : vérifier que tout passe**

Run : `cd src && dotnet test`
Expected : PASS.

- [ ] **Step 14 : commit**

```bash
git checkout -- src/snoopy.microservice/ApiDocumentation.xml 2>/dev/null || true
git add src/snoopy.microservice/Controllers/ContactsController.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Controllers/ContactsControllerTests.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreTests.cs
git commit -F - <<'MSG'
feat(contacts): la photo voyage avec le formulaire

Corps borne a 2 Mo, hasPhoto dit la verite a la creation.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PCtivr8FyYuYHxChFv1hVX
MSG
```

---

### Task 4 : `contactPhoto.ts`, le réducteur navigateur

Décision 8. Une photo de téléphone pèse 3 à 6 Mo ; envoyée telle quelle elle serait refusée à chaque fois. Module pur : ni requête, ni React.

**Files :**
- Create : `src/frontend/src/modules/contacts/contactPhoto.ts`
- Test : `src/frontend/src/modules/contacts/contactPhoto.test.ts`

**Interfaces (produit pour la tâche 5) :**
- `reducePhoto(file: File): Promise<{ base64: string; blob: Blob }>` — le blob sert l'aperçu de la décision 9, la base64 part au serveur.
- `PHOTO_UNREADABLE = 'editor.photoUnreadable'`, `PHOTO_TOO_LARGE = 'editor.photoTooLarge'` — les clés de traduction, jetées comme `Error.message`.

- [ ] **Step 1 : écrire les tests qui échouent**

`contactPhoto.test.ts` :

```ts
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PHOTO_TOO_LARGE, PHOTO_UNREADABLE, reducePhoto } from './contactPhoto'

const MAX = 512 * 1024

let drawn: number[][]
let filled: number[]
let order: string[]
let sides: number[]
let qualities: number[]
let blobSizes: number[]

function mockBitmap(width: number, height: number) {
  vi.stubGlobal('createImageBitmap', vi.fn(async () => ({ width, height, close: vi.fn() })))
}

beforeEach(() => {
  drawn = []; filled = []; order = []; sides = []; qualities = []; blobSizes = []
  mockBitmap(400, 200)
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({
    fillStyle: '',
    fillRect: (...args: number[]) => { filled = args; order.push('fill') },
    drawImage: (_b: unknown, ...args: number[]) => { drawn.push(args); order.push('draw') },
  } as unknown as CanvasRenderingContext2D)
  vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(function (
    this: HTMLCanvasElement, callback: BlobCallback, _type?: string, quality?: number) {
    sides.push(this.width)
    qualities.push(quality as number)
    callback(new Blob([new Uint8Array(blobSizes.shift() ?? 10)], { type: 'image/jpeg' }))
  })
})

describe('reducePhoto', () => {
  it('crops a landscape to a centred square', async () => {
    await reducePhoto(new File([], 'p.jpg'))

    // sx, sy, sw, sh : la moitié du débord à gauche, rien en haut, le côté court des deux côtés.
    expect(drawn[0].slice(0, 4)).toEqual([100, 0, 200, 200])
  })

  it('crops a portrait to a centred square', async () => {
    mockBitmap(200, 400)

    await reducePhoto(new File([], 'p.jpg'))

    expect(drawn[0].slice(0, 4)).toEqual([0, 100, 200, 200])
  })

  it('never enlarges a small image', async () => {
    mockBitmap(300, 300)

    await reducePhoto(new File([], 'p.jpg'))

    expect(sides[0]).toBe(300)
  })

  it('paints the white ground before the image', async () => {
    // Un canvas naît noir transparent et le JPEG jette l'alpha : sans ce fond, un logo sur fond
    // transparent devient un carré noir.
    await reducePhoto(new File([], 'p.jpg'))

    expect(order.slice(0, 2)).toEqual(['fill', 'draw'])
    expect(filled).toEqual([0, 0, 200, 200])
  })

  it('asks the browser to apply the EXIF orientation', async () => {
    await reducePhoto(new File([], 'p.jpg'))

    expect(createImageBitmap).toHaveBeenCalledWith(expect.anything(), { imageOrientation: 'from-image' })
  })

  it('walks the quality down while the blob is over the ceiling', async () => {
    mockBitmap(2000, 2000)
    blobSizes = [MAX + 1, MAX + 1, 10]

    await reducePhoto(new File([], 'p.jpg'))

    expect(qualities).toEqual([0.85, 0.7, 0.55])
    expect(sides).toEqual([1024, 1024, 1024])
  })

  it('falls back to 512 px when the quality descent is not enough', async () => {
    mockBitmap(2000, 2000)
    blobSizes = [MAX + 1, MAX + 1, MAX + 1, MAX + 1, MAX + 1, 10]

    await reducePhoto(new File([], 'p.jpg'))

    expect(sides).toEqual([1024, 1024, 1024, 512, 512, 512])
  })

  it('refuses rather than sending something bound for a 400', async () => {
    mockBitmap(2000, 2000)
    blobSizes = Array(6).fill(MAX + 1)

    await expect(reducePhoto(new File([], 'p.jpg'))).rejects.toThrow(PHOTO_TOO_LARGE)
  })

  it('reports a file the browser cannot decode', async () => {
    vi.stubGlobal('createImageBitmap', vi.fn(async () => { throw new Error('HEIC') }))

    await expect(reducePhoto(new File([], 'p.heic'))).rejects.toThrow(PHOTO_UNREADABLE)
  })

  it('answers bare base64, not a data URL', async () => {
    blobSizes = [7]

    const { base64, blob } = await reducePhoto(new File([], 'p.jpg'))

    expect(base64.startsWith('data:')).toBe(false)
    expect(base64).not.toContain(',')
    expect(atob(base64).length).toBe(7)
    expect(blob.size).toBe(7)
  })
})
```

- [ ] **Step 2 : vérifier l'échec**

Run : `cd src/frontend && npx vitest run src/modules/contacts/contactPhoto.test.ts`
Expected : FAIL — `Failed to resolve import "./contactPhoto"`.

- [ ] **Step 3 : écrire le module**

`contactPhoto.ts` :

```ts
/**
 * The reducer the editor runs before anything leaves the browser (décision 8). Pure — no query, no
 * React, no i18n: it throws the translation key and lets the caller render it.
 */

/** `ContactValidator.MaxPhotoBytes`, mirrored: what the server accepts once decoded. */
const MAX_BYTES = 512 * 1024

/** 1024 first; 512 is the second chance for an image too noisy to compress at any quality. */
const SIDES = [1024, 512]

const QUALITIES = [0.85, 0.7, 0.55]

export const PHOTO_UNREADABLE = 'editor.photoUnreadable'

export const PHOTO_TOO_LARGE = 'editor.photoTooLarge'

export interface ReducedPhoto {
  /** Bare base64, no data: prefix — the shape `ContactRequest.photo` reads. */
  base64: string
  /** The same bytes, for the editor's preview object URL. */
  blob: Blob
}

export async function reducePhoto(file: File): Promise<ReducedPhoto> {
  let bitmap: ImageBitmap
  try {
    // Asked for explicitly: the re-encoded JPEG loses its EXIF tag, so a portrait taken on a phone
    // would lie down for good if the browser did not apply the orientation while decoding.
    bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' })
  } catch {
    throw new Error(PHOTO_UNREADABLE)
  }

  try {
    for (const side of SIDES) {
      for (const quality of QUALITIES) {
        const blob = await encode(bitmap, side, quality)
        if (blob.size <= MAX_BYTES) return { base64: await toBase64(blob), blob }
      }
    }
  } finally {
    bitmap.close()
  }

  throw new Error(PHOTO_TOO_LARGE)
}

/** Centre-cropped square, never enlarged, drawn on white: a canvas is born transparent black and
    JPEG throws the alpha away, so a logo on a transparent ground would come back a black square. */
async function encode(bitmap: ImageBitmap, side: number, quality: number): Promise<Blob> {
  const crop = Math.min(bitmap.width, bitmap.height)
  const size = Math.min(side, crop)
  const canvas = document.createElement('canvas')
  canvas.width = size
  canvas.height = size

  const context = canvas.getContext('2d')
  if (!context) throw new Error(PHOTO_UNREADABLE)
  context.fillStyle = '#ffffff'
  context.fillRect(0, 0, size, size)
  context.drawImage(bitmap,
    (bitmap.width - crop) / 2, (bitmap.height - crop) / 2, crop, crop, 0, 0, size, size)

  return await new Promise<Blob>((resolve, reject) => canvas.toBlob(
    blob => blob ? resolve(blob) : reject(new Error(PHOTO_UNREADABLE)), 'image/jpeg', quality))
}

/** readAsDataURL and the cut at the first comma, never
    `btoa(String.fromCharCode(...new Uint8Array(buffer)))`: spreading half a million arguments into
    a call is how that idiom blows the stack. */
function toBase64(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onerror = () => reject(new Error(PHOTO_UNREADABLE))
    reader.onload = () => {
      const result = String(reader.result)
      resolve(result.slice(result.indexOf(',') + 1))
    }
    reader.readAsDataURL(blob)
  })
}
```

- [ ] **Step 4 : vérifier que tout passe**

Run : `cd src/frontend && npx vitest run src/modules/contacts/contactPhoto.test.ts`
Expected : PASS, 11 tests.

- [ ] **Step 5 : commit**

```bash
git add src/frontend/src/modules/contacts/contactPhoto.ts \
        src/frontend/src/modules/contacts/contactPhoto.test.ts
git commit -F - <<'MSG'
feat(contacts): le navigateur reduit la photo avant de l'envoyer

Carre centre, 1024 px, JPEG sur fond blanc, degradation jusqu'a 512 Ko.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PCtivr8FyYuYHxChFv1hVX
MSG
```

---

### Task 5 : le cache par `cardHash`, puis l'éditeur

Décision 10. Le cache actuel ne sait ni retirer — `enabled: false` continue de servir le blob — ni remplacer sans montrer un instant l'ancienne photo. Une clé qui porte le `cardHash` règle les deux par construction.

**Files :**
- Modify : `src/frontend/src/modules/contacts/queries.ts:16` et `:63-72`
- Modify : `src/frontend/src/modules/contacts/useContactPhotoUrl.ts:9-10`
- Modify : `src/frontend/src/modules/contacts/ContactCard.tsx:68`
- Modify : `src/frontend/src/modules/contacts/ContactsLayout.tsx:137`
- Test : `src/frontend/src/modules/contacts/useContactPhotoUrl.test.tsx` *(nouveau)*

**Interfaces (produit pour la seconde moitié) :**
- `contactKeys.photo(accountId, id, cardHash)`.
- `useContactPhoto(id, hasPhoto, cardHash)`, `useContactPhotoUrl(contactId, hasPhoto, cardHash)` — `cardHash` est `string | null` (`ContactDetail.cardHash` est optionnel, l'API omettant les nulls).

- [ ] **Step 1 : écrire le test qui échoue**

`useContactPhotoUrl.test.tsx` :

```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { contactKeys } from './queries'
import { useContactPhotoUrl } from './useContactPhotoUrl'

vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'acc' }))

beforeEach(() => {
  URL.createObjectURL = vi.fn(() => 'blob:photo')
  URL.revokeObjectURL = vi.fn()
})

function withCache(seed: (client: QueryClient) => void) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, enabled: false } } })
  seed(client)
  return ({ children }: { children: ReactNode }) =>
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

describe('useContactPhotoUrl', () => {
  it('serves the blob cached under the card it was read at', () => {
    const wrapper = withCache(c =>
      c.setQueryData(contactKeys.photo('acc', 'c1', 'h1'), new Blob(['x'])))

    const { result } = renderHook(() => useContactPhotoUrl('c1', true, 'h1'), { wrapper })

    expect(result.current).toBe('blob:photo')
  })

  // Le retrait et le remplacement sont le même fait : la carte a changé, donc la clé aussi, donc
  // il n'y a rien de périmé à servir (décision 10).
  it('does not serve it under the next card hash', () => {
    const wrapper = withCache(c =>
      c.setQueryData(contactKeys.photo('acc', 'c1', 'h1'), new Blob(['x'])))

    const { result } = renderHook(() => useContactPhotoUrl('c1', false, 'h2'), { wrapper })

    expect(result.current).toBeNull()
  })
})
```

- [ ] **Step 2 : vérifier l'échec**

Run : `cd src/frontend && npx vitest run src/modules/contacts/useContactPhotoUrl.test.tsx`
Expected : FAIL — `contactKeys.photo` n'accepte que deux arguments.

- [ ] **Step 3 : la clé et le hook**

`queries.ts` :

```ts
  /** Keyed by the card's hash: a removal, a replacement and a stale entry are then three different
      keys, so react-query has nothing old left to serve while it refetches (décision 10). */
  photo: (accountId: string, id: string, cardHash: string) =>
    ['contacts', accountId, id, 'photo', cardHash] as const,
```

```ts
/** The avatar's bytes, asked for only once the card says there are some. */
export function useContactPhoto(id: string | null, hasPhoto: boolean, cardHash: string | null) {
  const accountId = useAccountId()

  return useQuery({
    queryKey: contactKeys.photo(accountId, id ?? '', cardHash ?? ''),
    queryFn: () => api.getContactPhoto(id) as Promise<Blob>,
    enabled: hasPhoto && id != null,
    staleTime: 5 * 60_000,
  })
}
```

- [ ] **Step 4 : le hook d'URL et ses deux appelants**

`useContactPhotoUrl.ts` :

```ts
export function useContactPhotoUrl(
  contactId: string | null, hasPhoto: boolean, cardHash: string | null): string | null {
  const { data: blob } = useContactPhoto(contactId, hasPhoto, cardHash)
```

`ContactCard.tsx:68` :

```tsx
  const photo = useContactPhotoUrl(contact?.id ?? null, detail?.hasPhoto ?? false, detail?.cardHash ?? null)
```

`ContactsLayout.tsx:137` :

```tsx
  const editorPhoto = useContactPhotoUrl(routeId ?? null, detail?.hasPhoto ?? false, detail?.cardHash ?? null)
```

- [ ] **Step 5 : vérifier que tout passe**

Run : `cd src/frontend && npx vitest run src/modules/contacts`
Expected : PASS — `ContactCard.test.tsx` et `ContactsLayout.test.tsx` compris.

- [ ] **Step 6 : commit**

```bash
git add src/frontend/src/modules/contacts/queries.ts \
        src/frontend/src/modules/contacts/useContactPhotoUrl.ts \
        src/frontend/src/modules/contacts/useContactPhotoUrl.test.tsx \
        src/frontend/src/modules/contacts/ContactCard.tsx \
        src/frontend/src/modules/contacts/ContactsLayout.tsx
git commit -F - <<'MSG'
fix(contacts): la cle du cache photo porte le cardHash

Retrait et remplacement ne servent plus le blob precedent.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PCtivr8FyYuYHxChFv1hVX
MSG
```

#### Seconde moitié : l'éditeur

Décision 9. L'éditeur ne fait toujours aucune requête. Il tient **ce que l'utilisateur a fait**, pas une valeur comparée à un départ : la prop `photo` vaut `null` au premier rendu et ne devient un object URL qu'une fois le blob téléchargé, donc un seed gelé au montage perdrait silencieusement un retrait.

**Files :**- Modify : `src/frontend/src/modules/contacts/contactTypes.ts:111-136`
- Modify : `src/frontend/src/modules/contacts/useCaptureContacts.ts:10-22`
- Modify : `src/frontend/src/modules/contacts/ContactEditView.tsx`
- Modify : `src/frontend/src/locales/en/contacts.json`, `src/frontend/src/locales/fr/contacts.json`
- Modify : `src/frontend/src/index.css:2582-2620`
- Test : `src/frontend/src/modules/contacts/ContactEditView.test.tsx`

**Interfaces (consomme) :** `reducePhoto`, `PHOTO_UNREADABLE`, `PHOTO_TOO_LARGE` (tâche 4) ; la forme filaire `photo?: string | null` (tâche 2).

- [ ] **Step 7 : écrire les tests qui échouent**

Dans `ContactEditView.test.tsx` :

```tsx
// Le réducteur est testé chez lui : ici on veut l'éditeur, pas le canvas.
vi.mock('./contactPhoto', () => ({
  PHOTO_UNREADABLE: 'editor.photoUnreadable',
  PHOTO_TOO_LARGE: 'editor.photoTooLarge',
  reducePhoto: vi.fn(async () => ({ base64: 'QUJD', blob: new Blob(['ABC']) })),
}))
```

```tsx
  it('submits the chosen photo as base64', async () => {
    const { onSave } = setup({ contact: bruno })

    await userEvent.upload(screen.getByTestId('editor-photo-input'), new File(['x'], 'p.jpg'))
    await userEvent.click(screen.getByRole('button', { name: 'editor.save' }))

    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ photo: 'QUJD' }))
  })

  it('shows the chosen photo at once', async () => {
    setup({ contact: bruno })

    await userEvent.upload(screen.getByTestId('editor-photo-input'), new File(['x'], 'p.jpg'))

    expect(screen.getByTestId('editor-photo')).toHaveAttribute('src', 'blob:preview')
  })

  it('submits an empty string when the seeded photo is removed', async () => {
    const { onSave } = setup({ contact: bruno, photo: 'blob:seeded' })

    await userEvent.click(screen.getByRole('button', { name: 'editor.removePhoto' }))
    await userEvent.click(screen.getByRole('button', { name: 'editor.save' }))

    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ photo: '' }))
  })

  // Le test qui tient la décision 9 : la prop arrive après le montage, comme le blob dans la vraie
  // application. Un seed gelé vaudrait null ici, et le retrait partirait en null. `setup` ne rend
  // qu'une fois, alors ce test-ci tient son propre rerender.
  it('still removes a photo that arrived after mount', async () => {
    const onSave = vi.fn<(draft: ContactDraft) => void>()
    const props = { contact: bruno, saving: false, error: null, onCancel: vi.fn(), onSave }
    const { rerender } = render(<ContactEditView {...props} photo={null} />)

    rerender(<ContactEditView {...props} photo="blob:late" />)
    await userEvent.click(screen.getByRole('button', { name: 'editor.removePhoto' }))
    await userEvent.click(screen.getByRole('button', { name: 'editor.save' }))

    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ photo: '' }))
  })

  it('returns to the seeded photo when a local choice is removed', async () => {
    const { onSave } = setup({ contact: bruno, photo: 'blob:seeded' })

    await userEvent.upload(screen.getByTestId('editor-photo-input'), new File(['x'], 'p.jpg'))
    await userEvent.click(screen.getByRole('button', { name: 'editor.removePhoto' }))
    await userEvent.click(screen.getByRole('button', { name: 'editor.save' }))

    expect(screen.getByTestId('editor-photo')).toHaveAttribute('src', 'blob:seeded')
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ photo: null }))
  })

  it('reports an unreadable file under the avatar, not in the banner', async () => {
    vi.mocked(reducePhoto).mockRejectedValueOnce(new Error('editor.photoUnreadable'))
    const { onSave } = setup({ contact: bruno })

    await userEvent.upload(screen.getByTestId('editor-photo-input'), new File(['x'], 'p.heic'))
    await userEvent.click(screen.getByRole('button', { name: 'editor.save' }))

    expect(screen.getByTestId('editor-photo-error')).toHaveTextContent('editor.photoUnreadable')
    expect(screen.queryByRole('alert')).toBeNull()
    expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ photo: null }))
  })
```

Prérequis dans le fichier :
- `URL.createObjectURL = vi.fn(() => 'blob:preview')` et `URL.revokeObjectURL = vi.fn()` dans un `beforeEach` (jsdom ne les implémente pas).
- `setup` accepte déjà `photo` par son `overrides` (`Partial<Parameters<typeof ContactEditView>[0]>`), et rend sans renvoyer `rerender` — d'où le rendu explicite du quatrième test.
- `import { reducePhoto } from './contactPhoto'` pour `vi.mocked`.

- [ ] **Step 8 : vérifier l'échec**

Run : `cd src/frontend && npx vitest run src/modules/contacts/ContactEditView.test.tsx`
Expected : FAIL — `editor-photo-input` introuvable.

- [ ] **Step 9 : le champ du draft et son autre producteur**

`contactTypes.ts`, dans `ContactDraft`, après `notes` :

```ts
  /** `null` = unchanged, `''` = removed, base64 = replaced — the convention of the other optional
      scalars. Never an object URL: the preview is the editor's business, not the wire's. */
  photo: string | null
```

`useCaptureContacts.ts`, dans `draftFor`, à côté de `notes: null` :

```ts
    website: null, notes: null, photo: null,
```

- [ ] **Step 10 : l'état d'action et ses gestes**

`ContactEditView.tsx` — imports (`useEffect`, `useRef`, `type ChangeEvent`, et le module) :

```tsx
import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { reducePhoto } from './contactPhoto'
```

Au-dessus du composant :

```tsx
/** What the user did to the photo, not what it is worth: the seeded picture arrives asynchronously
    (the layout resolves it from a query), so a value frozen at mount would read `null` and turn a
    removal into "unchanged" (décision 9). */
type PhotoChoice =
  | { kind: 'kept' }
  | { kind: 'removed' }
  | { kind: 'chosen'; base64: string; url: string }
```

Dans le composant, avec les autres états :

```tsx
  const [choice, setChoice] = useState<PhotoChoice>({ kind: 'kept' })
  const [photoError, setPhotoError] = useState<string | null>(null)
  const fileInput = useRef<HTMLInputElement>(null)

  // Révoqué avec le choix qui l'a créé : trois choix successifs tiendraient sinon trois images
  // pour la vie de l'onglet.
  useEffect(() => {
    if (choice.kind !== 'chosen') return
    return () => URL.revokeObjectURL(choice.url)
  }, [choice])

  const shownPhoto = choice.kind === 'chosen' ? choice.url : choice.kind === 'kept' ? photo : null
  const removable = choice.kind === 'chosen' || (choice.kind === 'kept' && photo != null)

  async function pickPhoto(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    // Vidé tout de suite : rechoisir le même fichier après une erreur ne lève aucun change sinon.
    event.target.value = ''
    if (!file) return

    setPhotoError(null)
    try {
      const { base64, blob } = await reducePhoto(file)
      setChoice({ kind: 'chosen', base64, url: URL.createObjectURL(blob) })
    } catch (error) {
      setPhotoError((error as Error).message)
    }
  }

  function removePhoto() {
    setPhotoError(null)
    setChoice(choice.kind === 'chosen' ? { kind: 'kept' } : { kind: 'removed' })
  }
```

Dans `submit`, à côté de `isFavorite` :

```tsx
      photo: choice.kind === 'chosen' ? choice.base64 : choice.kind === 'removed' ? '' : null,
```

- [ ] **Step 11 : le rendu du bloc face**

Remplacer le bloc de l'avatar dans `contact-editor-hero` :

```tsx
        {/* The face first, then the names beside it: what identifies the contact, before the ways
            of reaching them. The avatar is the file picker — no separate button competing with it. */}
        <div className="contact-editor-hero">
          <div className="contact-editor-face">
            <button type="button" className="contact-editor-avatar-button"
              aria-label={t('editor.changePhoto')} onClick={() => fileInput.current?.click()}>
              {shownPhoto && (
                <img className="contact-editor-avatar" src={shownPhoto} alt="" data-testid="editor-photo" />
              )}
              {!shownPhoto && initials !== '' && (
                <span className="contact-editor-avatar is-initials" data-testid="editor-initials">{initials}</span>
              )}
              {!shownPhoto && initials === '' && (
                <span className="contact-editor-avatar is-blank" data-testid="editor-avatar-blank">
                  <PersonPlusIcon />
                </span>
              )}
            </button>
            <input ref={fileInput} type="file" hidden data-testid="editor-photo-input"
              accept="image/jpeg,image/png,image/gif,image/webp" onChange={pickPhoto} />
            {removable && (
              <button type="button" className="contact-editor-photo-remove" onClick={removePhoto}>
                {t('editor.removePhoto')}
              </button>
            )}
            {/* Sous l'avatar et non dans le banner : c'est le champ qui a échoué, pas la sauvegarde. */}
            {photoError && (
              <p className="contact-editor-photo-error" data-testid="editor-photo-error">{t(photoError)}</p>
            )}
          </div>
          <div className="contact-editor-identity">
```

- [ ] **Step 12 : les libellés**

`locales/en/contacts.json`, dans `editor` :

```json
    "changePhoto": "Change photo",
    "removePhoto": "Remove photo",
    "photoUnreadable": "This file cannot be read. Choose a JPEG, PNG, GIF or WebP image.",
    "photoTooLarge": "This image is too large, even reduced. Choose another one.",
```

`locales/fr/contacts.json`, aux mêmes clés :

```json
    "changePhoto": "Modifier la photo",
    "removePhoto": "Retirer la photo",
    "photoUnreadable": "Ce fichier ne peut pas être lu. Choisissez une image JPEG, PNG, GIF ou WebP.",
    "photoTooLarge": "Cette image est trop lourde, même réduite. Choisissez-en une autre.",
```

- [ ] **Step 13 : le style**

`index.css`, après `.contact-editor-avatar.is-blank` :

```css
.contact-editor-face {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  flex: none;
}

/* The avatar is the picker: a bare disc until it is hovered or focused, so nothing competes with
   the face itself. */
.contact-editor-avatar-button {
  display: block;
  padding: 0;
  border: none;
  background: none;
  border-radius: 50%;
  cursor: pointer;
}

.contact-editor-avatar-button:hover .contact-editor-avatar,
.contact-editor-avatar-button:focus-visible .contact-editor-avatar {
  outline: 2px solid var(--action-primary);
  outline-offset: 2px;
}

.contact-editor-photo-remove {
  padding: 0;
  border: none;
  background: none;
  color: var(--text-muted);
  font-size: 12px;
  cursor: pointer;
  text-decoration: underline;
}

.contact-editor-photo-error {
  margin: 0;
  max-width: 140px;
  color: var(--danger);
  font-size: 12px;
  text-align: center;
}
```

- [ ] **Step 14 : vérifier que tout passe**

Run : `cd src/frontend && npx vitest run && npx tsc --noEmit`
Expected : PASS. `tsc` est ce qui prouve que `ContactDraft.photo` étant obligatoire, aucun producteur de draft n'a été oublié.

- [ ] **Step 15 : commit**

```bash
git add src/frontend/src/modules/contacts/ContactEditView.tsx \
        src/frontend/src/modules/contacts/ContactEditView.test.tsx \
        src/frontend/src/modules/contacts/contactTypes.ts \
        src/frontend/src/modules/contacts/useCaptureContacts.ts \
        src/frontend/src/locales/en/contacts.json src/frontend/src/locales/fr/contacts.json \
        src/frontend/src/index.css
git commit -F - <<'MSG'
feat(contacts): l'avatar de l'editeur se clique

Etat a trois cas plutot qu'un seed gele : la photo arrive apres le montage.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PCtivr8FyYuYHxChFv1hVX
MSG
```

---

### Task 6 : documentation et vérification manuelle

L'orientation EXIF ne se teste pas sous jsdom, qui ne décode aucune image : c'est une vérification humaine, et c'est le dernier verrou de la tranche.

**Files :**
- Modify : `docs/architecture-contacts.md`
- Modify : `docs/superpowers/carddav-4d-conformance.md`

- [ ] **Step 1 : documenter le chemin de la photo**

Dans `docs/architecture-contacts.md`, à la suite de la section qui décrit déjà `GET /Photo` et la projection `contact_photos`, ajouter une section « L'écriture de la photo (4f) » disant, en propre :

- le champ `photo` et ses trois valeurs (`null` / `""` / base64 nu), et que le serveur ne lit ni type MIME ni préfixe `data:` ;
- les trois refus (`not valid base64`, `not a JPEG, PNG, GIF or WebP image`, `exceeds 512 KB`), le plafond de 512 Ko décodés et celui d'1 Mo sur la carte qui reste au-dessus ;
- que la famille `PHOTO` entière est remplacée ou retirée, et la ligne écrite dans le dialecte de la carte (`ENCODING=b;TYPE=…` en 3.0, `data:` URI en 4.0) ;
- que le navigateur réduit à 1024 px, carré centré, JPEG sur fond blanc — donc pas de transparence, pas d'animation, pas de format d'origine conservé ;
- que 512 Ko ne borne que la porte webmail : une photo déposée en `PUT` CardDAV va jusqu'au plafond de la carte ;
- que l'ETag de `GET /Photo` reste le `cardHash`, donc qu'un renommage fait re-télécharger la photo — dette nommée, pas oubliée.

- [ ] **Step 2 : la vérification manuelle**

À faire dans un navigateur réel, sur le déploiement (pas en tout-local : `localhost` n'est pas dans les origines CORS de l'API dev) :

1. Sur **Chrome**, **Firefox** et **Safari**, ouvrir l'éditeur d'un contact, cliquer l'avatar, choisir **une photo de téléphone prise en portrait** (donc porteuse d'une balise EXIF `Orientation`). Vérifier que l'aperçu est **debout**, pas couché. C'est la seule preuve de `imageOrientation: 'from-image'`.
2. Sauver, recharger la fiche : la photo est debout aussi après l'aller-retour serveur.
3. Choisir une image PNG à fond transparent : le fond est **blanc**, pas noir.
4. « Remove », sauver, recharger : les initiales reviennent, et aucune photo ne réapparaît en changeant de contact et en revenant.
5. Sur un HEIC d'iPhone dans **Chrome** : l'erreur inline `photoUnreadable` s'affiche sous l'avatar, aucun banner, la sauvegarde reste possible.
6. Sur l'appareil DAVx⁵ de la campagne 4d : synchroniser, vérifier que la photo apparaît dans le contact Android, puis en changer depuis le téléphone et re-synchroniser vers le webmail.

Consigner le résultat des six points (navigateur, version, OK/KO) dans `docs/superpowers/carddav-4d-conformance.md`. **Un KO sur les points 1, 3 ou 6 rejette la tranche** : ce sont les trois que le code ne peut pas prouver seul.

- [ ] **Step 3 : commit**

```bash
git add docs/architecture-contacts.md docs/superpowers/carddav-4d-conformance.md
git commit -F - <<'MSG'
docs(contacts): le chemin d'ecriture de la photo et sa verification

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01PCtivr8FyYuYHxChFv1hVX
MSG
```

---

## Couverture de la spec

| Décision | Tâche |
|---|---|
| 1. La photo voyage avec le formulaire | 2 (le champ), 3 (la route) |
| 2. Convention `null` / `""` / base64, `PhotoPayload?` | 2 |
| 3. Les octets décident, sniff et nom partagés | 2 |
| 4. 512 Ko décodés, pas de garde de longueur, `RequestSizeLimit` | 2 (le plafond), 3 (la borne du corps) |
| 5. La famille `PHOTO` entière | 3 |
| 6. La ligne suit le dialecte, jamais échappée, `Emit` la pose | 3 |
| 6 bis. `LogicalLines` et `IsName` linéaires | 1 |
| 7. Le store ne change pas de forme, `hasPhoto` à la création | 3 |
| 8. Le navigateur réduit avant d'envoyer | 4 |
| 9. L'éditeur reste sans réseau, état à trois cas | 5 |
| 10. Le `cardHash` dans la clé du cache | 5 |
| API (trois 400, `RequestSizeLimit`) | 2, 3 |
| Ce que la tranche ne fait pas | 6 (documenté) |
