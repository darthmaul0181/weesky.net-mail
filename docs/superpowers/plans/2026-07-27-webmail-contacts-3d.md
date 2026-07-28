# Contacts 3d — Import et export CSV : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal :** donner au module Contacts un import CSV qui fusionne sur l'adresse et un export CSV
relisible par Rainloop, Outlook et par nous-mêmes.

**Architecture :** tout le travail CSV est au backend, en briques pures et indépendantes — un
lecteur et un écrivain CSV qui ignorent tout des contacts, un mappeur d'en-têtes qui ignore tout de
la base, un écrivain vCard, et une seule méthode de dépôt qui indexe le carnet, fusionne et
n'écrit qu'une fois. Le frontend n'ajoute qu'un pied de colonne, une mutation et une modale de
rapport.

**Tech Stack :** ASP.NET Core 10 / EF Core (Pomelo) / xUnit + Moq + EF InMemory côté backend ;
React 18 + TypeScript + TanStack Query + Vitest + Testing Library côté frontend. **Aucune nouvelle
dépendance NuGet ou npm.**

**Spec :** `docs/superpowers/specs/2026-07-27-webmail-contacts-3d-design.md`

## Global Constraints

- **Aucune dépendance ajoutée.** Le lecteur et l'écrivain CSV sont internes ; l'encodage de repli est
  `Encoding.Latin1`, natif .NET, jamais `System.Text.Encoding.CodePages`.
- **Plafonds existants, jamais redéclarés :** `ContactStore.MaxPerUser` = 5000,
  `ContactValidator.MaxAddressesPerContact` = 50. Le corps de l'import est plafonné à
  **5 × 1024 × 1024 octets**.
- **Une seule définition d'« adresse valable »** : le prédicat MimeKit de `ContactValidator`. Ne pas
  en écrire un second.
- **Une seule canonicalisation** : `IdentityResolver.Canonical`.
- **Style C#** : namespaces à portée de fichier, `sealed`, `internal` par défaut, records pour les
  DTO, constructeurs primaires pour l'injection, `CancellationToken` sur tout `async`, collection
  expressions (`[..]`).
- **Style commentaire** : ne commenter que ce que le code ne dit pas. Trois lignes maximum.
- **L'UI du site reste en anglais** ; les libellés ci-dessous sont à reprendre verbatim.
- **Chaque tâche se termine par un commit**, message concis (deux lignes maximum) et **jamais un
  message commençant ou finissant par `@`**.
- Backend : `dotnet test` depuis `src/snoopy.microservice` (jamais `--no-build` quand un fichier de
  test est créé). Frontend : `npm test` depuis `src/frontend`.

## Structure des fichiers

| Fichier | Responsabilité |
|---|---|
| `Services/Csv/CsvReader.cs` | octets → `CsvDocument` : BOM, encodage, séparateur, grammaire RFC 4180 |
| `Services/Csv/CsvWriter.cs` | en-tête + lignes → octets UTF-8 avec BOM |
| `Services/Contacts/ContactCsvMapper.cs` | `CsvDocument` → `ContactCsvRow[]` ; table d'intitulés |
| `Services/Contacts/ContactVCardWriter.cs` | `ContactCsvRow` → vCard 3.0 ou `null` |
| `Services/Contacts/ContactCsvExporter.cs` | `ContactView[]` → octets CSV |
| `Models/Contacts/ContactImportRow.cs` | l'entrée du dépôt, sans notion de CSV |
| `Models/Contacts/ContactImportReport.cs` | `ContactImportError`, `ContactImportOutcome`, `ContactImportReport` |
| `Repositories/ContactStore.cs` | `ImportAsync` : index, fusion, plafonds, un seul `SaveChanges` |
| `Controllers/ContactsController.cs` | `POST /Import`, `GET /Export` |
| `src/lib/downloadBlob.ts` | téléchargement d'un blob (extrait de `MessageReader`) |
| `src/modules/contacts/ContactsTransfer.tsx` | les deux boutons, l'input caché, la modale |
| `src/modules/contacts/ImportReportModal.tsx` | le rapport |

---

### Task 1 : lecteur CSV

**Files:**
- Create: `src/snoopy.microservice/Services/Csv/CsvReader.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/CsvReaderTests.cs`

**Interfaces:**
- Consumes: rien.
- Produces: `internal sealed record CsvRecord(int Line, IReadOnlyList<string> Fields)`,
  `internal sealed record CsvDocument(IReadOnlyList<string> Header, IReadOnlyList<CsvRecord> Rows)`,
  `internal static class CsvReader { internal static CsvDocument Read(byte[] content); }`.
  `Line` est le numéro de ligne **dans le fichier**, 1 pour l'en-tête, 2 pour la première ligne de
  données.

- [ ] **Step 1: Write the failing test**

Créer `snoopy.microservice.Tests/Services/CsvReaderTests.cs` :

```csharp
using System.Text;
using weesky.Snoopy.Microservice.Services.Csv;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class CsvReaderTests
{
    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    [Fact]
    public void Read_SplitsHeaderAndRows()
    {
        var document = CsvReader.Read(Utf8("First Name,Last Name\r\nBruno,Mertens\r\n"));

        Assert.Equal(["First Name", "Last Name"], document.Header);
        var row = Assert.Single(document.Rows);
        Assert.Equal(["Bruno", "Mertens"], row.Fields);
    }

    // The number the user reads in their spreadsheet, header included — not the data index.
    [Fact]
    public void Read_NumbersRowsFromTwo()
    {
        var document = CsvReader.Read(Utf8("A,B\r\nx,y\r\nz,w\r\n"));

        Assert.Equal([2, 3], document.Rows.Select(r => r.Line));
    }

    [Fact]
    public void Read_KeepsDelimiterInsideQuotes()
    {
        var document = CsvReader.Read(Utf8("A,B\r\n\"Mertens, Bruno\",x\r\n"));

        Assert.Equal("Mertens, Bruno", Assert.Single(document.Rows).Fields[0]);
    }

    [Fact]
    public void Read_KeepsNewlineInsideQuotes_AndCountsIt()
    {
        var document = CsvReader.Read(Utf8("A,B\r\n\"one\ntwo\",x\r\nlast,y\r\n"));

        Assert.Equal("one\ntwo", document.Rows[0].Fields[0]);
        Assert.Equal(4, document.Rows[1].Line);
    }

    [Fact]
    public void Read_UnescapesDoubledQuote()
    {
        var document = CsvReader.Read(Utf8("A\r\n\"say \"\"hi\"\"\"\r\n"));

        Assert.Equal("say \"hi\"", Assert.Single(document.Rows).Fields[0]);
    }

    // Excel in a French locale writes semicolons; read with a comma the file is one column, which
    // is not an error but an import that silently does nothing.
    [Theory]
    [InlineData(';')]
    [InlineData('\t')]
    public void Read_SniffsTheDelimiter(char delimiter)
    {
        var document = CsvReader.Read(Utf8($"First Name{delimiter}Last Name\r\nBruno{delimiter}Mertens\r\n"));

        Assert.Equal(["First Name", "Last Name"], document.Header);
        Assert.Equal(["Bruno", "Mertens"], Assert.Single(document.Rows).Fields);
    }

    [Fact]
    public void Read_StripsTheByteOrderMark()
    {
        var content = (byte[])[.. Encoding.UTF8.GetPreamble(), .. Utf8("First Name\r\nBruno\r\n")];

        Assert.Equal("First Name", Assert.Single(CsvReader.Read(content).Header));
    }

    // Outlook still exports Windows-1252, which Latin-1 matches on every accented letter a name
    // can carry.
    [Fact]
    public void Read_FallsBackToLatin1WhenNotUtf8()
    {
        var content = Encoding.Latin1.GetBytes("Name\r\nDupré\r\n");

        Assert.Equal("Dupré", Assert.Single(CsvReader.Read(content).Rows).Fields[0]);
    }

    [Fact]
    public void Read_DropsBlankLines()
    {
        var document = CsvReader.Read(Utf8("A,B\r\nx,y\r\n,\r\n\r\n"));

        Assert.Single(document.Rows);
    }

    [Fact]
    public void Read_AnswersEmptyForAnEmptyFile()
    {
        var document = CsvReader.Read([]);

        Assert.Empty(document.Header);
        Assert.Empty(document.Rows);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~CsvReaderTests`
Expected: échec de compilation — `CsvReader` n'existe pas.

- [ ] **Step 3: Write the implementation**

Créer `Services/Csv/CsvReader.cs` :

```csharp
using System.Text;

namespace weesky.Snoopy.Microservice.Services.Csv;

/// <summary>One record, carrying the file line it started on.</summary>
internal sealed record CsvRecord(int Line, IReadOnlyList<string> Fields);

internal sealed record CsvDocument(IReadOnlyList<string> Header, IReadOnlyList<CsvRecord> Rows);

/// <summary>
/// RFC 4180 with the three things a real file needs beyond it: a byte-order mark, an encoding that
/// may not be UTF-8, and a delimiter that may not be a comma.
/// </summary>
internal static class CsvReader
{
    private static readonly char[] Candidates = [',', ';', '\t'];

    internal static CsvDocument Read(byte[] content)
    {
        var text = Decode(content);
        if (text.Length == 0) return new CsvDocument([], []);

        var records = Parse(text, SniffDelimiter(text));
        return records.Count == 0
            ? new CsvDocument([], [])
            : new CsvDocument(records[0].Fields, [.. records.Skip(1)]);
    }

    /// <summary>
    /// UTF-8 when the bytes decode strictly, Latin-1 otherwise. Latin-1 differs from Windows-1252
    /// only over 0x80–0x9F — typographic quotes and the euro sign, never a letter in a name.
    /// </summary>
    private static string Decode(byte[] content)
    {
        var start = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF
            ? 3 : 0;
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true)
                .GetString(content, start, content.Length - start);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(content, start, content.Length - start);
        }
    }

    // Counted over the header record alone. Read with the wrong delimiter a file does not fail —
    // it comes back as one column, which is an import that silently does nothing.
    private static char SniffDelimiter(string text)
    {
        var best = ',';
        var bestCount = 0;

        foreach (var candidate in Candidates)
        {
            var count = CountInFirstRecord(text, candidate);
            if (count <= bestCount) continue;
            best = candidate;
            bestCount = count;
        }

        return best;
    }

    private static int CountInFirstRecord(string text, char delimiter)
    {
        var count = 0;
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"') continue;
                if (i + 1 < text.Length && text[i + 1] == '"') i++;
                else quoted = false;
            }
            else if (c == '"') quoted = true;
            else if (c == delimiter) count++;
            else if (c is '\r' or '\n') break;
        }

        return count;
    }

    private static List<CsvRecord> Parse(string text, char delimiter)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var line = 1;
        var recordLine = 1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else { if (c == '\n') line++; field.Append(c); }
                continue;
            }

            switch (c)
            {
                case '"': quoted = true; break;
                case '\r': break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    line++;
                    Flush(records, fields, recordLine);
                    fields.Clear();
                    recordLine = line;
                    break;
                default:
                    if (c == delimiter) { fields.Add(field.ToString()); field.Clear(); }
                    else field.Append(c);
                    break;
            }
        }

        fields.Add(field.ToString());
        Flush(records, fields, recordLine);
        return records;
    }

    // A record of nothing but empty fields is a spreadsheet's trailing line, never a contact.
    private static void Flush(List<CsvRecord> records, List<string> fields, int line)
    {
        if (fields.All(string.IsNullOrWhiteSpace)) return;
        records.Add(new CsvRecord(line, [.. fields]));
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~CsvReaderTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Services/Csv/CsvReader.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/CsvReaderTests.cs
git commit -F - <<'EOF'
Add a CSV reader for the contacts import

RFC 4180 plus BOM, encoding fallback and delimiter sniffing.
EOF
```

---

### Task 2 : écrivain CSV

**Files:**
- Create: `src/snoopy.microservice/Services/Csv/CsvWriter.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/CsvWriterTests.cs`

**Interfaces:**
- Consumes: rien.
- Produces: `internal static class CsvWriter { internal static byte[] Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows); }`.
  Sortie UTF-8 **précédée du BOM**, enregistrements séparés par `\r\n`, séparateur `,`.

- [ ] **Step 1: Write the failing test**

Créer `snoopy.microservice.Tests/Services/CsvWriterTests.cs` :

