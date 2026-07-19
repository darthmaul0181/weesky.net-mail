using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services
{
    public class MailHtmlSanitizerTests
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
}
