using System.Diagnostics;
using System.Text.RegularExpressions;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Services;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services;

public sealed class MailHtmlSanitizerTests
{
    private readonly MailHtmlSanitizer _sut = new();

    [Theory]
    [InlineData("<html><head><meta name=\"color-scheme\" content=\"light dark\"></head><body><p>hi</p></body></html>")]
    [InlineData("<html><head><meta name=\"supported-color-schemes\" content=\"light dark\"></head><body><p>hi</p></body></html>")]
    [InlineData("<html><head><META NAME=\"Color-Scheme\" CONTENT=\"dark\"></head><body><p>hi</p></body></html>")]
    [InlineData("<html><head><style>:root { color-scheme: light dark; }</style></head><body><p>hi</p></body></html>")]
    public void Sanitize_ReportsADeclaredDarkScheme(string html)
    {
        Assert.True(_sut.Sanitize(html).DeclaresDarkScheme);
    }

    // A dark block without the declaration is not a promise: prefers-color-scheme alone is how a
    // sender reacts to a client, not how it says the design was made for both.
    [Theory]
    [InlineData("<html><head><style>@media (prefers-color-scheme: dark) { .x { color: #fff } }</style></head><body><p>hi</p></body></html>")]
    [InlineData("<html><head><meta name=\"color-scheme\" content=\"light\"></head><body><p>hi</p></body></html>")]
    [InlineData("<html><head><meta name=\"color-scheme\"></head><body><p>hi</p></body></html>")]
    [InlineData("<p>hi</p>")]
    public void Sanitize_ReportsNoDarkSchemeWithoutTheDeclaration(string html)
    {
        Assert.False(_sut.Sanitize(html).DeclaresDarkScheme);
    }

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
    [InlineData("<template><p>tpl</p></template><p>hi</p>", "tpl")]
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

    // A responsive newsletter adapts to a phone through its own @media rules, and they need
    // !important to beat the inline widths they override.
    [Fact]
    public void Sanitize_KeepsAHeadStylesheetAtTheHeadOfTheBody()
    {
        var result = _sut.Sanitize(
            "<html><head><style>@media only screen and (max-width: 499px) { .full-width { width: 100% !important } }</style></head>"
            + "<body><table class=\"full-width\" id=\"main\"><tr><td>hi</td></tr></table></body></html>").Html;

        Assert.StartsWith("<style>", result);
        Assert.Contains("@media only screen and (max-width: 499px)", result);
        Assert.Contains("width: 100% !important", result);
        Assert.Contains("class=\"full-width\"", result);
        Assert.Contains("id=\"main\"", result);
    }

    [Fact]
    public void Sanitize_KeepsHeadStylesheetsInDocumentOrder()
    {
        var result = _sut.Sanitize(
            "<html><head><style>.a { color: red }</style><style>.b { color: blue }</style></head><body><p>hi</p></body></html>").Html;

        Assert.True(result.IndexOf(".a", StringComparison.Ordinal) < result.IndexOf(".b", StringComparison.Ordinal));
    }

    [Fact]
    public void Sanitize_KeepsChildCombinatorsInASheet()
    {
        var result = _sut.Sanitize("<style>td > p.a { color: red }</style><p class=\"a\">hi</p>").Html;

        Assert.Contains("td>p.a", result.Replace(" ", string.Empty));
    }

    [Theory]
    [InlineData("position")]
    [InlineData("z-index")]
    [InlineData("float")]
    public void Sanitize_AppliesThePropertyAllowlistInsideASheet(string property)
    {
        var result = _sut.Sanitize(
            $"<style>@media (max-width: 499px) {{ .x {{ {property}: 1; color: red }} }}</style><p>hi</p>").Html;

        Assert.DoesNotContain(property, result);
        Assert.Contains("color", result);
    }

