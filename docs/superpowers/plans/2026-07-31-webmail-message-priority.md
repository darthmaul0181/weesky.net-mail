# Message Priority Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the composer declare a message priority (High / Normal / Low), write it as RFC headers on the sent message and on the saved draft, and read it back on incoming mail into the list row and the reader.

**Architecture:** One enum shared by both directions. A pure static pair in `Services/` — `MailPriorityReader` (headers → enum) and `MailPriorityHeaders` (enum → headers) — sitting side by side so the write and the read cannot drift apart. The reader is wired at exactly two points on the backend (`ImapSession.FillSummary`, which the list and the search already share, and the detail mapping) plus the draft-open mapping. On the client, the composer reveals a `Priority` row beside Cc/Bcc, and the list row and reader render a glyph.

**Tech Stack:** ASP.NET Core (.NET 10), MimeKit/MailKit, xUnit + Moq. React 18 + TypeScript, TanStack Query, Vitest + jsdom + Testing Library.

**Spec:** `docs/superpowers/specs/2026-07-31-webmail-message-priority-design.md`

## Global Constraints

- **`Normal` writes no header at all** — not `X-Priority: 3`. An ordinary message says nothing about its priority.
- **Three headers are written together** for High and for Low: `X-Priority`, `Importance`, `X-MSMail-Priority`. Never one of the three alone — different clients read different ones.
- **A reply never inherits the priority.** `PreparedQuote` is untouched; reply, forward and edit-as-new all open at Normal.
- **Backend tests:** run `dotnet test` from `src/snoopy.microservice` — **never** `--no-build` when a new test file has been added.
- **`ApiDocumentation.xml` is a tracked artefact that `dotnet test` rewrites** with hundreds of unrelated lines. Check `git status` before every backend commit and `git checkout -- src/snoopy.microservice/ApiDocumentation.xml` if it moved for reasons unrelated to your change.
- **Frontend tests:** `npx vitest run <path>` from `src/frontend`; `npm run lint` and `npm run typecheck` before each frontend commit.
- **No literal colours** in `mail.css` — use role tokens (`--danger`, `--text-muted`).
- **jsdom sees no layout.** Do not assert geometry in a test; the browser check is Task 7.

## Two refinements over the spec

Both are deliberate and are called out where they land:

1. **The header writing is `MailPriorityHeaders`, a static beside `MailPriorityReader`** — not a private method inside `OutgoingMessageFactory` as the spec wrote. The factory has no unit test of its own and standing one up needs six mocked dependencies; a static pair costs nothing to test and buys a genuine write-then-read round-trip assertion. The factory's own call site stays one line, covered by review rather than by a test — stated here rather than glossed over.
2. **The enum carries `[JsonStringEnumMemberName]`.** `Program.cs` registers a bare `JsonStringEnumConverter`, which serialises `High` as `"High"`; every other field on these payloads is camelCase. The attribute pins the wire format to `"normal" | "high" | "low"` without touching global serialisation, and Task 1 asserts it.

---

### Task 1: The enum and the pure header pair

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/MailPriority.cs`
- Create: `src/snoopy.microservice/Services/MailPriorityReader.cs`
- Create: `src/snoopy.microservice/Services/MailPriorityHeaders.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailPriorityReaderTests.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailPriorityHeadersTests.cs`

**Interfaces:**
- Consumes: `HeaderListExtensions.Topmost(this HeaderList, string field)` — already exists in `Services/HeaderListExtensions.cs`, returns `Header?`.
- Produces:
  - `enum MailPriority { Normal, High, Low }` in namespace `weesky.Snoopy.Microservice.Models.Mail`
  - `MailPriorityReader.Parse(HeaderList headers) → MailPriority`
  - `MailPriorityReader.Fields → string[]` (the four header names a FETCH must request)
  - `MailPriorityHeaders.Apply(MimeMessage message, MailPriority priority) → void`

- [ ] **Step 1: Write the failing reader tests**

Create `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailPriorityReaderTests.cs`:

```csharp
using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Tests.Services;

public class MailPriorityReaderTests
{
    private static HeaderList Headers(params (string Field, string Value)[] entries)
    {
        var headers = new HeaderList();
        foreach (var (field, value) in entries) headers.Add(field, value);
        return headers;
    }

    [Theory]
    [InlineData("1", MailPriority.High)]
    [InlineData("1 (Highest)", MailPriority.High)]
    [InlineData("2 (High)", MailPriority.High)]
    [InlineData("3", MailPriority.Normal)]
    [InlineData("3 (Normal)", MailPriority.Normal)]
    [InlineData("4 (Low)", MailPriority.Low)]
    [InlineData("5 (Lowest)", MailPriority.Low)]
    public void ReadsTheLevelOutOfXPriorityPastItsComment(string value, MailPriority expected) =>
        Assert.Equal(expected, MailPriorityReader.Parse(Headers(("X-Priority", value))));

    /// <summary>An explicit 3 is an explicit Normal — going on to Importance would overrule the sender.</summary>
    [Fact]
    public void AnExplicitNormalStopsTheChain() =>
        Assert.Equal(MailPriority.Normal, MailPriorityReader.Parse(
            Headers(("X-Priority", "3"), ("Importance", "high"))));

    [Fact]
    public void AnUnreadableXPriorityFallsThroughToImportance() =>
        Assert.Equal(MailPriority.High, MailPriorityReader.Parse(
            Headers(("X-Priority", "urgent"), ("Importance", "high"))));

    [Fact]
    public void AnOutOfRangeXPriorityFallsThrough() =>
        Assert.Equal(MailPriority.Low, MailPriorityReader.Parse(
            Headers(("X-Priority", "9"), ("Importance", "low"))));

    [Theory]
    [InlineData("Importance", "high", MailPriority.High)]
    [InlineData("Importance", "LOW", MailPriority.Low)]
    [InlineData("Importance", "normal", MailPriority.Normal)]
    [InlineData("X-MSMail-Priority", "High", MailPriority.High)]
    [InlineData("X-MSMail-Priority", "Low", MailPriority.Low)]
    [InlineData("Priority", "urgent", MailPriority.High)]
    [InlineData("Priority", "non-urgent", MailPriority.Low)]
    public void ReadsTheWordHeaders(string field, string value, MailPriority expected) =>
        Assert.Equal(expected, MailPriorityReader.Parse(Headers((field, value))));

