using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ImapSessionTests
{
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
}