```csharp
using System.Text;
using weesky.Snoopy.Microservice.Services.Csv;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class CsvWriterTests
{
    private static string Text(byte[] content) =>
        new UTF8Encoding(false).GetString(content, 3, content.Length - 3);

    [Fact]
    public void Write_EmitsHeaderThenRows()
    {
        var content = CsvWriter.Write(["A", "B"], [["x", "y"]]);

        Assert.Equal("A,B\r\nx,y\r\n", Text(content));
    }

    // Without it Excel reads a UTF-8 export in 1252 and renders "Dupré" as "DuprÃ©".
    [Fact]
    public void Write_StartsWithTheByteOrderMark()
    {
        var content = CsvWriter.Write(["A"], []);

        Assert.Equal(Encoding.UTF8.GetPreamble(), content[..3]);
    }

    [Fact]
    public void Write_QuotesOnlyWhatNeedsIt()
    {
        var content = CsvWriter.Write(["A", "B", "C"], [["plain", "has,comma", "has\nnewline"]]);

        Assert.Equal("A,B,C\r\nplain,\"has,comma\",\"has\nnewline\"\r\n", Text(content));
    }

    [Fact]
    public void Write_DoublesAnEmbeddedQuote()
    {
        var content = CsvWriter.Write(["A"], [["say \"hi\""]]);

        Assert.Equal("A\r\n\"say \"\"hi\"\"\"\r\n", Text(content));
    }

    [Fact]
    public void Write_RoundTripsThroughTheReader()
    {
        var content = CsvWriter.Write(["A", "B"], [["Mertens, Bruno", "say \"hi\""]]);

        var row = Assert.Single(CsvReader.Read(content).Rows);
        Assert.Equal(["Mertens, Bruno", "say \"hi\""], row.Fields);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~CsvWriterTests`
Expected: échec de compilation — `CsvWriter` n'existe pas.

- [ ] **Step 3: Write the implementation**

Créer `Services/Csv/CsvWriter.cs` :

```csharp
using System.Text;

namespace weesky.Snoopy.Microservice.Services.Csv;

internal static class CsvWriter
{
    private const char Delimiter = ',';

    private static readonly char[] MustQuote = [Delimiter, '"', '\r', '\n'];

    /// <summary>
    /// UTF-8 with a byte-order mark: without it Excel reads the file in the system code page and
    /// mangles every accent. <see cref="CsvReader"/> strips it, so a round trip never sees it.
    /// </summary>
    internal static byte[] Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        AppendRecord(builder, header);
        foreach (var row in rows) AppendRecord(builder, row);

        return [.. Encoding.UTF8.GetPreamble(), .. new UTF8Encoding(false).GetBytes(builder.ToString())];
    }

    private static void AppendRecord(StringBuilder builder, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0) builder.Append(Delimiter);
            builder.Append(Quote(fields[i]));
        }

        builder.Append("\r\n");
    }

    // Quoted only where it has to be: a wholly quoted file is valid and unreadable at a glance.
    private static string Quote(string field) =>
        field.IndexOfAny(MustQuote) >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd src/snoopy.microservice && dotnet test --filter "FullyQualifiedName~Csv"`
Expected: PASS — lecteur et écrivain.

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Services/Csv/CsvWriter.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/CsvWriterTests.cs
git commit -F - <<'EOF'
Add a CSV writer for the contacts export

UTF-8 with a BOM so Excel reads the accents.
EOF
```

---

### Task 3 : mappeur d'en-têtes

**Files:**
- Create: `src/snoopy.microservice/Services/Contacts/ContactCsvMapper.cs`
- Modify: `src/snoopy.microservice/Services/ContactValidator.cs` (renommer `Parses` en `IsValidAddress` et le rendre `internal`)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ContactCsvMapperTests.cs`

**Interfaces:**
- Consumes: `CsvDocument`, `CsvRecord` (tâche 1) ; `IdentityResolver.Canonical(string)`.
- Produces:
  - `ContactValidator.IsValidAddress(string address) → bool` (internal)
  - `internal sealed record ContactCsvRow(int Line, string? FirstName, string? LastName, string? Nickname, bool IsFavorite, IReadOnlyList<string> Addresses, IReadOnlyList<string> RejectedAddresses, IReadOnlyDictionary<string, string> Extras)` — `Extras` est **clé normalisée → valeur**, jamais l'intitulé d'origine
  - `ContactCsvMapper.Map(CsvDocument document) → Result<IReadOnlyList<ContactCsvRow>>`
  - `ContactCsvMapper.NoRecognisedColumn` (const string)

- [ ] **Step 1: Write the failing test**

Créer `snoopy.microservice.Tests/Services/ContactCsvMapperTests.cs` :

```csharp
using System.Text;
using weesky.Snoopy.Microservice.Services.Contacts;
using weesky.Snoopy.Microservice.Services.Csv;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ContactCsvMapperTests
{
    private static IReadOnlyList<ContactCsvRow> Map(string csv)
    {
        var mapped = ContactCsvMapper.Map(CsvReader.Read(new UTF8Encoding(false).GetBytes(csv)));
        Assert.True(mapped.IsSuccess);
        return mapped.Value;
    }

    // The real Snappymail/Rainloop export, which is Outlook's column set.
    private const string RainloopHeader =
        "Title,First Name,Middle Name,Last Name,Nick Name,Display Name,Company,Department,Job Title," +
        "Office Location,E-mail Address,Notes,Web Page,Birthday,Other Email,Other Phone,Other Mobile," +
        "Mobile Phone,Home Email,Home Phone,Home Fax,Home Street,Home City,Home State,Home Postal Code," +
        "Home Country,Business Email,Business Phone,Business Fax,Business Street,Business City," +
        "Business State,Business Postal Code,Business Country";

    [Fact]
    public void Map_ReadsTheRainloopExport()
    {
        var row = Assert.Single(Map(RainloopHeader + "\r\n" +
            "Mr,Bruno,J,Mertens,bruno,Bruno Mertens,Weesky,Support,Engineer,Room 3," +
            "bruno@example.com,a note,https://x.be,1980-01-15,other@example.com,,,+32470000000," +
            "home@example.com,,,,,,,,,,,,,,,"));

        Assert.Equal("Bruno", row.FirstName);
        Assert.Equal("Mertens", row.LastName);
        Assert.Equal("bruno", row.Nickname);
        Assert.Equal(["bruno@example.com", "other@example.com", "home@example.com"], row.Addresses);
    }

    [Fact]
    public void Map_ReadsTheGoogleExport()
    {
        var row = Assert.Single(Map(
            "Given Name,Family Name,E-mail 1 - Value,E-mail 2 - Value\r\n" +
            "Bruno,Mertens,bruno@example.com,second@example.com"));

        Assert.Equal("Bruno", row.FirstName);
        Assert.Equal(["bruno@example.com", "second@example.com"], row.Addresses);
    }

    [Fact]
    public void Map_ReadsTheThunderbirdExport()
    {
        var row = Assert.Single(Map(
            "First Name,Last Name,Nickname,Primary Email,Secondary Email\r\n" +
            "Bruno,Mertens,bruno,bruno@example.com,second@example.com"));

        Assert.Equal("bruno", row.Nickname);
        Assert.Equal(["bruno@example.com", "second@example.com"], row.Addresses);
    }

    // Our own export numbers its extra address columns, and how many there are depends on the book
    // it came from — so a finite list would cap what we can read back from ourselves.
    [Fact]
    public void Map_ReadsOurOwnNumberedAddressColumns()
    {
        var row = Assert.Single(Map(
            "First Name,E-mail Address,E-mail 2 Address,E-mail 7 Address,Favorite\r\n" +
            "Bruno,a@example.com,b@example.com,c@example.com,true"));

        Assert.Equal(["a@example.com", "b@example.com", "c@example.com"], row.Addresses);
        Assert.True(row.IsFavorite);
    }

    [Theory]
    [InlineData("FIRST NAME")]
    [InlineData("first_name")]
    [InlineData("First-Name")]
    public void Map_IgnoresCaseAndSeparatorsInHeaders(string header)
    {
        Assert.Equal("Bruno", Assert.Single(Map($"{header}\r\nBruno")).FirstName);
    }

    // Position 0 is the primary, and the file's own column order is what decides it — no column is
    // named "the primary one".
    [Fact]
    public void Map_TakesAddressesInColumnOrder()
    {
        var row = Assert.Single(Map(
            "Other Email,E-mail Address\r\nother@example.com,main@example.com"));

        Assert.Equal(["other@example.com", "main@example.com"], row.Addresses);
    }

    [Fact]
    public void Map_DropsAnUnparsableAddressAndReportsIt()
    {
        var row = Assert.Single(Map(
            "First Name,E-mail Address,Other Email\r\nBruno,n/a,bruno@example.com"));

        Assert.Equal(["bruno@example.com"], row.Addresses);
        Assert.Equal(["n/a"], row.RejectedAddresses);
    }

    [Fact]
    public void Map_FoldsAnAddressRepeatedAcrossColumns()
    {
        var row = Assert.Single(Map(
            "E-mail Address,Home Email\r\nBruno@Example.com,bruno@example.com"));

        Assert.Equal(["Bruno@Example.com"], row.Addresses);
    }

    // Splitting it on a space would be guessing, and wrong on every compound name. The nickname is
    // exactly where displayNameOf looks next.
    [Fact]
    public void Map_UsesTheDisplayNameOnlyWhenNoNameAtAll()
    {
        var withName = Assert.Single(Map("First Name,Display Name\r\nBruno,Bruno Mertens"));
        var without = Assert.Single(Map("Display Name,E-mail Address\r\nBruno Mertens,b@example.com"));

        Assert.Null(withName.Nickname);
        Assert.Equal("Bruno Mertens", without.Nickname);
    }

    [Fact]
    public void Map_KeepsUnmodelledColumnsAsExtras()
    {
        var row = Assert.Single(Map(
            "First Name,Mobile Phone,Company,Empty Column\r\nBruno,+32470000000,Weesky,"));

        Assert.Equal("+32470000000", row.Extras["mobilephone"]);
        Assert.Equal("Weesky", row.Extras["company"]);
        Assert.False(row.Extras.ContainsKey("emptycolumn"));
    }

    // It catches the file read with the wrong delimiter, the one with no header row, and the one
    // that is not a CSV at all.
    [Fact]
    public void Map_RefusesAFileWithNoRecognisedColumn()
    {
        var mapped = ContactCsvMapper.Map(
            CsvReader.Read(new UTF8Encoding(false).GetBytes("Alpha,Beta\r\n1,2")));

        Assert.True(mapped.IsFailure);
        Assert.Equal(ContactCsvMapper.NoRecognisedColumn, mapped.Error);
    }

    [Fact]
    public void Map_ToleratesARowShorterThanTheHeader()
    {
        var row = Assert.Single(Map("First Name,Last Name,E-mail Address\r\nBruno"));

        Assert.Equal("Bruno", row.FirstName);
        Assert.Empty(row.Addresses);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~ContactCsvMapperTests`
Expected: échec de compilation — `ContactCsvMapper` n'existe pas.

- [ ] **Step 3: Expose the address predicate**

Dans `Services/ContactValidator.cs`, renommer la méthode privée `Parses` en `IsValidAddress` et la
rendre `internal`, en gardant son commentaire ; mettre à jour le seul appel dans `Validate` :

```csharp
    // MimeKit is the authority here as it is on the send path: a hand-rolled regex accepts and
    // rejects a different set than the library that will actually address the mail. Parsed with
    // RecipientAddressParser.Options — the shared policy every address field uses — because the
    // default options accept a bare local part with no domain (see its own doc comment).
    internal static bool IsValidAddress(string address) =>
        MailboxAddress.TryParse(RecipientAddressParser.Options, address, out var parsed) &&
        parsed.Address == address;
```

- [ ] **Step 4: Write the mapper**

Créer `Services/Contacts/ContactCsvMapper.cs` :