    [Theory]
    [InlineData("@import url(https://t.example/a.css);", "t.example")]
    [InlineData("@keyframes k { from { color: red } }", "keyframes")]
    [InlineData("@namespace svg url(http://www.w3.org/2000/svg);", "namespace")]
    public void Sanitize_DropsEveryOtherAtRule(string rule, string forbidden)
    {
        var result = _sut.Sanitize($"<style>{rule} .x {{ color: red }}</style><p>hi</p>").Html;

        Assert.DoesNotContain(forbidden, result);
        Assert.Contains(".x", result);
    }

    // No consent surface exists for a url() in a rule: it would fetch on render.
    [Theory]
    [InlineData(".x { background-image: url(https://t.example/p.gif); color: red }")]
    [InlineData("@media (max-width: 499px) { .x { background-image: url(https://t.example/p.gif); color: red } }")]
    [InlineData(".x { background: url(https://t.example/p.gif) #fff; color: red }")]
    [InlineData(".x { background-image: url(cid:logo@mail); color: red }")]
    public void Sanitize_DropsAUrlDeclarationFromASheetRule(string css)
    {
        var result = _sut.Sanitize($"<style>{css}</style><p>hi</p>").Html;

        Assert.DoesNotContain("url(", result);
        Assert.Contains("color", result);
    }

    // Ganss strips `media`, so the sheet's condition must travel into its text.
    [Theory]
    [InlineData("screen and (max-width: 600px)", "@media screen and (max-width: 600px)")]
    [InlineData("only screen and (max-width: 600px)", "@media only screen and (max-width: 600px)")]
    [InlineData("screen, print", "@media screen, print")]
    [InlineData("print", "@media print")]
    public void Sanitize_CarriesAStyleSheetsMediaIntoItsText(string media, string rule)
    {
        var result = _sut.Sanitize($"<style media=\"{media}\">.x{{display:none}}</style><p class=\"x\">hi</p>").Html;

        Assert.Contains(rule, result);
        Assert.DoesNotContain("media=", result);
    }

    [Theory]
    [InlineData(" ALL ")]
    [InlineData("")]
    public void Sanitize_KeepsAStyleSheetForAllMediaUnconditional(string media)
    {
        var result = _sut.Sanitize($"<style media=\"{media}\">.x{{color:red}}</style><p class=\"x\">hi</p>").Html;

        Assert.Contains("color", result);
        Assert.DoesNotContain("@media", result);
    }

    // The value is spliced into CSS text: anything that could end the prelude or escape a
    // character is refused rather than interpolated.
    [Theory]
    [InlineData("print { } .y")]
    [InlineData("print; .y")]
    [InlineData("\\70 rint")]
    [InlineData("print /*")]
    [InlineData("((( screen")]
    public void Sanitize_DropsAStyleSheetWhoseMediaCannotBeCarried(string media)
    {
        var result = _sut.Sanitize($"<style media=\"{media}\">.x{{display:none}}</style><p class=\"x\">hi</p>").Html;

        Assert.DoesNotContain("display", result);
    }

    // A text closing the wrapper early would put its later rules outside the condition.
    [Fact]
    public void Sanitize_DropsAMediaStyleSheetThatEscapesItsWrapper()
    {
        var result = _sut.Sanitize("<style media=\"print\">.a{color:red} } .x{display:none}</style><p class=\"x\">hi</p>").Html;

        Assert.DoesNotContain("display", result);
    }

    // A font is gated by the reader's CSP until consent, like an image. Its descriptors are not
    // filtered by the property allowlist.
    [Fact]
    public void Sanitize_KeepsAnHttpsFontFace()
    {
        var result = _sut.Sanitize(
            "<style>@font-face { font-family: Brand; src: url(https://fonts.example/b.woff2) format(\"woff2\"), local(\"Arial\"); unicode-range: U+0000-00FF }</style><p>hi</p>").Html;

        Assert.Contains("@font-face", result);
        Assert.Contains("https://fonts.example/b.woff2", result);
        Assert.Contains("unicode-range", result);
    }

