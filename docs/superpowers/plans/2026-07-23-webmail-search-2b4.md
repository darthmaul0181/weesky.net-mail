# Webmail 2b4 — Recherche IMAP — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recherche rapide (objet OU expéditeur) et avancée (De/À/Objet/Texte/Date/Non lu/Suivi/PJ, portée dossier ou boîte entière) sur la liste de messages, via un nouvel endpoint `POST /api/Mail/Messages/Search`.

**Architecture:** Le backend compile les critères en `SearchQuery` MailKit (fonction pure testée à part), exécute SORT/SEARCH dans une session unique (fusion par date interne en multi-dossiers, raffinement pièce jointe **avant** pagination) et répond une `MailSearchPage` dont chaque ligne est un `MailMessageSummary` + `FolderPath`/`UidValidity`. Le frontend affiche les résultats dans la liste existante (bandeau + pagination), critères portés par `MailLayout`, requête TanStack keyée par critères.

**Tech Stack:** .NET 10 / MailKit / xUnit+Moq — React / TanStack Query v5 / Vitest+RTL.

**Déviation actée vs spec §4** : plutôt qu'un « retrait local par MessageList », les mutations optimistes existantes (`useSetFlags`, retraits) patchent **aussi** les caches de recherche via le même mécanisme snapshot/rollback — moins de code, rollback correct, étoile/lu visibles dans les résultats. Le poll, lui, ne touche toujours pas les résultats.

## Global Constraints

- UI en **anglais** ; communication utilisateur en français.
- **Tokens uniquement** dans le CSS (`mail.css`), jamais de littéral couleur.
- Chemins de dossier **jamais en segment de route** (query string ou body).
- `pageSize`/lot plafonnés à **200** ; erreurs : 400 validation / 401 `credentials_unavailable` / 502 serveur.
- **Jamais `invalidateQueries` sur la clé `messageStream`** ; `settle()` (de `src/test-utils`) avant toute assertion de silence.
- La recherche est une lecture : **pas** de `mailKeys.writes` sur `useSearchMessages`.
- Backend : `dotnet test` (jamais `--no-build`) quand un fichier de test est ajouté ; `Assert.IsType<BadRequestObjectResult>` pour les 400 via `BadRequest(body)`.
- Commits : message concis (≤2 lignes de corps), ne jamais commencer/finir par `@` ; `.claude/settings.local.json` ne doit **jamais** être commité.
- Frontend : composants nouveaux en TypeScript ; tests à côté du fichier testé.

---

### Task 1: Backend — critères + compilation pure en `SearchQuery`

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/MailSearchCriteria.cs`
- Create: `src/snoopy.microservice/Services/MailSearchQueryBuilder.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailSearchQueryBuilderTests.cs`

**Interfaces:**
- Produces: `MailSearchCriteria(string? Quick, string? From, string? To, string? Subject, string? Text, int? SinceDays, bool Unread, bool Flagged, bool HasAttachment)` ; `MailSearchQueryBuilder.HasAnyCriterion(MailSearchCriteria) → bool` ; `MailSearchQueryBuilder.Build(MailSearchCriteria, DateTime todayUtc) → SearchQuery`.

- [ ] **Step 1: Écrire les tests qui échouent**

```csharp
using MailKit.Search;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Tests.Services;

public class MailSearchQueryBuilderTests
{
    private static readonly DateTime Today = new(2026, 7, 23);

    private static MailSearchCriteria Empty => new(null, null, null, null, null, null, false, false, false);

    [Fact]
    public void HasAnyCriterion_is_false_when_everything_is_blank()
        => Assert.False(MailSearchQueryBuilder.HasAnyCriterion(Empty with { Quick = "  " }));

    [Theory]
    [InlineData("quick")]
    [InlineData("from")]
    [InlineData("to")]
    [InlineData("subject")]
    [InlineData("text")]
    public void HasAnyCriterion_sees_each_text_field(string field)
    {
        var criteria = field switch
        {
            "quick" => Empty with { Quick = "x" },
            "from" => Empty with { From = "x" },
            "to" => Empty with { To = "x" },
            "subject" => Empty with { Subject = "x" },
            _ => Empty with { Text = "x" },
        };
        Assert.True(MailSearchQueryBuilder.HasAnyCriterion(criteria));
    }

    [Fact]
    public void HasAnyCriterion_sees_flags_and_date()
    {
        Assert.True(MailSearchQueryBuilder.HasAnyCriterion(Empty with { Unread = true }));
        Assert.True(MailSearchQueryBuilder.HasAnyCriterion(Empty with { Flagged = true }));
        Assert.True(MailSearchQueryBuilder.HasAnyCriterion(Empty with { HasAttachment = true }));
        Assert.True(MailSearchQueryBuilder.HasAnyCriterion(Empty with { SinceDays = 7 }));
    }

    [Fact]
    public void Quick_compiles_to_subject_or_from()
    {
        var query = MailSearchQueryBuilder.Build(Empty with { Quick = "facture" }, Today);

        var or = Assert.IsType<BinarySearchQuery>(query);
        Assert.Equal(SearchTerm.Or, or.Term);
        var subject = Assert.IsType<TextSearchQuery>(or.Left);
        Assert.Equal(SearchTerm.SubjectContains, subject.Term);
        Assert.Equal("facture", subject.Text);
        var from = Assert.IsType<TextSearchQuery>(or.Right);
        Assert.Equal(SearchTerm.FromContains, from.Term);
        Assert.Equal("facture", from.Text);
    }

    [Fact]
    public void Each_advanced_field_compiles_to_its_term()
    {
        Assert.Equal(SearchTerm.FromContains,
            Assert.IsType<TextSearchQuery>(MailSearchQueryBuilder.Build(Empty with { From = "a" }, Today)).Term);
        Assert.Equal(SearchTerm.ToContains,
            Assert.IsType<TextSearchQuery>(MailSearchQueryBuilder.Build(Empty with { To = "a" }, Today)).Term);
        Assert.Equal(SearchTerm.SubjectContains,
            Assert.IsType<TextSearchQuery>(MailSearchQueryBuilder.Build(Empty with { Subject = "a" }, Today)).Term);
        Assert.Equal(SearchTerm.BodyContains,
            Assert.IsType<TextSearchQuery>(MailSearchQueryBuilder.Build(Empty with { Text = "a" }, Today)).Term);
        Assert.Equal(SearchTerm.NotSeen,
            MailSearchQueryBuilder.Build(Empty with { Unread = true }, Today).Term);
        Assert.Equal(SearchTerm.Flagged,
            MailSearchQueryBuilder.Build(Empty with { Flagged = true }, Today).Term);
    }

    [Fact]
    public void SinceDays_compiles_to_delivered_after_today_minus_days()
    {
        var query = MailSearchQueryBuilder.Build(Empty with { SinceDays = 7 }, Today);

        var date = Assert.IsType<DateSearchQuery>(query);
        Assert.Equal(SearchTerm.DeliveredAfter, date.Term);
        Assert.Equal(Today.AddDays(-7), date.Date);
    }

    [Fact]
    public void Filled_fields_combine_with_and()
    {
        var query = MailSearchQueryBuilder.Build(
            Empty with { From = "alice", Unread = true }, Today);

        var and = Assert.IsType<BinarySearchQuery>(query);
        Assert.Equal(SearchTerm.And, and.Term);
        Assert.Equal(SearchTerm.FromContains, Assert.IsType<TextSearchQuery>(and.Left).Term);
        Assert.Equal(SearchTerm.NotSeen, and.Right.Term);
    }

    [Fact]
    public void Attachment_alone_compiles_to_all_it_is_a_post_filter()
        => Assert.Same(SearchQuery.All, MailSearchQueryBuilder.Build(Empty with { HasAttachment = true }, Today));

    [Fact]
    public void Blank_criteria_compile_to_all()
        => Assert.Same(SearchQuery.All, MailSearchQueryBuilder.Build(Empty, Today));
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test src/snoopy.microservice --filter MailSearchQueryBuilderTests`
Expected: FAIL — `MailSearchCriteria` et `MailSearchQueryBuilder` n'existent pas (erreur de compilation).

- [ ] **Step 3: Implémenter**

`Models/Mail/MailSearchCriteria.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// What the user is searching for. Quick is the fast-bar text (subject OR sender);
/// the rest are the advanced form's fields, combined with AND.
/// </summary>
public sealed record MailSearchCriteria(
    string? Quick,
    string? From,
    string? To,
    string? Subject,
    string? Text,
    int? SinceDays,
    bool Unread,
    bool Flagged,
    bool HasAttachment);
```

`Services/MailSearchQueryBuilder.cs` :

```csharp
using MailKit.Search;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Compiles search criteria into a MailKit <see cref="SearchQuery"/>. Pure: the date is
/// injected so tests never race midnight. HasAttachment is deliberately not compiled —
/// no standard IMAP criterion exists, it is post-filtered on BODYSTRUCTURE by the session.
/// </summary>
internal static class MailSearchQueryBuilder
{
    public static bool HasAnyCriterion(MailSearchCriteria criteria) =>
        !string.IsNullOrWhiteSpace(criteria.Quick)
        || !string.IsNullOrWhiteSpace(criteria.From)
        || !string.IsNullOrWhiteSpace(criteria.To)
        || !string.IsNullOrWhiteSpace(criteria.Subject)
        || !string.IsNullOrWhiteSpace(criteria.Text)
        || criteria.SinceDays is > 0
        || criteria.Unread || criteria.Flagged || criteria.HasAttachment;