```csharp
using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using weesky.Snoopy.Microservice.Services.Csv;

namespace weesky.Snoopy.Microservice.Services.Contacts;

/// <summary>
/// One CSV row understood. <paramref name="Extras"/> is keyed by the normalised header, not the
/// spelling in the file: its only reader is the vCard writer, which recognises names rather than
/// prints them.
/// </summary>
internal sealed record ContactCsvRow(
    int Line,
    string? FirstName,
    string? LastName,
    string? Nickname,
    bool IsFavorite,
    IReadOnlyList<string> Addresses,
    IReadOnlyList<string> RejectedAddresses,
    IReadOnlyDictionary<string, string> Extras);

/// <summary>
/// The header table. Knows nothing about the database: it turns a parsed file into rows, and the
/// store turns rows into contacts.
/// </summary>
internal static partial class ContactCsvMapper
{
    internal const string NoRecognisedColumn =
        "No recognised column in this file. It needs a header row naming a name or an e-mail column.";

    private enum Column { Unknown, FirstName, LastName, Nickname, DisplayName, Address, Favorite }

    private static readonly HashSet<string> FirstNameKeys = ["firstname", "givenname", "prénom", "prenom"];
    private static readonly HashSet<string> LastNameKeys = ["lastname", "familyname", "surname", "nom"];
    private static readonly HashSet<string> NicknameKeys = ["nickname"];
    private static readonly HashSet<string> DisplayNameKeys = ["displayname", "name", "fullname"];
    private static readonly HashSet<string> FavoriteKeys = ["favorite", "favourite"];

    private static readonly HashSet<string> AddressKeys =
    [
        "emailaddress", "email", "otheremail", "homeemail", "businessemail",
        "primaryemail", "secondaryemail",
        "email1value", "email2value", "email3value", "email4value",
    ];

    // Our own export numbers its extra address columns, and their count follows the book it came
    // from — a finite list would cap what we can read back from ourselves.
    [GeneratedRegex(@"^email\d+address$")]
    private static partial Regex NumberedAddressKey();

    internal static Result<IReadOnlyList<ContactCsvRow>> Map(CsvDocument document)
    {
        var columns = document.Header.Select(Classify).ToArray();
        var usable = columns.Any(c =>
            c is Column.FirstName or Column.LastName or Column.Nickname or Column.DisplayName or Column.Address);
        if (!usable) return Result.Failure<IReadOnlyList<ContactCsvRow>>(NoRecognisedColumn);

        return Result.Success<IReadOnlyList<ContactCsvRow>>(
            [.. document.Rows.Select(record => MapRow(document.Header, columns, record))]);
    }

    private static Column Classify(string header)
    {
        var key = Normalise(header);
        if (FirstNameKeys.Contains(key)) return Column.FirstName;
        if (LastNameKeys.Contains(key)) return Column.LastName;
        if (NicknameKeys.Contains(key)) return Column.Nickname;
        if (DisplayNameKeys.Contains(key)) return Column.DisplayName;
        if (FavoriteKeys.Contains(key)) return Column.Favorite;
        if (AddressKeys.Contains(key) || NumberedAddressKey().IsMatch(key)) return Column.Address;
        return Column.Unknown;
    }

    /// <summary>
    /// Lower-cased with every separator dropped, so "E-mail 1 - Value", "e_mail_1_value" and
    /// "E-Mail 1 Value" are one key. Accents are kept — they are what tells "Prénom" apart.
    /// </summary>
    private static string Normalise(string header) =>
        new([.. header.ToLowerInvariant().Where(char.IsLetterOrDigit)]);

    private static ContactCsvRow MapRow(
        IReadOnlyList<string> header, Column[] columns, CsvRecord record)
    {
        string? first = null, last = null, nick = null, display = null;
        var favorite = false;
        var addresses = new List<string>();
        var rejected = new List<string>();
        var extras = new Dictionary<string, string>();
        var seen = new HashSet<string>();

        for (var i = 0; i < columns.Length; i++)
        {
            var value = i < record.Fields.Count ? record.Fields[i].Trim() : string.Empty;
            if (value.Length == 0) continue;

            switch (columns[i])
            {
                case Column.FirstName: first ??= value; break;
                case Column.LastName: last ??= value; break;
                case Column.Nickname: nick ??= value; break;
                case Column.DisplayName: display ??= value; break;
                case Column.Favorite: favorite = IsTruthy(value); break;
                case Column.Address:
                    if (!ContactValidator.IsValidAddress(value)) rejected.Add(value);
                    else if (seen.Add(IdentityResolver.Canonical(value))) addresses.Add(value);
                    break;
                default: extras[Normalise(header[i])] = value; break;
            }
        }

        // A fallback, never a field: splitting it on a space would be guessing, and wrong on every
        // compound name. The nickname is exactly where displayNameOf looks next.
        nick ??= first == null && last == null ? display : null;

        return new ContactCsvRow(record.Line, first, last, nick, favorite, addresses, rejected, extras);
    }

    private static bool IsTruthy(string value) =>
        value == "1"
        || value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || value.Equals("oui", StringComparison.OrdinalIgnoreCase)
        || value.Equals("x", StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~ContactCsvMapperTests`
Expected: PASS.

- [ ] **Step 6: Run the whole backend suite**

Run: `cd src/snoopy.microservice && dotnet test`
Expected: PASS — le renommage de `Parses` ne casse rien (`ContactValidatorTests` n'appelle que
`Validate`).

- [ ] **Step 7: Commit**

```bash
git add src/snoopy.microservice/Services/Contacts/ContactCsvMapper.cs \
        src/snoopy.microservice/Services/ContactValidator.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/ContactCsvMapperTests.cs
git commit -F - <<'EOF'
Map CSV headers onto contacts

Covers the Rainloop/Outlook, Google and Thunderbird exports plus our own.
EOF
```

---

### Task 4 : écrivain vCard

**Files:**
- Create: `src/snoopy.microservice/Services/Contacts/ContactVCardWriter.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ContactVCardWriterTests.cs`

**Interfaces:**
- Consumes: `ContactCsvRow` (tâche 3).
- Produces: `internal static class ContactVCardWriter { internal static string? Write(ContactCsvRow row); }` —
  `null` quand la ligne ne portait rien hors modèle.

- [ ] **Step 1: Write the failing test**

Créer `snoopy.microservice.Tests/Services/ContactVCardWriterTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Services.Contacts;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ContactVCardWriterTests
{
    private static ContactCsvRow Row(Dictionary<string, string> extras, string? first = "Bruno",
        string? last = "Mertens", string? nick = null, params string[] addresses) =>
        new(2, first, last, nick, false, addresses, [], extras);

    // A card repeating the columns next to it is a MEDIUMTEXT per contact with nothing to read back.
    [Fact]
    public void Write_AnswersNullWhenNothingIsOutsideTheModel()
    {
        Assert.Null(ContactVCardWriter.Write(Row([], addresses: "bruno@example.com")));
    }

    [Fact]
    public void Write_KeepsThePhones()
    {
        var card = ContactVCardWriter.Write(Row(new()
        {
            ["mobilephone"] = "+32470000000",
            ["homephone"] = "+3281000000",
            ["businessfax"] = "+3281000001",
        }))!;

        Assert.Contains("TEL;TYPE=CELL:+32470000000", card);
        Assert.Contains("TEL;TYPE=HOME,VOICE:+3281000000", card);
        Assert.Contains("TEL;TYPE=WORK,FAX:+3281000001", card);
    }

    [Fact]
    public void Write_KeepsTheOrganisationAndRole()
    {
        var card = ContactVCardWriter.Write(Row(new()
        {
            ["company"] = "Weesky", ["department"] = "Support", ["jobtitle"] = "Engineer",
        }))!;

        Assert.Contains("ORG:Weesky;Support", card);
        Assert.Contains("TITLE:Engineer", card);
    }

    [Fact]
    public void Write_KeepsThePostalAddresses()
    {
        var card = ContactVCardWriter.Write(Row(new()
        {
            ["homestreet"] = "Rue X 1", ["homecity"] = "Namur",
            ["homepostalcode"] = "5000", ["homecountry"] = "Belgium",
            ["businessstreet"] = "Rue Y 2", ["officelocation"] = "Room 3",
        }))!;

        Assert.Contains("ADR;TYPE=HOME:;;Rue X 1;Namur;;5000;Belgium", card);
        Assert.Contains("ADR;TYPE=WORK:;Room 3;Rue Y 2;;;;", card);
    }

    [Fact]
    public void Write_KeepsTheRemainingScalars()
    {
        var card = ContactVCardWriter.Write(Row(new()
        {
            ["notes"] = "a note", ["birthday"] = "1980-01-15", ["webpage"] = "https://x.be",
        }))!;

        Assert.Contains("NOTE:a note", card);
        Assert.Contains("BDAY:1980-01-15", card);
        Assert.Contains("URL:https://x.be", card);
    }

    // Outlook's "Title" is the honorific and its "Job Title" the role; the honorific has no property
    // of its own, it is N's fourth component.
    [Fact]
    public void Write_PutsTheMiddleNameAndHonorificInTheStructuredName()
    {
        var card = ContactVCardWriter.Write(Row(new() { ["middlename"] = "J", ["title"] = "Mr" }))!;

        Assert.Contains("N:Mertens;Bruno;J;Mr;", card);
        Assert.Contains("FN:Bruno J Mertens", card);
    }

    [Fact]
    public void Write_EmitsAWellFormedCardWithTheModelledFields()
    {
        var card = ContactVCardWriter.Write(
            Row(new() { ["notes"] = "x" }, nick: "bruno", addresses: "bruno@example.com"))!;

        Assert.StartsWith("BEGIN:VCARD\r\nVERSION:3.0\r\n", card);
        Assert.EndsWith("END:VCARD\r\n", card);
        Assert.Contains("NICKNAME:bruno", card);
        Assert.Contains("EMAIL;TYPE=INTERNET:bruno@example.com", card);
    }

    [Fact]
    public void Write_EscapesTheSeparators()
    {
        var card = ContactVCardWriter.Write(Row(new() { ["notes"] = "a; b, c\\d\ne" }))!;

        Assert.Contains(@"NOTE:a\; b\, c\\d\ne", card);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~ContactVCardWriterTests`
Expected: échec de compilation — `ContactVCardWriter` n'existe pas.

- [ ] **Step 3: Write the implementation**

Créer `Services/Contacts/ContactVCardWriter.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Services.Contacts;

/// <summary>
/// Turns the columns the tables do not model into a vCard 3.0, stored verbatim in
/// <c>contacts.vcard_raw</c>. Nothing reads it yet: it is what stops a phone number from being
/// destroyed by an import, per the slice 3a rule that an unstored property is found nowhere.
/// </summary>
internal static class ContactVCardWriter
{
    private const string Break = "\r\n";

    private static readonly (string Key, string Property)[] Phones =
    [
        ("mobilephone", "TEL;TYPE=CELL"),
        ("othermobile", "TEL;TYPE=CELL"),
        ("homephone", "TEL;TYPE=HOME,VOICE"),
        ("businessphone", "TEL;TYPE=WORK,VOICE"),
        ("homefax", "TEL;TYPE=HOME,FAX"),
        ("businessfax", "TEL;TYPE=WORK,FAX"),
        ("otherphone", "TEL;TYPE=VOICE"),
    ];

    private static readonly (string Key, string Property)[] Scalars =
    [
        ("jobtitle", "TITLE"),
        ("notes", "NOTE"),
        ("birthday", "BDAY"),
        ("webpage", "URL"),
    ];

    internal static string? Write(ContactCsvRow row)
    {
        var properties = new List<string>();

        foreach (var (key, property) in Phones)
            if (Value(row, key) is { } phone) properties.Add($"{property}:{Escape(phone)}");

        if (Value(row, "company") != null || Value(row, "department") != null)
            properties.Add($"ORG:{Escape(Value(row, "company"))};{Escape(Value(row, "department"))}");

        AppendAddress(properties, row, "home", "HOME", null);
        AppendAddress(properties, row, "business", "WORK", Value(row, "officelocation"));

        foreach (var (key, property) in Scalars)
            if (Value(row, key) is { } scalar) properties.Add($"{property}:{Escape(scalar)}");

        var middle = Value(row, "middlename");
        var honorific = Value(row, "title");
        if (properties.Count == 0 && middle == null && honorific == null) return null;

        var card = new List<string>
        {
            "BEGIN:VCARD",
            "VERSION:3.0",
            $"N:{Escape(row.LastName)};{Escape(row.FirstName)};{Escape(middle)};{Escape(honorific)};",
            $"FN:{Escape(FullName(row, middle))}",
        };
        if (row.Nickname != null) card.Add($"NICKNAME:{Escape(row.Nickname)}");
        card.AddRange(row.Addresses.Select(a => $"EMAIL;TYPE=INTERNET:{Escape(a)}"));
        card.AddRange(properties);
        card.Add("END:VCARD");

        return string.Join(Break, card) + Break;
    }

    // The seven vCard 3.0 components: po-box, extended, street, locality, region, code, country.
    // "Office Location" is the extended slot — the one place it means what it says.
    private static void AppendAddress(
        List<string> properties, ContactCsvRow row, string prefix, string type, string? extended)
    {
        string?[] parts =
        [
            null, extended, Value(row, $"{prefix}street"), Value(row, $"{prefix}city"),
            Value(row, $"{prefix}state"), Value(row, $"{prefix}postalcode"), Value(row, $"{prefix}country"),
        ];
        if (parts.All(p => p == null)) return;

        properties.Add($"ADR;TYPE={type}:{string.Join(';', parts.Select(Escape))}");
    }

    private static string? Value(ContactCsvRow row, string key) =>
        row.Extras.TryGetValue(key, out var value) ? value : null;

    private static string FullName(ContactCsvRow row, string? middle)
    {
        var parts = new[] { row.FirstName, middle, row.LastName }.Where(p => p != null);
        var full = string.Join(' ', parts);
        return full.Length > 0 ? full : row.Nickname ?? row.Addresses.FirstOrDefault() ?? string.Empty;
    }

    // Backslash first, or every escape written after it gets escaped a second time.
    private static string Escape(string? value) =>
        value == null ? string.Empty
            : value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
                   .Replace("\r\n", "\\n").Replace("\n", "\\n");
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~ContactVCardWriterTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Services/Contacts/ContactVCardWriter.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/ContactVCardWriterTests.cs
git commit -F - <<'EOF'
Keep unmodelled CSV columns as a vCard

Phones, org, postal addresses, notes, birthday and URL survive in vcard_raw.
EOF
```

---

### Task 5 : fusion à l'import dans le dépôt

**Files:**
- Create: `src/snoopy.microservice/Models/Contacts/ContactImportRow.cs`
- Create: `src/snoopy.microservice/Models/Contacts/ContactImportReport.cs`
- Modify: `src/snoopy.microservice/Repositories/IContactStore.cs`
- Modify: `src/snoopy.microservice/Repositories/ContactStore.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreImportTests.cs`