    /// <summary>The rule every header reader here follows — everything below the top could be forged.</summary>
    [Fact]
    public void TheTopmostOccurrenceWins() =>
        Assert.Equal(MailPriority.High, MailPriorityReader.Parse(
            Headers(("X-Priority", "1"), ("X-Priority", "5"))));

    [Fact]
    public void NoHeaderAtAllIsNormal() => Assert.Equal(MailPriority.Normal, MailPriorityReader.Parse(Headers()));

    [Fact]
    public void AnUnreadableValueEverywhereIsNormal() =>
        Assert.Equal(MailPriority.Normal, MailPriorityReader.Parse(
            Headers(("X-Priority", "banana"), ("Importance", "very"))));

    [Fact]
    public void FieldsNamesTheFourHeadersAFetchMustRequest() =>
        Assert.Equal(["X-Priority", "Importance", "X-MSMail-Priority", "Priority"], MailPriorityReader.Fields);
}
```

- [ ] **Step 2: Write the failing wire-format and writer tests**

Create `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailPriorityHeadersTests.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Tests.Services;

public class MailPriorityHeadersTests
{
    private static MimeMessage Applied(MailPriority priority)
    {
        var message = new MimeMessage();
        MailPriorityHeaders.Apply(message, priority);
        return message;
    }

    [Fact]
    public void HighWritesTheThreeHeaders()
    {
        var message = Applied(MailPriority.High);

        Assert.Equal("1 (Highest)", message.Headers["X-Priority"]);
        Assert.Equal("high", message.Headers["Importance"]);
        Assert.Equal("High", message.Headers["X-MSMail-Priority"]);
    }

    [Fact]
    public void LowWritesTheThreeHeaders()
    {
        var message = Applied(MailPriority.Low);

        Assert.Equal("5 (Lowest)", message.Headers["X-Priority"]);
        Assert.Equal("low", message.Headers["Importance"]);
        Assert.Equal("Low", message.Headers["X-MSMail-Priority"]);
    }

    /// <summary>An ordinary message says nothing about its priority — three absent headers, not "3".</summary>
    [Fact]
    public void NormalWritesNothing()
    {
        var message = Applied(MailPriority.Normal);

        Assert.Null(message.Headers["X-Priority"]);
        Assert.Null(message.Headers["Importance"]);
        Assert.Null(message.Headers["X-MSMail-Priority"]);
    }

    /// <summary>The pair exists so the two directions cannot drift; this is the assertion that says so.</summary>
    [Theory]
    [InlineData(MailPriority.High)]
    [InlineData(MailPriority.Low)]
    [InlineData(MailPriority.Normal)]
    public void WhatIsWrittenIsWhatIsReadBack(MailPriority priority) =>
        Assert.Equal(priority, MailPriorityReader.Parse(Applied(priority).Headers));

    /// <summary>Program.cs registers a bare JsonStringEnumConverter, which would otherwise emit "High".</summary>
    [Theory]
    [InlineData(MailPriority.Normal, "\"normal\"")]
    [InlineData(MailPriority.High, "\"high\"")]
    [InlineData(MailPriority.Low, "\"low\"")]
    public void SerialisesToTheLowerCaseWireValue(MailPriority priority, string expected)
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

        Assert.Equal(expected, JsonSerializer.Serialize(priority, options));
    }

    [Fact]
    public void DeserialisesFromTheLowerCaseWireValue()
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };

        Assert.Equal(MailPriority.High, JsonSerializer.Deserialize<MailPriority>("\"high\"", options));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run from `src/snoopy.microservice`:

```bash
dotnet test --filter "FullyQualifiedName~MailPriority"
```

Expected: FAIL — `MailPriority`, `MailPriorityReader` and `MailPriorityHeaders` do not exist (compile errors).

- [ ] **Step 4: Write the enum**

Create `src/snoopy.microservice/Models/Mail/MailPriority.cs`:

```csharp
using System.Text.Json.Serialization;

namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>
/// The importance a sender declared. Normal is the *absence* of any priority header, not a header
/// spelling "normal" — an ordinary message carries none.
/// The member names are pinned because Program.cs registers a bare JsonStringEnumConverter, which
/// would otherwise put "High" on a wire whose every other field is camelCase.
/// </summary>
public enum MailPriority
{
    [JsonStringEnumMemberName("normal")] Normal,
    [JsonStringEnumMemberName("high")] High,
    [JsonStringEnumMemberName("low")] Low
}
```

- [ ] **Step 5: Write the reader**

Create `src/snoopy.microservice/Services/MailPriorityReader.cs`:

```csharp
using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Reads the priority a sender declared. Four headers are consulted in the order clients actually
/// write them, and a header that is present but unreadable falls through to the next — but a
/// header that is readable ends the search, so an explicit "3" is an explicit Normal rather than
/// an invitation to consult Importance behind the sender's back.
/// </summary>
internal static class MailPriorityReader
{
    /// <summary>The header fields a summary FETCH has to ask for. Order matches the search below.</summary>
    public static readonly string[] Fields = ["X-Priority", "Importance", "X-MSMail-Priority", "Priority"];

    public static MailPriority Parse(HeaderList headers) =>
        FromXPriority(headers)
        ?? FromWord(headers, "Importance", "high", "normal", "low")
        ?? FromWord(headers, "X-MSMail-Priority", "high", "normal", "low")
        ?? FromWord(headers, "Priority", "urgent", "normal", "non-urgent")
        ?? MailPriority.Normal;

    // "1 (Highest)" — the digits are the value, the parenthesised comment is decoration.
    private static MailPriority? FromXPriority(HeaderList headers)
    {
        var header = headers.Topmost("X-Priority");
        if (header is null) return null;

        var text = header.Value.TrimStart();
        var end = 0;
        while (end < text.Length && char.IsAsciiDigit(text[end])) end++;
        if (end == 0 || !int.TryParse(text[..end], out var level)) return null;

        return level switch
        {
            1 or 2 => MailPriority.High,
            3 => MailPriority.Normal,
            4 or 5 => MailPriority.Low,
            _ => (MailPriority?)null
        };
    }

    private static MailPriority? FromWord(HeaderList headers, string field, string high, string normal, string low)
    {
        var value = headers.Topmost(field)?.Value.Trim();
        if (value is null) return null;

        if (string.Equals(value, high, StringComparison.OrdinalIgnoreCase)) return MailPriority.High;
        if (string.Equals(value, normal, StringComparison.OrdinalIgnoreCase)) return MailPriority.Normal;
        if (string.Equals(value, low, StringComparison.OrdinalIgnoreCase)) return MailPriority.Low;
        return null;
    }
}
```

