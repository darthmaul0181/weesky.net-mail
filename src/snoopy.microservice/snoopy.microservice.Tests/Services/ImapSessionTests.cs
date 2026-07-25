using MailKit;
using MimeKit;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ImapSessionTests
{
    /// <summary>A discovery candidate. Selectable unless a test says otherwise.</summary>
    private static (string Path, string Name, string? AttributeRole, bool Selectable) F(
        string path, string name, string? attributeRole = null, bool selectable = true)
        => (path, name, attributeRole, selectable);

    [Theory]
    [InlineData(FolderAttributes.Sent, "sent")]
    [InlineData(FolderAttributes.Drafts, "drafts")]
    [InlineData(FolderAttributes.Trash, "trash")]
    [InlineData(FolderAttributes.Junk, "junk")]
    [InlineData(FolderAttributes.Archive, "archive")]
    public void SpecialUseFromAttributes_MapsEachServerFlag(FolderAttributes attributes, string expected)
    {
        Assert.Equal(expected, ImapSession.SpecialUseFromAttributes(attributes, isInbox: false));
    }

    [Fact]
    public void SpecialUseFromAttributes_ReturnsInboxForTheInbox()
    {
        Assert.Equal("inbox", ImapSession.SpecialUseFromAttributes(FolderAttributes.None, isInbox: true));
    }

    [Fact]
    public void SpecialUseFromAttributes_ReturnsNullWithoutAFlag()
    {
        Assert.Null(ImapSession.SpecialUseFromAttributes(FolderAttributes.None, isInbox: false));
    }

    [Theory]
    [InlineData("Sent", "sent")]
    [InlineData("Sent Messages", "sent")]
    [InlineData("Drafts", "drafts")]
    [InlineData("Trash", "trash")]
    [InlineData("Deleted Messages", "trash")]
    [InlineData("Junk", "junk")]
    [InlineData("Spam", "junk")]
    [InlineData("Archive", "archive")]
    public void SpecialUseFromName_RecognisesTheWellKnownNames(string name, string expected)
    {
        Assert.Equal(expected, ImapSession.SpecialUseFromName(name));
    }

    [Fact]
    public void SpecialUseFromName_MatchesCaseInsensitively()
    {
        Assert.Equal("trash", ImapSession.SpecialUseFromName("TRASH"));
    }

    [Fact]
    public void SpecialUseFromName_ReturnsNullForAnOrdinaryFolder()
    {
        Assert.Null(ImapSession.SpecialUseFromName("Projects"));
    }

    [Theory]
    [InlineData("Brouillons", "drafts")]
    [InlineData("Courrier indésirable", "junk")]
    [InlineData("Éléments supprimés", "trash")]
    [InlineData("Corbeille", "trash")]
    [InlineData("Envoyés", "sent")]
    public void SpecialUseFromName_RecognisesLocalisedNames(string name, string expected)
    {
        Assert.Equal(expected, ImapSession.SpecialUseFromName(name));
    }

    // A mailbox provisioned by two clients holds both "Drafts" and "Brouillons". Naming
    // both as the drafts folder would sort two folders into the well-known block and leave
    // the client with no way to say which one a draft belongs in.
    [Fact]
    public void ResolveSpecialUses_GivesEachRoleToOneFolderOnly()
    {
        var roles = ImapSession.ResolveSpecialUses(
        [
            F("Drafts", "Drafts"),
            F("Brouillons", "Brouillons")
        ]);

        Assert.Equal("drafts", roles["Drafts"].Role);
        Assert.False(roles.ContainsKey("Brouillons"));
    }

    [Fact]
    public void ResolveSpecialUses_LetsTheServerFlagBeatTheNameGuess()
    {
        var roles = ImapSession.ResolveSpecialUses(
        [
            F("Drafts", "Drafts"),
            F("Brouillons", "Brouillons", "drafts")
        ]);

        Assert.Equal("drafts", roles["Brouillons"].Role);
        Assert.Equal(SpecialUseAssignment.FromFlag, roles["Brouillons"].Source);
        Assert.False(roles.ContainsKey("Drafts"));
    }

    [Fact]
    public void ResolveSpecialUses_KeepsDistinctRolesApart()
    {
        var roles = ImapSession.ResolveSpecialUses(
        [
            F("INBOX", "INBOX", "inbox"),
            F("Sent", "Sent"),
            F("Archive", "Archive"),
            F("Projects", "Projects")
        ]);

        Assert.Equal("inbox", roles["INBOX"].Role);
        Assert.Equal("sent", roles["Sent"].Role);
        Assert.Equal(SpecialUseAssignment.FromName, roles["Sent"].Source);
        Assert.Equal("archive", roles["Archive"].Role);
        Assert.False(roles.ContainsKey("Projects"));
    }

    // A folder flagged \Sent but named "Trash" used to claim both roles, and the
    // path→role inversion then crashed on the duplicate key. One folder, one role.
    [Fact]
    public void ResolveSpecialUses_NeverGivesOneFolderTwoRoles()
    {
        var roles = ImapSession.ResolveSpecialUses(
        [
            F("Weird", "Trash", "sent")
        ]);

        Assert.Equal("sent", roles["Weird"].Role);
        Assert.DoesNotContain(roles.Values, a => a.Role == "trash");
    }

    [Fact]
    public void ResolveSpecialUses_ASeededRoleIsNotClaimable()
    {
        var roles = ImapSession.ResolveSpecialUses(
            [F("Drafts", "Drafts", "drafts")],
            claimedRoles: ["drafts"]);

        Assert.Empty(roles);
    }

    // Spec § 4.1, second half: the folder is taken by an override, so its flag claims
    // nothing — and the name pass hands the freed role to the next candidate.
    [Fact]
    public void ResolveSpecialUses_ASeededFolderClaimsNothingAndTheRolePassesOn()
    {
        var roles = ImapSession.ResolveSpecialUses(
            [F("Drafts", "Drafts", "drafts"), F("Brouillons", "Brouillons")],
            claimedFolders: ["Drafts"]);

        Assert.False(roles.ContainsKey("Drafts"));
        Assert.Equal("drafts", roles["Brouillons"].Role);
        Assert.Equal(SpecialUseAssignment.FromName, roles["Brouillons"].Source);
    }

    // Two folders flagged \Sent: the loser must end up with no role at all. Marking only
    // the winner as taken let the name pass re-purpose the loser, so a guess contradicted
    // what the server had explicitly declared.
    [Fact]
    public void ResolveSpecialUses_AFlaggedFolderThatLosesItsRoleIsNotRenamedByAGuess()
    {
        var roles = ImapSession.ResolveSpecialUses(
        [
            F("Sent", "Sent", "sent"),
            F("Weird", "Trash", "sent")
        ]);

        Assert.Equal("sent", roles["Sent"].Role);
        Assert.False(roles.ContainsKey("Weird"));
    }

    // The call shape the override task will use: some roles already filled, and the
    // folders holding them already spoken for.
    [Fact]
    public void ResolveSpecialUses_HonoursBothSeededSetsAtOnce()
    {
        var roles = ImapSession.ResolveSpecialUses(
            [F("Corbeille", "Corbeille"), F("Deleted Items", "Deleted Items", "trash"), F("Sent", "Sent", "sent")],
            claimedRoles: ["trash"],
            claimedFolders: ["Corbeille"]);

        // trash is held by an override on Corbeille: neither the flagged folder nor the
        // name-matched one may claim it.
        Assert.False(roles.ContainsKey("Corbeille"));
        Assert.False(roles.ContainsKey("Deleted Items"));
        // A role no override touched still resolves normally.
        Assert.Equal("sent", roles["Sent"].Role);
    }

    // The ordinary shape: "Archive" exists only as a \NoSelect container for the year
    // folders under it. It must not win the name pass — the role would sit on a mailbox
    // that cannot hold a message, and the real archive folder could never take it back.
    [Fact]
    public void ResolveSpecialUses_ANoSelectContainerNeverWinsTheNamePass()
    {
        var roles = ImapSession.ResolveSpecialUses(
        [
            F("Archive", "Archive", selectable: false),
            F("Archive/2024", "2024"),
            F("Archive/2025", "2025"),
            F("Archives", "Archives")
        ]);

        Assert.False(roles.ContainsKey("Archive"));
        Assert.Equal("archive", roles["Archives"].Role);
        Assert.Equal(SpecialUseAssignment.FromName, roles["Archives"].Source);
    }

    // Same rule on the flag pass: a server may flag a container it also refuses to open.
    [Fact]
    public void ResolveSpecialUses_ANonSelectableFolderNeverWinsTheFlagPass()
    {
        var roles = ImapSession.ResolveSpecialUses(
        [
            F("Container", "Container", "sent", selectable: false),
            F("Sent", "Sent", "sent")
        ]);

        Assert.False(roles.ContainsKey("Container"));
        Assert.Equal("sent", roles["Sent"].Role);
        Assert.Equal(SpecialUseAssignment.FromFlag, roles["Sent"].Source);
    }

    // Skipped, not merely outranked: a non-selectable folder must not consume the role
    // and leave it unassignable when nothing else can claim it.
    [Fact]
    public void ResolveSpecialUses_ANonSelectableFolderDoesNotConsumeTheRole()
    {
        var roles = ImapSession.ResolveSpecialUses([F("Trash", "Trash", "trash", selectable: false)]);

        Assert.Empty(roles);
    }

    [Theory]
    [InlineData("INBOX", '/', null)]
    [InlineData("INBOX/Projects", '/', "INBOX")]
    [InlineData("INBOX/Projects/Alpha", '/', "INBOX/Projects")]
    [InlineData("INBOX.Projects", '.', "INBOX")]
    [InlineData("/leading", '/', null)]
    public void ParentPath_TrimsTheLastSegment(string fullName, char separator, string? expected)
    {
        Assert.Equal(expected, ImapSession.ParentPath(fullName, separator));
    }

    [Theory]
    [InlineData("Projects", '/', true)]
    [InlineData("Pro/jects", '/', false)]
    [InlineData("Pro.jects", '.', false)]
    [InlineData("Pro.jects", '/', true)]
    [InlineData("", '/', false)]
    [InlineData("   ", '/', false)]
    public void IsValidLeafName_RejectsSeparatorsAndBlanks(string name, char separator, bool expected)
    {
        Assert.Equal(expected, ImapSession.IsValidLeafName(name, separator));
    }

    [Theory]
    [InlineData("", "Projects", '/', "Projects")]
    [InlineData("INBOX", "Projects", '/', "INBOX/Projects")]
    [InlineData("INBOX", "Projects", '.', "INBOX.Projects")]
    public void CombinePath_JoinsWithTheServerSeparator(string parent, string name, char separator, string expected)
    {
        Assert.Equal(expected, ImapSession.CombinePath(parent, name, separator));
    }

    [Theory]
    // total, page, pageSize  =>  startIndex, endIndex
    [InlineData(100, 0, 50, 50, 99)]   // newest 50
    [InlineData(100, 1, 50, 0, 49)]    // next 50
    [InlineData(100, 2, 50, -1, -1)]   // past the end
    [InlineData(30, 0, 50, 0, 29)]     // fewer messages than a page
    [InlineData(0, 0, 50, -1, -1)]     // empty folder
    [InlineData(75, 1, 50, 0, 24)]     // partial last page
    [InlineData(1, 0, 50, 0, 0)]       // single message
    public void ComputePageWindow_MapsNewestFirstPagesToSequenceRanges(
        int total, int page, int pageSize, int expectedStart, int expectedEnd)
    {
        var (start, end) = ImapSession.ComputePageWindow(total, page, pageSize);

        Assert.Equal(expectedStart, start);
        Assert.Equal(expectedEnd, end);
    }

    // ── Sorted paging (IMAP SORT) ───────────────────────────────────────

    [Theory]
    [InlineData(0, 3, new[] { 9, 8, 7 })]
    [InlineData(1, 3, new[] { 6, 5, 4 })]
    [InlineData(3, 3, new[] { 0 })]          // partial last page
    [InlineData(4, 3, new int[0])]           // past the end
    [InlineData(-1, 3, new int[0])]
    [InlineData(0, 0, new int[0])]
    public void PageOf_SlicesTheOrderedList(int page, int pageSize, int[] expected)
    {
        int[] newestFirst = [9, 8, 7, 6, 5, 4, 3, 2, 1, 0];

        Assert.Equal(expected, ImapSession.PageOf(newestFirst, page, pageSize));
    }

    // The server may answer a UID FETCH in any order — in practice ascending UID, which is
    // precisely not the sort order asked for. Restoring it is what makes SORT worth using.
    [Fact]
    public void InOrderOf_RestoresTheRequestedOrder()
    {
        var fetched = new[] { (Uid: 4, Subject: "d"), (Uid: 9, Subject: "a"), (Uid: 7, Subject: "b") };

        var ordered = ImapSession.InOrderOf(fetched, [9, 7, 4], item => item.Uid);

        Assert.Equal(["a", "b", "d"], ordered.Select(item => item.Subject));
    }

    [Fact]
    public void InOrderOf_DropsAnItemTheOrderDoesNotMention()
    {
        var fetched = new[] { (Uid: 4, Subject: "d"), (Uid: 99, Subject: "stray") };

        var ordered = ImapSession.InOrderOf(fetched, [4], item => item.Uid);

        Assert.Equal(["d"], ordered.Select(item => item.Subject));
    }

    [Fact]
    public void InOrderOf_SkipsAnOrderEntryThatWasNotFetched()
    {
        var fetched = new[] { (Uid: 4, Subject: "d") };

        var ordered = ImapSession.InOrderOf(fetched, [9, 4], item => item.Uid);

        Assert.Equal(["d"], ordered.Select(item => item.Subject));
    }

    [Theory]
    [InlineData(100, -1, 50)]
    [InlineData(100, 0, 0)]
    [InlineData(100, 0, -10)]
    public void ComputePageWindow_RejectsNonsensicalInput(int total, int page, int pageSize)
    {
        Assert.Equal((-1, -1), ImapSession.ComputePageWindow(total, page, pageSize));
    }

    // ── Merge-path ordering / attachment filter (all-folders, no-SORT) ──

    private static ImapSession.SearchHit Hit(uint uid, DateTimeOffset sortKey, bool hasAttachment = false)
        => new(new UniqueId(uid), null!, sortKey, hasAttachment);

    [Fact]
    public void OrderHits_SortsNewestSentDateFirst()
    {
        var d = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var hits = new[] { Hit(1, d.AddDays(1)), Hit(2, d.AddDays(3)), Hit(3, d.AddDays(2)) };

        var ordered = ImapSession.OrderHits(hits, attachmentsOnly: false);

        Assert.Equal([2u, 3u, 1u], ordered.Select(h => h.Uid.Id));
    }

    [Fact]
    public void OrderHits_SortsByTheKeyRegardlessOfInputOrder()
    {
        var older = new DateTimeOffset(2020, 6, 15, 0, 0, 0, TimeSpan.Zero);
        var recent = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var hits = new[] { Hit(1, older), Hit(2, recent) };

        var ordered = ImapSession.OrderHits(hits, attachmentsOnly: false);

        Assert.Equal([2u, 1u], ordered.Select(h => h.Uid.Id));
    }

    [Fact]
    public void SortKeyOf_UsesTheSentDateWhenPresent()
    {
        var sent = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var arrival = new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(sent, ImapSession.SortKeyOf(sent, arrival));
    }

    // The robustness path #3 exists for: a malformed message with no Envelope.Date must fall
    // back to its arrival date rather than sort as MinValue or crash.
    [Fact]
    public void SortKeyOf_FallsBackToInternalDateWhenSentDateIsNull()
    {
        var arrival = new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(arrival, ImapSession.SortKeyOf(null, arrival));
    }

    [Fact]
    public void SortKeyOf_ReturnsMinValueWhenBothAreNull()
    {
        Assert.Equal(DateTimeOffset.MinValue, ImapSession.SortKeyOf(null, null));
    }

    [Fact]
    public void OrderHits_WhenAttachmentsOnlyDropsHitsWithout()
    {
        var d = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var hits = new[] { Hit(1, d.AddDays(1), hasAttachment: true), Hit(2, d.AddDays(2), hasAttachment: false) };

        var ordered = ImapSession.OrderHits(hits, attachmentsOnly: true);

        Assert.Equal([1u], ordered.Select(h => h.Uid.Id));
    }

    [Fact]
    public void OrderHits_WhenNotAttachmentsOnlyKeepsEveryHit()
    {
        var d = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var hits = new[] { Hit(1, d.AddDays(1), hasAttachment: true), Hit(2, d.AddDays(2), hasAttachment: false) };

        var ordered = ImapSession.OrderHits(hits, attachmentsOnly: false);

        Assert.Equal([2u, 1u], ordered.Select(h => h.Uid.Id));
    }

    // ── Address info conversion ──────────────────────────────────────

    [Fact]
    public void ToAddressInfos_PreservesDisplayNameWhenPresent()
    {
        var addresses = new InternetAddressList { new MailboxAddress("Bob Smith", "bob@example.com") };

        var result = ImapSession.ToAddressInfos(addresses);

        Assert.Single(result);
        Assert.Equal("Bob Smith", result[0].Name);
        Assert.Equal("bob@example.com", result[0].Address);
    }

    [Fact]
    public void ToAddressInfos_UsesEmptyStringWhenDisplayNameIsNull()
    {
        var addresses = new InternetAddressList { new MailboxAddress(null, "alice@example.com") };

        var result = ImapSession.ToAddressInfos(addresses);

        Assert.Single(result);
        Assert.Equal(string.Empty, result[0].Name);
        Assert.Equal("alice@example.com", result[0].Address);
    }

    [Fact]
    public void ToAddressInfos_ReturnsEmptyListForNullAddressList()
    {
        var result = ImapSession.ToAddressInfos(null);

        Assert.Empty(result);
    }

    [Fact]
    public void ToAddressInfos_ReturnsEmptyListForEmptyAddressList()
    {
        var addresses = new InternetAddressList();

        var result = ImapSession.ToAddressInfos(addresses);

        Assert.Empty(result);
    }

    [Fact]
    public void ToAddressInfos_PreservesOrderWithMultipleRecipients()
    {
        var addresses = new InternetAddressList
        {
            new MailboxAddress("Charlie", "charlie@example.com"),
            new MailboxAddress("Diana", "diana@example.com"),
            new MailboxAddress(null, "eve@example.com")
        };

        var result = ImapSession.ToAddressInfos(addresses);

        Assert.Equal(3, result.Count);
        Assert.Equal("Charlie", result[0].Name);
        Assert.Equal("charlie@example.com", result[0].Address);
        Assert.Equal("Diana", result[1].Name);
        Assert.Equal("diana@example.com", result[1].Address);
        Assert.Equal(string.Empty, result[2].Name);
        Assert.Equal("eve@example.com", result[2].Address);
    }

    [Fact]
    public void ApplyThreading_TranscribesTheHeaders()
    {
        var message = new MimeMessage();
        message.MessageId = "current@id";
        message.InReplyTo = "parent@id";
        message.References.Add("grandparent@id");
        message.References.Add("parent@id");
        message.ReplyTo.Add(new MailboxAddress("List", "list@x.example"));
        message.Bcc.Add(new MailboxAddress("Hidden", "bcc@x.example"));

        var detail = new MailMessageDetail();
        ImapSession.ApplyThreading(detail, message);

        Assert.Equal("current@id", detail.MessageId);
        Assert.Equal("parent@id", detail.InReplyTo);
        Assert.Equal(new[] { "grandparent@id", "parent@id" }, detail.References);
        Assert.Equal("list@x.example", Assert.Single(detail.ReplyTo).Address);
        Assert.Equal("bcc@x.example", Assert.Single(detail.Bcc).Address);
    }

    [Fact]
    public void ApplyThreading_DefaultsToNullAndEmptyWhenAbsent()
    {
        // MimeMessage's constructor generates a Message-Id — remove it to model a header-less original.
        var message = new MimeMessage();
        message.Headers.Remove(HeaderId.MessageId);

        var detail = new MailMessageDetail();
        ImapSession.ApplyThreading(detail, message);

        Assert.Null(detail.MessageId);
        Assert.Null(detail.InReplyTo);
        Assert.Empty(detail.References);
        Assert.Empty(detail.ReplyTo);
        Assert.Empty(detail.Bcc);
    }

    // ── Content-Id normalisation ─────────────────────────────────────
    //
    // GetMessageAsync itself is not unit-testable: it drives a real MailKit ImapClient
    // (ImapSession wraps a concrete ImapClient, not an interface) through GetFolderAsync/
    // FetchAsync/GetMessageAsync, none of which can be produced from a fixture without a
    // live or fake IMAP server. The transcription this task adds is the pure normalisation
    // step, exercised here directly instead — the closest seam the rest of this suite
    // already uses for ImapSession's other pure static helpers (ApplyThreading, ToAddressInfos, ...).

    [Fact]
    public void TrimAngleBrackets_StripsAngleBracketsFromAServerReportedContentId()
    {
        Assert.Equal("logo@mail", ImapSession.TrimAngleBrackets("<logo@mail>"));
    }

    [Fact]
    public void TrimAngleBrackets_LeavesABareContentIdUnchanged()
    {
        Assert.Equal("logo@mail", ImapSession.TrimAngleBrackets("logo@mail"));
    }

    [Fact]
    public void TrimAngleBrackets_ReturnsNullForNull()
    {
        Assert.Null(ImapSession.TrimAngleBrackets(null));
    }

    [Fact]
    public void TrimAngleBrackets_ReturnsNullForWhitespaceOrEmpty()
    {
        Assert.Null(ImapSession.TrimAngleBrackets(string.Empty));
        Assert.Null(ImapSession.TrimAngleBrackets("   "));
    }
}