    public static SearchQuery Build(MailSearchCriteria criteria, DateTime todayUtc)
    {
        var terms = new List<SearchQuery>();

        if (!string.IsNullOrWhiteSpace(criteria.Quick))
            terms.Add(SearchQuery.SubjectContains(criteria.Quick).Or(SearchQuery.FromContains(criteria.Quick)));
        if (!string.IsNullOrWhiteSpace(criteria.From)) terms.Add(SearchQuery.FromContains(criteria.From));
        if (!string.IsNullOrWhiteSpace(criteria.To)) terms.Add(SearchQuery.ToContains(criteria.To));
        if (!string.IsNullOrWhiteSpace(criteria.Subject)) terms.Add(SearchQuery.SubjectContains(criteria.Subject));
        if (!string.IsNullOrWhiteSpace(criteria.Text)) terms.Add(SearchQuery.BodyContains(criteria.Text));
        if (criteria.SinceDays is int days and > 0) terms.Add(SearchQuery.DeliveredAfter(todayUtc.AddDays(-days)));
        if (criteria.Unread) terms.Add(SearchQuery.NotSeen);
        if (criteria.Flagged) terms.Add(SearchQuery.Flagged);

        return terms.Count == 0 ? SearchQuery.All : terms.Aggregate((left, right) => left.And(right));
    }
}
```

- [ ] **Step 4: Vérifier le vert**

Run: `dotnet test src/snoopy.microservice --filter MailSearchQueryBuilderTests`
Expected: PASS (11 tests).

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Models/Mail/MailSearchCriteria.cs src/snoopy.microservice/Services/MailSearchQueryBuilder.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/MailSearchQueryBuilderTests.cs
git commit -m "Backend 2b4: search criteria compiled to MailKit SearchQuery"
```

---

### Task 2: Backend — DTO, page de résultats, mapper partagé

**Files:**
- Modify: `src/snoopy.microservice/Models/Mail/MessageRequests.cs` (ajout `SearchMessagesRequest`)
- Create: `src/snoopy.microservice/Models/Mail/MailSearchPage.cs`
- Create: `src/snoopy.microservice/Models/Mail/MailSearchResult.cs`
- Modify: `src/snoopy.microservice/Models/Mail/MailMessageSummary.cs` (retirer `sealed`)
- Modify: `src/snoopy.microservice/Services/ImapSession.cs` (refactor `ToSummary` → `FillSummary<T>`)

**Interfaces:**
- Produces: `SearchMessagesRequest { FolderPath, AllFolders, Quick?, From?, To?, Subject?, Text?, SinceDays?, Unread, Flagged, HasAttachment, Page, PageSize }` ; `MailSearchPage { Total, Page, PageSize, Results: List<MailSearchResult> }` ; `MailSearchResult : MailMessageSummary { FolderPath, UidValidity }` ; `ImapSession.FillSummary<T>(T, IMessageSummary) where T : MailMessageSummary`.

- [ ] **Step 1: Ajouter les types**

Fin de `MessageRequests.cs` :

```csharp
/// <summary>
/// Search criteria plus paging. FolderPath is required even when AllFolders is set — it
/// names the folder the user searched from. Quick is the fast bar (subject OR sender).
/// </summary>
public sealed class SearchMessagesRequest
{
    public string FolderPath { get; set; } = string.Empty;
    public bool AllFolders { get; set; }
    public string? Quick { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Subject { get; set; }
    public string? Text { get; set; }
    /// <summary>Compiled server-side to SINCE (today - N): the client never sends a literal date.</summary>
    public int? SinceDays { get; set; }
    public bool Unread { get; set; }
    public bool Flagged { get; set; }
    public bool HasAttachment { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; } = 50;
}
```

`MailSearchResult.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// One search hit: a summary plus where it lives. In all-folders scope each row must name
/// its folder; in single-folder scope they are uniform but the shape stays one.
/// </summary>
public sealed class MailSearchResult : MailMessageSummary
{
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>UID validity of that folder at search time — the result is a snapshot.</summary>
    public uint UidValidity { get; set; }
}
```

`MailSearchPage.cs` :

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>One page of search results, newest first across every searched folder.</summary>
public sealed class MailSearchPage
{
    /// <summary>Total matches, all pages combined.</summary>
    public int Total { get; set; }

    /// <summary>Zero-based page index.</summary>
    public int Page { get; set; }

    public int PageSize { get; set; }