- [ ] **Step 6: Write the header writer**

Create `src/snoopy.microservice/Services/MailPriorityHeaders.cs`:

```csharp
using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>
/// Declares a priority on an outgoing message. Three headers, because no single one is read by
/// everybody: Outlook and Exchange read Importance, Thunderbird and Roundcube read X-Priority,
/// older Microsoft clients read X-MSMail-Priority. Written raw rather than through MimeKit's
/// MimeMessage.Importance / .XPriority properties: those cannot express the "1 (Highest)" spelling
/// the wire actually carries, and there is no property at all for X-MSMail-Priority — three
/// headers written three different ways would be the drift this pair exists to prevent.
/// </summary>
internal static class MailPriorityHeaders
{
    public static void Apply(MimeMessage message, MailPriority priority)
    {
        if (priority == MailPriority.Normal) return;

        var high = priority == MailPriority.High;
        message.Headers.Add("X-Priority", high ? "1 (Highest)" : "5 (Lowest)");
        message.Headers.Add("Importance", high ? "high" : "low");
        message.Headers.Add("X-MSMail-Priority", high ? "High" : "Low");
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet test --filter "FullyQualifiedName~MailPriority"
```

Expected: PASS, all of them.

If `SerialisesToTheLowerCaseWireValue` fails, `JsonStringEnumMemberName` is not doing what this plan assumes. Do **not** change the global converter in `Program.cs` — that would move every other enum on the API. Instead drop the attributes and use the PascalCase values (`'Normal' | 'High' | 'Low'`) in the frontend union type in Task 4, and note the change in the commit message.

- [ ] **Step 8: Commit**

```bash
git status   # revert ApiDocumentation.xml if dotnet test rewrote it
git add src/snoopy.microservice/Models/Mail/MailPriority.cs \
        src/snoopy.microservice/Services/MailPriorityReader.cs \
        src/snoopy.microservice/Services/MailPriorityHeaders.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/MailPriorityReaderTests.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/MailPriorityHeadersTests.cs
git commit -m "Add the mail priority enum and its header pair"
```

---

### Task 2: The write path — send, draft, and the draft round trip

**Files:**
- Modify: `src/snoopy.microservice/Models/Mail/SendMessageRequest.cs`
- Modify: `src/snoopy.microservice/Models/Mail/OpenedDraft.cs`
- Modify: `src/snoopy.microservice/Services/OutgoingMessageFactory.cs` (in `BuildMessageAsync`, beside the existing `ApplyThreadingHeaders` call)
- Modify: `src/snoopy.microservice/Controllers/MailController.cs:1176-1184` (`ToOpenedDraft`)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs`

**Interfaces:**
- Consumes: `MailPriority`, `MailPriorityReader.Parse`, `MailPriorityHeaders.Apply` from Task 1.
- Produces:
  - `SendMessageRequest.Priority` (type `MailPriority`, defaults to `MailPriority.Normal`) — inherited by `SaveDraftRequest`, which already derives from it.
  - `OpenedDraft.Priority` as the **tenth positional parameter**, after `References`.

- [ ] **Step 1: Add the request field**

In `src/snoopy.microservice/Models/Mail/SendMessageRequest.cs`, after the `References` property:

```csharp
    /// <summary>Priority to declare. Normal writes no header at all — see MailPriorityHeaders.</summary>
    public MailPriority Priority { get; init; } = MailPriority.Normal;
```

`SaveDraftRequest` derives from this record, so drafts get the field with no change of their own.

- [ ] **Step 2: Call the writer from the factory**

In `src/snoopy.microservice/Services/OutgoingMessageFactory.cs`, inside `BuildMessageAsync`, immediately after the existing line:

```csharp
        ApplyThreadingHeaders(message, request);
```

add:

```csharp
        MailPriorityHeaders.Apply(message, request.Priority);
```

- [ ] **Step 3: Carry the priority back out of a saved draft**

In `src/snoopy.microservice/Models/Mail/OpenedDraft.cs`, add the parameter at the end of the record:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>Everything the composer needs to resume a draft: envelope, editable body, re-staged parts.</summary>
public sealed record OpenedDraft(
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> Bcc,
    string Subject,
    string? FromAddress,
    string HtmlBody,
    IReadOnlyList<StagedAttachmentInfo> Attachments,
    string? InReplyTo,
    IReadOnlyList<string> References,
    /// <summary>Read back off the saved message. Without it a saved High silently resumes as Normal.</summary>
    MailPriority Priority);
```

In `src/snoopy.microservice/Controllers/MailController.cs`, `ToOpenedDraft` — add the argument after `message.References`:

```csharp
    private static OpenedDraft ToOpenedDraft(MimeMessage message, PreparedQuote prepared) =>
        new(
            Addresses(message.To), Addresses(message.Cc), Addresses(message.Bcc),
            message.Subject ?? string.Empty,
            message.From?.Mailboxes?.FirstOrDefault()?.Address,
            prepared.QuotableHtml,
            prepared.Attachments,
            string.IsNullOrWhiteSpace(message.InReplyTo) ? null : message.InReplyTo,
            message.References?.ToList() ?? [],
            MailPriorityReader.Parse(message.Headers));
```

- [ ] **Step 4: Write the failing controller tests**

In `src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs`, add these three tests. Follow the file's own existing conventions for building an authenticated controller and its mocks — read a neighbouring `Send`/`SaveDraft`/`OpenDraft` test in that file and mirror its arrangement rather than inventing a new one.