**Interfaces:**
- Consumes: `IdentityResolver.Canonical`, `ContactValidator.MaxAddressesPerContact`,
  `ContactStore.MaxPerUser`, `ContactStore.CapReached`.
- Produces:
  - `public sealed record ContactImportRow(int Line, string? FirstName, string? LastName, string? Nickname, bool IsFavorite, IReadOnlyList<string> Addresses, string? VCard)`
  - `public sealed record ContactImportError(int Line, string Reason)`
  - `public sealed record ContactImportOutcome(int Created, int Merged, int Skipped, int Failed, IReadOnlyList<ContactImportError> Errors)`
  - `public sealed record ContactImportReport(int Created, int Merged, int Skipped, int Failed, int TotalErrors, IReadOnlyList<ContactImportError> Errors)`
  - `IContactStore.ImportAsync(Guid userId, IReadOnlyList<ContactImportRow> rows, CancellationToken cancellationToken) → Task<ContactImportOutcome>`
  - `ContactStore.AmbiguousAddress`, `ContactStore.NoNameOrAddress`, `ContactStore.AddressCapReached` (messages)

- [ ] **Step 1: Write the failing test**

Créer `snoopy.microservice.Tests/Repositories/ContactStoreImportTests.cs` :

```csharp
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class ContactStoreImportTests
{
    private static ContactStore CreateStore(string dbName) => new(new PreferencesTestDbContext(dbName));

    private static ContactImportRow Row(
        int line = 2, string? first = null, string? last = null, string? nick = null,
        bool favorite = false, string? vcard = null, params string[] addresses) =>
        new(line, first, last, nick, favorite, addresses, vcard);

    private static ContactWrite Write(
        string? first = null, string? last = null, string? nick = null,
        bool favorite = false, params string[] addresses) =>
        new(first, last, nick, favorite, addresses, "manual");

    [Fact]
    public async Task Import_CreatesAnUnknownContact()
    {
        var db = nameof(Import_CreatesAnUnknownContact);
        var user = Guid.NewGuid();

        var outcome = await CreateStore(db).ImportAsync(
            user, [Row(first: "Bruno", addresses: "bruno@example.com")], CancellationToken.None);

        Assert.Equal(1, outcome.Created);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal("Bruno", stored.FirstName);
        Assert.Equal("bruno@example.com", Assert.Single(stored.Addresses));
    }

    [Fact]
    public async Task Import_FilesACreatedContactAsImported()
    {
        var db = nameof(Import_FilesACreatedContactAsImported);
        var user = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);

        await new ContactStore(context).ImportAsync(
            user, [Row(first: "Bruno", addresses: "bruno@example.com")], CancellationToken.None);

        Assert.Equal("imported", Assert.Single(new PreferencesTestDbContext(db).Contacts).Source);
    }

    // Nothing is ever overwritten: only the empty fields are filled in.
    [Fact]
    public async Task Import_MergesIntoTheContactHoldingTheAddress_WithoutOverwriting()
    {
        var db = nameof(Import_MergesIntoTheContactHoldingTheAddress_WithoutOverwriting);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(
            user, Write(first: "Bruno", addresses: "bruno@example.com"), CancellationToken.None);

        var outcome = await CreateStore(db).ImportAsync(user, [Row(
            first: "Brunon", last: "Mertens", addresses: ["bruno@example.com", "second@example.com"])],
            CancellationToken.None);

        Assert.Equal(1, outcome.Merged);
        Assert.Equal(0, outcome.Created);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal("Bruno", stored.FirstName);
        Assert.Equal("Mertens", stored.LastName);
        Assert.Equal(["bruno@example.com", "second@example.com"], stored.Addresses);
    }

    [Fact]
    public async Task Import_RaisesTheFavouriteButNeverLowersIt()
    {
        var db = nameof(Import_RaisesTheFavouriteButNeverLowersIt);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(
            user, Write(first: "Bruno", favorite: true, addresses: "bruno@example.com"), CancellationToken.None);

        await CreateStore(db).ImportAsync(
            user, [Row(addresses: "bruno@example.com")], CancellationToken.None);

        Assert.True(Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None)).IsFavorite);
    }

    // The open question slice 3a left here: an address on two cards names neither of them.
    [Fact]
    public async Task Import_SkipsARowWhoseAddressBelongsToTwoContacts()
    {
        var db = nameof(Import_SkipsARowWhoseAddressBelongsToTwoContacts);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(user, Write(first: "A", addresses: "shared@example.com"), CancellationToken.None);
        await CreateStore(db).CreateAsync(user, Write(first: "B", addresses: "shared@example.com"), CancellationToken.None);

        var outcome = await CreateStore(db).ImportAsync(
            user, [Row(line: 7, first: "C", addresses: "shared@example.com")], CancellationToken.None);

        Assert.Equal(1, outcome.Skipped);
        Assert.Equal(0, outcome.Created);
        var error = Assert.Single(outcome.Errors);
        Assert.Equal(7, error.Line);
        Assert.Equal(ContactStore.AmbiguousAddress, error.Reason);
    }

    [Fact]
    public async Task Import_SkipsARowReachingTwoDifferentContacts()
    {
        var db = nameof(Import_SkipsARowReachingTwoDifferentContacts);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(user, Write(first: "A", addresses: "a@example.com"), CancellationToken.None);
        await CreateStore(db).CreateAsync(user, Write(first: "B", addresses: "b@example.com"), CancellationToken.None);

        var outcome = await CreateStore(db).ImportAsync(
            user, [Row(addresses: ["a@example.com", "b@example.com"])], CancellationToken.None);

        Assert.Equal(1, outcome.Skipped);
        Assert.Equal(ContactStore.AmbiguousAddress, Assert.Single(outcome.Errors).Reason);
    }

    [Fact]
    public async Task Import_FailsARowWithNeitherNameNorAddress()
    {
        var db = nameof(Import_FailsARowWithNeitherNameNorAddress);

        var outcome = await CreateStore(db).ImportAsync(
            Guid.NewGuid(), [Row(line: 5, vcard: "BEGIN:VCARD")], CancellationToken.None);

        Assert.Equal(1, outcome.Failed);
        Assert.Equal(ContactStore.NoNameOrAddress, Assert.Single(outcome.Errors).Reason);
    }

    // A file listing one person twice must not leave two cards behind.
    [Fact]
    public async Task Import_MergesASecondRowIntoTheContactTheFirstOneCreated()
    {
        var db = nameof(Import_MergesASecondRowIntoTheContactTheFirstOneCreated);
        var user = Guid.NewGuid();

        var outcome = await CreateStore(db).ImportAsync(user,
        [
            Row(line: 2, first: "Bruno", addresses: "bruno@example.com"),
            Row(line: 3, last: "Mertens", addresses: ["bruno@example.com", "second@example.com"]),
        ], CancellationToken.None);

        Assert.Equal(1, outcome.Created);
        Assert.Equal(1, outcome.Merged);
        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal("Mertens", stored.LastName);
        Assert.Equal(["bruno@example.com", "second@example.com"], stored.Addresses);
    }

    [Fact]
    public async Task Import_KeepsTheVCardOnlyWhenThereWasNone()
    {
        var db = nameof(Import_KeepsTheVCardOnlyWhenThereWasNone);
        var user = Guid.NewGuid();

        await CreateStore(db).ImportAsync(user,
        [
            Row(line: 2, first: "Bruno", vcard: "FIRST", addresses: "bruno@example.com"),
            Row(line: 3, vcard: "SECOND", addresses: "bruno@example.com"),
        ], CancellationToken.None);

        Assert.Equal("FIRST", Assert.Single(new PreferencesTestDbContext(db).Contacts).VCardRaw);
    }

    [Fact]
    public async Task Import_StopsCreatingAtTheUserCap()
    {
        var db = nameof(Import_StopsCreatingAtTheUserCap);
        var user = Guid.NewGuid();
        var context = new PreferencesTestDbContext(db);
        for (var i = 0; i < ContactStore.MaxPerUser; i++)
            context.Contacts.Add(new weesky.Snoopy.Microservice.Data.Preferences.Contact
            {
                Id = Guid.NewGuid(), UserId = user, Uid = Guid.NewGuid().ToString(), FirstName = $"C{i}",
            });
        await context.SaveChangesAsync();

        var outcome = await new ContactStore(new PreferencesTestDbContext(db)).ImportAsync(
            user, [Row(line: 2, first: "Over", addresses: "over@example.com")], CancellationToken.None);

        Assert.Equal(0, outcome.Created);
        Assert.Equal(1, outcome.Skipped);
        Assert.Equal(ContactStore.CapReached, Assert.Single(outcome.Errors).Reason);
    }

    [Fact]
    public async Task Import_CapsTheAddressesOfOneContact()
    {
        var db = nameof(Import_CapsTheAddressesOfOneContact);
        var user = Guid.NewGuid();
        var many = Enumerable.Range(0, 60).Select(i => $"a{i}@example.com").ToArray();

        var outcome = await CreateStore(db).ImportAsync(
            user, [Row(first: "Bruno", addresses: many)], CancellationToken.None);

        var stored = Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None));
        Assert.Equal(50, stored.Addresses.Count);
        Assert.Equal(ContactStore.AddressCapReached, Assert.Single(outcome.Errors).Reason);
    }

    [Fact]
    public async Task Import_NeverReachesAnotherUsersBook()
    {
        var db = nameof(Import_NeverReachesAnotherUsersBook);
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await CreateStore(db).CreateAsync(theirs, Write(first: "Theirs", addresses: "shared@example.com"), CancellationToken.None);

        var outcome = await CreateStore(db).ImportAsync(
            mine, [Row(first: "Mine", addresses: "shared@example.com")], CancellationToken.None);

        Assert.Equal(1, outcome.Created);
        Assert.Single(await CreateStore(db).ListAsync(mine, CancellationToken.None));
        Assert.Single(await CreateStore(db).ListAsync(theirs, CancellationToken.None));
    }

    [Fact]
    public async Task Import_FoldsTheAddressBeforeMatching()
    {
        var db = nameof(Import_FoldsTheAddressBeforeMatching);
        var user = Guid.NewGuid();
        await CreateStore(db).CreateAsync(user, Write(first: "Bruno", addresses: "bruno@example.com"), CancellationToken.None);

        var outcome = await CreateStore(db).ImportAsync(
            user, [Row(last: "Mertens", addresses: " BRUNO@Example.COM ")], CancellationToken.None);

        Assert.Equal(1, outcome.Merged);
        Assert.Single(Assert.Single(await CreateStore(db).ListAsync(user, CancellationToken.None)).Addresses);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~ContactStoreImportTests`
Expected: échec de compilation — `ImportAsync` et les records n'existent pas.

- [ ] **Step 3: Add the models**

Créer `Models/Contacts/ContactImportRow.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Contacts;

/// <summary>
/// One row on its way into the book. Free of any notion of CSV, so the vCard import of the next
/// slice feeds the same merge rather than a second one.
/// </summary>
/// <param name="Line">The line in the source file, header included — what the user reads.</param>
public sealed record ContactImportRow(
    int Line,
    string? FirstName,
    string? LastName,
    string? Nickname,
    bool IsFavorite,
    IReadOnlyList<string> Addresses,
    string? VCard);
```

Créer `Models/Contacts/ContactImportReport.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Contacts;

public sealed record ContactImportError(int Line, string Reason);

/// <summary>What the store did. Its error list is unbounded — the controller caps it.</summary>
public sealed record ContactImportOutcome(
    int Created, int Merged, int Skipped, int Failed, IReadOnlyList<ContactImportError> Errors);

/// <summary>
/// What the client reads. The four counters count rows and add up to the file's data rows;
/// <paramref name="TotalErrors"/> counts every reason, including the ones past the cap on
/// <paramref name="Errors"/> — a wholly bad file must not answer ten thousand messages.
/// </summary>
public sealed record ContactImportReport(
    int Created, int Merged, int Skipped, int Failed, int TotalErrors,
    IReadOnlyList<ContactImportError> Errors);
```

- [ ] **Step 4: Extend the store interface**

Dans `Repositories/IContactStore.cs`, ajouter :

```csharp
    /// <summary>
    /// Merges a whole file into the book in one transaction. Never fails as a whole: a row that
    /// cannot be filed comes back in the outcome rather than as an error status.
    /// </summary>
    Task<ContactImportOutcome> ImportAsync(
        Guid userId, IReadOnlyList<ContactImportRow> rows, CancellationToken cancellationToken);
```

- [ ] **Step 5: Implement the merge**

Dans `Repositories/ContactStore.cs` :

1. Ajouter les messages, à côté de `CapReached` :

```csharp
    internal const string AmbiguousAddress =
        "An address on this row already belongs to more than one contact";

    internal const string NoNameOrAddress = "Neither a name nor a valid e-mail address";

    internal static readonly string AddressCapReached =
        $"Only the first {ContactValidator.MaxAddressesPerContact} addresses were kept";
```

2. Donner une position de départ à `AddAddresses` (signature seule ; les deux appels existants ne
   changent pas) :

```csharp
    private void AddAddresses(Guid contactId, IReadOnlyList<string> addresses, int startPosition = 0)
    {
        var seen = new HashSet<string>();
        var position = startPosition;
        // … corps inchangé
    }
```

