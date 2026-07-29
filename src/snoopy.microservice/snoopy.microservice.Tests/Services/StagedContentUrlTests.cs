using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class StagedContentUrlTests
{
    private static readonly Guid Id = Guid.NewGuid();

    // The composer appends "?account=..." because an <img> subresource cannot carry the
    // X-Account-Id header. Both spellings name the same staged file and must inline the same way.
    [Theory]
    [InlineData("")]
    [InlineData("?account=primary")]
    [InlineData("?account=6f1b6d1e-6c1e-4a2f-9f0d-1c2b3a4d5e6f")]
    public void TryRewrite_MatchesWithAndWithoutAQueryString(string query)
    {
        var html = $"""<p>Hi</p><img src="{StagedContentUrl.For(Id)}{query}"><p>after</p>""";

        Assert.True(StagedContentUrl.TryRewrite(html, Id, "cid:logo@mail", out var rewritten));
        Assert.Equal("""<p>Hi</p><img src="cid:logo@mail"><p>after</p>""", rewritten);
    }

    [Fact]
    public void TryRewrite_RewritesEveryOccurrence()
    {
        var url = StagedContentUrl.For(Id);
        var html = $"""<img src="{url}"><img src="{url}?account=x">""";

        Assert.True(StagedContentUrl.TryRewrite(html, Id, "cid:c", out var rewritten));
        Assert.Equal("""<img src="cid:c"><img src="cid:c">""", rewritten);
    }

    [Fact]
    public void TryRewrite_ReportsABodyThatNoLongerReferencesTheFile()
    {
        const string html = "<p>no image left</p>";

        Assert.False(StagedContentUrl.TryRewrite(html, Id, "cid:c", out var rewritten));
        Assert.Same(html, rewritten);
    }

    [Fact]
    public void TryRewrite_LeavesAnotherFilesUrlAlone()
    {
        var html = $"""<img src="{StagedContentUrl.For(Guid.NewGuid())}?account=x">""";

        Assert.False(StagedContentUrl.TryRewrite(html, Id, "cid:c", out _));
    }

    // The tolerance is a query string, not a prefix match: the URL has to end where it ends.
    [Theory]
    [InlineData("s")]
    [InlineData("/thumbnail")]
    [InlineData("-backup")]
    public void TryRewrite_DoesNotMatchALongerPath(string suffix)
    {
        var html = $"""<img src="{StagedContentUrl.For(Id)}{suffix}">""";

        Assert.False(StagedContentUrl.TryRewrite(html, Id, "cid:c", out _));
    }

    [Fact]
    public void TryRewrite_StopsTheQueryAtTheAttributeDelimiter()
    {
        var html = $"""<img src='{StagedContentUrl.For(Id)}?account=x' alt='keep'><b>tail</b>""";

        Assert.True(StagedContentUrl.TryRewrite(html, Id, "cid:c", out var rewritten));
        Assert.Equal("""<img src='cid:c' alt='keep'><b>tail</b>""", rewritten);
    }

    // A Content-ID may legally carry '$', which a replacement *string* would read as a group
    // reference and silently mangle.
    [Fact]
    public void TryRewrite_KeepsADollarSignInTheReplacement()
    {
        var html = $"""<img src="{StagedContentUrl.For(Id)}">""";

        Assert.True(StagedContentUrl.TryRewrite(html, Id, "cid:a$1b@x", out var rewritten));
        Assert.Equal("""<img src="cid:a$1b@x">""", rewritten);
    }
}
