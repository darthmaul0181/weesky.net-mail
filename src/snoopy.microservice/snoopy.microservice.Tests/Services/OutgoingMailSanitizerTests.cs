using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class OutgoingMailSanitizerTests
{
    private readonly OutgoingMailSanitizer _sanitizer = new();

    [Fact]
    public void Prepare_StripsScriptsAndHandlers()
    {
        var body = _sanitizer.Prepare("<div onclick=\"x()\">hi<script>evil()</script></div>");

        Assert.DoesNotContain("script", body.Html);
        Assert.DoesNotContain("onclick", body.Html);
        Assert.Contains("hi", body.Html);
    }

    [Fact]
    public void Prepare_KeepsTheStylesTheToolbarProduces()
    {
        var body = _sanitizer.Prepare(
            "<div style=\"color: #e2674a; background-color: #ffffff; font-family: Georgia; text-align: center\">x</div>");

        Assert.Contains("color", body.Html);
        Assert.Contains("Georgia", body.Html);
        Assert.Contains("text-align", body.Html);
    }

    [Fact]
    public void Prepare_KeepsAPastedTable()
    {
        var body = _sanitizer.Prepare("<table><tr><td>cell</td></tr></table>");

        Assert.Contains("<table", body.Html);
        Assert.Contains("cell", body.Html);
    }

    [Fact]
    public void Prepare_KeepsRemoteImagesAndDropsDataUriImages()
    {
        var body = _sanitizer.Prepare(
            "<img src=\"https://example.org/a.png\"><img src=\"data:image/png;base64,AAAA\">");

        Assert.Contains("https://example.org/a.png", body.Html);
        Assert.DoesNotContain("data:", body.Html);
    }

    [Fact]
    public void Prepare_RefusesJavascriptLinks()
    {
        var body = _sanitizer.Prepare("<a href=\"javascript:evil()\">x</a>");

        Assert.DoesNotContain("javascript", body.Html);
    }

    [Fact]
    public void Prepare_DerivesAPlainTextAlternative()
    {
        var body = _sanitizer.Prepare("<div>Hello</div><div>World<br>again</div><ul><li>one</li><li>two</li></ul>");

        Assert.Equal("Hello\nWorld\nagain\none\ntwo", body.Text.Trim());
    }

    [Theory]
    [InlineData("a.png")]
    [InlineData("httpfoo.png")]
    [InlineData("//example.org/a.png")]
    public void Prepare_DropsImagesWithoutARemoteScheme(string src)
    {
        var body = _sanitizer.Prepare($"<img src=\"{src}\">");

        Assert.DoesNotContain("<img", body.Html);
    }

    [Fact]
    public void Prepare_SurvivesADocumentTooDeepForARecursiveWalk()
    {
        var html = string.Concat(Enumerable.Repeat("<div>", 10_000)) + "deep";

        var body = _sanitizer.Prepare(html);

        Assert.Equal("deep", body.Text);
    }

    [Fact]
    public void Prepare_KeepsGanssAttributeEscapingThroughReserialization()
    {
        var body = _sanitizer.Prepare(
            "<select><option title=\"</select><img src=x onerror=alert(1)>\">");

        Assert.Contains("&lt;/select&gt;&lt;img src=x onerror=alert(1)&gt;", body.Html);
        Assert.DoesNotContain("<img src=x onerror=alert(1)>", body.Html);
    }

    [Fact]
    public void Prepare_KeepsMailtoLinks()
    {
        var body = _sanitizer.Prepare("<a href=\"mailto:someone@example.org\">x</a>");

        Assert.Contains("mailto:someone@example.org", body.Html);
    }

    [Fact]
    public void Prepare_KeepsACidImageWithItsSrcIntact()
    {
        var body = _sanitizer.Prepare("<p>Hi</p><img src=\"cid:logo@mail\">");

        Assert.Contains("cid:logo@mail", body.Html);
        Assert.Contains("<img", body.Html);
    }

    // The composer resizes an inline image with Squire's own handles, which write width/height as
    // inline style. Culling either here would silently send every resized image at full size.
    [Fact]
    public void Prepare_KeepsTheInlineSizeStyleAResizeWrites()
    {
        var body = _sanitizer.Prepare(
            "<img src=\"cid:logo@mail\" style=\"max-width: 100%; width: 320px; height: auto\">");

        Assert.Contains("width: 320px", body.Html);
        Assert.Contains("max-width: 100%", body.Html);
        Assert.Contains("height: auto", body.Html);
    }

    [Fact]
    public void Prepare_StillRemovesNonRemoteNonCidImages()
    {
        var body = _sanitizer.Prepare(
            "<img src=\"file:///etc/passwd\"><img src=\"/relative.png\"><img src=\"data:image/png;base64,AA==\">");

        Assert.DoesNotContain("<img", body.Html);
    }
}