```csharp
    /// <summary>The priority has to survive the hop from the request into the factory's argument.</summary>
    [Fact]
    public async Task Send_CarriesThePriorityIntoTheOutgoingMessage()
    {
        SendMessageRequest? seen = null;
        _outgoing.Setup(f => f.CreateAsync(
                It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<SendMessageRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback((User _, MailAccountConnection _, SendMessageRequest r, CancellationToken _) => seen = r)
            .ReturnsAsync(Result.Success(new MimeMessage()));

        await CreateController().Send(ValidSendRequest() with { Priority = MailPriority.High }, CancellationToken.None);

        Assert.Equal(MailPriority.High, seen!.Priority);
    }

    [Fact]
    public async Task SaveDraft_CarriesThePriorityIntoTheSavedMessage()
    {
        SendMessageRequest? seen = null;
        _outgoing.Setup(f => f.CreateAsync(
                It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<SendMessageRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback((User _, MailAccountConnection _, SendMessageRequest r, CancellationToken _) => seen = r)
            .ReturnsAsync(Result.Success(new MimeMessage()));

        await CreateController().SaveDraft(
            ValidDraftRequest() with { Priority = MailPriority.Low }, CancellationToken.None);

        Assert.Equal(MailPriority.Low, seen!.Priority);
    }

    /// <summary>Saved at High, reopened at High — otherwise the setting dies on the round trip.</summary>
    [Fact]
    public async Task OpenDraft_ReadsThePriorityBackOffTheSavedMessage()
    {
        var saved = new MimeMessage();
        MailPriorityHeaders.Apply(saved, MailPriority.High);
        StubOpenDraft(saved);

        var ok = Assert.IsType<OkObjectResult>(
            (await CreateController().OpenDraft(new OpenDraftRequest { Folder = "Drafts", Uid = 7 },
                CancellationToken.None)).Result);

        Assert.Equal(MailPriority.High, Assert.IsType<OpenedDraft>(Envelope(ok).Data).Priority);
    }
```

`_outgoing`, `CreateController()`, `ValidSendRequest()`, `ValidDraftRequest()`, `StubOpenDraft(...)` and `Envelope(...)` stand in for whatever this file already calls those things. If a helper does not exist under some name, build the arrangement inline exactly the way the neighbouring test for the same endpoint does. Do **not** add a new test-helper layer.

- [ ] **Step 5: Run the tests to verify they fail, then pass**

```bash
dotnet test --filter "FullyQualifiedName~MailControllerTests"
```

Expected on a first run before Steps 1–3 are in place: FAIL. With them in place: PASS. Because Steps 1–3 and the tests land together here, run it once and expect PASS; if it fails, the message names which of the three wirings is missing.

- [ ] **Step 6: Run the whole backend suite**

```bash
dotnet test
```

Expected: PASS. `OpenedDraft` gained a positional parameter, so any other construction of it in the suite is now a compile error — the compiler names each one; add `MailPriority.Normal` there.

- [ ] **Step 7: Commit**

```bash
git status   # revert ApiDocumentation.xml if it moved
git add src/snoopy.microservice/Models/Mail/SendMessageRequest.cs \
        src/snoopy.microservice/Models/Mail/OpenedDraft.cs \
        src/snoopy.microservice/Services/OutgoingMessageFactory.cs \
        src/snoopy.microservice/Controllers/MailController.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Controllers/MailControllerTests.cs
git commit -m "Write the priority headers on send and on draft save"
```

---

### Task 3: The read path — summary, detail, and the FETCH that pays for it

**Files:**
- Modify: `src/snoopy.microservice/Models/Mail/MailMessageSummary.cs`
- Modify: `src/snoopy.microservice/Models/Mail/MailMessageDetail.cs`
- Modify: `src/snoopy.microservice/Services/ImapSession.cs` — `SummaryItems` (~line 640), `FillSummary` (~line 648), the three `FetchAsync` call sites (~486, ~615, ~627), the detail mapping (~line 744)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapSessionTests.cs`

**Interfaces:**
- Consumes: `MailPriority`, `MailPriorityReader.Parse`, `MailPriorityReader.Fields` from Task 1.
- Produces: `MailMessageSummary.Priority` and `MailMessageDetail.Priority`, both `MailPriority`, both defaulting to `Normal`.

- [ ] **Step 1: Add the two properties**

In `src/snoopy.microservice/Models/Mail/MailMessageSummary.cs`, after `Preview`:

```csharp
    /// <summary>Priority the sender declared. Normal when the message carries no priority header.</summary>
    public MailPriority Priority { get; set; } = MailPriority.Normal;
```

In `src/snoopy.microservice/Models/Mail/MailMessageDetail.cs`, after `TlsReceived`:

```csharp
    /// <summary>Priority the sender declared. Normal when the message carries no priority header.</summary>
    public MailPriority Priority { get; set; } = MailPriority.Normal;
```

- [ ] **Step 2: Ask the FETCH for the four header fields**

In `src/snoopy.microservice/Services/ImapSession.cs`, beside the existing `SummaryItems` constant:

```csharp
    private const MessageSummaryItems SummaryItems =
        MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
        MessageSummaryItems.Size | MessageSummaryItems.BodyStructure | MessageSummaryItems.InternalDate |
        MessageSummaryItems.PreviewText;

    /// <summary>
    /// Priority is not in the envelope, so the summary FETCH has to name its headers. On the wire
    /// this is one BODY.PEEK[HEADER.FIELDS (...)] alongside the items above — one more item in the
    /// same round trip, not a second request, and the price of showing priority in the list at all.
    /// </summary>
    private static readonly string[] SummaryHeaders = MailPriorityReader.Fields;
```

Then switch the three `FetchAsync` calls that pass `SummaryItems` to the overload that also takes header names. All three are in this file:

```csharp
// ~line 486, filling search results
var items = await group.Key.FetchAsync(
    group.Select(m => m.Uid).ToList(), SummaryItems, SummaryHeaders, cancellationToken);

// ~line 615, the SORT-ordered page
var sortedItems = await folder.FetchAsync(wanted, SummaryItems, SummaryHeaders, cancellationToken);

// ~line 627, the sequence-window fallback
var items = await folder.FetchAsync(start, end, SummaryItems, SummaryHeaders, cancellationToken);
```

Leave every *other* `FetchAsync` in the file alone — the attachment-filter fetch (~line 531) and the search-hit fetch (~line 462) ask for different items and need no headers.

- [ ] **Step 3: Read it in the one shared mapping**

In `FillSummary`, after the `summary.Preview` line and before `return summary;`:

```csharp
        summary.Priority = item.Headers is { } headers ? MailPriorityReader.Parse(headers) : MailPriority.Normal;
