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
        [InlineData("INBOX", '/', null)]
        [InlineData("INBOX/Projects", '/', "INBOX")]
        [InlineData("INBOX/Projects/Alpha", '/', "INBOX/Projects")]
        [InlineData("INBOX.Projects", '.', "INBOX")]
        [InlineData("/leading", '/', null)]
        public void ParentPath_TrimsTheLastSegment(string fullName, char separator, string? expected)
        {
            Assert.Equal(expected, ImapSession.ParentPath(fullName, separator));
        }
    }
}
