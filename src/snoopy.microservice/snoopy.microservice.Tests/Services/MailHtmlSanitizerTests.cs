using System.Text.RegularExpressions;
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

    // Unwrapping <center> kept its content but lost the centring itself, leaving mail headers
    // hugging the left edge. It and <font> are harmless presentation tags real mail still uses.
    [Fact]
    public void Sanitize_KeepsCenterAndFont()
    {
        var result = _sut.Sanitize(
            "<center><font face=\"Arial\" size=\"4\" color=\"#ff0000\">x</font></center>").Html;

        Assert.Contains("<center>", result);
        Assert.Contains("face=", result);
        Assert.Contains("color=", result);
    }

    // Separator rules and card borders ride on per-side longhands, written directly by real
    // mail (border-top-style, border-bottom-width...) or produced by shorthand expansion.
    // Dropping them erased every hairline rule the user compared against Rainloop.
    [Theory]
    [InlineData("border-top: 1px solid #e0e0e0", "solid")]
    [InlineData("border-bottom: 2px dashed #cccccc", "dashed")]
    [InlineData("border-top-width: 1px", "border-top-width")]
    [InlineData("border-top-style: solid", "border-top-style")]
    [InlineData("border-top-color: #e0e0e0", "border-top-color")]
    [InlineData("border-bottom-width: 3px", "border-bottom-width")]
    [InlineData("border-left-style: solid", "border-left-style")]
    [InlineData("border-right-color: #cccccc", "border-right-color")]
    public void Sanitize_KeepsHairlineBorders(string declaration, string expected)
    {
        var result = _sut.Sanitize(
            $"<table><tr><td style=\"{declaration}\">x</td></tr></table>").Html;

        Assert.Contains(expected, result);
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

    // A url() in a property outside the withholding rule would fetch without consent.
    [Theory]
    [InlineData("<div style=\"border-image: url(http://evil.example/pix.gif)\">x</div>")]
    [InlineData("<div style=\"list-style-image: url(http://evil.example/pix.gif)\">x</div>")]
    public void Sanitize_NeverKeepsACssUrl(string html)
    {
        var result = _sut.Sanitize(html).Html;

        Assert.DoesNotContain("evil.example", result);
        Assert.Contains("x", result);
    }

    // Withheld like an <img src>: the URL survives inert, out of the CSS, and counts toward the
    // banner. Nothing fetches until the reader consents.
    [Fact]
    public void Sanitize_MovesARemoteBackgroundToDataBlockedBgAndCountsIt()
    {
        var result = _sut.Sanitize(
            "<div style=\"background-image: url(https://cdn.example/logo.png); background-size: contain\">x</div>");

        Assert.Equal(1, result.BlockedImageCount);
        Assert.Contains("data-blocked-bg=\"https://cdn.example/logo.png\"", result.Html);
        Assert.DoesNotContain("url(", result.Html);
        Assert.Contains("background-size", result.Html);
    }

    // The shorthand reaches this pass already expanded by Ganss, so quoting is whatever it
    // serialised; the rule must read every form it can produce.
    [Fact]
    public void Sanitize_WithholdsAQuotedBackgroundUrl()
    {
        var result = _sut.Sanitize(
            "<div style=\"background: url('https://cdn.example/logo.png') center / contain no-repeat #fff\">x</div>");

        Assert.Equal(1, result.BlockedImageCount);
        Assert.Contains("data-blocked-bg=\"https://cdn.example/logo.png\"", result.Html);
    }

    // Rule 6(a) applied to the background shorthand: AngleSharp expands `center` and `no-repeat`
    // into -x/-y longhands, so allowing the base names alone left the restored logo painting at
    // 0% 0% and tiling across the cell.
    [Fact]
    public void Sanitize_KeepsThePositionAndRepeatOfAWithheldBackground()
    {
        var style = StyleOf(_sut.Sanitize(
            "<div style=\"background: url('https://cdn.example/logo.png') center / contain no-repeat #fff\">x</div>").Html);

        Assert.Contains("background-position: center", style);
        Assert.Contains("background-repeat: no-repeat", style);
        Assert.Contains("background-size: contain", style);
        Assert.Contains("background-color: rgba(255, 255, 255, 1)", style);
    }

    // Today the gradient dies with the image it shares a declaration with.
    [Fact]
    public void Sanitize_KeepsAGradientSharingTheDeclarationWithAWithheldLayer()
    {
        var result = _sut.Sanitize(
            "<div style=\"background-image: linear-gradient(to right, #000, #fff), url(https://cdn.example/l.png)\">x</div>");

        Assert.Contains("linear-gradient", result.Html);
        Assert.Contains("data-blocked-bg=\"https://cdn.example/l.png\"", result.Html);
    }

    [Fact]
    public void Sanitize_WithholdsEveryLayerInOrder()
    {
        var result = _sut.Sanitize(
            "<div style=\"background-image: url(https://a.example/1.png), url(https://b.example/2.png)\">x</div>");

        Assert.Equal(2, result.BlockedImageCount);
        Assert.Contains("data-blocked-bg=\"https://a.example/1.png https://b.example/2.png\"", result.Html);
    }

    // A quoted url() can carry a raw space, which the space-separated attribute would read back
    // as two URLs.
    [Fact]
    public void Sanitize_EncodesASpaceInAWithheldBackgroundUrl()
    {
        var result = _sut.Sanitize("<div style=\"background-image: url('https://cdn.example/a b.png')\">x</div>");

        Assert.Equal(1, result.BlockedImageCount);
        Assert.Contains("data-blocked-bg=\"https://cdn.example/a%20b.png\"", result.Html);
    }

    // The bytes never leave the mailbox, so there is nothing to consent to: the client resolves it.
    [Fact]
    public void Sanitize_LeavesACidBackgroundInTheCss()
    {
        var result = _sut.Sanitize("<div style=\"background-image: url(cid:logo@mail)\">x</div>");

        Assert.Equal(0, result.BlockedImageCount);
        Assert.Contains("cid:logo@mail", result.Html);
        Assert.DoesNotContain("data-blocked-bg", result.Html);
    }

    // An escape can spell the same function past a naive reader, so the row is not worth an exception.
    [Fact]
    public void Sanitize_CullsABackgroundDeclarationCarryingABackslash()
    {
        var result = _sut.Sanitize(
            "<div style=\"background-image: \\75 rl(https://cdn.example/l.png)\">x</div>");

        Assert.Equal(0, result.BlockedImageCount);
        Assert.DoesNotContain("cdn.example", result.Html);
        Assert.DoesNotContain("data-blocked-bg", result.Html);
    }

    // A `;` is legal in a URL path and in a CSS string, so splitting declarations on it blindly
    // tore the url() in half and left the halves — a working fetch — in the CSS.
    [Fact]
    public void Sanitize_WithholdsABackgroundUrlCarryingASemicolon()
    {
        var result = _sut.Sanitize("<div style=\"background-image: url('https://evil.example/a;b.png')\">x</div>");

        Assert.Equal(1, result.BlockedImageCount);
        Assert.DoesNotContain("evil.example", StyleOf(result.Html));
    }

    [Fact]
    public void Sanitize_CountsEveryLayerWhenOneCarriesASemicolon()
    {
        var result = _sut.Sanitize(
            "<div style=\"background-image: url('https://evil.example/1.png;x'), url('https://evil.example/2.png')\">x</div>");

        Assert.Equal(2, result.BlockedImageCount);
        Assert.DoesNotContain("evil.example", StyleOf(result.Html));
    }

    // A parenthesis inside a quoted cid: URL used to merge the layers, and the cid branch then
    // whitelisted the remote one riding along in the merged layer.
    [Theory]
    [InlineData("cid:a)b")]
    [InlineData("cid:a(b")]
    public void Sanitize_DoesNotLetACidLayerShelterARemoteOne(string cid)
    {
        var result = _sut.Sanitize(
            $"<div style=\"background-image: url('{cid}'), url('https://evil.example/z.png')\">x</div>");

        Assert.Equal(1, result.BlockedImageCount);
        Assert.DoesNotContain("evil.example", StyleOf(result.Html));
        Assert.Contains("data-blocked-bg=\"https://evil.example/z.png\"", result.Html);
    }

    // The parenthesis inside the quoted URL made the gradient's own commas read as layer
    // separators, emitting CSS the browser drops.
    [Fact]
    public void Sanitize_KeepsAGradientBesideAUrlCarryingAParenthesis()
    {
        var result = _sut.Sanitize(
            "<div style=\"background-image: url('https://evil.example/x)y.png'), linear-gradient(to right,#000,#fff)\">x</div>");

        Assert.Equal(1, result.BlockedImageCount);
        Assert.DoesNotContain("evil.example", StyleOf(result.Html));
        Assert.Contains("linear-gradient(90deg, rgba(0, 0, 0, 1), rgba(255, 255, 255, 1))", StyleOf(result.Html));
    }

    // Accepted collateral of the escape cull: this declaration used to render, because the CSS
    // parser resolved the escape before the second pass looked.
    [Fact]
    public void Sanitize_LosesAnEscapedDeclarationButKeepsItsNeighbours()
    {
        var result = _sut.Sanitize("<div style=\"font-family: \\41 rial; color: red\">x</div>").Html;

        Assert.DoesNotContain("Arial", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("color: rgba(255, 0, 0, 1)", result);
    }

    // The escape cull reads raw CSS, where a `;` inside a quoted URL would otherwise truncate the
    // declarations around it.
    [Fact]
    public void Sanitize_CullsOnlyTheEscapedDeclarationAroundAQuotedSemicolon()
    {
        var result = _sut.Sanitize(
            "<div style=\"background-image: url('https://cdn.example/a;b.png'); color: red; font-family: \\41 rial\">x</div>");

        Assert.Equal(1, result.BlockedImageCount);
        Assert.Contains("data-blocked-bg=\"https://cdn.example/a;b.png\"", result.Html);
        Assert.Contains("color: rgba(255, 0, 0, 1)", result.Html);
        Assert.DoesNotContain("Arial", result.Html, StringComparison.OrdinalIgnoreCase);
    }

    // Only our own post-Ganss pass may create the attribute; a message cannot arrive carrying one.
    [Fact]
    public void Sanitize_DropsADataBlockedBgTheMessageBrought()
    {
        var result = _sut.Sanitize("<div data-blocked-bg=\"https://evil.example/p.gif\">x</div>");

        Assert.DoesNotContain("evil.example", result.Html);
    }

    // Its twin: a forged data-blocked-src would load on consent without ever being counted, so the
    // banner's number — the only thing the reader consents against — would understate what it grants.
    [Fact]
    public void Sanitize_DropsADataBlockedSrcTheMessageBrought()
    {
        var result = _sut.Sanitize("<img data-blocked-src=\"https://evil.example/track.gif\" alt=\"x\">");

        Assert.Equal(0, result.BlockedImageCount);
        Assert.DoesNotContain("evil.example", result.Html);
    }

    // The cull targets the url( function. A declaration merely containing those three letters —
    // a font really named Curly — is not a fetch and must survive.
    [Fact]
    public void Sanitize_KeepsADeclarationWhoseValueMerelyContainsTheLettersUrl()
    {
        var result = _sut.Sanitize("<div style=\"font-family: Curly\">x</div>").Html;

        Assert.Contains("Curly", result, StringComparison.OrdinalIgnoreCase);
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

    // AngleSharp's default formatter (InnerHtml) leaves < and > unescaped in attribute values,
    // so a payload smuggled in a title came back as live markup wherever the value is re-read
    // as HTML. The final serialisation must use Ganss's formatter, like OutgoingMailSanitizer.
    [Fact]
    public void Sanitize_EscapesAngleBracketsInAttributeValues()
    {
        var result = _sut.Sanitize("<p title=\"<img src=x onerror=alert(1)>\">hi</p>").Html;

        Assert.DoesNotContain("<img", result);
        Assert.Contains("&lt;img", result);
        Assert.Contains("hi", result);
    }

    // The ceiling is applied before any parse, so the kept part crosses the whole pipeline:
    // a script inside it is still removed, and nothing past the cut survives.
    [Fact]
    public void Sanitize_TruncatesAnOversizedBodyAndStillSanitisesIt()
    {
        var html = "<script>alert(1)</script><p>hi</p><p>"
            + new string('a', MailHtmlSanitizer.MaxInputLength)
            + "</p><img src=\"https://tail.example/z.png\">";

        var result = _sut.Sanitize(html);

        Assert.True(result.Truncated);
        Assert.DoesNotContain("script", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hi", result.Html);
        Assert.DoesNotContain("tail.example", result.Html);
        Assert.InRange(result.Html.Length, 1, MailHtmlSanitizer.MaxInputLength + 1024);
    }

    [Fact]
    public void Sanitize_DoesNotFlagANormalBodyAsTruncated()
    {
        var result = _sut.Sanitize("<p>hi</p>");

        Assert.False(result.Truncated);
        Assert.Contains("hi", result.Html);
    }

    // The width ceiling bounds characters; the pipeline's cost is proportional to nodes. 2M
    // characters of <div>x</div> is ~175 000 of them, measured between 22.7 s and 71.5 s with the
    // width ceiling alone. What is asserted is the bound, not a duration: the timings belong to
    // the measurement harness, and a millisecond threshold only ever encodes the runner's speed.
    [Fact]
    public void Sanitize_BoundsTheCostOfAnElementDenseBody()
    {
        var html = string.Concat(Enumerable.Repeat("<div>x</div>", MailHtmlSanitizer.MaxInputLength / 12));

        var result = _sut.Sanitize(html);

        Assert.True(result.Truncated);
        Assert.Equal(20_000, Regex.Matches(result.Html, "<div>").Count);
    }

    // A comment is a node the parser builds and an element ceiling never sees. 233 000 of them
    // inside kept levels measured 55 s, against 0.5 s for the same count of elements: removing
    // them is quadratic in siblings, so the ceiling has to count them too.
    [Theory]
    [InlineData("<div></1>")]
    [InlineData("<div><!--x-->")]
    public void Sanitize_BoundsTheCostOfACommentDenseBody(string unit)
    {
        var html = string.Concat(Enumerable.Repeat(unit, MailHtmlSanitizer.MaxInputLength / unit.Length));

        var result = _sut.Sanitize(html);

        Assert.True(result.Truncated);
        Assert.Equal(1024, Regex.Matches(result.Html, "<div>").Count);
    }

    // The cut keeps the leading part and flags it, exactly as the width ceiling does — and what
    // it keeps has still crossed every pass.
    [Fact]
    public void Sanitize_CutsAtTheElementCeilingAndStillSanitisesWhatItKeeps()
    {
        var html = "<script>alert(1)</script><p>hi</p>"
                   + string.Concat(Enumerable.Repeat("<div>x</div>", 30_000))
                   + "<img src=\"https://tail.example/z.png\">";

        var result = _sut.Sanitize(html);

        Assert.True(result.Truncated);
        Assert.Contains("hi", result.Html);
        Assert.DoesNotContain("alert", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tail.example", result.Html);
    }

    [Fact]
    public void Sanitize_DoesNotCutAnOrdinaryElementCount()
    {
        var html = string.Concat(Enumerable.Repeat("<div>x</div>", 2_000));

        var result = _sut.Sanitize(html);

        Assert.False(result.Truncated);
        Assert.Equal(2_000, Regex.Matches(result.Html, "<div>").Count);
    }

    // A tag that closes a peer is a sibling, not a level. Counted as a level, 1024 unclosed
    // paragraphs or table cells would flatten the rest of a perfectly ordinary newsletter.
    [Theory]
    [InlineData("<p>", "<p>")]
    [InlineData("<tr><td>x", "<td>")]
    public void Sanitize_DoesNotReadAPeerTagAsALevel(string unit, string expected)
    {
        var html = "<table>" + string.Concat(Enumerable.Repeat(unit, 3_000)) + "</table>";

        var result = _sut.Sanitize(html);

        Assert.False(result.Truncated);
        Assert.Equal(3_000, Regex.Matches(result.Html, expected).Count);
    }

    // AngleSharp's tree construction is superlinear in nesting depth: measured on this runtime,
    // parsing 50 000 nested divs takes 6.5 s and 100 000 takes 43.6 s, and the pipeline parses
    // three times. 200 000 levels fit in a body a sender chooses freely.
    [Fact]
    public void Sanitize_SurvivesADocumentTooDeepForTheParser()
    {
        var html = string.Concat(Enumerable.Repeat("<div>", 200_000)) + "deep";

        var result = _sut.Sanitize(html).Html;

        // The tree the parser is handed is what matters, and it is countable: 1024 levels, never
        // the 200 000 the sender chose.
        Assert.Contains("deep", result);
        Assert.Equal(1024, Regex.Matches(result, "<div>").Count);
    }

    [Fact]
    public void Sanitize_KeepsNestingRealMailReaches()
    {
        var html = string.Concat(Enumerable.Repeat("<div>", 200)) + "deep"
                   + string.Concat(Enumerable.Repeat("</div>", 200));

        var result = _sut.Sanitize(html).Html;

        Assert.Equal(200, Regex.Matches(result, "<div>").Count);
    }

    // The over-deep wrappers go, their content stays — rule 6(b)'s unwrap applied to depth.
    [Fact]
    public void Sanitize_FlattensNestingPastTheCapWithoutLosingContent()
    {
        var html = string.Concat(Enumerable.Repeat("<div>", 5_000)) + "<p>the whole message</p>"
                   + string.Concat(Enumerable.Repeat("</div>", 5_000));

        var result = _sut.Sanitize(html).Html;

        Assert.Contains("the whole message", result);
        Assert.Equal(1024, Regex.Matches(result, "<div>").Count);
    }

    // HTML has no self-closing syntax outside void elements: <div/> opens a level, and honouring
    // its slash would let a sender nest past the cap uncounted.
    [Theory]
    [InlineData("<div/>")]
    [InlineData("<DIV>")]
    public void Sanitize_CountsTagsThatOnlyLookSelfClosingOrLowercase(string open)
    {
        var result = _sut.Sanitize(string.Concat(Enumerable.Repeat(open, 5_000)) + "deep").Html;

        Assert.Contains("deep", result);
        Assert.Equal(1024, Regex.Matches(result, "<div>").Count);
    }

    // A quote opens an attribute value only right after '='. Read as a delimiter anywhere, a
    // stray apostrophe would let one tag swallow the document and the nesting inside it.
    [Fact]
    public void Sanitize_CountsNestingBehindAStrayApostrophe()
    {
        var html = "<p title=it's>" + string.Concat(Enumerable.Repeat("<div>", 5_000)) + "deep'";

        var result = _sut.Sanitize(html).Html;

        Assert.Contains("deep", result);
        Assert.Equal(1024, Regex.Matches(result, "<div>").Count);
    }

    [Fact]
    public void Sanitize_KeepsAQuotedAngleBracketInsideAnAttribute()
    {
        var result = _sut.Sanitize("<div title=\"a>b\"><p>hi</p></div>").Html;

        Assert.Contains("<p>hi</p>", result);
    }

    [Fact]
    public void Sanitize_DoesNotCountVoidTagsAsNesting()
    {
        var result = _sut.Sanitize(string.Concat(Enumerable.Repeat("<br>", 2_000)) + "<p>hi</p>").Html;

        Assert.Equal(2_000, Regex.Matches(result, "<br>").Count);
        Assert.Contains("<p>hi</p>", result);
    }

    // A comparison inside a script is text, not a start tag; counting it would flatten the
    // markup that follows a perfectly ordinary message.
    [Fact]
    public void Sanitize_DoesNotReadScriptTextAsNesting()
    {
        var script = "<script>" + string.Concat(Enumerable.Repeat("if(a<b){}", 2_000)) + "</script>";

        var result = _sut.Sanitize(script + "<div><p>hi</p></div>").Html;

        Assert.Contains("<div><p>hi</p></div>", result);
    }

    // Where a tag name ends is the one thing the scan and the tokeniser must agree on for every
    // input. Each of these reads as a name the scan once acted on and the tokeniser never emits,
    // and each left the scan seeing no depth at all while the parser built the whole tree:
    // <script_x> as raw-text `script` whose skip swallowed the document, <br_x> as a void tag,
    // <p_x> as a peer, </1> as an end tag closing a level nothing opened, and the two comment
    // terminators the scan did not know, which skipped every element up to the next `-->`.
    [Theory]
    [InlineData("<script_x>")]
    [InlineData("<style:x>")]
    [InlineData("<iframe.x>")]
    [InlineData("<textarea1>")]
    [InlineData("<!-- x --!>")]
    [InlineData("<!-->")]
    [InlineData("<!--->")]
    public void Sanitize_DoesNotLetAPrefixThatOnlyLooksKnownHideTheDocument(string prefix)
    {
        // 1 100 levels, not 60 000: what discriminates is the count that survives, so the payload
        // only has to cross the cap. A prefix the scan mis-reads leaves all 1 100 standing.
        var html = prefix + string.Concat(Enumerable.Repeat("<div>", 1_100)) + "deep";

        var result = _sut.Sanitize(html).Html;

        // 1023 where the prefix is an element the parser nests — it holds the first level itself —
        // and 1024 where it is a comment, which opens nothing.
        Assert.Contains("deep", result);
        Assert.InRange(Regex.Matches(result, "<div>").Count, 1023, 1024);
    }

    [Theory]
    [InlineData("<br_x>")]
    [InlineData("<p_x>")]
    [InlineData("<td:x>")]
    [InlineData("<div></1>")]
    public void Sanitize_CountsDepthATagOnlyLookingLikeAVoidPeerOrEndTagWouldHide(string unit)
    {
        var html = string.Concat(Enumerable.Repeat(unit, 1_100)) + "<b>marker</b>";

        var result = _sut.Sanitize(html).Html;

        // Past the cap a wrapper is elided and its content kept. A unit the scan reads as opening
        // no level leaves the marker inside the cap, still wrapped — which is the whole tell.
        Assert.Contains("marker", result);
        Assert.DoesNotContain("<b>", result);
    }

    // The tokeniser's whitespace set, not Unicode's: a no-break space does not separate
    // attributes, so a quote after one opens no value and the tag still ends at its first '>'.
    [Fact]
    public void Sanitize_DoesNotLetANoBreakSpaceOpenAnAttributeValue()
    {
        var html = "<div title= \"x><p>hi</p>\">" + string.Concat(Enumerable.Repeat("<div>", 5_000)) + "deep";

        var result = _sut.Sanitize(html).Html;

        Assert.Contains("hi", result);
        Assert.Contains("deep", result);
        Assert.InRange(Regex.Matches(result, "<div>").Count, 1023, 1024);
    }

    // Conditional comments carry markup Outlook alone reads. It is comment text to everyone else,
    // and reading it as elements would spend a real message's depth and element budget on it.
    [Fact]
    public void Sanitize_TreatsAConditionalCommentAsComment()
    {
        var html = "<!--[if mso]>" + string.Concat(Enumerable.Repeat("<table><tr><td>x</td></tr></table>", 500))
                   + "<![endif]--><p>hi</p>";

        var result = _sut.Sanitize(html);

        Assert.False(result.Truncated);
        Assert.Contains("<p>hi</p>", result.Html);
        Assert.DoesNotContain("<table>", result.Html);
    }

    // What survives the cap crosses the whole pipeline unchanged — the guard runs before it,
    // never instead of it, so rule 6's three sub-rules still decide the outcome.
    [Theory]
    [InlineData("<script>alert(1)</script>", "alert(1)")]
    [InlineData("<img src=x onerror=\"alert(1)\">", "onerror")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "javascript:")]
    [InlineData("<p style=\"border-image: url(https://evil.example/a.png)\">x</p>", "evil.example")]
    [InlineData("<p style=\"background-image: \\75 rl(https://evil.example/a.png)\">x</p>", "evil.example")]
    public void Sanitize_StillStripsHostileContentJustInsideTheCap(string hostile, string forbidden)
    {
        var html = string.Concat(Enumerable.Repeat("<div>", 1_000)) + hostile;

        var result = _sut.Sanitize(html).Html;

        Assert.DoesNotContain(forbidden, result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_StillWithholdsARemoteBackgroundJustInsideTheCap()
    {
        var html = string.Concat(Enumerable.Repeat("<div>", 1_000))
                   + "<p style=\"background-image: url(https://cdn.example/logo.png)\">x</p>";

        var result = _sut.Sanitize(html);

        Assert.Equal(1, result.BlockedImageCount);
        Assert.Contains("data-blocked-bg=\"https://cdn.example/logo.png\"", result.Html);
        Assert.DoesNotContain("url(", result.Html);
    }

    // The cap runs before the pipeline, never instead of it.
    [Theory]
    [InlineData("<script>alert(1)</script>", "alert(1)")]
    [InlineData("<style>body{color:red}</style>", "color:red")]
    [InlineData("<img src=x onerror=\"alert(1)\">", "onerror")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>", "javascript:")]
    [InlineData("<p style=\"border-image: url(https://evil.example/a.png)\">x</p>", "evil.example")]
    [InlineData("<p style=\"background-image: \\75 rl(https://evil.example/a.png)\">x</p>", "evil.example")]
    public void Sanitize_StillStripsHostileContentBuriedPastTheCap(string hostile, string forbidden)
    {
        var html = string.Concat(Enumerable.Repeat("<div>", 5_000)) + hostile;

        var result = _sut.Sanitize(html).Html;

        Assert.DoesNotContain(forbidden, result, StringComparison.OrdinalIgnoreCase);
    }

    // A peer tag is never elided, so an over-deep document still carries elements the CSS pass
    // must judge: a remote background there is withheld, not left fetchable in the style.
    [Fact]
    public void Sanitize_StillWithholdsARemoteBackgroundPastTheCap()
    {
        var html = string.Concat(Enumerable.Repeat("<div>", 5_000))
                   + "<p style=\"background-image: url(https://cdn.example/logo.png)\">x</p>";

        var result = _sut.Sanitize(html);

        Assert.Equal(1, result.BlockedImageCount);
        Assert.Contains("data-blocked-bg=\"https://cdn.example/logo.png\"", result.Html);
        Assert.DoesNotContain("url(", result.Html);
    }

    // A withheld URL is meant to appear in data-blocked-bg; what must never appear is the URL
    // still inside the CSS, which is the only place a leak can fetch from.
    private static string StyleOf(string html) =>
        Regex.Match(html, "style=\"(?<s>[^\"]*)\"").Groups["s"].Value;
}
