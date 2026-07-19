using MailKit;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services
{
    public class ImapSessionTests
    {
        [Theory]
        [InlineData(FolderAttributes.Sent, "Whatever", "sent")]
        [InlineData(FolderAttributes.Drafts, "Whatever", "drafts")]
        [InlineData(FolderAttributes.Trash, "Whatever", "trash")]
        [InlineData(FolderAttributes.Junk, "Whatever", "junk")]
        [InlineData(FolderAttributes.Archive, "Whatever", "archive")]
        public void ResolveSpecialUse_PrefersTheServerFlag(FolderAttributes attributes, string name, string expected)
        {
            Assert.Equal(expected, ImapSession.ResolveSpecialUse(attributes, name, isInbox: false));
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
        public void ResolveSpecialUse_FallsBackToTheNameWhenNoFlag(string name, string expected)
        {
            Assert.Equal(expected, ImapSession.ResolveSpecialUse(FolderAttributes.None, name, isInbox: false));
        }

        [Fact]
        public void ResolveSpecialUse_MatchesTheNameCaseInsensitively()
        {
            Assert.Equal("trash", ImapSession.ResolveSpecialUse(FolderAttributes.None, "TRASH", isInbox: false));
        }

        [Fact]
        public void ResolveSpecialUse_ReturnsInboxForTheInbox()
        {
            Assert.Equal("inbox", ImapSession.ResolveSpecialUse(FolderAttributes.None, "INBOX", isInbox: true));
        }

        [Fact]
        public void ResolveSpecialUse_ReturnsNullForAnOrdinaryFolder()
        {
            Assert.Null(ImapSession.ResolveSpecialUse(FolderAttributes.None, "Projects", isInbox: false));
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
                ("Drafts", "Drafts", null),
                ("Brouillons", "Brouillons", null)
            ]);

            Assert.Equal("drafts", roles["Drafts"].Role);
            Assert.False(roles.ContainsKey("Brouillons"));
        }

        [Fact]
        public void ResolveSpecialUses_LetsTheServerFlagBeatTheNameGuess()
        {
            var roles = ImapSession.ResolveSpecialUses(
            [
                ("Drafts", "Drafts", null),
                ("Brouillons", "Brouillons", "drafts")
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
                ("INBOX", "INBOX", "inbox"),
                ("Sent", "Sent", null),
                ("Archive", "Archive", null),
                ("Projects", "Projects", null)
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
                ("Weird", "Trash", "sent")
            ]);

            Assert.Equal("sent", roles["Weird"].Role);
            Assert.DoesNotContain(roles.Values, a => a.Role == "trash");
        }

        [Fact]
        public void ResolveSpecialUses_ASeededRoleIsNotClaimable()
        {
            var roles = ImapSession.ResolveSpecialUses(
                [("Drafts", "Drafts", "drafts")],
                claimedRoles: ["drafts"]);

            Assert.Empty(roles);
        }

        // Spec § 4.1, second half: the folder is taken by an override, so its flag claims
        // nothing — and the name pass hands the freed role to the next candidate.
        [Fact]
        public void ResolveSpecialUses_ASeededFolderClaimsNothingAndTheRolePassesOn()
        {
            var roles = ImapSession.ResolveSpecialUses(
                [("Drafts", "Drafts", "drafts"), ("Brouillons", "Brouillons", null)],
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
                ("Sent", "Sent", "sent"),
                ("Weird", "Trash", "sent")
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
                [("Corbeille", "Corbeille", null), ("Deleted Items", "Deleted Items", "trash"), ("Sent", "Sent", "sent")],
                claimedRoles: ["trash"],
                claimedFolders: ["Corbeille"]);

            // trash is held by an override on Corbeille: neither the flagged folder nor the
            // name-matched one may claim it.
            Assert.False(roles.ContainsKey("Corbeille"));
            Assert.False(roles.ContainsKey("Deleted Items"));
            // A role no override touched still resolves normally.
            Assert.Equal("sent", roles["Sent"].Role);
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

        [Theory]
        [InlineData(100, -1, 50)]
        [InlineData(100, 0, 0)]
        [InlineData(100, 0, -10)]
        public void ComputePageWindow_RejectsNonsensicalInput(int total, int page, int pageSize)
        {
            Assert.Equal((-1, -1), ImapSession.ComputePageWindow(total, page, pageSize));
        }
    }
}