    public List<MailSearchResult> Results { get; set; } = new();
}
```

`MailMessageSummary.cs` : `public sealed class` → `public class` (désormais conçu pour l'héritage de `MailSearchResult`).

Dans `ImapSession.cs`, remplacer le corps de `ToSummary` par le mapper partagé (mêmes affectations, aucune de plus) :

```csharp
private static MailMessageSummary ToSummary(IMessageSummary item) => FillSummary(new MailMessageSummary(), item);

/// <summary>One mapping for list rows and search hits — the eleven fields cannot drift apart.</summary>
private static T FillSummary<T>(T summary, IMessageSummary item) where T : MailMessageSummary
{
    var sender = item.Envelope?.From?.Mailboxes?.FirstOrDefault();

    summary.Uid = item.UniqueId.Id;
    summary.Subject = item.Envelope?.Subject ?? string.Empty;
    summary.FromName = sender?.Name is { Length: > 0 } name ? name : sender?.Address ?? string.Empty;
    summary.FromAddress = sender?.Address ?? string.Empty;
    summary.Date = item.InternalDate ?? item.Envelope?.Date ?? DateTimeOffset.MinValue;
    summary.Seen = item.Flags?.HasFlag(MessageFlags.Seen) ?? false;
    summary.Flagged = item.Flags?.HasFlag(MessageFlags.Flagged) ?? false;
    summary.Answered = item.Flags?.HasFlag(MessageFlags.Answered) ?? false;
    summary.HasAttachments = item.Attachments?.Any() ?? false;
    summary.Size = item.Size ?? 0;
    summary.Preview = item.PreviewText ?? string.Empty;
    return summary;
}
```

Conserver le commentaire « Arrival date, not the Date header… » au-dessus de l'affectation `Date`.

- [ ] **Step 2: Vérifier que rien ne casse**

Run: `dotnet test src/snoopy.microservice`
Expected: PASS — refactor pur, aucun test ne bouge.

- [ ] **Step 3: Commit**

```bash
git add src/snoopy.microservice/Models/Mail/MessageRequests.cs src/snoopy.microservice/Models/Mail/MailSearchPage.cs src/snoopy.microservice/Models/Mail/MailSearchResult.cs src/snoopy.microservice/Models/Mail/MailMessageSummary.cs src/snoopy.microservice/Services/ImapSession.cs
git commit -m "Backend 2b4: search DTOs and shared summary mapper"
```

---

### Task 3: Backend — `ImapSession.SearchAsync`

**Files:**
- Modify: `src/snoopy.microservice/Services/IImapSession.cs`
- Modify: `src/snoopy.microservice/Services/ImapSession.cs`

**Interfaces:**
- Consumes: `MailSearchQueryBuilder.Build`, `PageOf`, `SummaryItems`, `FillSummary<T>` (Tasks 1–2).
- Produces: `Task<Result<MailSearchPage>> SearchAsync(string folderPath, bool allFolders, MailSearchCriteria criteria, int page, int pageSize, CancellationToken ct)` sur `IImapSession`.

Note testabilité : comme `EmptyAsync` (2b3), l'orchestration MailKit n'a pas de couture de test unitaire (`ImapClient` concret) — la logique testable est dans le builder (Task 1) et les statiques existantes (`PageOf`). Repo et contrôleur la couvrent par mock de `IImapSession`.

- [ ] **Step 1: Déclarer sur l'interface**

Après `EmptyAsync` dans `IImapSession.cs` :

```csharp
/// <summary>
/// One page of search results, newest first. Single folder uses server SORT when
/// advertised; all-folders (or no SORT) merges per-folder SEARCH matches by internal
/// date in memory. HasAttachment is refined on BODYSTRUCTURE before paging, so Total
/// and the page windows stay honest.
/// </summary>
Task<Result<MailSearchPage>> SearchAsync(string folderPath, bool allFolders, MailSearchCriteria criteria, int page, int pageSize, CancellationToken cancellationToken);
```

- [ ] **Step 2: Implémenter dans `ImapSession`** (après `EmptyAsync`)

```csharp
public async Task<Result<MailSearchPage>> SearchAsync(
    string folderPath, bool allFolders, MailSearchCriteria criteria, int page, int pageSize, CancellationToken cancellationToken)
{
    ThrowIfDisposed();

    try
    {
        var query = MailSearchQueryBuilder.Build(criteria, DateTime.UtcNow.Date);
        var result = new MailSearchPage { Page = page, PageSize = pageSize };

        // Every match as (folder, uid), already newest-first once this list is final.
        List<(IMailFolder Folder, UniqueId Uid)> matches;

        if (!allFolders && _client.Capabilities.HasFlag(ImapCapabilities.Sort))
        {
            // Single folder with SORT: the server hands the order, no dates needed.
            var folder = await _client.GetFolderAsync(folderPath, cancellationToken);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var sorted = await folder.SortAsync(query, [OrderBy.ReverseDate], cancellationToken);
            var uids = criteria.HasAttachment
                ? await WithAttachmentsAsync(folder, sorted, cancellationToken)
                : sorted;
            matches = uids.Select(uid => (folder, uid)).ToList();
        }
        else
        {
            // All folders — or a server without SORT: SEARCH each, fetch internal dates
            // (the same date the list orders by), merge-sort in memory.
            var dated = new List<(IMailFolder Folder, UniqueId Uid, DateTimeOffset Date)>();

            foreach (var folder in await SearchableFoldersAsync(folderPath, allFolders, cancellationToken))
            {
                await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

                var found = await folder.SearchAsync(query, cancellationToken);
                if (found.Count == 0) continue;

                var uids = criteria.HasAttachment
                    ? await WithAttachmentsAsync(folder, found, cancellationToken)
                    : found;
                if (uids.Count == 0) continue;

                var dates = await folder.FetchAsync(
                    uids, MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate, cancellationToken);
                dated.AddRange(dates.Select(item =>
                    (folder, item.UniqueId, item.InternalDate ?? DateTimeOffset.MinValue)));
            }

            dated.Sort((a, b) => b.Date.CompareTo(a.Date));
            matches = dated.Select(entry => (entry.Folder, entry.Uid)).ToList();
        }

        result.Total = matches.Count;

        var wanted = PageOf(matches, page, pageSize);
        if (wanted.Count == 0) return Result.Success(result);

        // One summary fetch per folder present in the page. Each folder is re-opened:
        // IMAP selects one mailbox at a time, so the loop above left only the last one open.
        var byKey = new Dictionary<(string, uint), MailSearchResult>();
        foreach (var group in wanted.GroupBy(m => m.Folder))
        {
            await group.Key.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
            var items = await group.Key.FetchAsync(
                group.Select(m => m.Uid).ToList(), SummaryItems, cancellationToken);
            foreach (var item in items)
            {
                byKey[(group.Key.FullName, item.UniqueId.Id)] = FillSummary(new MailSearchResult
                {
                    FolderPath = group.Key.FullName,
                    UidValidity = group.Key.UidValidity,
                }, item);
            }
        }

        // Back into merged order; a uid expunged between search and fetch just drops out.
        foreach (var match in wanted)
        {
            if (byKey.TryGetValue((match.Folder.FullName, match.Uid.Id), out var row))
                result.Results.Add(row);
        }

        return Result.Success(result);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to search messages from {Folder} (allFolders: {AllFolders})", folderPath, allFolders);
        return Result.Failure<MailSearchPage>("Unable to search the messages");
    }
}

/// <summary>The folders one search sweeps: the named one, or every selectable folder.</summary>
private async Task<IReadOnlyList<IMailFolder>> SearchableFoldersAsync(
    string folderPath, bool allFolders, CancellationToken cancellationToken)
{
    if (!allFolders) return [await _client.GetFolderAsync(folderPath, cancellationToken)];

    var folders = await _client.GetFoldersAsync(_client.PersonalNamespaces[0], cancellationToken);
    return folders
        .Where(f => (f.Attributes & (FolderAttributes.NonExistent | FolderAttributes.NoSelect)) == 0)
        .ToList();
}

/// <summary>
/// Keeps only the matches whose BODYSTRUCTURE shows an attachment — the same predicate
/// that fills HasAttachments. Runs before paging: filtering after would falsify Total.
/// </summary>
private static async Task<IList<UniqueId>> WithAttachmentsAsync(
    IMailFolder folder, IList<UniqueId> uids, CancellationToken cancellationToken)
{
    if (uids.Count == 0) return uids;

    var items = await folder.FetchAsync(
        uids, MessageSummaryItems.UniqueId | MessageSummaryItems.BodyStructure, cancellationToken);
    var keep = items.Where(i => i.Attachments?.Any() ?? false).Select(i => i.UniqueId).ToHashSet();
    return uids.Where(keep.Contains).ToList();
}
```

- [ ] **Step 3: Compiler et vérifier**

Run: `dotnet build src/snoopy.microservice && dotnet test src/snoopy.microservice`
Expected: build OK, tous les tests existants PASS.

- [ ] **Step 4: Commit**

```bash
git add src/snoopy.microservice/Services/IImapSession.cs src/snoopy.microservice/Services/ImapSession.cs
git commit -m "Backend 2b4: ImapSession.SearchAsync (single and all-folders merge)"
```

---

### Task 4: Backend — repository passe-plat

**Files:**
- Modify: `src/snoopy.microservice/Repositories/IMailMessageRepository.cs`
- Modify: `src/snoopy.microservice/Repositories/MailMessageRepository.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/MailMessageRepositoryTests.cs` (ajout)

**Interfaces:**
- Produces: `Task<Result<MailSearchPage>> SearchAsync(User user, string password, string folderPath, bool allFolders, MailSearchCriteria criteria, int page, int pageSize, CancellationToken ct)`.

- [ ] **Step 1: Écrire les tests qui échouent** (suivre le patron des tests `EmptyAsync` du même fichier : mock de `IImapConnectionFactory` + `IImapSession`)

```csharp
[Fact]
public async Task SearchAsync_forwards_to_the_session()
{
    var criteria = new MailSearchCriteria("hello", null, null, null, null, null, false, false, false);
    var page = new MailSearchPage { Total = 1 };
    var session = new Mock<IImapSession>();
    session.Setup(s => s.SearchAsync("INBOX", false, criteria, 0, 50, It.IsAny<CancellationToken>()))
        .ReturnsAsync(Result.Success(page));
    var repository = CreateRepository(session);

    var result = await repository.SearchAsync(User, "secret", "INBOX", false, criteria, 0, 50, CancellationToken.None);

    Assert.True(result.IsSuccess);
    Assert.Same(page, result.Value);
}

[Fact]
public async Task SearchAsync_fails_when_the_session_cannot_open()
{
    var criteria = new MailSearchCriteria("hello", null, null, null, null, null, false, false, false);
    var repository = CreateRepositoryWithFailedConnection("nope");

    var result = await repository.SearchAsync(User, "secret", "INBOX", false, criteria, 0, 50, CancellationToken.None);

    Assert.True(result.IsFailure);
    Assert.Equal("nope", result.Error);
}
```

(Adapter les noms `CreateRepository` / `CreateRepositoryWithFailedConnection` / `User` aux helpers réellement présents dans ce fichier de tests — réutiliser les mêmes que les tests `EmptyAsync`.)

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test src/snoopy.microservice --filter MailMessageRepositoryTests`
Expected: FAIL — `SearchAsync` absent (compilation).

- [ ] **Step 3: Implémenter**

`IMailMessageRepository.cs` :

```csharp
/// <summary>One page of search results across one folder or the whole mailbox.</summary>
Task<Result<MailSearchPage>> SearchAsync(User user, string password, string folderPath, bool allFolders, MailSearchCriteria criteria, int page, int pageSize, CancellationToken cancellationToken);
```

`MailMessageRepository.cs` (même forme que les autres méthodes) :

```csharp
public async Task<Result<MailSearchPage>> SearchAsync(User user, string password, string folderPath, bool allFolders, MailSearchCriteria criteria, int page, int pageSize, CancellationToken cancellationToken)
{
    if (user == null) throw new ArgumentNullException(nameof(user));

    var sessionResult = await _factory.OpenAsync(user.Email, password, cancellationToken);
    if (sessionResult.IsFailure) return Result.Failure<MailSearchPage>(sessionResult.Error);
    await using var session = sessionResult.Value;

    return await session.SearchAsync(folderPath, allFolders, criteria, page, pageSize, cancellationToken);
}
```

- [ ] **Step 4: Vérifier le vert, puis commit**

Run: `dotnet test src/snoopy.microservice`
Expected: PASS.

```bash
git add src/snoopy.microservice/Repositories/IMailMessageRepository.cs src/snoopy.microservice/Repositories/MailMessageRepository.cs src/snoopy.microservice/snoopy.microservice.Tests/Repositories/MailMessageRepositoryTests.cs
git commit -m "Backend 2b4: repository SearchAsync passthrough"
```

---

### Task 5: Backend — endpoint `POST /api/Mail/Messages/Search`

**Files:**
- Modify: `src/snoopy.microservice/Controllers/MailController.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs` (ajout)

**Interfaces:**
- Consumes: `IMailMessageRepository.SearchAsync` (Task 4), `MailSearchQueryBuilder.HasAnyCriterion` (Task 1).
- Produces: `POST /api/Mail/Messages/Search` → 200 `MailSearchPage` / 400 / 401 / 502.

- [ ] **Step 1: Écrire les tests qui échouent** (patron des tests `GetMessages`/`EmptyFolder` du fichier : contrôleur construit avec mocks + `ControllerTestHelpers.CreateAuthenticatedContext`)

Cas à couvrir :

```csharp
[Fact] public async Task SearchMessages_requires_a_folder()
// request { FolderPath = "" , Quick = "x" } → Assert.IsType<BadRequestObjectResult>

[Fact] public async Task SearchMessages_refuses_a_negative_page()
// { FolderPath = "INBOX", Quick = "x", Page = -1 } → BadRequestObjectResult

[Theory] [InlineData(0)] [InlineData(201)]
public async Task SearchMessages_bounds_the_page_size(int pageSize)
// { FolderPath = "INBOX", Quick = "x", PageSize = pageSize } → BadRequestObjectResult

[Fact] public async Task SearchMessages_requires_at_least_one_criterion()
// { FolderPath = "INBOX" } (tout vide) → BadRequestObjectResult

[Fact] public async Task SearchMessages_answers_401_without_credentials()
// credentials store IsFailure → UnauthorizedObjectResult

[Fact] public async Task SearchMessages_returns_the_page()
// repo mock renvoie Result.Success(new MailSearchPage { Total = 2 }) ;
// vérifier le critère transmis: It.Is<MailSearchCriteria>(c => c.Quick == "hello")
// et AllFolders / Page / PageSize transmis tels quels → OkObjectResult

[Fact] public async Task SearchMessages_maps_server_failure_to_502()
// repo mock Result.Failure<MailSearchPage>("boom") → ObjectResult 502
```

- [ ] **Step 2: Vérifier l'échec**

Run: `dotnet test src/snoopy.microservice --filter MailControllerTests`
Expected: FAIL — `SearchMessages` absent (compilation).

- [ ] **Step 3: Implémenter** (après `EmptyFolder`)

```csharp
/// <summary>
/// One page of search results, newest first. Criteria combine with AND; Quick is the
/// fast bar and means subject OR sender. AllFolders sweeps every selectable folder in
/// one session. Paths travel in the body, never in a route segment.
/// </summary>
/// <param name="request">criteria, scope and paging</param>
/// <param name="cancellationToken">cancellation token</param>
/// <response code="200">The page of results</response>
/// <response code="400">The folder is missing, no criterion is filled, or the paging arguments are out of range</response>
/// <response code="401">Not authenticated, or the mail credentials are no longer available</response>
/// <response code="502">The mail server could not be reached</response>
[HttpPost("Messages/Search")]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status502BadGateway)]
public async Task<ActionResult<MailSearchPage>> SearchMessages(SearchMessagesRequest request, CancellationToken cancellationToken)
{
    if (request == null) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Request body is required"));
    if (string.IsNullOrWhiteSpace(request.FolderPath)) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("A folder is required"));
    if (request.Page < 0) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Page must not be negative"));
    if (request.PageSize is < 1 or > 200) return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("Page size must be between 1 and 200"));

    var criteria = new MailSearchCriteria(
        request.Quick, request.From, request.To, request.Subject, request.Text,
        request.SinceDays, request.Unread, request.Flagged, request.HasAttachment);
    if (!MailSearchQueryBuilder.HasAnyCriterion(criteria))
        return BadRequest(ResultEnveloppe.CreateErrorEnveloppe("At least one search criterion is required"));

    var password = _credentials.Retrieve(Request);
    if (password.IsFailure) return Unauthorized(ResultEnveloppe.CreateErrorEnveloppe(password.Error));

    var result = await _messages.SearchAsync(
        AuthenticatedUser, password.Value, request.FolderPath, request.AllFolders,
        criteria, request.Page, request.PageSize, cancellationToken);

    return FromResult(result, errorStatusCode: StatusCodes.Status502BadGateway);
}
```

- [ ] **Step 4: Vérifier le vert (suite complète — nouveaux fichiers de test ⇒ jamais `--no-build`)**

Run: `dotnet test src/snoopy.microservice`
Expected: PASS. Le build régénère `ApiDocumentation.xml` — l'inclure au commit s'il a bougé.

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Controllers/MailController.cs src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs
git status --short   # si ApiDocumentation.xml a bougé, l'ajouter aussi
git commit -m "Backend 2b4: POST Messages/Search endpoint"
```

---

### Task 6: Frontend — `api.searchMessages`

**Files:**
- Modify: `src/frontend/src/api.js` (après `emptyFolder`)
- Test: `src/frontend/src/api.test.js` (ajout)

**Interfaces:**
- Produces: `api.searchMessages(criteria, page, pageSize, options)` — `criteria` contient déjà `folderPath`/`allFolders` ; POST body `{ ...criteria, page, pageSize }`.

- [ ] **Step 1: Test qui échoue** (patron des tests `emptyFolder` du fichier : mock de `fetch`, assert URL/method/body)

```js
it('posts search criteria with paging', async () => {
  fetch.mockResolvedValueOnce(jsonResponse({ total: 0, page: 0, pageSize: 50, results: [] }))

  await api.searchMessages({ folderPath: 'INBOX', allFolders: false, quick: 'hello' }, 0, 50)

  const [url, options] = fetch.mock.calls[0]
  expect(url).toBe('https://api.mail.weesky.net/api/Mail/Messages/Search')
  expect(options.method).toBe('POST')
  expect(JSON.parse(options.body)).toEqual({
    folderPath: 'INBOX', allFolders: false, quick: 'hello', page: 0, pageSize: 50,
  })
})
```

(Adapter `jsonResponse` au helper réel du fichier.)

- [ ] **Step 2: Vérifier l'échec**

Run: `cd src/frontend && npx vitest run src/api.test.js`
Expected: FAIL — `api.searchMessages is not a function`.

- [ ] **Step 3: Implémenter**

```js
searchMessages: (criteria, page, pageSize, options) =>
  request('POST', '/api/Mail/Messages/Search', { ...criteria, page, pageSize }, options),
```

- [ ] **Step 4: Vérifier le vert, puis commit**

Run: `npx vitest run src/api.test.js`
Expected: PASS.

```bash
git add src/frontend/src/api.js src/frontend/src/api.test.js
git commit -m "Frontend 2b4: api.searchMessages"
```

---

### Task 7: Frontend — types + `searchCriteria.ts`

**Files:**
- Modify: `src/frontend/src/modules/mail/api/mailTypes.ts`
- Create: `src/frontend/src/modules/mail/list/searchCriteria.ts`
- Test: `src/frontend/src/modules/mail/list/searchCriteria.test.ts`

**Interfaces:**
- Produces (mailTypes): `MailSearchResult extends MailMessageSummary { folderPath, uidValidity }` ; `MailSearchPage { total, page, pageSize, results: MailSearchResult[] }`.
- Produces (searchCriteria): `SearchCriteria`, `AdvancedForm`, `isEmptyCriteria(c)`, `labelOf(c): string | null`, `criteriaFromForm(folderPath, form): SearchCriteria | null`, `daysSinceYearStart(now: Date): number`.

- [ ] **Step 1: Tests qui échouent**

```ts
import { criteriaFromForm, daysSinceYearStart, isEmptyCriteria, labelOf } from './searchCriteria'
import type { AdvancedForm } from './searchCriteria'

const blankForm: AdvancedForm = {
  from: '', to: '', subject: '', text: '',
  sinceDays: null, unread: false, flagged: false, hasAttachment: false, allFolders: false,
}

describe('isEmptyCriteria', () => {
  it('is true when only folderPath and allFolders are set', () => {
    expect(isEmptyCriteria({ folderPath: 'INBOX', allFolders: true })).toBe(true)
  })
  it('is false for any text field, date or flag', () => {
    expect(isEmptyCriteria({ folderPath: 'INBOX', allFolders: false, quick: 'x' })).toBe(false)
    expect(isEmptyCriteria({ folderPath: 'INBOX', allFolders: false, sinceDays: 7 })).toBe(false)
    expect(isEmptyCriteria({ folderPath: 'INBOX', allFolders: false, hasAttachment: true })).toBe(false)
  })
  it('ignores whitespace-only text', () => {
    expect(isEmptyCriteria({ folderPath: 'INBOX', allFolders: false, quick: '  ' })).toBe(true)
  })
})

describe('labelOf', () => {
  it('prefers the quick text', () => {
    expect(labelOf({ folderPath: 'F', allFolders: false, quick: 'facture', subject: 'x' })).toBe('facture')
  })
  it('falls back through subject, text, from, to', () => {
    expect(labelOf({ folderPath: 'F', allFolders: false, text: 'body' })).toBe('body')
    expect(labelOf({ folderPath: 'F', allFolders: false, from: 'alice' })).toBe('alice')
  })
  it('is null for a checkbox-only search', () => {
    expect(labelOf({ folderPath: 'F', allFolders: false, unread: true })).toBeNull()
  })
})

describe('criteriaFromForm', () => {
  it('trims fields and drops the empty ones', () => {
    const criteria = criteriaFromForm('INBOX', { ...blankForm, from: ' alice ', unread: true })
    expect(criteria).toEqual({ folderPath: 'INBOX', allFolders: false, from: 'alice', unread: true })
  })
  it('returns null when nothing is filled', () => {
    expect(criteriaFromForm('INBOX', blankForm)).toBeNull()
    expect(criteriaFromForm('INBOX', { ...blankForm, allFolders: true })).toBeNull()
  })
  it('carries scope and date', () => {
    const criteria = criteriaFromForm('INBOX', { ...blankForm, subject: 'x', sinceDays: 30, allFolders: true })
    expect(criteria).toEqual({ folderPath: 'INBOX', allFolders: true, subject: 'x', sinceDays: 30 })
  })
})

describe('daysSinceYearStart', () => {
  it('counts days since January 1st, minimum 1', () => {
    expect(daysSinceYearStart(new Date(2026, 0, 1))).toBe(1)
    expect(daysSinceYearStart(new Date(2026, 6, 23))).toBe(203)
  })
})
```

- [ ] **Step 2: Vérifier l'échec**

Run: `npx vitest run src/modules/mail/list/searchCriteria.test.ts`
Expected: FAIL — module absent.

- [ ] **Step 3: Implémenter**

`mailTypes.ts` (après `MailFolderPage`) :

```ts
/** One search hit: a summary plus where it lives — in all-folders scope each row names its folder. */
export interface MailSearchResult extends MailMessageSummary {
  folderPath: string
  /** That folder's UID validity at search time — the result is a snapshot. */
  uidValidity: number
}

export interface MailSearchPage {
  total: number
  page: number
  pageSize: number
  results: MailSearchResult[]
}
```

`searchCriteria.ts` :

```ts
/** What the user is searching for. Sent as-is (plus paging) to POST Messages/Search. */
export interface SearchCriteria {
  folderPath: string
  allFolders: boolean
  /** Fast-bar text: subject OR sender, compiled server-side. */
  quick?: string
  from?: string
  to?: string
  subject?: string
  text?: string
  /** Compiled server-side to SINCE (today - N): the client never sends a literal date. */
  sinceDays?: number
  unread?: boolean
  flagged?: boolean
  hasAttachment?: boolean
}

/** The advanced popup's raw fields, before trimming. */
export interface AdvancedForm {
  from: string
  to: string
  subject: string
  text: string
  sinceDays: number | null
  unread: boolean
  flagged: boolean
  hasAttachment: boolean
  allFolders: boolean
}

const TEXT_FIELDS = ['quick', 'subject', 'text', 'from', 'to'] as const

export function isEmptyCriteria(criteria: SearchCriteria): boolean {
  return TEXT_FIELDS.every(field => !criteria[field]?.trim())
    && !criteria.sinceDays
    && !criteria.unread && !criteria.flagged && !criteria.hasAttachment
}

/** The text the results banner quotes — TEXT_FIELDS order, so the fast bar wins. */
export function labelOf(criteria: SearchCriteria): string | null {
  for (const field of TEXT_FIELDS) {
    const value = criteria[field]?.trim()
    if (value) return value
  }
  return null
}

/** Builds the criteria a submitted advanced form means, or null when it asks nothing. */
export function criteriaFromForm(folderPath: string, form: AdvancedForm): SearchCriteria | null {
  const criteria: SearchCriteria = { folderPath, allFolders: form.allFolders }
  if (form.from.trim()) criteria.from = form.from.trim()
  if (form.to.trim()) criteria.to = form.to.trim()
  if (form.subject.trim()) criteria.subject = form.subject.trim()
  if (form.text.trim()) criteria.text = form.text.trim()
  if (form.sinceDays) criteria.sinceDays = form.sinceDays
  if (form.unread) criteria.unread = true
  if (form.flagged) criteria.flagged = true
  if (form.hasAttachment) criteria.hasAttachment = true
  return isEmptyCriteria(criteria) ? null : criteria
}

/** "This year" as a day count, so the server still receives SinceDays, never a date. */
export function daysSinceYearStart(now: Date): number {
  const start = new Date(now.getFullYear(), 0, 1)
  return Math.max(1, Math.floor((now.getTime() - start.getTime()) / 86_400_000) + 1)
}
```

- [ ] **Step 4: Vérifier le vert, puis commit**

Run: `npx vitest run src/modules/mail/list/searchCriteria.test.ts`
Expected: PASS.

```bash
git add src/frontend/src/modules/mail/api/mailTypes.ts src/frontend/src/modules/mail/list/searchCriteria.ts src/frontend/src/modules/mail/list/searchCriteria.test.ts
git commit -m "Frontend 2b4: search criteria model and helpers"
```

---

### Task 8: Frontend — `useSearchMessages`

**Files:**
- Modify: `src/frontend/src/modules/mail/queries.ts`
- Test: `src/frontend/src/modules/mail/queries.test.tsx` (ajout)

**Interfaces:**
- Consumes: `api.searchMessages` (Task 6), `SearchCriteria`/`MailSearchPage` (Task 7).
- Produces: `mailKeys.searchIn(accountId)` (préfixe), `mailKeys.search(accountId, criteria, page, pageSize)`, `useSearchMessages(criteria: SearchCriteria | null, page: number, pageSize: number)`.

- [ ] **Step 1: Tests qui échouent** (patron des tests de hooks du fichier : `QueryClientProvider` wrapper + mock `api`)

```tsx
describe('useSearchMessages', () => {
  it('fetches when criteria are set', async () => {
    mocks.searchMessages.mockResolvedValue({ total: 1, page: 0, pageSize: 50, results: [] })
    const { result } = renderHook(
      () => useSearchMessages({ folderPath: 'INBOX', allFolders: false, quick: 'x' }, 0, 50),
      { wrapper },
    )
    await waitFor(() => expect(result.current.data?.total).toBe(1))
    expect(mocks.searchMessages).toHaveBeenCalledWith(
      { folderPath: 'INBOX', allFolders: false, quick: 'x' }, 0, 50, expect.anything())
  })

  it('stays idle with null criteria', async () => {
    renderHook(() => useSearchMessages(null, 0, 50), { wrapper })
    await settle()
    expect(mocks.searchMessages).not.toHaveBeenCalled()
  })
})
```

(Ajouter `searchMessages` au mock `api` du fichier ; `settle()` vient de `src/test-utils` — assertion de silence.)

- [ ] **Step 2: Vérifier l'échec**

Run: `npx vitest run src/modules/mail/queries.test.tsx`
Expected: FAIL — `useSearchMessages` absent.

- [ ] **Step 3: Implémenter**

Dans `mailKeys` :

```ts
/** Prefix for every cached search — what the optimistic writes patch. */
searchIn: (accountId: string) => ['mail', accountId, 'search'] as const,
// Criteria in the key: two different searches are two caches.
search: (accountId: string, criteria: SearchCriteria, page: number, pageSize: number) =>
  ['mail', accountId, 'search', criteria, page, pageSize] as const,
```

Imports : `import type { SearchCriteria } from './list/searchCriteria'` et `MailSearchPage` depuis `./api/mailTypes`.

Hook (après `useMessage`) :

```ts
/**
 * A search is a snapshot: no window-focus replay (an all-folders sweep is N IMAP SEARCHes),
 * no poll, no writes key. placeholderData keeps the previous page while the next loads.
 */
export function useSearchMessages(criteria: SearchCriteria | null, page: number, pageSize: number) {
  const accountId = useAccountId()

  return useQuery<MailSearchPage>({
    queryKey: criteria
      ? mailKeys.search(accountId, criteria, page, pageSize)
      : [...mailKeys.searchIn(accountId), 'idle'],
    queryFn: ({ signal }) => api.searchMessages(criteria, page, pageSize, { signal }),
    enabled: criteria !== null && pageSize > 0,
    refetchOnWindowFocus: false,
    placeholderData: (previous) => previous,
  })
}
```

- [ ] **Step 4: Vérifier le vert, puis commit**

Run: `npx vitest run src/modules/mail/queries.test.tsx`
Expected: PASS.

```bash
git add src/frontend/src/modules/mail/queries.ts src/frontend/src/modules/mail/queries.test.tsx
git commit -m "Frontend 2b4: useSearchMessages query"
```

---

### Task 9: Frontend — les mutations patchent aussi les caches de recherche

**Files:**
- Modify: `src/frontend/src/modules/mail/list/listPatch.ts`
- Modify: `src/frontend/src/modules/mail/queries.ts` (`useSetFlags`, `removeFromFolderCaches`)
- Test: `src/frontend/src/modules/mail/list/listPatch.test.ts` (ajout) et `src/frontend/src/modules/mail/queries.test.tsx` (ajout)

**Interfaces:**
- Produces (listPatch): `patchSearchResults(results, folderPath, uids, flag, value) → { results, unreadDelta, found }` ; `removeSearchResults(results, folderPath, uids) → { results, removed, removedUnread }`. Les deux ne touchent que les lignes dont `folderPath` correspond — un même uid dans un autre dossier est un autre message.

- [ ] **Step 1: Tests listPatch qui échouent**

```ts
const result = (uid: number, folderPath: string, seen = true) =>
  ({ ...summary(uid, { seen }), folderPath, uidValidity: 1 })
// `summary(uid, overrides)` : réutiliser/adapter le builder du fichier de tests existant.

describe('patchSearchResults', () => {
  it('patches only the rows of the mutated folder', () => {
    const rows = [result(1, 'INBOX', false), result(1, 'Archive', false)]
    const patch = patchSearchResults(rows, 'INBOX', [1], 'seen', true)
    expect(patch.found).toBe(1)
    expect(patch.unreadDelta).toBe(-1)
    expect(patch.results[0].seen).toBe(true)
    expect(patch.results[1].seen).toBe(false)
  })
})

describe('removeSearchResults', () => {
  it('removes only the rows of the mutated folder and counts unread', () => {
    const rows = [result(1, 'INBOX', false), result(1, 'Archive'), result(2, 'INBOX')]
    const removal = removeSearchResults(rows, 'INBOX', [1, 2])
    expect(removal.removed).toBe(2)
    expect(removal.removedUnread).toBe(1)
    expect(removal.results).toEqual([rows[1]])
  })
})
```

- [ ] **Step 2: Vérifier l'échec, puis implémenter dans `listPatch.ts`**

```ts
import type { MailFolderNode, MailMessageSummary, MailSearchResult } from '../api/mailTypes'

export interface SearchResultsPatch {
  results: MailSearchResult[]
  unreadDelta: number
  found: number
}

/** patchSummaries scoped to one folder: a same uid under another folder is another message. */
export function patchSearchResults(
  results: MailSearchResult[], folderPath: string, uids: number[], flag: MailFlagName, value: boolean,
): SearchResultsPatch {
  const targets = new Set(uids)
  let unreadDelta = 0
  let found = 0

  const patched = results.map(row => {
    if (row.folderPath !== folderPath || !targets.has(row.uid)) return row
    found += 1
    if (flag === 'seen') {
      if (row.seen === value) return row
      unreadDelta += value ? -1 : 1
      return { ...row, seen: value }
    }
    if (row.flagged === value) return row
    return { ...row, flagged: value }
  })

  return { results: patched, unreadDelta, found }
}

export interface RemovedSearchResults {
  results: MailSearchResult[]
  removed: number
  removedUnread: number
}

export function removeSearchResults(
  results: MailSearchResult[], folderPath: string, uids: number[],
): RemovedSearchResults {
  const targets = new Set(uids)
  let removed = 0
  let removedUnread = 0

  const kept = results.filter(row => {
    if (row.folderPath !== folderPath || !targets.has(row.uid)) return true
    removed += 1
    if (!row.seen) removedUnread += 1
    return false
  })

  return { results: removed === 0 ? results : kept, removed, removedUnread }
}
```

Run: `npx vitest run src/modules/mail/list/listPatch.test.ts` → PASS.

- [ ] **Step 3: Test queries qui échoue — une action retire la ligne des résultats, un rollback la restaure**

```tsx
it('a move drops the row from cached search results and rolls back on error', async () => {
  // seed : queryClient.setQueryData(mailKeys.search('primary', criteria, 0, 50), searchPage)
  // où searchPage.results contient un uid de 'INBOX' ; mocks.moveMessages rejette.
  // 1) après mutate: le cache de recherche ne contient plus la ligne
  // 2) après l'échec (await settle()): la ligne est revenue (snapshot rollback)
})

it('setting seen patches cached search results', async () => {
  // seed pareil avec un résultat non lu ; mocks.setMessageFlags résout.
  // après mutate: la ligne du cache de recherche porte seen=true
})
```

- [ ] **Step 4: Implémenter dans `queries.ts`**

Import : `patchSearchResults, removeSearchResults` depuis `./list/listPatch`, `MailSearchPage` déjà importé (Task 8).

Dans `useSetFlags.onMutate`, après la boucle stream, ajouter :

```ts
// Search caches are summaries too: the same patch, scoped to the mutated folder, with the
// same snapshot rollback. The results stay a snapshot otherwise — the poll never touches them.
const searchKey = mailKeys.searchIn(accountId)
await queryClient.cancelQueries({ queryKey: searchKey })
for (const [key, page] of queryClient.getQueriesData<MailSearchPage>({ queryKey: searchKey })) {
  if (!page) continue
  const patch = patchSearchResults(page.results, folderPath, uids, flag, value)
  if (patch.found === 0) continue
  snapshots.push([key, page])
  queryClient.setQueryData(key, { ...page, results: patch.results })
  tally.count(page.results.filter(row => row.folderPath === folderPath))
}
```

Dans `removeFromFolderCaches`, après la boucle stream, ajouter :

```ts
for (const [key, page] of
  queryClient.getQueriesData<MailSearchPage>({ queryKey: mailKeys.searchIn(accountId) })) {
  if (!page) continue
  const removal = removeSearchResults(page.results, folderPath, uids)
  if (removal.removed === 0) continue
  snapshots.push([key, page])
  tally.count(page.results.filter(row => row.folderPath === folderPath))
  queryClient.setQueryData(key, {
    ...page, results: removal.results, total: Math.max(0, page.total - removal.removed),
  })
}
```

(`removeFromFolderCaches` est appelé par move/delete — les deux profitent du retrait. `blankFolderCaches`/`dropFolderCaches` ne changent pas : vider un dossier pendant une recherche reste un instantané que le poll réconcilie.)

- [ ] **Step 5: Vérifier le vert (suite mail complète), puis commit**

Run: `npx vitest run src/modules/mail`
Expected: PASS.

```bash
git add src/frontend/src/modules/mail/list/listPatch.ts src/frontend/src/modules/mail/list/listPatch.test.ts src/frontend/src/modules/mail/queries.ts src/frontend/src/modules/mail/queries.test.tsx
git commit -m "Frontend 2b4: optimistic writes patch search caches too"
```

---

### Task 10: Frontend — `SearchIcon` + `SearchBar`

**Files:**
- Create: `src/frontend/src/icons/SearchIcon.tsx`
- Create: `src/frontend/src/modules/mail/list/SearchBar.tsx`
- Modify: `src/frontend/src/styles/mail.css`
- Test: `src/frontend/src/modules/mail/list/SearchBar.test.tsx`

**Interfaces:**
- Produces: `SearchBar({ folderTitle, onSearch(text), onOpenAdvanced(text), onClose })` — Entrée soumet (texte non vide), Escape ferme, chevron ouvre l'avancée avec le texte courant. `SearchIcon({ size?: number })` style Feather stroke 2 (patron : `ArchiveIcon`).

- [ ] **Step 1: Tests qui échouent**

```tsx
import { fireEvent, render, screen } from '@testing-library/react'
import SearchBar from './SearchBar'

function setup() {
  const onSearch = vi.fn(); const onOpenAdvanced = vi.fn(); const onClose = vi.fn()
  render(<SearchBar folderTitle="Inbox" onSearch={onSearch} onOpenAdvanced={onOpenAdvanced} onClose={onClose} />)
  return { onSearch, onOpenAdvanced, onClose, input: screen.getByPlaceholderText('Search in Inbox') }
}

it('submits the trimmed text on Enter', () => {
  const { onSearch, input } = setup()
  fireEvent.change(input, { target: { value: '  facture ' } })
  fireEvent.keyDown(input, { key: 'Enter' })
  expect(onSearch).toHaveBeenCalledWith('facture')
})

it('ignores Enter on a blank field', () => {
  const { onSearch, input } = setup()
  fireEvent.keyDown(input, { key: 'Enter' })
  expect(onSearch).not.toHaveBeenCalled()
})

it('closes on Escape', () => {
  const { onClose, input } = setup()
  fireEvent.keyDown(input, { key: 'Escape' })
  expect(onClose).toHaveBeenCalled()
})

it('opens the advanced search with the current text', () => {
  const { onOpenAdvanced, input } = setup()
  fireEvent.change(input, { target: { value: 'alice' } })
  fireEvent.click(screen.getByRole('button', { name: 'Advanced search' }))
  expect(onOpenAdvanced).toHaveBeenCalledWith('alice')
})

it('focuses the field on mount', () => {
  const { input } = setup()
  expect(input).toHaveFocus()
})
```

- [ ] **Step 2: Vérifier l'échec, puis implémenter**

`SearchIcon.tsx` (patron des icônes existantes, ex. `ArchiveIcon`) :

```tsx
export default function SearchIcon({ size = 20 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <circle cx="11" cy="11" r="7" />
      <path d="m20 20-3.5-3.5" />
    </svg>
  )
}
```

`SearchBar.tsx` :

```tsx
import { useState } from 'react'
import type { KeyboardEvent } from 'react'
import ChevronRightIcon from '../../../icons/ChevronRightIcon'

interface Props {
  folderTitle: string
  onSearch: (text: string) => void
  onOpenAdvanced: (text: string) => void
  onClose: () => void
}

/** The collapsible quick-search band. Enter searches subject OR sender in the open folder. */
export default function SearchBar({ folderTitle, onSearch, onOpenAdvanced, onClose }: Props) {
  const [text, setText] = useState('')

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Enter' && text.trim()) onSearch(text.trim())
    // Escape must not also clear the list selection behind the bar.
    if (event.key === 'Escape') { event.stopPropagation(); onClose() }
  }