```

In the detail mapping (~line 744, the object initialiser that already sets `SpamScore`), add after `TlsReceived`:

```csharp
                Priority = MailPriorityReader.Parse(message.Headers)
```

Note the trailing comma on the line above it.

- [ ] **Step 4: Write the failing tests**

In `src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapSessionTests.cs`, add tests exercising `ImapSession.FillSummary` directly — it is `internal static` and the test project already sees internals (the file tests other internals of this class; if `InternalsVisibleTo` is missing, the existing tests would not compile, so it is present).

```csharp
    /// <summary>The list row's marker comes from here, and search hits share the mapping.</summary>
    [Fact]
    public void FillSummary_ReadsThePriorityOffTheFetchedHeaders()
    {
        var headers = new HeaderList();
        headers.Add("X-Priority", "1 (Highest)");

        var summary = ImapSession.FillSummary(new MailMessageSummary(), FakeSummary(headers));

        Assert.Equal(MailPriority.High, summary.Priority);
    }

    [Fact]
    public void FillSummary_IsNormalWhenTheMessageCarriesNoPriorityHeader()
    {
        var summary = ImapSession.FillSummary(new MailMessageSummary(), FakeSummary(new HeaderList()));

        Assert.Equal(MailPriority.Normal, summary.Priority);
    }

    /// <summary>A server that answered without the header set must not throw or invent a priority.</summary>
    [Fact]
    public void FillSummary_IsNormalWhenTheServerReturnedNoHeadersAtAll()
    {
        var summary = ImapSession.FillSummary(new MailMessageSummary(), FakeSummary(headers: null));

        Assert.Equal(MailPriority.Normal, summary.Priority);
    }
```

`FakeSummary(HeaderList?)` builds an `IMessageSummary` test double carrying those headers. This file already fabricates `IMessageSummary` values for its other `FillSummary` assertions — reuse that helper and give it a headers parameter rather than writing a second one. If no such helper exists, add one local to this test class using the same mocking approach (`Moq`) the file already uses for MailKit interfaces.

- [ ] **Step 5: Run the tests**

```bash
dotnet test --filter "FullyQualifiedName~ImapSessionTests"
```

Expected: PASS.

- [ ] **Step 6: Run the whole backend suite**

```bash
dotnet test
```

Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git status   # revert ApiDocumentation.xml if it moved
git add src/snoopy.microservice/Models/Mail/MailMessageSummary.cs \
        src/snoopy.microservice/Models/Mail/MailMessageDetail.cs \
        src/snoopy.microservice/Services/ImapSession.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Services/ImapSessionTests.cs
git commit -m "Read the priority into the message summary and detail"
```

---

### Task 4: The composer sets a priority

**Files:**
- Modify: `src/frontend/src/modules/mail/api/mailTypes.ts`
- Modify: `src/frontend/src/modules/mail/compose/ComposeView.tsx`
- Modify: `src/frontend/src/styles/mail.css:1466-1482`
- Test: `src/frontend/src/modules/mail/compose/ComposeView.test.tsx`

**Interfaces:**
- Consumes: the wire values from Task 1 — `"normal" | "high" | "low"`.
- Produces:
  - `export type MailPriority = 'normal' | 'high' | 'low'` in `mailTypes.ts`
  - `priority` on `MailMessageSummary`, `MailMessageDetail` and `OpenedDraft` (all required, all `MailPriority`)
  - `priority` in the object `ComposeView.buildPayload()` returns

- [ ] **Step 1: Add the type and the three fields**

In `src/frontend/src/modules/mail/api/mailTypes.ts`, near the top with the other shared types:

```ts
/** What the sender declared. 'normal' is the absence of any priority header. */
export type MailPriority = 'normal' | 'high' | 'low'
```

Add `priority: MailPriority` to `MailMessageSummary` (after `preview`), to `MailMessageDetail` (after the header-detail fields), and to `OpenedDraft` (after `references`).

- [ ] **Step 2: Run typecheck to find every fixture that now lacks the field**

```bash
cd src/frontend && npm run typecheck
```

Expected: FAIL, naming each test fixture that builds a `MailMessageSummary`, `MailMessageDetail` or `OpenedDraft` without `priority`. Add `priority: 'normal'` to each one it names. Re-run until it passes. Do **not** make the field optional to dodge this — the API always sends it, and an optional field would let a real omission through.

- [ ] **Step 3: Write the failing composer tests**

In `src/frontend/src/modules/mail/compose/ComposeView.test.tsx`, following that file's existing render helper and mocks:

```tsx
it('reveals the priority row from the link beside Cc and Bcc', async () => {
  renderCompose()
  expect(screen.queryByRole('button', { name: 'Priority' })).not.toBeInTheDocument()

  fireEvent.click(screen.getByRole('button', { name: 'Priority', exact: true }))

  expect(screen.getByRole('button', { name: 'Priority' })).toHaveTextContent('Normal')
})

it('sends the chosen priority', async () => {
  renderCompose()
  fireEvent.click(screen.getByText('Priority'))
  fireEvent.click(screen.getByRole('button', { name: 'Priority' }))
  fireEvent.click(screen.getByRole('menuitem', { name: 'High' }))
  await fillRecipientAndSend()

  expect(api.sendMessage).toHaveBeenCalledWith(
    expect.objectContaining({ priority: 'high' }), expect.anything())
})

it('sends normal when the row was never opened', async () => {
  renderCompose()
  await fillRecipientAndSend()

  expect(api.sendMessage).toHaveBeenCalledWith(
    expect.objectContaining({ priority: 'normal' }), expect.anything())
})

/** Folding the row away would take a live setting off the screen while it still rides the mail. */
it('keeps the priority row open while the value is not normal', () => {
  renderCompose()
  fireEvent.click(screen.getByText('Priority'))
  fireEvent.click(screen.getByRole('button', { name: 'Priority' }))
  fireEvent.click(screen.getByRole('menuitem', { name: 'Low' }))

  expect(screen.queryByText('Priority', { selector: '.compose-link-btn' })).not.toBeInTheDocument()
})

/** Same rule as a From-only edit: leaving must not silently drop it. */
it('arms the leave guard on a priority-only change', () => {
  renderCompose()
  fireEvent.click(screen.getByText('Priority'))
  fireEvent.click(screen.getByRole('button', { name: 'Priority' }))
  fireEvent.click(screen.getByRole('menuitem', { name: 'High' }))
  fireEvent.click(screen.getByRole('button', { name: 'Close' }))

  expect(screen.getByText('Save draft')).toBeInTheDocument()
})
```