3. Ajouter la méthode :

```csharp
    public async Task<ContactImportOutcome> ImportAsync(
        Guid userId, IReadOnlyList<ContactImportRow> rows, CancellationToken cancellationToken)
    {
        var stored = await context.Contacts.CountAsync(c => c.UserId == userId, cancellationToken);
        // The same correlated subquery ListAsync uses: MariaDB cannot parametrise a collection, so
        // an IN list of up to MaxPerUser ids would be inlined and defeat the plan cache.
        var addressRows = await context.ContactEmails.AsNoTracking()
            .Where(e => context.Contacts.Any(c => c.Id == e.ContactId && c.UserId == userId))
            .ToListAsync(cancellationToken);

        var owners = new Dictionary<string, HashSet<Guid>>();
        var held = new Dictionary<Guid, HashSet<string>>();
        var nextPosition = new Dictionary<Guid, int>();
        foreach (var row in addressRows)
        {
            Register(owners, held, row.ContactId, row.Address);
            nextPosition[row.ContactId] = Math.Max(nextPosition.GetValueOrDefault(row.ContactId), row.Position + 1);
        }

        var born = new Dictionary<Guid, Contact>();
        var merges = new List<(Guid Target, ContactImportRow Row, List<string> Addresses)>();
        var errors = new List<ContactImportError>();
        int created = 0, merged = 0, skipped = 0, failed = 0;

        foreach (var row in rows)
        {
            var canonical = row.Addresses.Select(IdentityResolver.Canonical).Distinct().ToList();
            if (row.FirstName == null && row.LastName == null && row.Nickname == null && canonical.Count == 0)
            {
                failed++;
                errors.Add(new ContactImportError(row.Line, NoNameOrAddress));
                continue;
            }

            var targets = canonical
                .SelectMany(a => owners.TryGetValue(a, out var set) ? set : [])
                .Distinct().ToList();
            if (targets.Count > 1)
            {
                skipped++;
                errors.Add(new ContactImportError(row.Line, AmbiguousAddress));
                continue;
            }

            if (targets.Count == 1)
            {
                var target = targets[0];
                var room = ContactValidator.MaxAddressesPerContact - held[target].Count;
                var incoming = canonical.Where(a => !held[target].Contains(a)).ToList();
                if (incoming.Count > room)
                {
                    incoming = [.. incoming.Take(Math.Max(room, 0))];
                    errors.Add(new ContactImportError(row.Line, AddressCapReached));
                }

                foreach (var address in incoming) Register(owners, held, target, address);
                merges.Add((target, row, incoming));
                merged++;
                continue;
            }

            if (stored + created >= MaxPerUser)
            {
                skipped++;
                errors.Add(new ContactImportError(row.Line, CapReached));
                continue;
            }

            var kept = canonical.Take(ContactValidator.MaxAddressesPerContact).ToList();
            if (kept.Count < canonical.Count) errors.Add(new ContactImportError(row.Line, AddressCapReached));

            var id = Guid.NewGuid();
            var contact = new Contact
            {
                Id = id,
                UserId = userId,
                Uid = id.ToString(),
                FirstName = row.FirstName,
                LastName = row.LastName,
                Nickname = row.Nickname,
                IsFavorite = row.IsFavorite,
                Source = "imported",
                VCardRaw = row.VCard,
                UpdatedAt = DateTime.UtcNow
            };
            context.Contacts.Add(contact);
            born[id] = contact;
            AddAddresses(id, kept);
            nextPosition[id] = kept.Count;
            foreach (var address in kept) Register(owners, held, id, address);
            created++;
        }

        await ApplyMergesAsync(userId, merges, born, nextPosition, cancellationToken);
        // One write for the whole file: a failure on the eight-hundredth row must not leave a book
        // half imported that no screen can describe.
        await context.SaveChangesAsync(cancellationToken);

        return new ContactImportOutcome(created, merged, skipped, failed, errors);
    }

    private async Task ApplyMergesAsync(
        Guid userId,
        List<(Guid Target, ContactImportRow Row, List<string> Addresses)> merges,
        Dictionary<Guid, Contact> born,
        Dictionary<Guid, int> nextPosition,
        CancellationToken cancellationToken)
    {
        if (merges.Count == 0) return;

        var wanted = merges.Select(m => m.Target).Where(id => !born.ContainsKey(id)).Distinct().ToList();
        var tracked = wanted.Count == 0
            ? []
            : await context.Contacts
                .Where(c => c.UserId == userId && wanted.Contains(c.Id))
                .ToListAsync(cancellationToken);
        var byId = tracked.ToDictionary(c => c.Id);

        foreach (var (target, row, addresses) in merges)
        {
            var contact = born.TryGetValue(target, out var fresh) ? fresh : byId[target];
            var changed = false;

            if (contact.FirstName == null && row.FirstName != null) { contact.FirstName = row.FirstName; changed = true; }
            if (contact.LastName == null && row.LastName != null) { contact.LastName = row.LastName; changed = true; }
            if (contact.Nickname == null && row.Nickname != null) { contact.Nickname = row.Nickname; changed = true; }
            if (!contact.IsFavorite && row.IsFavorite) { contact.IsFavorite = true; changed = true; }
            if (contact.VCardRaw == null && row.VCard != null) { contact.VCardRaw = row.VCard; changed = true; }

            if (addresses.Count > 0)
            {
                AddAddresses(target, addresses, nextPosition.GetValueOrDefault(target));
                nextPosition[target] = nextPosition.GetValueOrDefault(target) + addresses.Count;
                changed = true;
            }

            // Only when something moved: updated_at is what a future CardDAV ETag rests on, and a
            // replayed file that changes nothing must not make every client resync.
            if (changed) contact.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static void Register(
        Dictionary<string, HashSet<Guid>> owners, Dictionary<Guid, HashSet<string>> held,
        Guid contactId, string address)
    {
        if (!owners.TryGetValue(address, out var contacts)) owners[address] = contacts = [];
        contacts.Add(contactId);

        if (!held.TryGetValue(contactId, out var addresses)) held[contactId] = addresses = [];
        addresses.Add(address);
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~ContactStoreImportTests`
Expected: PASS.

- [ ] **Step 7: Run the whole backend suite**

Run: `cd src/snoopy.microservice && dotnet test`
Expected: PASS. Si `ContactsControllerTests` ne compile plus, c'est que `Mock<IContactStore>` doit
connaître la nouvelle méthode — Moq la génère seul, aucune action n'est requise.

- [ ] **Step 8: Commit**

```bash
git add src/snoopy.microservice/Models/Contacts/ContactImportRow.cs \
        src/snoopy.microservice/Models/Contacts/ContactImportReport.cs \
        src/snoopy.microservice/Repositories/IContactStore.cs \
        src/snoopy.microservice/Repositories/ContactStore.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreImportTests.cs
git commit -F - <<'EOF'
Merge an imported book on the address

Nothing is overwritten, an ambiguous address is skipped, one write for the file.
EOF
```

---

### Task 6 : exportateur CSV

**Files:**
- Create: `src/snoopy.microservice/Services/Contacts/ContactCsvExporter.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ContactCsvExporterTests.cs`

**Interfaces:**
- Consumes: `CsvWriter.Write` (tâche 2), `ContactView`.
- Produces: `internal static class ContactCsvExporter { internal static byte[] Write(IReadOnlyList<ContactView> contacts); }`

- [ ] **Step 1: Write the failing test**

Créer `snoopy.microservice.Tests/Services/ContactCsvExporterTests.cs` :

```csharp
using System.Text;
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Contacts;
using weesky.Snoopy.Microservice.Services.Csv;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ContactCsvExporterTests
{
    private static ContactView Contact(
        string? first = null, string? last = null, string? nick = null,
        bool favorite = false, params string[] addresses) =>
        new(Guid.NewGuid(), first, last, nick, favorite, addresses);

    private static CsvDocument Parse(byte[] content) => CsvReader.Read(content);

    [Fact]
    public void Write_EmitsTheColumnsRainloopUnderstands()
    {
        var document = Parse(ContactCsvExporter.Write(
            [Contact(first: "Bruno", last: "Mertens", addresses: "bruno@example.com")]));

        Assert.Equal(
            ["First Name", "Last Name", "Nick Name", "Display Name", "E-mail Address", "Favorite"],
            document.Header);
        Assert.Equal(
            ["Bruno", "Mertens", "", "Bruno Mertens", "bruno@example.com", ""],
            Assert.Single(document.Rows).Fields);
    }

    // A column empty across the whole file is noise, and a fixed ceiling would lose addresses.
    [Fact]
    public void Write_SizesTheAddressColumnsToTheFullestContact()
    {
        var document = Parse(ContactCsvExporter.Write(
        [
            Contact(first: "A", addresses: "a@example.com"),
            Contact(first: "B", addresses: ["b1@example.com", "b2@example.com", "b3@example.com"]),
        ]));

        Assert.Equal(
            ["E-mail Address", "E-mail 2 Address", "E-mail 3 Address"],
            document.Header.Skip(4).Take(3));
    }

    [Fact]
    public void Write_MarksAFavourite()
    {
        var document = Parse(ContactCsvExporter.Write([Contact(first: "A", favorite: true, addresses: "a@example.com")]));

        Assert.Equal("true", Assert.Single(document.Rows).Fields[^1]);
    }

    // Written verbatim it would come back as a nickname on the next import, which is not a name the
    // user ever typed.
    [Fact]
    public void Write_LeavesTheDisplayNameEmptyForAnAddressOnlyContact()
    {
        var document = Parse(ContactCsvExporter.Write([Contact(addresses: "a@example.com")]));

        Assert.Equal("", Assert.Single(document.Rows).Fields[3]);
    }

    [Fact]
    public void Write_FallsBackToTheNicknameForTheDisplayName()
    {
        var document = Parse(ContactCsvExporter.Write([Contact(nick: "bruno", addresses: "a@example.com")]));

        Assert.Equal("bruno", Assert.Single(document.Rows).Fields[3]);
    }

    [Fact]
    public void Write_AnswersAHeaderOnlyFileForAnEmptyBook()
    {
        var document = Parse(ContactCsvExporter.Write([]));

        Assert.NotEmpty(document.Header);
        Assert.Empty(document.Rows);
    }

    // The claim the whole slice rests on: what we write, we read back — and reading it back a
    // second time creates nothing.
    [Fact]
    public async Task Write_RoundTripsThroughTheImport()
    {
        var db = nameof(Write_RoundTripsThroughTheImport);
        var user = Guid.NewGuid();
        var store = new ContactStore(new PreferencesTestDbContext(db));
        await store.CreateAsync(user, new ContactWrite("Bruno", "Mertens", "bruno", true,
            ["bruno@example.com", "second@example.com"], "manual"), CancellationToken.None);

        var book = await new ContactStore(new PreferencesTestDbContext(db)).ListAsync(user, CancellationToken.None);
        var mapped = ContactCsvMapper.Map(CsvReader.Read(ContactCsvExporter.Write(book)));
        Assert.True(mapped.IsSuccess);

        var outcome = await new ContactStore(new PreferencesTestDbContext(db)).ImportAsync(
            user,
            [.. mapped.Value.Select(r => new ContactImportRow(
                r.Line, r.FirstName, r.LastName, r.Nickname, r.IsFavorite, r.Addresses, null))],
            CancellationToken.None);

        Assert.Equal(0, outcome.Created);
        Assert.Equal(1, outcome.Merged);
        var after = Assert.Single(await new ContactStore(new PreferencesTestDbContext(db))
            .ListAsync(user, CancellationToken.None));
        Assert.Equal("bruno", after.Nickname);
        Assert.True(after.IsFavorite);
        Assert.Equal(["bruno@example.com", "second@example.com"], after.Addresses);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~ContactCsvExporterTests`
Expected: échec de compilation — `ContactCsvExporter` n'existe pas.

- [ ] **Step 3: Write the implementation**

Créer `Services/Contacts/ContactCsvExporter.cs` :