    [Theory]
    [InlineData("url(javascript:alert(1))")]
    [InlineData("url(data:font/woff2;base64,AAAA)")]
    [InlineData("url(https://fonts.example/b.woff2), url(file:///etc/passwd)")]
    public void Sanitize_DropsAFontFaceWithANonHttpSource(string source)
    {
        var result = _sut.Sanitize($"<style>@font-face {{ font-family: Brand; src: {source} }} .x {{ color: red }}</style><p>hi</p>").Html;

        Assert.DoesNotContain("@font-face", result);
        Assert.Contains(".x", result);
    }

    // The raw form is closed by the first HTML parse, which leaves an inert <img> behind.
    [Fact]
    public void Sanitize_NeverLetsASheetCloseItsOwnElement()
    {
        var result = _sut.Sanitize(
            "<style>.x { font-family: \"a</style><img src=x onerror=alert(1)>\" } .y { color: red }</style><p>hi</p>").Html;

        Assert.DoesNotContain("onerror", result);
        Assert.Equal(result.Split("<style").Length - 1, result.Split("</style>").Length - 1);
    }

    // `\3c` is an unsafe escape, so HoldsUnsafeEscape removes this sheet before any parse; the
    // `</` guard's own path is the safe `\/` escape below.
    [Fact]
    public void Sanitize_RemovesASheetWhoseEscapesWouldCloseIt()
    {
        var result = _sut.Sanitize(
            "<style>a[title=\"\\3c/style>\\3cimg src=x onerror=alert(1)>\"] { color: red }</style><p>hi</p>").Html;

        Assert.DoesNotContain("onerror", result);
        Assert.DoesNotContain("<img", result);
        Assert.Equal(result.Split("<style").Length - 1, result.Split("</style>").Length - 1);
    }

