using weesky.Snoopy.Microservice.Models.Mail;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Models.Mail
{
    public class MailFolderNodeExtensionsTests
    {
        private static MailFolderNode Node(string path, params MailFolderNode[] children) =>
            new() { Path = path, Name = path, Children = children.ToList() };

        [Fact]
        public void FindByPath_ReachesANestedFolder()
        {
            var tree = new[] { Node("Mail", Node("Mail/Deep", Node("Mail/Deep/Trash"))) };

            Assert.Equal("Mail/Deep/Trash", tree.FindByPath("Mail/Deep/Trash")?.Path);
        }

        [Fact]
        public void FindByPath_ReturnsNullWhenAbsent()
        {
            Assert.Null(new[] { Node("Mail") }.FindByPath("Other"));
        }

        [Fact]
        public void Descendants_ReturnsEveryFolderBelowButNotTheNodeItself()
        {
            var node = Node("Mail", Node("Mail/A", Node("Mail/A/B")), Node("Mail/C"));

            Assert.Equal(["Mail/A", "Mail/A/B", "Mail/C"], node.Descendants().Select(n => n.Path));
        }

        [Fact]
        public void Descendants_IsEmptyForALeaf()
        {
            Assert.Empty(Node("Mail").Descendants());
        }
    }
}