```csharp
using weesky.Snoopy.Microservice.Models.Contacts;
using weesky.Snoopy.Microservice.Services.Csv;

namespace weesky.Snoopy.Microservice.Services.Contacts;

/// <summary>
/// The book as a file. The first address goes in the column Rainloop and Outlook understand, the
/// rest in columns only we read back — so the export is complete here and usable elsewhere rather
/// than truncated on both sides.
/// </summary>
internal static class ContactCsvExporter
{
    internal static byte[] Write(IReadOnlyList<ContactView> contacts)
    {
        var ordered = contacts.OrderBy(SortKey, StringComparer.InvariantCultureIgnoreCase).ToList();
        var addressColumns = ordered.Count == 0 ? 1 : Math.Max(1, ordered.Max(c => c.Addresses.Count));

        List<string> header = ["First Name", "Last Name", "Nick Name", "Display Name", "E-mail Address"];
        for (var i = 2; i <= addressColumns; i++) header.Add($"E-mail {i} Address");
        header.Add("Favorite");

        return CsvWriter.Write(header, ordered.Select(contact => Row(contact, addressColumns)));
    }

    private static IReadOnlyList<string> Row(ContactView contact, int addressColumns)
    {
        List<string> fields =
        [
            contact.FirstName ?? string.Empty,
            contact.LastName ?? string.Empty,
            contact.Nickname ?? string.Empty,
            NameOf(contact),
        ];
        for (var i = 0; i < addressColumns; i++)
            fields.Add(i < contact.Addresses.Count ? contact.Addresses[i] : string.Empty);
        fields.Add(contact.IsFavorite ? "true" : string.Empty);

        return fields;
    }

    /// <summary>
    /// Mirrors the frontend's displayNameOf, minus its address fallback: written verbatim, an
    /// address would come back as a nickname on the next import — a name nobody typed.
    /// </summary>
    private static string NameOf(ContactView contact)
    {
        var full = string.Join(' ', new[] { contact.FirstName, contact.LastName }.Where(n => n != null));
        return full.Length > 0 ? full : contact.Nickname ?? string.Empty;
    }

    // Deterministic order: the list endpoint has none, and a file whose rows move between two
    // exports of an unchanged book is undiffable.
    private static string SortKey(ContactView contact)
    {
        var name = NameOf(contact);
        return name.Length > 0 ? name : contact.Addresses.FirstOrDefault() ?? string.Empty;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~ContactCsvExporterTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Services/Contacts/ContactCsvExporter.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/ContactCsvExporterTests.cs
git commit -F - <<'EOF'
Export the contacts book as CSV

Primary address in the column other clients read, extras in ours.
EOF
```

---

### Task 7 : les deux routes