`renderCompose()` and `fillRecipientAndSend()` stand in for this file's existing helpers — read a neighbouring send test and mirror it. The last test's final assertion should match however this file already asserts that the leave prompt appeared; copy that assertion rather than inventing one.

- [ ] **Step 4: Run them to verify they fail**

```bash
npx vitest run src/modules/mail/compose/ComposeView.test.tsx
```

Expected: FAIL — no `Priority` link exists.

- [ ] **Step 5: Add the state, the link and the row**

In `src/frontend/src/modules/mail/compose/ComposeView.tsx`:

Import at the top, beside the existing compose imports:

```tsx
import DropdownMenu from '../../../components/DropdownMenu'
import ChevronDownIcon from '../../../icons/ChevronDownIcon'
import type { MailPriority } from '../api/mailTypes'
```

Above the component, beside the other module constants:

```tsx
const PRIORITIES: { value: MailPriority; label: string }[] = [
  { value: 'high', label: 'High' },
  { value: 'normal', label: 'Normal' },
  { value: 'low', label: 'Low' },
]
```

State, beside `showCc` / `showBcc`:

```tsx
const [priority, setPriority] = useState<MailPriority>(seed?.priority ?? 'normal')
const [showPriority, setShowPriority] = useState((seed?.priority ?? 'normal') !== 'normal')
```

The change handler, beside `changeFrom` / `changeTo`:

```tsx
const changePriority = useCallback((v: MailPriority) => { markDirty(); setPriority(v) }, [markDirty])
```

In `buildPayload`, add `priority` to the returned object:

```tsx
  const buildPayload = () => ({
    to, cc, bcc, subject, htmlBody: relativizeStagedUrls(editor?.getHTML() ?? ''),
    attachmentIds: [...inlineIds, ...attachments.ids],
    fromAddress: effectiveFrom ?? undefined,
    inReplyTo: seed?.inReplyTo ?? undefined,
    references: seed?.references && seed.references.length > 0 ? seed.references : undefined,
    priority,
```

(keep whatever else the object already carries after this).

In the markup, extend `.compose-cc-links`:

```tsx
          <span className="compose-cc-links">
            {!showCc && <button type="button" className="compose-link-btn" onClick={() => setShowCc(true)}>Cc</button>}
            {!showBcc && <button type="button" className="compose-link-btn" onClick={() => setShowBcc(true)}>Bcc</button>}
            {!showPriority && (
              <button type="button" className="compose-link-btn" onClick={() => setShowPriority(true)}>Priority</button>
            )}
          </span>
```

and add the row itself between the Bcc field and the Subject `.field-h`:

```tsx
        {/* Stays open while the value is not Normal: folding it would take a live setting off the
            screen while it kept riding on the message. Cc and Bcc are safe to fold — their tokens
            stay visible either way. */}
        {(showPriority || priority !== 'normal') && (
          <div className="compose-priority">
            <span className="compose-priority-label">Priority</span>
            <DropdownMenu
              ariaLabel="Priority"
              className="compose-priority-select"
              align="left"
              trigger={<>{PRIORITIES.find(p => p.value === priority)!.label} <ChevronDownIcon size={13} /></>}
              items={PRIORITIES.map(p => ({ label: p.label, onSelect: () => changePriority(p.value) }))}
            />
          </div>
        )}
```

Finally, fold the row away when the value returns to Normal — add beside the other effects:

```tsx
useEffect(() => { if (priority === 'normal') setShowPriority(false) }, [priority])
```

- [ ] **Step 6: Style the row on the From row's rules**

In `src/frontend/src/styles/mail.css`, extend the three existing selectors rather than duplicating their values, so the label column stays 66px wide by construction:

```css
/* From is plain text, aligned on the same column as the To/Subject boxes. */
.compose-from, .compose-priority { display: flex; align-items: center; gap: 10px; }
.compose-from-label, .compose-priority-label {
  width: 66px; flex-shrink: 0; font-size: 13px; font-weight: 500; color: var(--text-muted);
  text-transform: uppercase; letter-spacing: 0.04em;
}
.compose-from-value { font-size: 14px; color: var(--text); }
.compose-from-select, .compose-priority-select {
  background: none; border: none; padding: 2px 6px; cursor: pointer;
  display: inline-flex; align-items: center; gap: 4px;
  font: inherit; color: var(--text); border-radius: 6px;
}
.compose-from-select:hover, .compose-priority-select:hover { background: var(--pane-item-hover); }
.compose-from-select svg { transform: rotate(90deg); }
```

The rotation rule stays on `.compose-from-select` alone — that trigger uses a right chevron turned down, while this one uses `ChevronDownIcon`, which already points down.

- [ ] **Step 7: Run the tests, lint and typecheck**

```bash
npx vitest run src/modules/mail/compose/ComposeView.test.tsx
npm run lint && npm run typecheck
```

Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/frontend/src/modules/mail/api/mailTypes.ts \
        src/frontend/src/modules/mail/compose/ComposeView.tsx \
        src/frontend/src/modules/mail/compose/ComposeView.test.tsx \
        src/frontend/src/styles/mail.css
git commit -m "Let the composer set a message priority"
```

Add any other test file the typecheck step made you touch to the same commit.

---

### Task 5: A resumed draft keeps its priority

**Files:**
- Modify: `src/frontend/src/modules/mail/compose/composeSeed.ts`
- Test: `src/frontend/src/modules/mail/compose/composeSeed.test.ts`

**Interfaces:**
- Consumes: `OpenedDraft.priority` (Task 4), `ComposeSeed` (existing).
- Produces: `ComposeSeed.priority` of type `MailPriority` — already read by `ComposeView` in Task 4 as `seed?.priority ?? 'normal'`.

- [ ] **Step 1: Write the failing seed tests**

In `src/frontend/src/modules/mail/compose/composeSeed.test.ts` (create it if this repo has no such file — check first; if the seed builders are tested inside another file, add these there instead):

```ts
it('carries a saved draft priority into the seed', () => {
  const seed = buildDraftSeed(openedDraft({ priority: 'high' }), [], ref, 'primary')

  expect(seed.priority).toBe('high')
})