  return (
    <div className="search-bar">
      <input
        type="text"
        className="search-bar-input"
        placeholder={`Search in ${folderTitle}`}
        value={text}
        autoFocus
        onChange={event => setText(event.target.value)}
        onKeyDown={onKeyDown}
      />
      <button
        type="button"
        className="search-bar-advanced"
        aria-label="Advanced search"
        title="Advanced search"
        onClick={() => onOpenAdvanced(text)}
      >
        <ChevronRightIcon size={16} />
      </button>
    </div>
  )
}
```

`mail.css` (près de `.selection-toolbar`) :

```css
/* Quick-search band, folded open by the toolbar's magnifier. */
.search-bar {
  flex: none;
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 6px 10px;
  border-bottom: 1px solid var(--list-separator);
}

.search-bar-input {
  flex: 1;
  min-width: 0;
  padding: 6px 10px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--surface);
  color: var(--text);
  font-size: 13px;
}

.search-bar-advanced {
  flex: none;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  border: none;
  border-radius: 6px;
  background: none;
  color: var(--text-muted);
  cursor: pointer;
}

.search-bar-advanced:hover {
  background: var(--pane-item-hover);
  color: var(--text);
}
```

- [ ] **Step 3: Vérifier le vert, puis commit**

Run: `npx vitest run src/modules/mail/list/SearchBar.test.tsx src/icons`
Expected: PASS.

```bash
git add src/frontend/src/icons/SearchIcon.tsx src/frontend/src/modules/mail/list/SearchBar.tsx src/frontend/src/modules/mail/list/SearchBar.test.tsx src/frontend/src/styles/mail.css
git commit -m "Frontend 2b4: quick search bar"
```

---

### Task 11: Frontend — `AdvancedSearchModal`

**Files:**
- Create: `src/frontend/src/modules/mail/list/AdvancedSearchModal.tsx`
- Modify: `src/frontend/src/styles/mail.css`
- Test: `src/frontend/src/modules/mail/list/AdvancedSearchModal.test.tsx`

**Interfaces:**
- Consumes: `AdvancedForm`, `daysSinceYearStart` (Task 7).
- Produces: `AdvancedSearchModal({ folderTitle, initialSubject, onSearch(form: AdvancedForm), onClose })`. Date select : All time / Last 7 days / Last 30 days / Last 6 months / This year. Case « Starred » (métaphore 2b1), « Has attachment », scope « This folder ({folderTitle}) / All folders ». Soumission refusée si formulaire vide.

- [ ] **Step 1: Tests qui échouent**

```tsx
import { fireEvent, render, screen } from '@testing-library/react'
import AdvancedSearchModal from './AdvancedSearchModal'