**Files:**
- Modify: `src/snoopy.microservice/Controllers/ContactsController.cs`
- Modify: `src/snoopy.microservice/CLAUDE.md`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/ContactsControllerTests.cs`

**Interfaces:**
- Consumes: tout ce qui précède.
- Produces: `POST /api/Contacts/Import` (`IFormFile? file`) → `ContactImportReport` ;
  `GET /api/Contacts/Export` → `text/csv` en pièce jointe nommée `contacts-AAAA-MM-JJ.csv`.

- [ ] **Step 1: Write the failing test**

Ajouter à `ContactsControllerTests.cs` (garder les `using` existants, ajouter
`using Microsoft.AspNetCore.Http;` et `using System.Text;`) :

```csharp
    private static IFormFile FileOf(string csv)
    {
        var bytes = new UTF8Encoding(false).GetBytes(csv);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "contacts.csv");
    }

    [Fact]
    public async Task Import_Returns200WithTheReport()
    {
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ContactImportOutcome(2, 1, 0, 0, []));

        var result = await CreateController().Import(
            FileOf("First Name,E-mail Address\r\nBruno,bruno@example.com"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var report = Assert.IsType<ContactImportReport>(ok.Value);
        Assert.Equal(2, report.Created);
        Assert.Equal(1, report.Merged);
    }

    [Fact]
    public async Task Import_HandsTheStoreTheMappedRowsAndTheirVCard()
    {
        IReadOnlyList<ContactImportRow>? seen = null;
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .Callback<Guid, IReadOnlyList<ContactImportRow>, CancellationToken>((_, rows, _) => seen = rows)
              .ReturnsAsync(new ContactImportOutcome(1, 0, 0, 0, []));

        await CreateController().Import(
            FileOf("First Name,E-mail Address,Mobile Phone\r\nBruno,bruno@example.com,+32470000000"),
            CancellationToken.None);

        var row = Assert.Single(seen!);
        Assert.Equal("Bruno", row.FirstName);
        Assert.Equal(2, row.Line);
        Assert.Contains("TEL;TYPE=CELL:+32470000000", row.VCard);
    }

    // An address the file spelled wrong is dropped, not fatal — and the report has to say so.
    [Fact]
    public async Task Import_ReportsADroppedAddressWithoutFailingItsRow()
    {
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ContactImportOutcome(1, 0, 0, 0, []));

        var result = await CreateController().Import(
            FileOf("First Name,E-mail Address,Other Email\r\nBruno,n/a,bruno@example.com"),
            CancellationToken.None);

        var report = Assert.IsType<ContactImportReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, report.Created);
        Assert.Equal(0, report.Failed);
        Assert.Contains("n/a", Assert.Single(report.Errors).Reason);
    }

    [Fact]
    public async Task Import_CapsTheErrorListAndCountsThemAll()
    {
        var many = Enumerable.Range(0, 60)
            .Select(i => new ContactImportError(i + 2, "bad")).ToList();
        _store.Setup(s => s.ImportAsync(Uid, It.IsAny<IReadOnlyList<ContactImportRow>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new ContactImportOutcome(0, 0, 0, 60, many));

        var result = await CreateController().Import(
            FileOf("First Name\r\nBruno"), CancellationToken.None);

        var report = Assert.IsType<ContactImportReport>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(60, report.TotalErrors);
        Assert.Equal(50, report.Errors.Count);
    }

    [Fact]
    public async Task Import_Returns400WithoutAFile()
    {
        var result = await CreateController().Import(null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // What catches the file read with the wrong delimiter and the one that is not a CSV at all.
    [Fact]
    public async Task Import_Returns400WhenNoColumnIsRecognised()
    {
        var result = await CreateController().Import(FileOf("Alpha,Beta\r\n1,2"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _store.Verify(s => s.ImportAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ContactImportRow>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Export_AnswersACsvAttachment()
    {
        _store.Setup(s => s.ListAsync(Uid, It.IsAny<CancellationToken>()))
              .ReturnsAsync([new ContactView(Guid.NewGuid(), "Bruno", "Mertens", null, false, ["bruno@example.com"])]);

        var result = await CreateController().Export(CancellationToken.None);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);
        Assert.StartsWith("contacts-", file.FileDownloadName);
        Assert.EndsWith(".csv", file.FileDownloadName);
        Assert.Contains("bruno@example.com", Encoding.UTF8.GetString(file.FileContents));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~ContactsControllerTests`
Expected: échec de compilation — `Import` et `Export` n'existent pas.

- [ ] **Step 3: Write the two actions**

Dans `Controllers/ContactsController.cs`, ajouter les `using` nécessaires
(`weesky.Snoopy.Microservice.Services.Contacts`, `weesky.Snoopy.Microservice.Services.Csv`) puis :

```csharp
    /// <summary>
    /// What bounds the request. A constant rather than configuration, so the attribute can carry
    /// it and the read is capped before model binding buffers the body to disk.
    /// </summary>
    private const int MaxImportBytes = 5 * 1024 * 1024;

    private const int MaxReportedErrors = 50;

    /// <summary>
    /// Merges a CSV file into the book and answers what it did. Nothing is overwritten and a row
    /// whose address is already on two contacts is skipped rather than filed at random.
    /// </summary>
    /// <param name="file">the CSV file</param>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The report</response>
    /// <response code="400">No file, an empty file, or no recognised column</response>
    /// <response code="401">Not authenticated</response>
    [HttpPost("Import")]
    [RequestSizeLimit(MaxImportBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ContactImportReport>> Import(
        IFormFile? file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0) return BadRequestEnveloppe("A file is required");

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        var document = CsvReader.Read(buffer.ToArray());
        if (document.Header.Count == 0) return BadRequestEnveloppe("The file is empty");

        var mapped = ContactCsvMapper.Map(document);
        if (mapped.IsFailure) return BadRequestEnveloppe(mapped.Error);

        var rows = mapped.Value;
        var outcome = await store.ImportAsync(
            AuthenticatedUser.WebmailUid,
            [.. rows.Select(r => new ContactImportRow(
                r.Line, r.FirstName, r.LastName, r.Nickname, r.IsFavorite, r.Addresses,
                ContactVCardWriter.Write(r)))],
            cancellationToken);

        // The store's reasons and the mapper's dropped addresses are one list to the reader: both
        // name a line in the file they can go and look at.
        var errors = outcome.Errors
            .Concat(rows.SelectMany(r => r.RejectedAddresses.Select(a =>
                new ContactImportError(r.Line, $"'{a}' is not a valid e-mail address and was ignored"))))
            .OrderBy(e => e.Line)
            .ToList();

        return Ok(new ContactImportReport(
            outcome.Created, outcome.Merged, outcome.Skipped, outcome.Failed,
            errors.Count, [.. errors.Take(MaxReportedErrors)]));
    }

    /// <summary>The whole book as a CSV file, in the columns other clients read.</summary>
    /// <param name="cancellationToken">cancellation token</param>
    /// <response code="200">The file</response>
    /// <response code="401">Not authenticated</response>
    [HttpGet("Export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> Export(CancellationToken cancellationToken)
    {
        var contacts = await store.ListAsync(AuthenticatedUser.WebmailUid, cancellationToken);

        return File(ContactCsvExporter.Write(contacts), "text/csv",
            $"contacts-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd src/snoopy.microservice && dotnet test --filter FullyQualifiedName~ContactsControllerTests`
Expected: PASS.

- [ ] **Step 5: Run the whole backend suite**

Run: `cd src/snoopy.microservice && dotnet test`
Expected: PASS.

- [ ] **Step 6: Document the two routes**

Dans `src/snoopy.microservice/CLAUDE.md`, à la fin du paragraphe `ContactsController` (celui qui se
termine par « … exactly why message flags are not sent as a whole message either »), ajouter :

> `POST /api/Contacts/Import` (multipart, capped at 5 MB by `[RequestSizeLimit]` so the read is
> bounded before `IFormFile` buffers it) and `GET /api/Contacts/Export` (`text/csv`, one attachment)
> are the CSV pair. **The import merges on the address and overwrites nothing**: a row whose address
> is already known to exactly one contact fills that contact's empty fields and appends its unknown
> addresses, one known to several contacts is skipped as ambiguous — the question slice 3a left open,
> answered by refusing to pick — and anything else is created with `source = "imported"`. It is one
> `SaveChanges` for the whole file. **Columns we do not model do not vanish**: `ContactVCardWriter`
> turns phones, org, postal addresses, notes, birthday and URL into a vCard 3.0 stored in
> `vcard_raw`, which nothing reads yet and which is what stops an import from destroying them. The
> export writes the primary address in `E-mail Address` — the column Rainloop and Outlook read — and
> the rest in `E-mail N Address`, which only we read back; the round trip through our own file is a
> tested claim, but a **CSV round trip does not carry `vcard_raw` back out**, since re-emitting it
> needs the vCard reader of the next slice.

- [ ] **Step 7: Commit**

```bash
git add src/snoopy.microservice/Controllers/ContactsController.cs \
        src/snoopy.microservice/CLAUDE.md \
        src/snoopy.microservice/snoopy.microservice.Tests/Controllers/ContactsControllerTests.cs
git commit -F - <<'EOF'
Add the contacts CSV import and export routes

Import answers a per-line report; export is one text/csv attachment.
EOF
```

---

### Task 8 : couche API et donnée du frontend

**Files:**
- Create: `src/frontend/src/lib/downloadBlob.ts`
- Modify: `src/frontend/src/api.js`
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx`
- Modify: `src/frontend/src/modules/contacts/contactTypes.ts`
- Modify: `src/frontend/src/modules/contacts/queries.ts`
- Test: `src/frontend/src/api.test.js`

**Interfaces:**
- Consumes: `POST /api/Contacts/Import`, `GET /api/Contacts/Export` (tâche 7).
- Produces:
  - `downloadBlob(blob: Blob, fileName: string): void`
  - `api.importContacts(file: File): Promise<ContactImportReport>`
  - `api.exportContacts(): Promise<{ blob: Blob, fileName: string }>`
  - `ContactImportError { line: number; reason: string }`,
    `ContactImportReport { created, merged, skipped, failed, totalErrors, errors }`
  - `useImportContacts()` — mutation prenant un `File` et rendant un `ContactImportReport`

- [ ] **Step 1: Write the failing test**

Ajouter à `src/frontend/src/api.test.js`, dans le corps du `describe` de plus haut niveau :

```js
  it('posts an imported CSV as multipart without a JSON content type', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true, status: 200, json: async () => ({ created: 1, merged: 0, skipped: 0, failed: 0, totalErrors: 0, errors: [] }),
    })

    const file = new File(['First Name\r\nBruno'], 'contacts.csv', { type: 'text/csv' })
    const report = await api.importContacts(file)

    const [url, options] = globalThis.fetch.mock.calls[0]
    expect(url).toContain('/api/Contacts/Import')
    expect(options.body).toBeInstanceOf(FormData)
    expect(options.body.get('file')).toBe(file)
    // The browser has to set the multipart boundary itself; naming a type here breaks the parse.
    expect(options.headers['Content-Type']).toBeUndefined()
    expect(report.created).toBe(1)
  })

  it('fetches the export as a blob with the served file name', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: { get: () => 'attachment; filename="contacts-2026-07-27.csv"' },
      blob: async () => new Blob(['x']),
    })

    const result = await api.exportContacts()

    expect(globalThis.fetch.mock.calls[0][0]).toContain('/api/Contacts/Export')
    expect(result.fileName).toBe('contacts-2026-07-27.csv')
  })
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/frontend && npx vitest run src/api.test.js`
Expected: FAIL — `api.importContacts is not a function`.

- [ ] **Step 3: Teach `request` about FormData and add the two methods**

Dans `src/frontend/src/api.js`, remplacer l'en-tête de `request` :

```js
async function request(method, path, body, options = {}) {
  // FormData carries its own multipart boundary; naming a content type here breaks the parse on
  // the server side.
  const isForm = typeof FormData !== 'undefined' && body instanceof FormData
  const headers = {}
  if (body && !isForm) headers['Content-Type'] = 'application/json'

  const res = await fetch(`${BASE}${path}`, {
    method,
    headers,
    credentials: 'include',
    body: body ? (isForm ? body : JSON.stringify(body)) : undefined,
    signal: options.signal,
  })
```

Puis, dans l'objet `api`, sous `setContactFavorite` :

```js
  importContacts: (file) => {
    const form = new FormData()
    form.append('file', file)
    return request('POST', '/api/Contacts/Import', form)
  },

  exportContacts: () => requestBlob('/api/Contacts/Export'),
```

- [ ] **Step 4: Extract the download helper**

Créer `src/frontend/src/lib/downloadBlob.ts` :

```ts
/** Hands a blob to the browser's downloader. Shared so the reader and the contacts export cannot
    drift into two spellings of the same six lines. */
export function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  URL.revokeObjectURL(url)
}
```

Dans `src/frontend/src/modules/mail/reader/MessageReader.tsx`, importer `downloadBlob` et remplacer
les cinq lignes du corps de `download` par :

```ts
      const result = await requestBlob(mailAttachmentUrl(folderPath!, uid!, part))
      downloadBlob(result.blob, result.fileName || fileName)
```

- [ ] **Step 5: Add the types and the mutation**

Dans `src/frontend/src/modules/contacts/contactTypes.ts`, ajouter :

```ts
export interface ContactImportError {
  /** The line in the file, header included — what the user reads in their spreadsheet. */
  line: number
  reason: string
}

/** The four counters count rows and add up to the file's data rows; `totalErrors` counts every
    reason, including those past the server's cap on `errors`. */
export interface ContactImportReport {
  created: number
  merged: number
  skipped: number
  failed: number
  totalErrors: number
  errors: ContactImportError[]
}
```

Dans `src/frontend/src/modules/contacts/queries.ts`, généraliser le résultat de l'aide de mutation et
ajouter la mutation :

```ts
function useContactMutation<TArgs, TResult = unknown>(mutationFn: (args: TArgs) => Promise<TResult>) {
```

```ts
export function useImportContacts() {
  return useContactMutation((file: File) => api.importContacts(file) as Promise<ContactImportReport>)
}
```

(et ajouter `ContactImportReport` à l'import de types en tête de fichier)

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd src/frontend && npx vitest run src/api.test.js src/modules/mail/reader/MessageReader.test.tsx`
Expected: PASS — les tests existants du téléchargement de pièce jointe passent inchangés, ce qui est
la preuve que l'extraction n'a rien changé.

- [ ] **Step 7: Typecheck and lint**

Run: `cd src/frontend && npm run typecheck && npm run lint`
Expected: aucune erreur.

- [ ] **Step 8: Commit**

```bash
git add src/frontend/src/lib/downloadBlob.ts src/frontend/src/api.js \
        src/frontend/src/modules/mail/reader/MessageReader.tsx \
        src/frontend/src/modules/contacts/contactTypes.ts \
        src/frontend/src/modules/contacts/queries.ts \
        src/frontend/src/api.test.js
git commit -F - <<'EOF'
Wire the contacts CSV endpoints into the client

Multipart upload, blob download, and a shared downloadBlob helper.
EOF
```

---

### Task 9 : la modale de rapport et le pied de bande

**Files:**
- Create: `src/frontend/src/modules/contacts/ImportReportModal.tsx`
- Create: `src/frontend/src/modules/contacts/ImportReportModal.test.tsx`
- Create: `src/frontend/src/modules/contacts/ContactsTransfer.tsx`
- Create: `src/frontend/src/modules/contacts/ContactsTransfer.test.tsx`
- Modify: `src/frontend/src/index.css`

**Interfaces:**
- Consumes: `useImportContacts`, `api.exportContacts`, `downloadBlob`, `ContactImportReport`
  (tâche 8) ; `Tooltip` (`content`, `placement`, `children`).
- Produces:
  - `ImportReportModal({ report, onClose })`
  - `ContactsTransfer({ contacts, onError })` — `contacts: Contact[] | undefined`,
    `onError: (message: string) => void`

- [ ] **Step 1: Write the failing tests**

Créer `src/frontend/src/modules/contacts/ImportReportModal.test.tsx` :

```tsx
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ImportReportModal from './ImportReportModal'
import type { ContactImportReport } from './contactTypes'

const report = (fields: Partial<ContactImportReport> = {}): ContactImportReport => ({
  created: 0, merged: 0, skipped: 0, failed: 0, totalErrors: 0, errors: [], ...fields,
})

describe('ImportReportModal', () => {
  it('prints the four counters', () => {
    render(<ImportReportModal report={report({ created: 12, merged: 3, skipped: 1, failed: 2 })}
      onClose={vi.fn()} />)

    expect(screen.getByText('12')).toBeInTheDocument()
    expect(screen.getByText(/added/i)).toBeInTheDocument()
    expect(screen.getByText(/updated/i)).toBeInTheDocument()
    expect(screen.getByText(/skipped/i)).toBeInTheDocument()
    expect(screen.getByText(/refused/i)).toBeInTheDocument()
  })

  it('lists a refused line with its number and reason', () => {
    render(<ImportReportModal
      report={report({ failed: 1, totalErrors: 1, errors: [{ line: 7, reason: 'Neither a name nor a valid e-mail address' }] })}
      onClose={vi.fn()} />)

    expect(screen.getByText(/line 7/i)).toBeInTheDocument()
    expect(screen.getByText(/neither a name/i)).toBeInTheDocument()
  })

  // Fifty of ten thousand is a report; ten thousand is a wall.
  it('says how many reasons it is not showing', () => {
    render(<ImportReportModal
      report={report({ failed: 312, totalErrors: 312, errors: [{ line: 2, reason: 'bad' }] })}
      onClose={vi.fn()} />)

    expect(screen.getByText(/311 more/i)).toBeInTheDocument()
  })

  it('says so when nothing went wrong', () => {
    render(<ImportReportModal report={report({ created: 4 })} onClose={vi.fn()} />)

    expect(screen.queryByText(/line /i)).not.toBeInTheDocument()
  })

  // The cross is the only way out, as in every dialog on the site.
  it('closes on the cross', async () => {
    const onClose = vi.fn()
    render(<ImportReportModal report={report()} onClose={onClose} />)

    await userEvent.click(screen.getByRole('button', { name: '✕' }))

    expect(onClose).toHaveBeenCalled()
  })
})
```

Créer `src/frontend/src/modules/contacts/ContactsTransfer.test.tsx` :

```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ContactsTransfer from './ContactsTransfer'
import type { Contact } from './contactTypes'

vi.mock('../../api.js', () => ({
  api: { importContacts: vi.fn(), exportContacts: vi.fn() },
  ApiError: class extends Error {},
}))
vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'primary' }))
vi.mock('../../lib/downloadBlob', () => ({ downloadBlob: vi.fn() }))

const { api } = await import('../../api.js') as unknown as {
  api: Record<'importContacts' | 'exportContacts', ReturnType<typeof vi.fn>>
}
const { downloadBlob } = await import('../../lib/downloadBlob') as unknown as {
  downloadBlob: ReturnType<typeof vi.fn>
}

const book: Contact[] = [
  { id: '1', firstName: 'Bruno', lastName: null, nickname: null, isFavorite: false, addresses: [] },
]

function renderTransfer(contacts: Contact[] | undefined, onError = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  render(
    <QueryClientProvider client={client}>
      <ContactsTransfer contacts={contacts} onError={onError} />
    </QueryClientProvider>,
  )
  return onError
}

// The input is hidden, so userEvent.upload cannot reach it; the change event is what the component
// actually listens to.
function choose(file: File) {
  const input = screen.getByTestId('contacts-import-input') as HTMLInputElement
  fireEvent.change(input, { target: { files: [file] } })
  return input
}

describe('ContactsTransfer', () => {
  beforeEach(() => vi.clearAllMocks())

  it('sends the chosen file and shows the report', async () => {
    api.importContacts.mockResolvedValue({
      created: 3, merged: 1, skipped: 0, failed: 0, totalErrors: 0, errors: [],
    })
    renderTransfer(book)

    const file = new File(['First Name\r\nBruno'], 'contacts.csv', { type: 'text/csv' })
    choose(file)

    await waitFor(() => expect(api.importContacts).toHaveBeenCalledWith(file))
    expect(await screen.findByText('3')).toBeInTheDocument()
  })

  // Without clearing it, choosing the same file twice fires no change event at all.
  it('clears the input so the same file can be chosen twice', async () => {
    api.importContacts.mockResolvedValue({
      created: 1, merged: 0, skipped: 0, failed: 0, totalErrors: 0, errors: [],
    })
    renderTransfer(book)

    const input = choose(new File(['x'], 'contacts.csv', { type: 'text/csv' }))

    await waitFor(() => expect(input.value).toBe(''))
  })

  it('reports a refused import to its caller', async () => {
    api.importContacts.mockRejectedValue(new Error('No recognised column in this file.'))
    const onError = renderTransfer(book)

    choose(new File(['x'], 'contacts.csv', { type: 'text/csv' }))

    await waitFor(() => expect(onError).toHaveBeenCalledWith('No recognised column in this file.'))
    expect(screen.queryByText(/added/i)).not.toBeInTheDocument()
  })

  it('downloads the export under the served name', async () => {
    const blob = new Blob(['x'])
    api.exportContacts.mockResolvedValue({ blob, fileName: 'contacts-2026-07-27.csv' })
    renderTransfer(book)

    await userEvent.click(screen.getByRole('button', { name: 'Export' }))

    await waitFor(() => expect(downloadBlob).toHaveBeenCalledWith(blob, 'contacts-2026-07-27.csv'))
  })

  // A file with no rows in it reads as a failure, so the door is shut rather than opened onto one.
  it('disables the export on an empty book', () => {
    renderTransfer([])

    expect(screen.getByRole('button', { name: 'Export' })).toBeDisabled()
  })

  it('disables the export while the book is still loading', () => {
    renderTransfer(undefined)

    expect(screen.getByRole('button', { name: 'Export' })).toBeDisabled()
  })

  it('reports a refused export to its caller', async () => {
    api.exportContacts.mockRejectedValue(new Error('Server error'))
    const onError = renderTransfer(book)

    await userEvent.click(screen.getByRole('button', { name: 'Export' }))

    await waitFor(() => expect(onError).toHaveBeenCalledWith('Server error'))
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/frontend && npx vitest run src/modules/contacts/ImportReportModal.test.tsx src/modules/contacts/ContactsTransfer.test.tsx`
Expected: FAIL — les deux modules n'existent pas.

- [ ] **Step 3: Write the modal**

Créer `src/frontend/src/modules/contacts/ImportReportModal.tsx` :

```tsx
import type { ContactImportReport } from './contactTypes'

interface Props {
  report: ContactImportReport
  onClose: () => void
}

/**
 * What the import did, line by line where it refused. The counters count rows, so they add up to
 * the file's data rows — a reader who is missing contacts can tell which bucket took them.
 */
export default function ImportReportModal({ report, onClose }: Props) {
  const counters: [number, string][] = [
    [report.created, 'added'],
    [report.merged, 'updated'],
    [report.skipped, 'skipped'],
    [report.failed, 'refused'],
  ]
  const hidden = report.totalErrors - report.errors.length

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">Import finished</span>
          <button className="modal-close" onClick={onClose}>✕</button>
        </div>

        <div className="import-counters">
          {counters.map(([value, label]) => (
            <div className="import-counter" key={label}>
              <span className="import-counter-value">{value}</span>
              <span className="import-counter-label">{label}</span>
            </div>
          ))}
        </div>

        {report.errors.length > 0 && (
          <ul className="import-errors">
            {report.errors.map(error => (
              <li key={`${error.line}-${error.reason}`}>
                <span className="import-error-line">Line {error.line}</span> {error.reason}
              </li>
            ))}
            {hidden > 0 && <li className="import-errors-more">and {hidden} more</li>}
          </ul>
        )}
      </div>
    </div>
  )
}
```

- [ ] **Step 4: Write the footer**

Créer `src/frontend/src/modules/contacts/ContactsTransfer.tsx` :

```tsx
import { useRef, useState, type ChangeEvent } from 'react'
import { api } from '../../api.js'
import Tooltip from '../../components/Tooltip'
import { downloadBlob } from '../../lib/downloadBlob'
import type { Contact, ContactImportReport } from './contactTypes'
import ImportReportModal from './ImportReportModal'
import { useImportContacts } from './queries'

interface Props {
  contacts: Contact[] | undefined
  onError: (message: string) => void
}

/**
 * The band's footer. The two file actions sit here rather than among the scopes because the band
 * is navigation and these are not — the same reason the mail column keeps its account block at the
 * foot.
 */
export default function ContactsTransfer({ contacts, onError }: Props) {
  const input = useRef<HTMLInputElement>(null)
  const [report, setReport] = useState<ContactImportReport | null>(null)
  const [exporting, setExporting] = useState(false)
  const importContacts = useImportContacts()

  async function pick(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    // Cleared before anything is awaited: an input keeping its value fires no change event when the
    // same file is chosen a second time.
    event.target.value = ''
    if (!file) return

    try {
      setReport(await importContacts.mutateAsync(file))
    } catch (error) {
      onError((error as Error).message || 'Could not import the file')
    }
  }

  async function download() {
    setExporting(true)
    try {
      const { blob, fileName } = await api.exportContacts()
      downloadBlob(blob, fileName)
    } catch (error) {
      onError((error as Error).message || 'Could not export the contacts')
    } finally {
      setExporting(false)
    }
  }

  const empty = !contacts?.length

  return (
    <div className="contacts-transfer">
      <input ref={input} type="file" accept=".csv,text/csv" hidden onChange={pick}
        data-testid="contacts-import-input" />

      <Tooltip content="Merge a CSV file into this book">
        <button type="button" className="btn" disabled={importContacts.isPending}
          onClick={() => input.current?.click()}>
          {importContacts.isPending ? <span className="spinner" /> : 'Import…'}
        </button>
      </Tooltip>

      <Tooltip content={empty ? 'Nothing to export' : 'Download this book as CSV'}>
        <button type="button" className="btn" disabled={empty || exporting} onClick={download}>
          {exporting ? <span className="spinner" /> : 'Export'}
        </button>
      </Tooltip>

      {report && <ImportReportModal report={report} onClose={() => setReport(null)} />}
    </div>
  )
}
```

- [ ] **Step 5: Style it**

Dans `src/frontend/src/index.css`, juste après la ligne `.contacts-scopes-scroll { … }` (≈ 2222) :

```css
/* The band's footer, the shape the mail column's account block wears: a flat strip separated by a
   rule, never a second block of navigation. */
.contacts-transfer {
  flex: none; display: flex; gap: 8px; padding: 10px 12px;
  border-top: 1px solid var(--border);
}
.contacts-transfer .tooltip-wrap { flex: 1; min-width: 0; }
.contacts-transfer .btn { width: 100%; justify-content: center; }
```

Puis, à la fin du bloc « Contacts module » (avant la section CSS suivante) :

```css
.import-counters { display: flex; gap: 8px; margin-bottom: 16px; }
.import-counter {
  flex: 1; display: flex; flex-direction: column; align-items: center; gap: 2px;
  padding: 10px 6px; border: 1px solid var(--border); border-radius: var(--radius-sm);
}
.import-counter-value { font-size: 18px; font-weight: 600; }
.import-counter-label { color: var(--text-muted); font-size: 12px; }

.import-errors {
  max-height: 220px; overflow-y: auto; margin: 0 0 20px; padding: 0 0 0 4px;
  list-style: none; font-size: 13px;
}
.import-errors li { padding: 4px 0; border-bottom: 1px solid var(--border); }
.import-errors li:last-child { border-bottom: 0; }
.import-error-line { color: var(--text-muted); font-variant-numeric: tabular-nums; }
.import-errors-more { color: var(--text-muted); }
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd src/frontend && npx vitest run src/modules/contacts/ImportReportModal.test.tsx src/modules/contacts/ContactsTransfer.test.tsx`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/modules/contacts/ImportReportModal.tsx \
        src/frontend/src/modules/contacts/ImportReportModal.test.tsx \
        src/frontend/src/modules/contacts/ContactsTransfer.tsx \
        src/frontend/src/modules/contacts/ContactsTransfer.test.tsx \
        src/frontend/src/index.css
git commit -F - <<'EOF'
Add the contacts import and export controls

Two buttons in the band footer, a report modal listing the refused lines.
EOF
```

---

### Task 10 : brancher sur le module

**Files:**
- Modify: `src/frontend/src/modules/contacts/ContactsLayout.tsx`
- Modify: `src/frontend/src/modules/contacts/ContactsLayout.test.tsx`
- Modify: `src/frontend/src/modules/contacts/ContactScopes.tsx` (commentaire seul)
- Modify: `src/frontend/CLAUDE.md`

**Interfaces:**
- Consumes: `ContactsTransfer` (tâche 9).
- Produces: rien de nouveau.

- [ ] **Step 1: Write the failing test**

Dans `ContactsLayout.test.tsx`, étendre le mock d'`../../api.js` avec `importContacts: vi.fn()` et
`exportContacts: vi.fn()` (et le type dérivé juste en dessous), puis ajouter un `describe` :

```tsx
describe('the transfer footer', () => {
  it('offers import and export under the scopes', async () => {
    api.getContacts.mockResolvedValue({ contacts: [contact({ id: '1', firstName: 'Bruno' })] })
    renderAt('/contacts')

    expect(await screen.findByRole('button', { name: 'Import…' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Export' })).toBeEnabled()
  })

  // The editor takes the two content columns and leaves the band standing, footer included — the
  // same rule the scopes follow.
  it('keeps the footer while the editor is open', async () => {
    api.getContacts.mockResolvedValue({ contacts: [] })
    renderAt('/contacts/new')

    expect(await screen.findByRole('button', { name: 'Import…' })).toBeInTheDocument()
  })

  it('surfaces an import failure as a toast', async () => {
    api.getContacts.mockResolvedValue({ contacts: [] })
    api.importContacts.mockRejectedValue(new Error('No recognised column in this file.'))
    renderAt('/contacts')

    await screen.findByRole('button', { name: 'Import…' })
    fireEvent.change(screen.getByTestId('contacts-import-input'),
      { target: { files: [new File(['x'], 'contacts.csv', { type: 'text/csv' })] } })

    expect(await screen.findByText('No recognised column in this file.')).toBeInTheDocument()
  })

  // Settled, not success: a refused import must leave the screen on the server's book.
  it('refetches the book after an import, refused or not', async () => {
    api.getContacts.mockResolvedValue({ contacts: [] })
    api.importContacts.mockRejectedValue(new Error('nope'))
    renderAt('/contacts')

    await screen.findByRole('button', { name: 'Import…' })
    api.getContacts.mockClear()
    fireEvent.change(screen.getByTestId('contacts-import-input'),
      { target: { files: [new File(['x'], 'contacts.csv', { type: 'text/csv' })] } })

    await waitFor(() => expect(api.getContacts).toHaveBeenCalled())
  })
})
```

(ajouter `fireEvent` à l'import de `@testing-library/react` en tête de fichier)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/frontend && npx vitest run src/modules/contacts/ContactsLayout.test.tsx`
Expected: FAIL — aucun bouton « Import… ».

- [ ] **Step 3: Mount the footer**

Dans `ContactsLayout.tsx`, importer `ContactsTransfer` et l'ajouter en pied de la colonne des
portées, **après** `.contacts-scopes-scroll` et à l'intérieur de `.contacts-scopes-column` :

```tsx
        <div className="contacts-scopes-scroll">
          <ContactScopes scope={scope} total={total} favorites={favorites} onScope={changeScope} />
        </div>
        <ContactsTransfer contacts={contacts}
          onError={message => addToast(message, 'error')} />
      </div>
```

- [ ] **Step 4: Correct the scopes comment**

Dans `ContactScopes.tsx`, la note de tête annonce encore l'import comme à venir. Remplacer sa
dernière phrase par :

```
 * Two scopes today, with import and export in the column's footer below and CardDAV address books
 * the next thing to land here — the reason the module has a band at all rather than starting flush
 * against the rail.
```

- [ ] **Step 5: Run the contacts suite**

Run: `cd src/frontend && npx vitest run src/modules/contacts`
Expected: PASS.

- [ ] **Step 6: Run the whole frontend suite, typecheck and lint**

Run: `cd src/frontend && npm test && npm run typecheck && npm run lint`
Expected: PASS.

- [ ] **Step 7: Document the module**

Dans `src/frontend/CLAUDE.md`, remplacer le paragraphe commençant par « **The band exists for what
will land in it, not for its two rows.** » par :

> **The band exists for what lands in it, not for its two rows.** `ContactScopes` carries All
> contacts and Favourites with their counts and nothing else; `ContactsTransfer` sits in the
> column's footer under them, separated by a rule, holding `Import…` and `Export` — file actions
> rather than navigation, which is why they are not rows among the scopes, the same reason the mail
> column keeps its account block at the foot. It wears the navigation language (fill plus weight,
> `aria-current` on the active row like `FolderTree`) and **no accent bar**: the bar belongs to
> content lists, and the selected tile is what carries it. **The import goes straight through**:
> the file posts on pick, the server merges on the address and answers a report, and
> `ImportReportModal` prints the four counters plus every refused line with its number — there is no
> preview step, because the merge overwrites nothing and replaying a file creates nothing. Its
> mutation invalidates `onSettled` like the module's four others, so a refused import still resyncs
> the book. **The hidden input is cleared before the upload is awaited**: an input that keeps its
> value fires no change event when the same file is picked twice. `Export` is disabled on an empty
> book — a file with no rows reads as a failure — and downloads through the shared
> `lib/downloadBlob.ts`, extracted from `MessageReader` so the two cannot drift.

- [ ] **Step 8: Commit**

```bash
git add src/frontend/src/modules/contacts/ContactsLayout.tsx \
        src/frontend/src/modules/contacts/ContactsLayout.test.tsx \
        src/frontend/src/modules/contacts/ContactScopes.tsx \
        src/frontend/CLAUDE.md
git commit -F - <<'EOF'
Mount the transfer footer in the contacts band

Import toasts its refusals and resyncs the book either way.
EOF
```

---

### Task 11 : passe navigateur

Aucun test jsdom ne mesure une mise en page : la géométrie se constate dans un vrai navigateur, pas
dans un raisonnement. Cette tâche ne modifie rien par défaut — elle ne produit un correctif que si
une mesure le réclame.

**Files:**
- Modify (seulement si une mesure l'exige) : `src/frontend/src/index.css`

- [ ] **Step 1: Lancer l'application**

Run: `cd src/frontend && npm run dev`, puis ouvrir `/contacts` (le backend doit tourner, ou le
`VITE_API_BASE` pointer sur l'environnement dev).

- [ ] **Step 2: Vérifier le pied de bande**

Constater, dans le navigateur et non par lecture du CSS :
- les deux boutons tiennent côte à côte à la largeur de 240 px de la colonne, sans débordement ni
  troncature du libellé ;
- le pied reste collé en bas quand la liste des portées est plus haute que la colonne — la bande
  défilante est `.contacts-scopes-scroll`, le pied est `flex: none` ;
- le pied est toujours là sur `/contacts/new`.

- [ ] **Step 3: Vérifier la modale**

Importer un fichier réel — un export Snappymail si possible — et constater :
- les quatre compteurs sur une ligne, lisibles ;
- la liste d'erreurs défile à l'intérieur de la modale plutôt que d'allonger celle-ci hors écran ;
- la modale se ferme sur la ✕ **et** sur un clic dans le voile.

- [ ] **Step 4: Vérifier l'export**

Cliquer `Export`, ouvrir le fichier dans un tableur, constater que les accents sont corrects (c'est
le BOM) et que le fichier se réimporte sans rien créer.

- [ ] **Step 5: Vérifier les deux thèmes**

Refaire les étapes 2 et 3 en thème sombre : aucune couleur en dur n'a été écrite, donc les jetons
doivent suffire. Toute couleur qui ne suit pas est un jeton manquant, pas une valeur à écrire.

- [ ] **Step 6: Commit (seulement si un correctif a été nécessaire)**

```bash
git add src/frontend/src/index.css
git commit -F - <<'EOF'
Fix the contacts transfer footer measurements

Found in the browser pass; jsdom computes no layout.
EOF
```

---

## Auto-relecture

**Couverture de la spec.** Format d'entrée → tâche 3 ; lecture du fichier (BOM, encodage,
séparateur, RFC 4180) → tâche 1 ; écriture du fichier → tâches 2 et 6 ; colonnes hors modèle →
tâche 4 ; les deux routes, le rapport, ses plafonds → tâche 7 ; fusion, ambiguïté, adresse illisible,
plafonds, une seule transaction → tâches 5 et 7 ; frontend (pied de bande, modale, export désactivé,
invalidation `onSettled`) → tâches 8 à 10 ; documentation → tâches 7 et 10 ; géométrie → tâche 11.

**Cohérence des types.** `ContactCsvRow` (tâche 3) est consommé par les tâches 4 et 7 ;
`ContactImportRow` / `ContactImportOutcome` / `ContactImportReport` (tâche 5) par la tâche 7 ;
`ContactImportReport` côté TypeScript (tâche 8) par les tâches 9 et 10. `ImportAsync` a la même
signature dans l'interface, l'implémentation, les tests du dépôt et l'appel du contrôleur.
`Extras` est **toujours** clé normalisée, jamais l'intitulé d'origine — c'est ce que la tâche 4
suppose et ce que la correction n°1 de la tâche 3 garantit.
