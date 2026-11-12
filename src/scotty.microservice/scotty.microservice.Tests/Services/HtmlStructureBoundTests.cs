using System.Text;
using System.Text.RegularExpressions;
using weesky.Scotty.Microservice.Services;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services;

public sealed class HtmlStructureBoundTests
{
    private readonly MailHtmlSanitizer _sanitizer = new();

    // Inside <svg>, <math> or <select> the parser reads a <style> or a <script> as markup, not as
    // text, so a scan skipping it as text let 50 000 nested tags through uncounted: 31 s for 150 KB.
    // Deleted from the text, such an element is nothing the two readings can disagree about.
    [Theory]
    [InlineData("<p>a</p><svg><style><g><g>x</svg><p>b</p>", "<p>a</p><p>b</p>")]
    [InlineData("<p>a</p><svg><style><g><g>", "<p>a</p>")]
    [InlineData("<math><mi><b>x</math>after", "after")]
    [InlineData("<select><style></select><p>y</p>", "<p>y</p>")]
    [InlineData("<script><!--<script></script><div>kept</div>", "<div>kept</div>")]
    [InlineData("<TITLE>t</TITLE >x", "x")]
    [InlineData("a<plaintext>b</plaintext><p>c", "a")]
    public void Apply_DeletesAnInertElementWithItsContent(string html, string expected)
    {
        Assert.Equal(expected, HtmlStructureBound.Apply(html).Html);
    }

    // The parser re-opens every unclosed <b>, <i>, <font>… in each block that follows: 500 of them
    // ahead of 2 000 paragraphs built a million elements out of 10 KB, in 25 s.
    [Fact]
    public void Apply_CutsWhereUnclosedFormattingWouldMultiply()
    {
        var (html, truncated) = HtmlStructureBound.Apply(Multiplying(500, "<p>x", 2000));

        Assert.True(truncated);
        Assert.InRange(Regex.Matches(html, "<p>x").Count, 1, 500);
    }

    [Theory]
    [InlineData("<b>", "<span>x</span>", "</b>", 5000)]
    [InlineData("<font color=\"red\"><b><i>", "<div><a href=\"https://a.example/\">x</a><br></div>", "</i></b></font>", 3000)]
    // Sloppy, and still a message: each unit leaves a <font> the parser will carry along.
    [InlineData("", "<font><p>a<p>b</font>", "", 100)]
    public void Apply_LeavesOrdinaryFormattingAlone(string before, string unit, string after, int count)
    {
        var source = before + string.Concat(Enumerable.Repeat(unit, count)) + after;

        var (html, truncated) = HtmlStructureBound.Apply(source);

        Assert.False(truncated);
        Assert.Equal(source, html);
    }

    // What the scan cannot see it cannot count: an end tag the parser ignores still closed a level
    // for the scan, so `<div></span>` nested without limit. The tree itself is the last word.
    [Fact]
    public void Sanitize_GivesUpOnATreeDeeperThanTheScanCounted()
    {
        var result = _sanitizer.Sanitize(string.Concat(Enumerable.Repeat("<div></span>", 6000)) + "x");

        Assert.True(result.Truncated);
        Assert.Empty(result.Html);
    }

    [Fact]
    public void Sanitize_GivesUpOnATreeLargerThanTheScanCounted()
    {
        var result = _sanitizer.Sanitize(Multiplying(100, "<p>x", 1000));

        Assert.True(result.Truncated);
        Assert.Empty(result.Html);
    }

    // Unwrapping the <button> leaves a paragraph inside a paragraph, which no parser builds from
    // text: written back and parsed again, the formatting multiplies on the second tree only.
    [Fact]
    public void Sanitize_BoundsATreeThatOnlyMultipliesOnceWrittenBack()
    {
        var result = _sanitizer.Sanitize(Multiplying(100, "<button><p>x</p></button>", 1000));

        Assert.True(result.Truncated);
        Assert.InRange(result.Html.Length, 0, 100_000);
    }

    private static string Multiplying(int formatting, string unit, int count)
    {
        var html = new StringBuilder("<p>");
        for (var i = 0; i < formatting; i++) html.Append("<b title=\"").Append(i).Append("\">");
        return html.Append(string.Concat(Enumerable.Repeat(unit, count))).ToString();
    }

    // A relative link resolves against the address of the page showing the message: the webmail's.
    [Theory]
    [InlineData("<a href=\"/settings/admin\">x</a>")]
    [InlineData("<a href=\"settings\">x</a>")]
    [InlineData("<a href=\"#top\">x</a>")]
    [InlineData("<a href=\"?uid=1\">x</a>")]
    public void Sanitize_DropsALinkThatNamesNoSite(string html)
    {
        var result = _sanitizer.Sanitize(html).Html;

        Assert.DoesNotContain("href", result);
        Assert.Contains(">x</a>", result);
    }

    [Theory]
    [InlineData("<a href=\"https://a.example/p?q=1\">x</a>", "href=\"https://a.example/p?q=1\"")]
    [InlineData("<a href=\"mailto:a@b.example\">x</a>", "href=\"mailto:a@b.example\"")]
    [InlineData("<a href=\"//a.example/p\">x</a>", "href=\"https://a.example/p\"")]
    public void Sanitize_KeepsALinkToASite(string html, string expected)
    {
        Assert.Contains(expected, _sanitizer.Sanitize(html).Html);
    }
}