function setup(initialSubject = '') {
  const onSearch = vi.fn(); const onClose = vi.fn()
  render(<AdvancedSearchModal folderTitle="Inbox" initialSubject={initialSubject}
    onSearch={onSearch} onClose={onClose} />)
  return { onSearch, onClose }
}

it('prefills the subject with the quick text', () => {
  setup('facture')
  expect(screen.getByLabelText('Subject')).toHaveValue('facture')
})

it('submits the assembled form', () => {
  const { onSearch } = setup()
  fireEvent.change(screen.getByLabelText('From'), { target: { value: 'alice' } })
  fireEvent.change(screen.getByLabelText('Date'), { target: { value: '30' } })
  fireEvent.click(screen.getByLabelText('Unread'))
  fireEvent.change(screen.getByLabelText('Scope'), { target: { value: 'all' } })
  fireEvent.click(screen.getByRole('button', { name: 'Search' }))
  expect(onSearch).toHaveBeenCalledWith({
    from: 'alice', to: '', subject: '', text: '',
    sinceDays: 30, unread: true, flagged: false, hasAttachment: false, allFolders: true,
  })
})

it('maps This year to a day count', () => {
  const { onSearch } = setup()
  fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'x' } })
  fireEvent.change(screen.getByLabelText('Date'), { target: { value: 'year' } })
  fireEvent.click(screen.getByRole('button', { name: 'Search' }))
  const form = onSearch.mock.calls[0][0]
  expect(form.sinceDays).toBeGreaterThanOrEqual(1)
  expect(form.sinceDays).toBeLessThanOrEqual(366)
})