/** A reply to an urgent message is not itself urgent. */
it('opens a reply at normal whatever the quoted message declared', () => {
  const seed = buildReplySeed(detail({ priority: 'high' }), quote, [], [], 'reply', 'primary')

  expect(seed.priority).toBe('normal')
})
```

`openedDraft(...)`, `detail(...)`, `ref` and `quote` stand in for the fixture builders the surrounding tests already use; mirror a neighbouring test's arrangement, and match the real signature of `buildReplySeed` as it exists in `composeSeed.ts` rather than the sketch above.

- [ ] **Step 2: Run them to verify they fail**

```bash
npx vitest run src/modules/mail/compose/composeSeed.test.ts
```

Expected: FAIL — `priority` is not on `ComposeSeed`.

- [ ] **Step 3: Add the field and fill it**

In `src/frontend/src/modules/mail/compose/composeSeed.ts`:

Add to the `ComposeSeed` interface, after `references`:

```ts
  /** Resumed from a saved draft; every other action opens at 'normal'. */
  priority: MailPriority
```

Import the type from `../api/mailTypes` alongside the existing imports there.

In `buildDraftSeed`, add to the returned object:

```ts
    priority: opened.priority,
```

In **every other** seed builder in this file — the reply / reply-all / forward / edit-as-new path — add:

```ts
    priority: 'normal',
```

Edit-as-new included: it re-opens a *sent* message as a fresh one, and the new message's priority is the sender's fresh choice.

- [ ] **Step 4: Run the tests, lint and typecheck**

```bash
npx vitest run src/modules/mail/compose/
npm run lint && npm run typecheck
```

Expected: PASS. Typecheck names any `ComposeSeed` fixture in another test file that now lacks `priority`; add `priority: 'normal'` there.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/compose/
git commit -m "Restore a draft's priority when it is reopened"
```

---

### Task 6: The list row and the reader show it

**Files:**
- Create: `src/frontend/src/icons/PriorityHighIcon.tsx`
- Create: `src/frontend/src/icons/PriorityLowIcon.tsx`
- Modify: `src/frontend/src/icons/icons.test.tsx`
- Modify: `src/frontend/src/modules/mail/list/MessageList.tsx` (~line 328 for the a11y label, ~455 wide skin, ~476 narrow skin)
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx` (the `<h1 className="reader-subject">`)
- Modify: `src/frontend/src/styles/mail.css`
- Test: `src/frontend/src/modules/mail/list/MessageList.test.tsx`, `src/frontend/src/modules/mail/reader/MessageReader.test.tsx`

**Interfaces:**
- Consumes: `MailMessageSummary.priority`, `MailMessageDetail.priority` (Task 4).
- Produces: `PriorityHighIcon` and `PriorityLowIcon`, both `({ size = 12 }: { size?: number })`.

- [ ] **Step 1: Create the two icons**

`src/frontend/src/icons/PriorityHighIcon.tsx`:

```tsx
export default function PriorityHighIcon({ size = 12 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="m6 13 6-6 6 6" />
      <path d="m6 19 6-6 6 6" />
    </svg>
  )
}
```

`src/frontend/src/icons/PriorityLowIcon.tsx`:

```tsx
export default function PriorityLowIcon({ size = 12 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="m6 11 6 6 6-6" />
      <path d="m6 5 6 6 6-6" />
    </svg>
  )
}
```

A matched pair — one scale read up and read down. Two unrelated marks (a flag and an arrow) would read as two unrelated facts.

Register both in `src/frontend/src/icons/icons.test.tsx`: add the two imports and two rows to the `icons` array, `defaultSize: '12'`.

- [ ] **Step 2: Write the failing list tests**

In `src/frontend/src/modules/mail/list/MessageList.test.tsx`, using the file's existing render helper and message fixture builder:

```tsx
it('marks a high-priority row', async () => {
  renderList([message({ uid: 1, subject: 'Devis', priority: 'high' })])

  expect(await screen.findByTitle('High priority')).toBeInTheDocument()
})

it('marks a low-priority row', async () => {
  renderList([message({ uid: 1, subject: 'Newsletter', priority: 'low' })])

  expect(await screen.findByTitle('Low priority')).toBeInTheDocument()
})

/** Every row, nearly always — it must add nothing at all. */
it('marks nothing on a normal-priority row', async () => {
  renderList([message({ uid: 1, subject: 'Bonjour', priority: 'normal' })])
  await screen.findByText('Bonjour')

  expect(screen.queryByTitle('High priority')).not.toBeInTheDocument()
  expect(screen.queryByTitle('Low priority')).not.toBeInTheDocument()
})

/** The row is children-presentational, so anything it states visually has to be in its name. */
it('says the priority in the row name', async () => {
  renderList([message({ uid: 1, subject: 'Devis', fromName: 'Camille', priority: 'high' })])

  expect(await screen.findByRole('button', { name: /High priority/ })).toBeInTheDocument()
})
```

- [ ] **Step 3: Write the failing reader test**

In `src/frontend/src/modules/mail/reader/MessageReader.test.tsx`, with that file's existing helpers:

```tsx
it('shows a chip for a high-priority message', async () => {
  renderReader(detail({ subject: 'Devis', priority: 'high' }))

  expect(await screen.findByText('High priority')).toBeInTheDocument()
})

it('shows no chip at normal priority', async () => {
  renderReader(detail({ subject: 'Devis', priority: 'normal' }))
  await screen.findByText('Devis')

  expect(screen.queryByText(/priority/i)).not.toBeInTheDocument()
})
```

- [ ] **Step 4: Run both to verify they fail**

```bash
npx vitest run src/modules/mail/list/MessageList.test.tsx src/modules/mail/reader/MessageReader.test.tsx
```

Expected: FAIL — nothing renders a priority mark.

- [ ] **Step 5: Render the mark in both row skins**

In `src/frontend/src/modules/mail/list/MessageList.tsx`, import the two icons, then inside the row-rendering callback, beside the existing `const subject = …` line:

```tsx
            const priorityLabel = message.priority === 'high' ? 'High priority'
              : message.priority === 'low' ? 'Low priority' : null
            const priorityMark = priorityLabel && (
              <span className={`message-row-priority is-${message.priority}`} title={priorityLabel}>
                {message.priority === 'high' ? <PriorityHighIcon /> : <PriorityLowIcon />}
              </span>
            )
