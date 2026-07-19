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
                ("Drafts", "Drafts", FolderAttributes.None, false),
                ("Brouillons", "Brouillons", FolderAttributes.None, false)
            ]);

            Assert.Equal("drafts", roles["Drafts"]);
            Assert.False(roles.ContainsKey("Brouillons"));
        }

        [Fact]
        public void ResolveSpecialUses_LetsTheServerFlagBeatTheNameGuess()
        {
            var roles = ImapSession.ResolveSpecialUses(
            [
                ("Drafts", "Drafts", FolderAttributes.None, false),
                ("Brouillons", "Brouillons", FolderAttributes.Drafts, false)
            ]);

            Assert.Equal("drafts", roles["Brouillons"]);
            Assert.False(roles.ContainsKey("Drafts"));
        }

        [Fact]
        public void ResolveSpecialUses_KeepsDistinctRolesApart()
        {
            var roles = ImapSession.ResolveSpecialUses(
            [
                ("INBOX", "INBOX", FolderAttributes.None, true),
                ("Sent", "Sent", FolderAttributes.None, false),
                ("Archive", "Archive", FolderAttributes.None, false),
                ("Projects", "Projects", FolderAttributes.None, false)
            ]);

            Assert.Equal("inbox", roles["INBOX"]);
            Assert.Equal("sent", roles["Sent"]);
            Assert.Equal("archive", roles["Archive"]);
            Assert.False(roles.ContainsKey("Projects"));
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