it('refuses an empty form', () => {
  const { onSearch } = setup()
  fireEvent.click(screen.getByRole('button', { name: 'Search' }))
  expect(onSearch).not.toHaveBeenCalled()
})

it('closes on Escape and on the cross', () => {
  const { onClose } = setup()
  fireEvent.keyDown(document, { key: 'Escape' })
  expect(onClose).toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Close' }))
  expect(onClose).toHaveBeenCalledTimes(2)
})
```

- [ ] **Step 2: Vérifier l'échec, puis implémenter**

Squelette `modal-overlay`/`modal`/`modal-header` de `MoveMessagesModal`, corps en `<form>` (Entrée soumet), champs `.field-h` avec `htmlFor`/`id` (règle projet : sans `id`, pas de nom accessible) :

```tsx
import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import SearchIcon from '../../../icons/SearchIcon'
import { daysSinceYearStart } from './searchCriteria'
import type { AdvancedForm } from './searchCriteria'

interface Props {
  folderTitle: string
  initialSubject: string
  onSearch: (form: AdvancedForm) => void
  onClose: () => void
}

/** The advanced-search popup. Filled fields combine with AND; scope widens to the whole box. */
export default function AdvancedSearchModal({ folderTitle, initialSubject, onSearch, onClose }: Props) {
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [subject, setSubject] = useState(initialSubject)
  const [text, setText] = useState('')
  const [date, setDate] = useState('')
  const [unread, setUnread] = useState(false)
  const [flagged, setFlagged] = useState(false)
  const [hasAttachment, setHasAttachment] = useState(false)
  const [scope, setScope] = useState<'this' | 'all'>('this')

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  const empty = !from.trim() && !to.trim() && !subject.trim() && !text.trim()
    && !date && !unread && !flagged && !hasAttachment

  function submit(event: FormEvent) {
    event.preventDefault()
    if (empty) return
    onSearch({
      from, to, subject, text,
      sinceDays: date === '' ? null : date === 'year' ? daysSinceYearStart(new Date()) : Number(date),
      unread, flagged, hasAttachment,
      allFolders: scope === 'all',
    })
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" style={{ maxWidth: '560px' }} onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">Advanced search</span>
          <button className="modal-close" aria-label="Close" onClick={onClose}>✕</button>
        </div>

        <form onSubmit={submit}>
          <div className="advanced-search-grid">
            <div className="field-h">
              <label htmlFor="adv-from">From</label>
              <input id="adv-from" type="text" value={from} autoFocus onChange={e => setFrom(e.target.value)} />
            </div>
            <div className="field-h">
              <label htmlFor="adv-date">Date</label>
              <select id="adv-date" value={date} onChange={e => setDate(e.target.value)}>
                <option value="">All time</option>
                <option value="7">Last 7 days</option>
                <option value="30">Last 30 days</option>
                <option value="180">Last 6 months</option>
                <option value="year">This year</option>
              </select>
            </div>
            <div className="field-h">
              <label htmlFor="adv-to">To</label>
              <input id="adv-to" type="text" value={to} onChange={e => setTo(e.target.value)} />
            </div>
            <label className="advanced-search-check">
              <input type="checkbox" checked={unread} onChange={e => setUnread(e.target.checked)} />
              Unread
            </label>
            <div className="field-h">
              <label htmlFor="adv-subject">Subject</label>
              <input id="adv-subject" type="text" value={subject} onChange={e => setSubject(e.target.value)} />
            </div>
            <label className="advanced-search-check">
              <input type="checkbox" checked={flagged} onChange={e => setFlagged(e.target.checked)} />
              Starred
            </label>
            <div className="field-h">
              <label htmlFor="adv-text">Text</label>
              <input id="adv-text" type="text" value={text} onChange={e => setText(e.target.value)} />
            </div>
            <label className="advanced-search-check">
              <input type="checkbox" checked={hasAttachment} onChange={e => setHasAttachment(e.target.checked)} />
              Has attachment
            </label>
            <div className="field-h">
              <label htmlFor="adv-scope">Scope</label>
              <select id="adv-scope" value={scope} onChange={e => setScope(e.target.value as 'this' | 'all')}>
                <option value="this">This folder ({folderTitle})</option>
                <option value="all">All folders</option>
              </select>
            </div>
          </div>

          <div className="folder-pick-actions">
            <button type="button" className="btn btn-ghost" onClick={onClose}>Cancel</button>
            <button type="submit" className="btn btn-primary" style={{ width: 'auto' }} disabled={empty}>
              <SearchIcon size={15} /> Search
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
```

Note libellé : les checkboxes portent le label **autour** de l'input (pas de `.field-h`), donc `getByLabelText('Unread')` fonctionne sans `id`. Le bouton submit garde le nom accessible « Search » (l'icône est `aria-hidden`).

`mail.css` :

```css
/* Two columns like the admin dialogs: fields left, date and checkboxes right. */
.advanced-search-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px 24px;
  padding: 4px 0 12px;
}

.advanced-search-check {
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 13px;
  color: var(--text);
}
```

- [ ] **Step 3: Vérifier le vert, puis commit**

Run: `npx vitest run src/modules/mail/list/AdvancedSearchModal.test.tsx`
Expected: PASS.

```bash
git add src/frontend/src/modules/mail/list/AdvancedSearchModal.tsx src/frontend/src/modules/mail/list/AdvancedSearchModal.test.tsx src/frontend/src/styles/mail.css
git commit -m "Frontend 2b4: advanced search modal"
```

---

### Task 12: Frontend — `SearchResultsBanner`

**Files:**
- Create: `src/frontend/src/modules/mail/list/SearchResultsBanner.tsx`
- Modify: `src/frontend/src/styles/mail.css`
- Test: `src/frontend/src/modules/mail/list/SearchResultsBanner.test.tsx`

**Interfaces:**
- Produces: `SearchResultsBanner({ total: number | null, label: string | null, onClear })` — `total` null pendant le chargement (« Searching… ») ; « Clear » toujours accessible.

- [ ] **Step 1: Tests qui échouent**

```tsx
import { fireEvent, render, screen } from '@testing-library/react'
import SearchResultsBanner from './SearchResultsBanner'

it('quotes the query with its count', () => {
  render(<SearchResultsBanner total={3} label="facture" onClear={() => {}} />)
  expect(screen.getByText('3 results for “facture”')).toBeInTheDocument()
})

it('singularizes one result and handles a label-less search', () => {
  const { rerender } = render(<SearchResultsBanner total={1} label="x" onClear={() => {}} />)
  expect(screen.getByText('1 result for “x”')).toBeInTheDocument()
  rerender(<SearchResultsBanner total={2} label={null} onClear={() => {}} />)
  expect(screen.getByText('2 results')).toBeInTheDocument()
})

it('says searching while the total is unknown, with Clear still offered', () => {
  const onClear = vi.fn()
  render(<SearchResultsBanner total={null} label="x" onClear={onClear} />)
  expect(screen.getByText('Searching…')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Clear' }))
  expect(onClear).toHaveBeenCalled()
})
```

- [ ] **Step 2: Vérifier l'échec, puis implémenter** (même famille que `EmptyFolderBanner`)

```tsx
import SearchIcon from '../../../icons/SearchIcon'

interface Props {
  /** Null while the search is in flight — the banner still shows so Clear stays reachable. */
  total: number | null
  /** The quoted text, or null for a checkbox-only search. */
  label: string | null
  onClear: () => void
}

export default function SearchResultsBanner({ total, label, onClear }: Props) {
  const text = total === null
    ? 'Searching…'
    : `${total} result${total === 1 ? '' : 's'}${label ? ` for “${label}”` : ''}`

  return (
    <div className="search-results-banner">
      <SearchIcon size={15} />
      <span className="search-results-banner-text">{text}</span>
      <button type="button" className="search-results-banner-clear" onClick={onClear}>Clear ✕</button>
    </div>
  )
}
```

`mail.css` (à côté de `.empty-folder-banner`, mêmes tokens) :

```css
.search-results-banner {
  flex: none;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 7px 12px;
  font-size: 12.5px;
  color: var(--text);
  background: color-mix(in oklab, var(--badge-count-bg) 12%, transparent);
  border-bottom: 1px solid var(--list-separator);
}

.search-results-banner-text {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.search-results-banner-clear {
  flex: none;
  border: none;
  background: none;
  color: var(--badge-count-bg);
  font-weight: 600;
  font-size: 12.5px;
  cursor: pointer;
}
```

- [ ] **Step 3: Vérifier le vert, puis commit**

Run: `npx vitest run src/modules/mail/list/SearchResultsBanner.test.tsx`
Expected: PASS.

```bash
git add src/frontend/src/modules/mail/list/SearchResultsBanner.tsx src/frontend/src/modules/mail/list/SearchResultsBanner.test.tsx src/frontend/src/styles/mail.css
git commit -m "Frontend 2b4: search results banner"
```

---

### Task 13: Frontend — loupe dans `SelectionToolbar`

**Files:**
- Modify: `src/frontend/src/modules/mail/list/SelectionToolbar.tsx`
- Modify: `src/frontend/src/styles/mail.css`
- Test: `src/frontend/src/modules/mail/list/SelectionToolbar.test.tsx` (ajout + mise à jour des props des tests existants)

**Interfaces:**
- Produces: props ajoutées — `searchOpen: boolean`, `onToggleSearch: () => void`, `selectionDisabled?: boolean` (all-folders : la case maître est désactivée). La loupe se place entre le bouton Delete et le kebab.

- [ ] **Step 1: Tests qui échouent**

```tsx
it('toggles the search bar from the magnifier', () => {
  const onToggleSearch = vi.fn()
  renderToolbar({ searchOpen: false, onToggleSearch })   // helper existant du fichier, props par défaut à compléter
  fireEvent.click(screen.getByRole('button', { name: 'Search' }))
  expect(onToggleSearch).toHaveBeenCalled()
})

it('marks the magnifier active while the bar is open', () => {
  renderToolbar({ searchOpen: true })
  expect(screen.getByRole('button', { name: 'Search' }).className).toContain('is-active')
})

it('disables the master checkbox when selection is disabled', () => {
  renderToolbar({ selectionDisabled: true })
  expect(screen.getByRole('checkbox', { name: 'Select all' })).toBeDisabled()
})
```

- [ ] **Step 2: Vérifier l'échec, puis implémenter**

Dans `SelectionToolbarProps` :

```ts
searchOpen: boolean
onToggleSearch: () => void
/** All-folders results: rows carry no checkbox, so the master must not promise one. */
selectionDisabled?: boolean
```

Case maître : ajouter `disabled={props.selectionDisabled}`.

Entre le bouton Delete et le `DropdownMenu` :

```tsx
<button
  type="button"
  className={`selection-btn${props.searchOpen ? ' is-active' : ''}`}
  aria-label="Search"
  title="Search"
  onClick={props.onToggleSearch}
>
  <SearchIcon size={20} />
</button>
```

Import `SearchIcon`. Dans `mail.css`, l'état actif (tokens existants) :

```css
.selection-btn.is-active {
  color: var(--badge-count-bg);
  background: color-mix(in oklab, var(--badge-count-bg) 14%, transparent);
}
```

Mettre à jour les tests existants du fichier pour fournir les deux nouvelles props obligatoires (`searchOpen: false`, `onToggleSearch: vi.fn()`) — idéalement via le helper de rendu commun.

- [ ] **Step 3: Vérifier le vert, puis commit**

Run: `npx vitest run src/modules/mail/list/SelectionToolbar.test.tsx`
Expected: PASS.

```bash
git add src/frontend/src/modules/mail/list/SelectionToolbar.tsx src/frontend/src/modules/mail/list/SelectionToolbar.test.tsx src/frontend/src/styles/mail.css
git commit -m "Frontend 2b4: toolbar magnifier and selection gate"
```

---

### Task 14: Frontend — mode recherche dans `MessageList`

**Files:**
- Modify: `src/frontend/src/modules/mail/list/MessageList.tsx`
- Test: `src/frontend/src/modules/mail/list/MessageList.test.tsx` (ajout)

**Interfaces:**
- Consumes: Tasks 7–13.
- Produces: props ajoutées à `MessageList` — `search: SearchCriteria | null`, `onSearchChange: (criteria: SearchCriteria | null) => void`, `onOpenResult?: (uid: number, folderPath: string) => void`. Comportement : liste ↔ résultats, pagination de recherche, neutralisation sélection/étoile/cluster/drag en all-folders, `onRows([])` en all-folders, bannière résultats, `EmptyFolderBanner` masquée pendant une recherche.

- [ ] **Step 1: Tests qui échouent** (dans le fichier existant ; réutiliser ses mocks/helpers ; mock `api.searchMessages`)

Cas à couvrir :

```tsx
describe('MessageList searching', () => {
  // 1) la loupe déplie la barre ; Entrée appelle onSearchChange({folderPath, allFolders:false, quick:'x'})
  // 2) avec search actif (prop), les résultats du mock sont rendus à la place de la liste,
  //    la bannière affiche « 2 results for “x” », EmptyFolderBanner absente même en trash
  // 3) Clear appelle onSearchChange(null)
  // 4) replier la barre (loupe) appelle onSearchChange(null)
  // 5) all-folders: aucune checkbox de ligne, master désactivée, clic ligne → onOpenResult(uid, 'Archive'),
  //    onRows appelé avec []
  // 6) dossier courant: clic ligne → onSelect(uid) ; checkboxes présentes
  // 7) pagination: total 300, pageSize 100 → pager 3 pages ; cliquer 2 → nouvelle requête page 1
})
```

- [ ] **Step 2: Vérifier l'échec, puis implémenter**

Modifications dans `MessageList.tsx` :

```tsx
// imports ajoutés
import AdvancedSearchModal from './AdvancedSearchModal'
import SearchBar from './SearchBar'
import SearchResultsBanner from './SearchResultsBanner'
import { criteriaFromForm, labelOf } from './searchCriteria'
import type { SearchCriteria } from './searchCriteria'
import type { MailSearchResult } from '../api/mailTypes'
import { requestSizeOf } from '../../../hooks/usePreferences'   // à côté de showPreviewOf
import { useSearchMessages } from '../queries'                   // à côté des autres hooks

// props ajoutées (interface Props)
search: SearchCriteria | null
onSearchChange: (criteria: SearchCriteria | null) => void
onOpenResult?: (uid: number, folderPath: string) => void
```

État et vue (remplace la destructuration directe de `useMessageList`) :

```tsx
const list = useMessageList(folderPath)
const [searchOpen, setSearchOpen] = useState(false)
const [advanced, setAdvanced] = useState<{ subject: string } | null>(null)
const [searchPage, setSearchPage] = useState(0)

// Render-time resets, the useMessageList pattern: no effect-lag page or stale-bar frame.
const [shownSearch, setShownSearch] = useState(search)
if (search !== shownSearch) { setShownSearch(search); setSearchPage(0) }
const [shownFolder, setShownFolder] = useState(folderPath)
if (folderPath !== shownFolder) { setShownFolder(folderPath); setSearchOpen(false); setAdvanced(null) }

const searchSize = preferences ? requestSizeOf(preferences) : 0
const searchQuery = useSearchMessages(search, searchPage, searchSize)
const searching = search !== null
const crossFolder = searching && search.allFolders

// One shape for the render, whichever source fills it — rows/pager/footer never learn which.
const view = searching
  ? {
      messages: (searchQuery.data?.results ?? []) as MailMessageSummary[],
      total: searchQuery.data?.total ?? 0,
      isLoading: searchQuery.isLoading,
      isError: searchQuery.isError,
      paging: {
        page: searchPage,
        lastPage: searchSize > 0
          ? Math.max(0, Math.ceil((searchQuery.data?.total ?? 0) / searchSize) - 1)
          : 0,
        onSelect: setSearchPage,
      },
      streaming: null,
    }
  : list
const { messages, total, isLoading, isError, paging, streaming } = view
```

`resetKey` (la sélection se vide en entrant/sortant de recherche et à chaque page de résultats) :

```tsx
const resetKey = `${folderPath}::${searching ? `search:${searchPage}` : (paging ? paging.page : 'stream')}`
```

Handlers :

```tsx
function toggleSearch() {
  if (searchOpen) closeSearch()
  else setSearchOpen(true)
}

function closeSearch() {
  setSearchOpen(false)
  setAdvanced(null)
  onSearchChange(null)
}

function quickSearch(text: string) {
  if (folderPath) onSearchChange({ folderPath, allFolders: false, quick: text })
}

function advancedSearch(form: AdvancedForm) {
  setAdvanced(null)
  if (folderPath) onSearchChange(criteriaFromForm(folderPath, form))
}
```

(import `AdvancedForm` type depuis `./searchCriteria`.)

Ouverture d'une ligne — remplacer les deux appels `onSelect(message.uid)` (clic + `onRowKey`) par :

```tsx
function openRow(message: MailMessageSummary) {
  if (crossFolder) onOpenResult?.(message.uid, (message as MailSearchResult).folderPath)
  else onSelect(message.uid)
}
```

Neutralisation all-folders dans le rendu de ligne :
- `check` : rendu seulement quand `!crossFolder` (dans les deux skins) ;
- `star` et `cluster` : rendus seulement quand `!crossFolder` ;
- `draggable={!crossFolder}` et `onDragStart` court-circuité (`if (crossFolder) return` en tête de `onRowDragStart`).

`onRows` :

```tsx
useEffect(() => {
  onRows?.(crossFolder ? [] : messages.map(message => message.uid))
}, [messages, crossFolder, onRows])
```

Rendu des bandes (ordre : toolbar → barre → bannière résultats OU bannière vider → scroll) :

```tsx
<SelectionToolbar
  ... props existantes ...
  searchOpen={searchOpen}
  onToggleSearch={toggleSearch}
  selectionDisabled={crossFolder}
/>

{searchOpen && folderPath && (
  <SearchBar
    folderTitle={folderName || folderPath}
    onSearch={quickSearch}
    onOpenAdvanced={text => setAdvanced({ subject: text })}
    onClose={closeSearch}
  />
)}

{searching && (
  <SearchResultsBanner
    total={searchQuery.data?.total ?? null}
    label={labelOf(search)}
    onClear={closeSearch}
  />
)}

{!searching && <EmptyFolderBanner role={folderRole ?? null} total={total} onEmpty={requestEmpty} />}

... zone scrollable et footers inchangés (le pager sert les deux vues) ...

{advanced && folderPath && (
  <AdvancedSearchModal
    folderTitle={folderName || folderPath}
    initialSubject={advanced.subject}
    onSearch={advancedSearch}
    onClose={() => setAdvanced(null)}
  />
)}
```

États vides : dans `rows()`, quand `searching`, remplacer « Loading messages… » par « Searching… » et « No messages » par « No results. » :

```tsx
if (isLoading) return <p className="mail-empty">{searching ? 'Searching…' : 'Loading messages…'}</p>
...
if (messages.length === 0) return <p className="mail-empty">{searching ? 'No results.' : 'No messages'}</p>
```

- [ ] **Step 3: Vérifier le vert (fichier + suite mail), puis commit**

Run: `npx vitest run src/modules/mail/list/MessageList.test.tsx && npx vitest run src/modules/mail`
Expected: PASS.

```bash
git add src/frontend/src/modules/mail/list/MessageList.tsx src/frontend/src/modules/mail/list/MessageList.test.tsx
git commit -m "Frontend 2b4: search mode in the message list"
```

---

### Task 15: Frontend — câblage `MailLayout` (critères, lecteur multi-dossiers)

**Files:**
- Modify: `src/frontend/src/modules/mail/MailLayout.tsx`
- Test: `src/frontend/src/modules/mail/MailLayout.test.tsx` (ajout ; mock `api.searchMessages`)

**Interfaces:**
- Consumes: props `search`/`onSearchChange`/`onOpenResult` de `MessageList` (Task 14).
- Produces: les critères actifs, effacés au changement de dossier ; le lecteur reçoit le dossier du résultat ouvert (`resultFolder ?? folder`) ; Clear ferme le lecteur si le message venait d'un autre dossier.

- [ ] **Step 1: Tests qui échouent**

Cas à couvrir (patron des tests d'intégration existants du fichier — **rappel** : tout test ouvrant un message doit retarder `getMailMessage` de 250 ms, sinon le détail immédiat marque la ligne lue et annule le fetch de liste en vol) :

```tsx
describe('searching from the layout', () => {
  // 1) loupe → saisir « x » → Entrée : api.searchMessages appelé avec
  //    { folderPath: 'INBOX', allFolders: false, quick: 'x' }, les résultats s'affichent
  // 2) résultat all-folders (folderPath 'Archive') cliqué : getMailMessage appelé avec
  //    folder 'Archive' — l'URL garde folder=INBOX
  // 3) Clear avec un résultat d'un autre dossier ouvert : le lecteur se ferme (uid retiré)
  // 4) changer de dossier dans l'arbre pendant une recherche : la liste redevient le dossier
  //    (api.searchMessages non rappelé, bannière absente)
})
```

- [ ] **Step 2: Vérifier l'échec, puis implémenter**

Dans `MailLayout.tsx` :

```tsx
import type { SearchCriteria } from './list/searchCriteria'

const [search, setSearch] = useState<SearchCriteria | null>(null)
// The folder a cross-folder result was opened from; null when the reader shows the URL folder.
const [resultFolder, setResultFolder] = useState<string | null>(null)

// A search belongs to the folder it was typed in: navigating away drops it (render-time
// reset, the useMessageList pattern).
const [searchFolder, setSearchFolder] = useState(folder)
if (folder !== searchFolder) {
  setSearchFolder(folder)
  setSearch(null)
  setResultFolder(null)
}
// The reader closed (departed past the last row, Back): a stale cross-folder origin must not
// survive to relabel the next open.
if (uid === null && resultFolder !== null) setResultFolder(null)

const changeSearch = useCallback((criteria: SearchCriteria | null) => {
  setSearch(criteria)
  if (criteria !== null) return
  // Clearing while a cross-folder result is open: its uid means nothing in the URL folder.
  setResultFolder(current => {
    if (current !== null) setParams(previous => {
      const path = previous.get('folder')
      return path ? { folder: path } : previous
    })
    return null
  })
}, [setParams])

const openResult = useCallback((nextUid: number, fromFolder: string) => {
  if (!folder) return
  setResultFolder(fromFolder === folder ? null : fromFolder)
  setParams({ folder, uid: String(nextUid) })
}, [folder, setParams])
```

`selectMessage` remet `resultFolder` à null :

```tsx
function selectMessage(nextUid: number) {
  if (!folder) return
  setResultFolder(null)
  setParams({ folder, uid: String(nextUid) })
}
```

Dossier du lecteur (au-dessus du JSX) :

```tsx
const readerFolder = resultFolder ?? folder
const readerNode = folders && readerFolder
  ? flatten(folders).find(entry => entry.node.path === readerFolder)?.node
  : undefined
```

Les trois rendus de `MessageReader` passent `folderPath={readerFolder}` et `folderRole={readerNode?.specialUse ?? null}` (au lieu de `folder` / `folderNode`).

`list()` passe les nouvelles props :

```tsx
<MessageList
  ... props existantes ...
  search={search}
  onSearchChange={changeSearch}
  onOpenResult={openResult}
/>
```

- [ ] **Step 3: Vérifier le vert (suite complète), puis commit**

Run: `npx vitest run`
Expected: PASS (toutes suites frontend).

```bash
git add src/frontend/src/modules/mail/MailLayout.tsx src/frontend/src/modules/mail/MailLayout.test.tsx
git commit -m "Frontend 2b4: layout wiring and cross-folder reader"
```

---

### Task 16: Vérification finale + documentation

**Files:**
- Modify: `src/frontend/CLAUDE.md` (le paragraphe « Composing and search are not in yet » + la liste des fichiers `list/`)
- Modify: `src/snoopy.microservice/CLAUDE.md` (ajout de `POST /api/Mail/Messages/Search` à la liste des endpoints `MailController`)

- [ ] **Step 1: Suites complètes des deux projets**

Run: `dotnet test src/snoopy.microservice` → PASS.
Run: `cd src/frontend && npx vitest run` → PASS.
Run: `npm run build && npx tsc --noEmit && npx eslint src` (frontend) → clean.

- [ ] **Step 2: Documentation**

- `src/frontend/CLAUDE.md` : « Composing and search are not in yet. » → « Composing is not in yet. » + une phrase décrivant la recherche (loupe → barre rapide objet/expéditeur, popup avancée avec portée tous-dossiers, résultats paginés en instantané) ; ajouter `list/SearchBar.tsx`, `list/AdvancedSearchModal.tsx`, `list/SearchResultsBanner.tsx`, `list/searchCriteria.ts` à l'inventaire des fichiers.
- `src/snoopy.microservice/CLAUDE.md` : ajouter l'endpoint à la description de `MailController` (critères ET, Quick = objet OU expéditeur, AllFolders en une session, PJ post-filtrée avant pagination, 200/400/401/502).

- [ ] **Step 3: Commit**

```bash
git add src/frontend/CLAUDE.md src/snoopy.microservice/CLAUDE.md
git commit -m "Docs 2b4: search endpoint and frontend search files"
```

**Jamais** ajouter `.claude/settings.local.json` à un commit.

---

## Self-review (fait à la rédaction)

- Spec §1–§7 couverts : portée (T3/T5/T11), rapide OR (T1 `Quick`), popup (T11), bandeau/Clear (T12/T14), pagination (T14), URL intacte + lecteur multi-dossiers (T15), pas de stream ni de poll sur les résultats (T8), PJ avant pagination (T3), neutralisation all-folders (T13/T14).
- Déviation spec §4 (patch des caches de recherche par les mutations au lieu d'un retrait local) : annoncée en tête de plan, implémentée en T9.
- Types cohérents inter-tâches : `MailSearchCriteria` (T1) consommé par T3/T4/T5 ; `MailSearchPage`/`MailSearchResult` (T2/T7) par T3–T9/T14 ; `SearchCriteria` (T7) par T8/T9/T14/T15 ; `AdvancedForm` (T7) par T11/T14.