    // `\/` is safe punctuation, so these sheets reach the CSS parser, which resolves it: only the
    // `</` guard in VetStyleSheets stands between the serialised text and a closed element.
    [Theory]
    [InlineData("a[title=\"<\\/style><img src=x onerror=alert(1)>\"] { color: red }")]
    [InlineData("@font-face { font-family: \"<\\/style><img src=x onerror=alert(1)>\"; src: url(https://x.example/a.woff) }")]
    public void Sanitize_RemovesASheetThatWouldCloseItsElementOnceParsed(string css)
    {
        var result = _sut.Sanitize($"<style>{css}</style><p>hi</p>").Html;

        Assert.DoesNotContain("<style", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", result, StringComparison.OrdinalIgnoreCase);
    }

    // Ganss strips `type`, so a block the sender marked as not CSS would otherwise go live.
    [Theory]
    [InlineData("text/template")]
    [InlineData("text/x-handlebars")]
    public void Sanitize_RemovesASheetTypedAsSomethingElse(string type)
    {
        var result = _sut.Sanitize($"<style type=\"{type}\">.x {{ display: none }}</style><p class=\"x\">hi</p>").Html;

        Assert.DoesNotContain("<style", result, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("text/css")]
    [InlineData(" TEXT/CSS ")]
    [InlineData("")]
    public void Sanitize_KeepsASheetTypedAsCss(string type)
    {
        var result = _sut.Sanitize($"<style type=\"{type}\">.x {{ display: none }}</style><p class=\"x\">hi</p>").Html;

        Assert.Contains(".x { display: none }", result);
    }

    // Every bypass found across two review rounds of a hand-rolled CSS tokeniser, replayed against
    // the rule that replaced it: a sheet holding any escape besides plain punctuation is removed
    // whole, so none of these need parsing to be judged safe or not.
    [Theory]
    // The original three (round 1's first pass): an escaped url(), an escaped style breakout, an
    // escaped @import.
    [InlineData(".x { background-image: \\75 rl(https://t.example/e.gif); color: red }")]
    [InlineData(".x { font-family: \"\\3c/style\\3e<img src=x onerror=alert(1)>\"; color: red }")]
    [InlineData("@\\69 mport url(https://t.example/a.css); .x { color: red }")]
    // The four probes that defeated round 1's tokeniser fix: an escaped brace, a quote inside an
    // unquoted url(), a raw form feed inside a string, a bogus block right after the value.
    [InlineData(".x { background-image: \\75 rl(https://t.example/e.gif) \\{ } }")]
    [InlineData(".x { a: url(q\"b); background-image: \\75 rl(https://t.example/e.gif); c: \"z { } }")]
    [InlineData(".x { font-family: \"a\f; background-image: \\75 rl(https://t.example/e.gif); b: \" { } }")]
    [InlineData(".x { color: red; background-image: \\75 rl(https://t.example/e.gif) {} }")]
    // The comment-gluing breakout: removing `/**/` with nothing in its place reassembled a real
    // closing tag, rendering a phishing link as live page content.
    [InlineData(".y { width: 1px\\31 } </**//style><img src=x onerror=alert(1)><a href=https://evil.example/login>Sign in</a> .z { color: red }")]
    // The four that defeated round 2's tokeniser fix: an escaped `url(`, an escaped letter inside
    // `url(`, an `@font-face` block wrongly treated as a grouping rule, and a url-shaped word
    // fooling the token detector.
    [InlineData("@media s { \\url(q\"b) } .y { color: \\72 ed } .z \" { color: blue } }")]
    [InlineData(".x { background-image: u\\72 l(https://t.example/e.gif) }")]
    [InlineData("@font-face { font-family: f; src: \\75 rl(https://t.example/f.woff) {} }")]
    [InlineData("xurl(q\"b) \") } .y { color: \\72 ed } \" { } }")]
    public void Sanitize_RemovesTheWholeSheetForAnyUnsafeEscape(string css)
    {
        var result = _sut.Sanitize($"<style>{css}</style><p>hi</p>").Html;

        Assert.DoesNotContain("<style", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("t.example", result);
        Assert.DoesNotContain("Sign in", result);
        Assert.DoesNotContain("onerror", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hi", result);
    }

    // Generated mail escapes punctuation in its own class names; none of it can spell a letter, a
    // hex digit, a quote or a brace, so the sheet is kept untouched rather than parsed at all.
    [Theory]
    [InlineData(".sm\\:px-4", "padding-left: 16px !important")]
    [InlineData(".w-1\\/2", "width: 50% !important")]
    public void Sanitize_KeepsASheetEscapingOnlySafePunctuation(string selector, string declaration)
    {
        var result = _sut.Sanitize(
            $"<style>@media (max-width: 600px) {{ {selector} {{ {declaration} }} }}</style><p>hi</p>").Html;

        Assert.Contains(selector, result);
        Assert.Contains(declaration, result);
    }

    // A brace is not in the safe set: `\}` cannot be told apart from an attempt to escape
    // structure, so the whole sheet goes, unlike the punctuation escapes above.
    [Fact]
    public void Sanitize_RemovesASheetEscapingAClosingBrace()
    {
        var result = _sut.Sanitize("<style>.a\\}b { color: red }</style><p>hi</p>").Html;

        Assert.DoesNotContain("<style", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hi", result);
    }

    // A backslash with nothing after it is unsafe by definition — there is no character to check.
    [Fact]
    public void Sanitize_RemovesASheetEndingInATrailingBackslash()
    {
        var result = _sut.Sanitize("<style>.x { color: red }\\</style><p>hi</p>").Html;

        Assert.DoesNotContain("<style", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hi", result);
    }

    // Accepted cost: a legitimate CJK font name written as a CSS unicode escape is not
    // distinguishable from an escaped fetch by punctuation alone, so its sheet is dropped too —
    // exactly what every sheet did before this pass existed at all.
    [Fact]
    public void Sanitize_RemovesASheetWithALegitimateUnicodeEscape()
    {
        var result = _sut.Sanitize("<style>.x { font-family: \"\\5FAE\\8F6F\" }</style><p>hi</p>").Html;

        Assert.DoesNotContain("<style", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hi", result);
    }

    [Theory]
    [InlineData(".sm\\:px-4 { color: red }")]
    [InlineData(".w-1\\/2 { color: red }")]
    [InlineData(".\\!mt-0 { color: red }")]
    [InlineData(".x { color: red }")]
    [InlineData("")]
    public void HoldsUnsafeEscape_AcceptsPunctuationEscapesAndPlainSheets(string css)
    {
        Assert.False(MailHtmlSanitizer.HoldsUnsafeEscape(css));
    }

    [Theory]
    [InlineData(".x { color: \\72 ed }")]
    [InlineData(".x { color: red }\\")]
    [InlineData(".a\\}b { color: red }")]
    [InlineData(".x { font-family: \"\\5FAE\\8F6F\" }")]
    [InlineData(".x { font-family: a\\* }")]
    public void HoldsUnsafeEscape_RejectsAnythingElse(string css)
    {
        Assert.True(MailHtmlSanitizer.HoldsUnsafeEscape(css));
    }

    // `*` is not in the safe set: `\/\*` would escape past the safe-punctuation check into a real
    // `/*` once Ganss serialises it back unescaped, opening a comment the second parser reads
    // differently — so `\*` alone removes the whole sheet before Ganss ever sees it.
    [Theory]
    [InlineData(".x { font-family: a\\/\\* }")]
    [InlineData("@media \\/\\* { .x { color: red } }")]
    public void Sanitize_RemovesASheetEscapingASlashStarCommentOpener(string css)
    {
        var result = _sut.Sanitize($"<style>{css}</style><p>hi</p>").Html;

        Assert.DoesNotContain("<style", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/*", result);
        Assert.Contains("hi", result);
    }

    // `(` and `[` are in the safe set, but Ganss serialises the escape away and hands
    // VetStyleSheets a plain, unbalanced `a(`/`a[` — AngleSharp's own parser then treats the
    // unterminated function/attribute selector as unparseable and drops the whole declaration
    // (verified against the real pipeline, not assumed), so no unbalanced punctuation survives
    // into the rendered rule either way.
    [Theory]
    [InlineData(".x { font-family: a\\( }", "a(")]
    [InlineData(".x { font-family: a\\[ }", "a[")]
    public void Sanitize_DropsADeclarationWhoseSafeEscapeUnbalancesAfterSerialisation(string css, string unbalanced)
    {
        var result = _sut.Sanitize($"<style>{css}</style><p>hi</p>").Html;

        Assert.DoesNotContain(unbalanced, result);
        Assert.Contains("hi", result);
    }

    // In an @font-face the bare `a(` is kept as an empty `font-family: ;`, which a re-parse drops:
    // only the fixpoint check sees that the written sheet would not re-read to itself.
    [Fact]
    public void Sanitize_RemovesASheetThatDoesNotReReadToItself()
    {
        const string html = "<style>@font-face { font-family: a\\(; src: url(https://x.example/a.woff) } .y { color: red }</style><p>hi</p>";

        var result = _sut.Sanitize(html).Html;

        Assert.DoesNotContain("<style", result);
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
    [InlineData("<style>.x{position:fixed}</style>", "position")]
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

    // AngleSharp.Css parses recursively: past ~2 000 nested blocks it overflows the stack, which
    // no catch survives — the whole API process dies. Run before the ceiling, this test crashes
    // the test runner instead of failing.
    [Theory]
    [InlineData("<style>{0}</style><p>hi</p>", "@media a{", "}")]
    [InlineData("<style>{0}</style><p>hi</p>", "@media a{", "")]
    [InlineData("<style media=\"screen\">{0}</style><p>hi</p>", "@media a{", "}")]
    [InlineData("<style>.x {{ width: {0} }}</style><p>hi</p>", "calc(", ")")]
    [InlineData("<p style=\"width: {0}\">hi</p>", "calc(", ")")]
    public void Sanitize_SurvivesCssNestedTooDeepForTheParser(string template, string open, string close)
    {
        var nested = string.Concat(Enumerable.Repeat(open, 5_000)) + "1px" + string.Concat(Enumerable.Repeat(close, 5_000));

        var result = _sut.Sanitize(string.Format(template, nested)).Html;

        Assert.Contains("hi", result);
        Assert.DoesNotContain(open, result);
    }

    [Fact]
    public void Sanitize_KeepsASheetNestedToTheCeiling()
    {
        var result = _sut.Sanitize("<style>" + Nested(15, ".x { display: none }") + "</style><p>hi</p>").Html;

        Assert.Contains("display: none", result);
    }

    // The media wrapper is one level more, so the same text crosses the ceiling once it has one.
    [Theory]
    [InlineData("<style>{0}</style><p>hi</p>")]
    [InlineData("<style media=\"screen\">{0}</style><p>hi</p>")]
    public void Sanitize_RemovesASheetNestedPastTheCeiling(string template)
    {
        var depth = template.Contains("media") ? 15 : 16;

        var result = _sut.Sanitize(string.Format(template, Nested(depth, ".x { display: none }"))).Html;

        Assert.DoesNotContain("<style", result);
    }

    [Theory]
    [InlineData(".x { display: none }")]
    [InlineData("@media (max-width: 600px) { .x { width: calc(100% - (2 * 8px)) !important } }")]
    [InlineData(".x { font-family: \"{{{{{{{{{{{{{{{{{\" }")]
    [InlineData(".x { display: none } /* {{{{{{{{{{{{{{{{{ */")]
    [InlineData(".x { background: url(https://cdn.example/a(1).png) }")]
    [InlineData(".x { background: url(  \"https://cdn.example/a.png\"  ) }")]
    [InlineData(".a\\(b { display: none } .c\\[d { color: blue }")]
    [InlineData("a) b] c} .x { display: none }")]
    public void NestsTooDeep_ReadsBracketsAsTheTokeniserDoes(string css)
    {
        Assert.False(MailHtmlSanitizer.NestsTooDeep(css));
    }

    // Wherever the scan and the parser could read the text differently, the sheet goes.
    [Theory]
    [InlineData(".x { font-family: \"a\n\" }")]
    [InlineData(".x { font-family: 'a }")]
    [InlineData(".x { color: red } /* open")]
    [InlineData(".x { background: url(a\"b) }")]
    [InlineData(".x { background: url(a\\)\"{{{{{{{{{{{{{{{{{\") }")]
    [InlineData(".x { background: url(a/*b) }")]
    [InlineData(".x { width: calc(} calc(} calc(} calc(} calc(} calc(} calc(} calc(} calc(} calc(} calc(} calc(} calc(} calc(} calc(} calc(} calc(} }")]
    public void NestsTooDeep_FailsClosedWhereTheReadingsCouldPart(string css)
    {
        Assert.True(MailHtmlSanitizer.NestsTooDeep(css));
    }

    // Measured before the ceiling: 2 MB of CSS took 13-16 s, 19 000 `<style media>` 9.4 s, and
    // 1.5 MB of `color-scheme:x ` 110 s in the dark-scheme scan alone.
    [Fact]
    public void Sanitize_DropsAStyleSheetPastTheSizeCeilingQuickly()
    {
        const string rule = ".x { display: none } ";
        var html = "<style>" + string.Concat(Enumerable.Repeat(rule, 2_000_000 / rule.Length)) + "</style><p>hi</p>";

        var result = AssertSanitisesWithinBudget(html);

        Assert.DoesNotContain("<style", result.Html);
        Assert.Contains("hi", result.Html);
    }

    [Fact]
    public void Sanitize_KeepsNoMoreSheetsThanTheCeilingQuickly()
    {
        var html = string.Concat(Enumerable.Repeat("<style media=\"screen\">.x { display: none }</style>", 19_000)) + "<p>hi</p>";

        var result = AssertSanitisesWithinBudget(html);

        Assert.Equal(64, Regex.Matches(result.Html, "<style>").Count);
    }

    [Fact]
    public void Sanitize_ScansAHostileColorSchemeRunQuickly()
    {
        var html = "<style>" + string.Concat(Enumerable.Repeat("color-scheme:x ", 100_000)) + "</style><p>hi</p>";

        Assert.False(AssertSanitisesWithinBudget(html).DeclaresDarkScheme);
    }

    [Fact]
    public void Sanitize_KeepsSheetsUnderTheCeilingUntouched()
    {
        var rules = string.Concat(Enumerable.Range(0, 5_000).Select(i => $".c{i} {{ display: none }}"));
        var html = $"<html><head><style>{rules}</style><style media=\"screen and (max-width: 600px)\">.x {{ width: 100% }}</style></head>"
                   + "<body><style>.y { display: block }</style><p>hi</p></body></html>";

        var result = _sut.Sanitize(html).Html;

        Assert.Equal(3, Regex.Matches(result, "<style>").Count);
        Assert.Contains(".c4999 { display: none }", result);
        Assert.Contains("@media screen and (max-width: 600px)", result);
        Assert.Contains(".y { display: block }", result);
    }

    // Sheets are kept in document order until the budget is spent; everything after is dropped,
    // even a sheet that alone would still have fit.
    [Fact]
    public void Sanitize_DropsEverySheetAfterTheSizeCeilingIsSpent()
    {
        var large = string.Concat(Enumerable.Repeat(".a { display: none } ", 150 * 1024 / 18));
        var html = $"<style>{large}</style><style>{large.Replace(".a", ".b")}</style><style>.c {{ display: block }}</style><p>hi</p>";

        var result = _sut.Sanitize(html).Html;

        Assert.Contains(".a { display: none }", result);
        Assert.DoesNotContain(".b {", result);
        Assert.DoesNotContain(".c {", result);
    }

    private SanitizedHtml AssertSanitisesWithinBudget(string html)
    {
        var clock = Stopwatch.StartNew();
        var result = _sut.Sanitize(html);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"sanitising took {clock.Elapsed}; the ceilings keep it well under a second.");
        return result;
    }

    // AngleSharp knows neither `word-break: break-word` nor its modern spelling, so Ganss drops the
    // one declaration that let an MJML mail's long tracking code wrap: an ING mail then overflowed.
    [Theory]
    [InlineData("padding: 10px; word-break: break-word")]
    [InlineData("WORD-BREAK:Break-Word;")]
    [InlineData("word-break: break-word !important")]
    public void Sanitize_MarksAnElementThatAskedToBreakLongWords(string style)
    {
        var result = _sut.Sanitize($"<table><tr><td style=\"{style}\">BE_AMB_INV_HYPE_INVEST_UP</td></tr></table>").Html;

        Assert.Contains("data-break-word", result);
    }

    [Theory]
    [InlineData("word-break: break-all")]
    [InlineData("word-break: normal")]
    [InlineData("overflow-wrap: break-word")]
    public void Sanitize_MarksNoElementThatDidNotAsk(string style)
    {
        var result = _sut.Sanitize($"<p style=\"{style}\">x</p>").Html;

        Assert.DoesNotContain("data-break-word", result);
    }

    private static string Nested(int depth, string content) =>
        string.Concat(Enumerable.Repeat("@media screen { ", depth)) + content + string.Concat(Enumerable.Repeat(" }", depth));
}