```

Add it to the accessible name — the existing `label` line becomes:

```tsx
            const label = `${message.seen ? '' : 'Unread. '}${drafts ? 'Draft. ' : ''}`
              + `${priorityLabel ? priorityLabel + '. ' : ''}${from}: ${subject}`
              + `${message.hasAttachments ? ', has attachments' : ''}, ${when}`
```

In the **wide** skin, put it first inside `.message-row-line`:

```tsx
                      <span className="message-row-line">
                        {priorityMark}
                        {subject}
```

In the **narrow** skin, put it first inside `.message-row-subject`:

```tsx
                      <div className="message-row-subject">{priorityMark}{subject}</div>
```

- [ ] **Step 6: Render the chip in the reader**

In `src/frontend/src/modules/mail/reader/MessageReader.tsx`, import `Tooltip` from `../../../components/Tooltip` if it is not already imported, and render the chip as the last child of the `<h1 className="reader-subject">`, after the subject text:

```tsx
            {message.priority !== 'normal' && (
              <Tooltip
                placement="bottom-left"
                label={message.priority === 'high'
                  ? 'The sender marked this message X-Priority: 1 (Highest)'
                  : 'The sender marked this message X-Priority: 5 (Lowest)'}
              >
                <span className={`reader-priority is-${message.priority}`}>
                  {message.priority === 'high' ? 'High priority' : 'Low priority'}
                </span>
              </Tooltip>
            )}
```

Match `Tooltip`'s real prop names by reading `src/frontend/src/components/Tooltip.tsx` first — the wrapper takes a trigger as children and its bubble text as a prop, but the prop's exact name is that file's to give.

- [ ] **Step 7: Style both**

In `src/frontend/src/styles/mail.css`, in the message-list section:

```css
/* Inline in the subject line, not a column of its own: a normal-priority row — nearly every row —
   must be exactly what it was before this existed. */
.message-row-priority { display: inline-flex; vertical-align: -2px; margin-right: 5px; }
.message-row-priority.is-high { color: var(--danger); }
.message-row-priority.is-low { color: var(--text-muted); }
```

and in the reader section:

```css
/* A glyph and a word, never a coloured band: X-Priority is the sender's claim, freely forged. */
.reader-priority {
  margin-left: 10px; padding: 1px 8px; border-radius: 999px;
  font-size: 12px; font-weight: 500; vertical-align: middle;
  border: 1px solid currentColor;
}
.reader-priority.is-high { color: var(--danger); }
.reader-priority.is-low { color: var(--text-muted); }
```

- [ ] **Step 8: Run the tests, lint and typecheck**

```bash
npx vitest run src/modules/mail/list/ src/modules/mail/reader/ src/icons/
npm run lint && npm run typecheck
```

Expected: PASS.

- [ ] **Step 9: Run the whole frontend suite**

```bash
npm test
```

Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add src/frontend/src/icons/ \
        src/frontend/src/modules/mail/list/ \
        src/frontend/src/modules/mail/reader/ \
        src/frontend/src/styles/mail.css
git commit -m "Show the declared priority in the list row and the reader"
```

---

### Task 7: Check it in a browser

jsdom sees no layout, so nothing above proves the glyph sits right. This task asserts nothing in code; it produces a written result.

**Files:** none modified unless a defect is found.

- [ ] **Step 1: Start the dev server**

```bash
cd src/frontend && npm run dev
```

- [ ] **Step 2: Check the four things a test cannot see**

Against a real mailbox, in **both** light and dark mode:

1. The composer's `Priority` link sits on the Cc/Bcc strip without wrapping it to a second line, and the revealed row's label lines up with `From`, `To` and `Subject` in the same 66px column.
2. The list glyph does not push the subject's ellipsis around — compare a high-priority row against a normal one at the same column width, in **both** row skins (reading pane right, then bottom).
3. The reader chip does not collide with the actions zone at a narrow reader width, and its tooltip opens down-left without being clipped by the column's `overflow: hidden`.
4. Both colours are legible on their ground in dark mode — `--danger` on `--surface` and `--text-muted` on `--surface`.

- [ ] **Step 3: Record the result**

Report what was checked and what was seen. If something is off, fix it and re-run the affected test file before committing; if nothing is, say so plainly and commit nothing.

---

## Self-Review

**Spec coverage**

| Spec section | Task |
|---|---|
| The model (`MailPriority`, Normal = no header) | 1 |
| Three headers on the way out | 1 (writer), 2 (call site) |
| The draft round trip (`OpenedDraft.Priority`) | 2 (backend), 5 (client) |
| A reply never inherits it | 5 |
| `MailPriorityReader`, precedence, explicit 3, first occurrence | 1 |
| Wiring at `FillSummary` + the detail mapping | 3 |
| The FETCH header cost | 3 |
| Composer link, row, no-fold rule, dirty | 4 |
| List row glyph, both skins, a11y name | 6 |
| Reader chip + tooltip naming the header | 6 |
| Restraint: glyph not band | 6 (CSS comment + no background) |
| Tests enumerated in the spec | 1, 2, 3, 4, 5, 6 |
| Browser check (jsdom sees no layout) | 7 |

No spec requirement is unassigned.

**Type consistency**

`MailPriority` is the type name on both sides. Backend members `Normal` / `High` / `Low` serialise to `'normal'` / `'high'` / `'low'`, which is exactly the frontend union — asserted in Task 1 Step 2 rather than assumed. `MailPriorityReader.Fields` is produced in Task 1 and consumed as `SummaryHeaders` in Task 3. `ComposeSeed.priority` is produced in Task 5 and consumed in Task 4 Step 5 as `seed?.priority ?? 'normal'` — the `??` is what lets Task 4 land before Task 5 without breaking.

**Known gap, stated rather than hidden:** the one-line `MailPriorityHeaders.Apply` call inside `OutgoingMessageFactory.BuildMessageAsync` has no test of its own. `OutgoingMessageFactory` has never had a unit test in this repo and standing one up needs six mocked dependencies for a single line. The writer itself, the reader, the round trip between them, and the request-to-factory hop are all covered; the uncovered link is that one call.
