using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class FolderRoleResolverTests
{
    private static MailFolderNode Node(string path, string? attributeRole = null,
        uint uidValidity = 1, string? mailboxId = null, bool selectable = true, string? name = null) =>
        new()
        {
            Path = path,
            Name = name ?? path,
            AttributeRole = attributeRole,
            UidValidity = uidValidity,
            MailboxId = mailboxId,
            Selectable = selectable,
        };

    private static readonly Guid Alice = Guid.NewGuid();

    private static FolderRoleOverride Override(string role, string path,
        ulong uidValidity = 1, string? mailboxId = null) =>
        new() { UserId = Alice, Role = role, FolderPath = path, UidValidity = uidValidity, MailboxId = mailboxId };

    private static FolderRoleEntry Entry(FolderRoleResolution resolution, string role) =>
        resolution.Roles.Single(e => e.Role == role);

    [Fact]
    public void WithoutOverrides_MatchesDiscoveryUnchanged()
    {
        var tree = new List<MailFolderNode>
        {
            Node("INBOX", attributeRole: "inbox"),
            Node("Deleted Items", attributeRole: "trash"),
            Node("Archive"),                                  // name fallback only
            Node("Projects"),
        };

        var resolution = FolderRoleResolver.Resolve(tree, []);

        Assert.Equal("inbox", resolution.RoleByPath["INBOX"]);
        Assert.Equal("trash", resolution.RoleByPath["Deleted Items"]);
        Assert.Equal("archive", resolution.RoleByPath["Archive"]);
        Assert.False(resolution.RoleByPath.ContainsKey("Projects"));
        Assert.Equal("specialUse", Entry(resolution, "trash").Provenance);
        Assert.Equal("name", Entry(resolution, "archive").Provenance);
    }

    [Fact]
    public void AnOverrideBeatsAServerFlag()
    {
        var tree = new List<MailFolderNode> { Node("Corbeille"), Node("Deleted Items", attributeRole: "trash") };

        var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Corbeille")]);

        var trash = Entry(resolution, "trash");
        Assert.Equal("Corbeille", trash.FolderPath);
        Assert.Equal("override", trash.Provenance);
        Assert.Null(trash.StaleOverride);
        // The flagged folder lost the role and gets nothing: it shows under its own name.
        Assert.False(resolution.RoleByPath.ContainsKey("Deleted Items"));
    }

    // Spec § 4.1, the case a roles-only implementation gets wrong: trash overridden onto
    // the flagged Drafts folder. Drafts must not also claim "drafts" — and the freed role
    // goes to the name-matched candidate instead.
    [Fact]
    public void AFolderTakenByAnOverrideClaimsNothingAtDiscovery()
    {
        var tree = new List<MailFolderNode> { Node("Drafts", attributeRole: "drafts"), Node("Brouillons") };

        var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Drafts")]);

        Assert.Equal("trash", resolution.RoleByPath["Drafts"]);
        Assert.Equal("drafts", resolution.RoleByPath["Brouillons"]);
        Assert.Equal("Brouillons", Entry(resolution, "drafts").FolderPath);
        Assert.Equal("name", Entry(resolution, "drafts").Provenance);
    }

    [Fact]
    public void ARoleWithNoSourceStaysNull()
    {
        var resolution = FolderRoleResolver.Resolve([Node("Projects")], []);

        var junk = Entry(resolution, "junk");
        Assert.Null(junk.FolderPath);
        Assert.Null(junk.Provenance);
        Assert.Null(junk.StaleOverride);
    }

    // Stale (path gone), and discovery still fructifies: both facts coexist (§ 5.3).
    [Fact]
    public void AStaleOverrideIsSignalledWhileDiscoveryFillsTheRole()
    {
        var tree = new List<MailFolderNode> { Node("Deleted Items", attributeRole: "trash") };

        var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Gone")]);

        var trash = Entry(resolution, "trash");
        Assert.Equal("Gone", trash.StaleOverride!.FolderPath);
        Assert.Equal("Deleted Items", trash.FolderPath);
        Assert.Equal("specialUse", trash.Provenance);
    }

    // Path reuse is the failure mode that lies rather than degrades: same path, different
    // folder, caught by UIDVALIDITY.
    [Fact]
    public void AReusedPathIsStale()
    {
        var tree = new List<MailFolderNode> { Node("Trash", uidValidity: 99) };

        var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Trash", uidValidity: 10)]);

        Assert.NotNull(Entry(resolution, "trash").StaleOverride);
        Assert.False(resolution.RoleByPath.ContainsKey("Trash") && resolution.RoleByPath["Trash"] == "trash"
                     && Entry(resolution, "trash").Provenance == "override");
    }

    // MAILBOXID beats the path: the folder was renamed by another client, the id still
    // finds it.
    [Fact]
    public void AMailboxIdMatchSurvivesARename()
    {
        var tree = new List<MailFolderNode> { Node("Renamed", mailboxId: "M1", uidValidity: 50) };

        var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Old", uidValidity: 10, mailboxId: "M1")]);

        var trash = Entry(resolution, "trash");
        Assert.Equal("Renamed", trash.FolderPath);
        Assert.Equal("override", trash.Provenance);
        Assert.Null(trash.StaleOverride);
    }

    // Stored id but a server that no longer offers OBJECTID: fall back to path + guard.
    [Fact]
    public void AStoredMailboxIdWithoutServerSupportFallsBackToThePath()
    {
        var tree = new List<MailFolderNode> { Node("Trash", uidValidity: 10) };

        var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Trash", uidValidity: 10, mailboxId: "M1")]);

        Assert.Equal("override", Entry(resolution, "trash").Provenance);
    }

    [Fact]
    public void ANonSelectableFolderIsStale()
    {
        var tree = new List<MailFolderNode> { Node("Container", selectable: false) };

        var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Container")]);

        Assert.NotNull(Entry(resolution, "trash").StaleOverride);
    }

    // The container shape: "Archive" is \NoSelect because only "Archive/2024" and
    // "Archive/2025" hold messages. Discovery must leave the role with the folder that can
    // actually take it — stamping the container blocked the real one for good, and Settings
    // then reported a holder it excluded from the picker.
    [Fact]
    public void ANoSelectContainerDoesNotTakeARoleFromARealFolder()
    {
        var container = Node("Archive", selectable: false);
        container.Children.Add(Node("Archive/2024", name: "2024"));
        var real = Node("Archives", name: "Archives");

        var resolution = FolderRoleResolver.Resolve([container, real], []);

        Assert.False(resolution.RoleByPath.ContainsKey("Archive"));
        Assert.Equal("archive", resolution.RoleByPath["Archives"]);
        Assert.Equal("Archives", Entry(resolution, "archive").FolderPath);
    }

    // Same rule for a flagged container, and the role must not simply vanish with it.
    [Fact]
    public void ANonSelectableFlaggedFolderIsSkippedByDiscovery()
    {
        var tree = new List<MailFolderNode>
        {
            Node("Container", attributeRole: "trash", selectable: false),
            Node("Corbeille", name: "Corbeille"),
        };

        var resolution = FolderRoleResolver.Resolve(tree, []);

        Assert.False(resolution.RoleByPath.ContainsKey("Container"));
        Assert.Equal("trash", resolution.RoleByPath["Corbeille"]);
    }

    // A server we will never configure may report the same path twice, or hand two
    // mailboxes one MAILBOXID. Indexing with ToDictionary threw ArgumentException straight
    // past Result<T>, so GET /Folders answered 500 and the whole mailbox went unreadable
    // instead of one role degrading.
    [Fact]
    public void DuplicatePathsAreToleratedRatherThanThrowing()
    {
        var tree = new List<MailFolderNode> { Node("Trash"), Node("Trash") };

        var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Trash")]);

        Assert.Equal("override", Entry(resolution, "trash").Provenance);
    }

    [Fact]
    public void DuplicateMailboxIdsAreToleratedRatherThanThrowing()
    {
        var tree = new List<MailFolderNode>
        {
            Node("First", mailboxId: "M1"),
            Node("Second", mailboxId: "M1"),
        };

        var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "First", mailboxId: "M1")]);

        // First wins — deterministic, and the mailbox stays readable.
        Assert.Equal("First", Entry(resolution, "trash").FolderPath);
    }

    // One flag for three causes made the client state something false in two of them.
    [Fact]
    public void AVanishedOverrideReportsTheMissingReason()
    {
        var resolution = FolderRoleResolver.Resolve([Node("Other")], [Override("trash", "Gone")]);

        Assert.Equal(StaleOverrideReasons.Missing, Entry(resolution, "trash").StaleOverride!.Reason);
    }

    [Fact]
    public void AReusedPathReportsTheMissingReason()
    {
        var tree = new List<MailFolderNode> { Node("Trash", uidValidity: 99) };

        var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Trash", uidValidity: 10)]);

        Assert.Equal(StaleOverrideReasons.Missing, Entry(resolution, "trash").StaleOverride!.Reason);
    }

    [Fact]
    public void ANonSelectableTargetReportsTheNotSelectableReason()
    {
        var tree = new List<MailFolderNode> { Node("Container", selectable: false) };

        var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "Container")]);

        Assert.Equal(StaleOverrideReasons.NotSelectable, Entry(resolution, "trash").StaleOverride!.Reason);
    }

    [Fact]
    public void ATargetClaimedByTheInboxReportsTheFolderTakenReason()
    {
        var tree = new List<MailFolderNode> { Node("INBOX", attributeRole: "inbox") };

        var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "INBOX")]);

        Assert.Equal(StaleOverrideReasons.FolderTaken, Entry(resolution, "trash").StaleOverride!.Reason);
    }

    [Fact]
    public void ATargetClaimedByAHigherPriorityOverrideReportsTheFolderTakenReason()
    {
        var resolution = FolderRoleResolver.Resolve(
            [Node("X")], [Override("trash", "X"), Override("junk", "X")]);

        Assert.Equal(StaleOverrideReasons.FolderTaken, Entry(resolution, "junk").StaleOverride!.Reason);
    }

    // INBOX is fixed by the protocol: an override pointing at it is invalid, and INBOX
    // keeps its role whatever the stored rows say.
    [Fact]
    public void AnOverrideCannotClaimTheInbox()
    {
        var tree = new List<MailFolderNode> { Node("INBOX", attributeRole: "inbox") };

        var resolution = FolderRoleResolver.Resolve(tree, [Override("trash", "INBOX")]);

        Assert.Equal("inbox", resolution.RoleByPath["INBOX"]);
        Assert.NotNull(Entry(resolution, "trash").StaleOverride);
    }

    // Two rows pointing at the same folder (belt-and-braces: the PUT rejects this). The
    // first role in FolderRoles.All order wins; the second is treated as stale.
    [Fact]
    public void TwoOverridesOnTheSameFolderResolveDeterministically()
    {
        var tree = new List<MailFolderNode> { Node("X") };

        var resolution = FolderRoleResolver.Resolve(
            tree, [Override("trash", "X"), Override("junk", "X")]);

        Assert.Equal("override", Entry(resolution, "trash").Provenance);   // trash < junk in All order
        Assert.NotNull(Entry(resolution, "junk").StaleOverride);
    }

    [Fact]
    public void ResolvesAcrossNestedFolders()
    {
        var parent = Node("Projects");
        parent.Children.Add(Node("Projects/Archive", name: "Archive"));

        var resolution = FolderRoleResolver.Resolve([parent], []);

        Assert.Equal("archive", resolution.RoleByPath["Projects/Archive"]);
    }
}
