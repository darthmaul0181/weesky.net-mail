using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class MailHtmlSanitizerTests
{
    private readonly MailHtmlSanitizer _sut = new();

    [Theory]
    [InlineData("<script>alert(1)</script><p>hi</p>", "script")]
    [InlineData("<p onclick=\"alert(1)\">hi</p>", "onclick")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe><p>hi</p>", "iframe")]
    [InlineData("<object data=\"evil\"></object><p>hi</p>", "object")]
    [InlineData("<embed src=\"evil\"><p>hi</p>", "embed")]
    [InlineData("<form action=\"https://evil.example\"><input name=\"p\"></form><p>hi</p>", "form")]
    [InlineData("<a href=\"javascript:alert(1)\">click</a><p>hi</p>", "javascript:")]
    [InlineData("<p style=\"position:fixed;top:0\">hi</p>", "position")]
    [InlineData("<svg onload=\"alert(1)\"></svg><p>hi</p>", "onload")]
    [InlineData("<base href=\"https://evil.example/\"><p>hi</p>", "<base")]
    public void Sanitize_StripsHostileContent(string hostile, string forbidden)
    {
        var result = _sut.Sanitize(hostile).Html;

        Assert.DoesNotContain(forbidden, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_KeepsTheHarmlessPartOfAHostileDocument()
    {
        var result = _sut.Sanitize("<script>alert(1)</script><p>hi</p>").Html;

        Assert.Contains("hi", result);
    }

    // A bpost notification wrapped its entire 62 KB body in one <center>; removing the tag
    // with its subtree rendered the message empty. Unwrap disallowed formatting tags instead.
    [Theory]
    [InlineData("<center><p>the whole message</p></center>")]
    [InlineData("<font color=\"red\"><p>the whole message</p></font>")]
    [InlineData("<section><p>the whole message</p></section>")]
    public void Sanitize_KeepsTheContentOfADisallowedWrapper(string wrapped)
    {
        var result = _sut.Sanitize(wrapped).Html;

        Assert.Contains("the whole message", result);
    }

    // Unwrapping must not extend to containers whose text is not content.
    [Theory]
    [InlineData("<script>alert(1)</script><p>hi</p>", "alert(1)")]
    [InlineData("<style>body{color:red}</style><p>hi</p>", "color:red")]
    [InlineData("<title>a subject</title><p>hi</p>", "a subject")]
    public void Sanitize_DropsTheContentOfNonRenderedContainers(string hostile, string forbidden)
    {
        var result = _sut.Sanitize(hostile).Html;

        Assert.DoesNotContain(forbidden, result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hi", result);
    }

    // The bpost layout: a 600px column, a bordered card, a button. All of it rides on
    // dimension and shape properties the old text-oriented allowlist dropped.
    [Theory]
    [InlineData("width: 100%")]
    [InlineData("max-width: 400px")]
    [InlineData("height: 40px")]
    [InlineData("display: inline-block")]
    [InlineData("border-top-left-radius: 4px")]
    // The shorthand expands into longhands, which must therefore be in the allowlist too —
    // this is the bpost button's underline.
    [InlineData("text-decoration: none")]
    [InlineData("border-spacing: 0px")]
    [InlineData("text-transform: none")]
    [InlineData("word-break: break-all")]
    [InlineData("direction: ltr")]
    [InlineData("background: rgb(239, 38, 55)")]
    public void Sanitize_KeepsLayoutStylesRealMailUses(string declaration)
    {
        var result = _sut.Sanitize($"<div style=\"{declaration}\">x</div>").Html;

        Assert.Contains(declaration.Split(':')[0], result);
    }

    [Theory]
    [InlineData("<table cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr><td>x</td></tr></table>", "cellpadding")]
    [InlineData("<table><tr><td bgcolor=\"#ef2637\">x</td></tr></table>", "bgcolor")]
    public void Sanitize_KeepsTableLayoutAttributes(string html, string attribute)
    {
        Assert.Contains(attribute, _sut.Sanitize(html).Html);
    }

    // The Amazon navbar sets background-color, then overrides it with a gradient shorthand.
    // The shorthand expands to background-image; dropping it left white links on white.
    [Fact]
    public void Sanitize_KeepsAGradientBackground()
    {
        var result = _sut.Sanitize(
            "<table><tr><td style=\"background: linear-gradient(to right, #232F3E, #232F3E)\">x</td></tr></table>").Html;

        Assert.Contains("linear-gradient", result);
    }

    // A url() in CSS would fetch without consent, bypassing the image-blocking model.
    [Theory]
    [InlineData("<div style=\"background: url(http://evil.example/pix.gif)\">x</div>")]
    [InlineData("<div style=\"background-image: url(http://evil.example/pix.gif)\">x</div>")]
    [InlineData("<div style=\"border-image: url(http://evil.example/pix.gif)\">x</div>")]
    public void Sanitize_NeverKeepsACssUrl(string html)
    {
        var result = _sut.Sanitize(html).Html;

        Assert.DoesNotContain("evil.example", result);
        Assert.Contains("x", result);
    }

    [Theory]
    [InlineData("position: fixed")]
    [InlineData("z-index: 9999")]
    public void Sanitize_StillDropsPositionalStyles(string declaration)
    {
        var result = _sut.Sanitize($"<div style=\"{declaration}\">x</div>").Html;

        Assert.DoesNotContain(declaration.Split(':')[0], result);
    }

    // A url() anywhere in CSS fetches without consent, bypassing the image-blocking model.
    [Theory]
    [InlineData("<style>.x { background: url(http://evil.example/p.gif) }</style><p>hi</p>")]
    [InlineData("<style>@import url(http://evil.example/a.css); .x { color: red }</style><p>hi</p>")]
    [InlineData("<style>@font-face { font-family: f; src: url(http://evil.example/f.woff) }</style><p>hi</p>")]
    public void Sanitize_NeverKeepsAUrlInASheet(string html)
    {
        var result = _sut.Sanitize(html).Html;

        Assert.DoesNotContain("evil.example", result);
        Assert.Contains("hi", result);
    }

    // A CSS string value must not be able to close the style element early.
    [Fact]
    public void Sanitize_NeutralisesAStyleBreakoutAttempt()
    {
        var result = _sut.Sanitize(
            "<style>.x { font-family: \"</style><img src=x onerror=alert(1)>\" }</style><p>hi</p>").Html;

        Assert.DoesNotContain("onerror", result);
        Assert.Contains("hi", result);
    }

    [Fact]
    public void Sanitize_KeepsFormattingTheEditorMustAlsoProduce()
    {
        const string formatted =
            "<p><strong>bold</strong> <em>italic</em> <u>underline</u></p>" +
            "<ul><li>one</li></ul><blockquote>quoted</blockquote>" +
            "<p style=\"font-family:Arial;font-size:14px;color:#333333\">styled</p>" +
            "<table><tr><td>cell</td></tr></table>";

        var result = _sut.Sanitize(formatted).Html;

        Assert.Contains("<strong>", result);
        Assert.Contains("<em>", result);
        Assert.Contains("<u>", result);
        Assert.Contains("<li>", result);
        Assert.Contains("blockquote", result);
        Assert.Contains("font-family", result);
        Assert.Contains("font-size", result);
        Assert.Contains("color", result);
        Assert.Contains("<td>", result);
    }

    [Fact]
    public void Sanitize_MovesRemoteImagesToDataBlockedSrcAndCountsThem()
    {
        var result = _sut.Sanitize(
            "<img src=\"https://tracker.example/pixel.gif\"><img src=\"http://other.example/a.png\">");

        Assert.Equal(2, result.BlockedImageCount);
        Assert.Contains("data-blocked-src=\"https://tracker.example/pixel.gif\"", result.Html);

        // Attribute names are whitespace-delimited, so the leading space is what
        // distinguishes a real src from the tail of data-blocked-src.
        Assert.DoesNotContain(" src=", result.Html);
    }

    [Fact]
    public void Sanitize_KeepsInlineCidImages()
    {
        var result = _sut.Sanitize("<img src=\"cid:logo@example\">");

        Assert.Equal(0, result.BlockedImageCount);
        Assert.Contains("cid:logo@example", result.Html);
    }

    [Fact]
    public void Sanitize_ForcesLinksToOpenSafely()
    {
        var result = _sut.Sanitize("<a href=\"https://example.org\">link</a>").Html;

        Assert.Contains("noopener", result);
        Assert.Contains("noreferrer", result);
        Assert.Contains("_blank", result);
    }

    [Fact]
    public void Sanitize_ReturnsEmptyForNullOrEmptyInput()
    {
        Assert.Equal(string.Empty, _sut.Sanitize(null!).Html);
        Assert.Equal(string.Empty, _sut.Sanitize("").Html);
        Assert.Equal(0, _sut.Sanitize(null!).BlockedImageCount);
    }

    [Fact]
    public void Sanitize_HandlesTheMathMlAnnotationXmlMutationVector()
    {
        // The parser bug fixed in AngleSharp 1.5.0: a MathML annotation-xml element with
        // an HTML encoding is an HTML integration point, so its contents must be parsed
        // as HTML. Parsing it otherwise lets script survive sanitisation because the
        // browser will build a different tree than the sanitiser did.
        const string vector =
            "<math><annotation-xml encoding=\"text/html\"><script>alert(1)</script></annotation-xml></math>";

        var result = _sut.Sanitize(vector).Html;

        Assert.DoesNotContain("script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result, StringComparison.OrdinalIgnoreCase);
    }
}
