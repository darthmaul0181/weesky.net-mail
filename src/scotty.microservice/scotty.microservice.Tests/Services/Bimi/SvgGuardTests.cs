using System.Text;
using weesky.Scotty.Microservice.Services.Bimi;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services.Bimi;

public sealed class SvgGuardTests
{
    private const string Open = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\">";

    private static bool IsSafe(string svg) => SvgGuard.IsSafe(Encoding.UTF8.GetBytes(svg));

    [Theory]
    [InlineData("<path d=\"M0 0L1 1\"/>")]
    [InlineData("<defs><linearGradient id=\"grad\"/></defs><rect fill=\"url(#grad)\"/>")]
    [InlineData("<rect xlink:href=\"#grad\"/>")]
    [InlineData("<use href=\"#a\"/>")]
    [InlineData("<style>.a{fill:red}</style>")]
    public void Plain_content_is_safe(string body) => Assert.True(IsSafe(Open + body + "</svg>"));

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<foreignObject><div/></foreignObject>")]
    [InlineData("<image href=\"http://evil.example/a.png\"/>")]
    [InlineData("<use href=\"http://evil.example/a.svg#x\"/>")]
    [InlineData("<animate attributeName=\"href\"/>")]
    [InlineData("<set attributeName=\"href\"/>")]
    [InlineData("<a href=\"javascript:alert(1)\"/>")]
    [InlineData("<rect xlink:href=\"https://evil.example/\"/>")]
    [InlineData("<style>@import 'x.css';</style>")]
    [InlineData("<style>.a{fill:url(http://evil.example/x)}</style>")]
    [InlineData("<style>.a{fill:URL(http://evil.example/x)}</style>")]
    [InlineData("<rect onload=\"alert(1)\"/>")]
    public void Active_or_external_content_is_unsafe(string body) => Assert.False(IsSafe(Open + body + "</svg>"));

    [Theory]
    [InlineData("<!DOCTYPE svg><svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("<!DOCTYPE svg [<!ENTITY x \"y\">]><svg xmlns=\"http://www.w3.org/2000/svg\">&x;</svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><path></svg>")]
    [InlineData("")]
    public void A_doctype_or_malformed_xml_is_unsafe(string svg) => Assert.False(IsSafe(svg));

    [Theory]
    [InlineData("<style>@im<!---->port \"http://e/x.css\";</style>")]
    [InlineData("<style>.a{fill:u<![CDATA[rl(http://e/y)]]>}</style>")]
    [InlineData("<style><g/>@import \"http://e/z.css\";</style>")]
    [InlineData("<STYLE>@import 'x.css';</STYLE>")]
    [InlineData("<rect style=\"fill:u&#x5c;72l(http://e)\"/>")]
    [InlineData("<rect style=\"fill:url(http://e)\"/>")]
    public void Style_tricks_are_unsafe(string body) => Assert.False(IsSafe(Open + body + "</svg>"));

    [Fact]
    public void A_processing_instruction_is_unsafe() =>
        Assert.False(IsSafe("<?xml-stylesheet href=\"http://evil/x.css\"?>" + Open + "</svg>"));

    [Fact]
    public void A_style_split_by_a_comment_is_unsafe() =>
        Assert.False(IsSafe(Open + "<style>@im<!---->port \"http://e/x.css\"; .a{fill:u<![CDATA[rl(http://e/y)]]>}</style></svg>"));

    [Fact]
    public void Empty_bytes_are_unsafe() => Assert.False(SvgGuard.IsSafe([]));

    [Theory]
    [InlineData("<pattern id=\"p\"/>")]
    [InlineData("<mask id=\"m\"/>")]
    [InlineData("<clipPath id=\"c\"/>")]
    [InlineData("<filter id=\"f\"><feGaussianBlur stdDeviation=\"9\"/></filter>")]
    [InlineData("<marker id=\"k\"/>")]
    [InlineData("<text>Brand</text>")]
    [InlineData("<symbol id=\"s\"/>")]
    [InlineData("<PATTERN id=\"p\"/>")]
    [InlineData("<a href=\"#x\"/>")]
    public void Elements_outside_the_logo_profile_are_unsafe(string body) => Assert.False(IsSafe(Open + body + "</svg>"));

    [Theory]
    [MemberData(nameof(PaintServerAmplifications))]
    public void Paint_server_amplification_is_refused_quickly(string svg)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.False(IsSafe(svg));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"took {watch.Elapsed}");
    }

    [Theory]
    [MemberData(nameof(DashAmplifications))]
    public void Dashes_are_refused_quickly(string svg)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.False(IsSafe(svg));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"took {watch.Elapsed}");
    }

    public static TheoryData<string> DashAmplifications()
    {
        static string Repeat(string element) => string.Concat(Enumerable.Repeat(element, 1000));
        return new TheoryData<string>
        {
            Open + Repeat("<line x2=\"100\" y2=\"100\" stroke=\"#000\" stroke-width=\"0.0001\" stroke-dasharray=\"0.0002\"/>") + "</svg>",
            Open + "<style>*{stroke:#000;stroke-dasharray:0.0002;stroke-width:0.0001}</style>" + Repeat("<line x2=\"100\" y2=\"100\"/>") + "</svg>",
            Open + "<style>LINE{STROKE-DASHARRAY:1}</style><line x2=\"9\"/></svg>",
            Open + "<line x2=\"9\" style=\"stroke:#000;Stroke-DashArray:1\"/></svg>",
            Open + "<line x2=\"9\" STROKE-DASHARRAY=\"1\"/></svg>",
            Open + "<g style=\"stroke:#000;stroke-width:.0001;stroke-dash/**/array:.0002\">" + string.Concat(Enumerable.Repeat("<line x2=\"100\" y2=\"100\"/>", 1150)) + "</g></svg>",
            Open + string.Concat(Enumerable.Repeat("<line x2=\"100\" y2=\"100\" style=\"stroke:#000;stroke-width:.0001;stroke-dash/**/array:.0002\"/>", 300)) + "</svg>",
        };
    }

    [Theory]
    [InlineData("<path d=\"M0 0L9 9\" style=\"fill:#000;stroke:none;stroke-dasharray:none\"/>")]
    [InlineData("<path d=\"M0 0L9 9\" style=\"stroke-dasharray: NONE ;stroke-opacity:1\"/>")]
    [InlineData("<path d=\"M0 0L9 9\" stroke-dasharray=\"none\"/>")]
    [InlineData("<path d=\"M0 0L9 9\" stroke-dasharray=\" None \"/>")]
    [InlineData("<style>.st0{fill:#000;stroke-dasharray:none}</style>")]
    public void A_dash_array_of_none_is_safe(string body) => Assert.True(IsSafe(Open + body + "</svg>"));

    [Theory]
    [InlineData("<path d=\"M0 0L9 9\" style=\"stroke-dasharray:none;stroke-dasharray:0.0002\"/>")]
    [InlineData("<path d=\"M0 0L9 9\" style=\"stroke-dasharray:none,0.0002\"/>")]
    [InlineData("<path d=\"M0 0L9 9\" style=\"stroke-dasharray:none !important\"/>")]
    [InlineData("<path d=\"M0 0L9 9\" stroke-dasharray=\"0.0002\"/>")]
    [InlineData("<path d=\"M0 0L9 9\" stroke-dasharray=\"none;0.0002\"/>")]
    [InlineData("<style>.st0{stroke-dasharray:none;stroke-dasharray:1}</style>")]
    [InlineData("<path d=\"M0 0L9 9\" style=\"fill:#000/* a comment */\"/>")]
    public void Any_other_dash_array_or_a_comment_in_an_attribute_is_unsafe(string body) => Assert.False(IsSafe(Open + body + "</svg>"));

    [Theory]
    [InlineData("<style>.cls-1,.cls-2{fill:#c00}#a{fill:red}*{opacity:.9}rect{fill:blue}</style>")]
    [InlineData("<style>\n  .st0 { fill: #FFFFFF; }\n  /* brand */ .st1 { fill: #000 }\n</style>")]
    public void Flat_stylesheets_are_safe(string body) => Assert.True(IsSafe(Open + body + "</svg>"));

    [Theory]
    [InlineData("<style>g rect{fill:red}</style>")]
    [InlineData("<style>g>rect{fill:red}</style>")]
    [InlineData("<style>g+rect{fill:red}</style>")]
    [InlineData("<style>g~rect{fill:red}</style>")]
    [InlineData("<style>rect:not(.a){fill:red}</style>")]
    [InlineData("<style>rect[width]{fill:red}</style>")]
    [InlineData("<style>@media screen{rect{fill:red}}</style>")]
    [InlineData("<style>.a{fill:red}/* open</style>")]
    [InlineData("<style>.a{fill:red</style>")]
    public void Combinators_pseudo_classes_and_at_rules_are_unsafe(string body) => Assert.False(IsSafe(Open + body + "</svg>"));

    [Fact]
    public void Backtracking_selectors_over_a_deep_tree_are_refused_quickly()
    {
        var rules = string.Concat(Enumerable.Repeat("g g g g g g g g:not(rect) *{opacity:.99}", 300));
        var svg = Open + $"<style>{rules}</style>" + string.Concat(Enumerable.Repeat("<g>", 44))
            + string.Concat(Enumerable.Repeat("<rect width=\"9\" height=\"9\"/>", 300)) + string.Concat(Enumerable.Repeat("</g>", 44)) + "</svg>";
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.False(IsSafe(svg));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"took {watch.Elapsed}");
    }

    public static TheoryData<string> PaintServerAmplifications()
    {
        static string Repeat(string element, int count) => string.Concat(Enumerable.Repeat(element, count));
        var circles = Repeat("<circle r=\"1\"/>", 1000);
        return new TheoryData<string>
        {
            Open + $"<defs><rect id=\"r\" width=\"9\" height=\"9\"/><pattern id=\"p\" width=\"1\" height=\"1\">{circles}</pattern></defs>"
                + $"<g fill=\"url(#p)\">{Repeat("<use href=\"#r\"/>", 1000)}</g></svg>",
            Open + $"<defs><pattern id=\"s\" width=\"1\" height=\"1\">{Repeat("<circle r=\"1\"/>", 300)}</pattern>"
                + $"<pattern id=\"q\" width=\"1\" height=\"1\"><g fill=\"url(#s)\">{Repeat("<rect width=\"1\" height=\"1\"/>", 300)}</g></pattern>"
                + $"<pattern id=\"p\" width=\"1\" height=\"1\"><g fill=\"url(#q)\">{Repeat("<rect width=\"1\" height=\"1\"/>", 300)}</g></pattern></defs>"
                + $"<g fill=\"url(#p)\">{Repeat("<rect width=\"100\" height=\"100\"/>", 300)}</g></svg>",
            Open + $"<defs><pattern id=\"p\" width=\"1\" height=\"1\">{circles}</pattern></defs>"
                + $"<g fill=\"url(#p)\">{Repeat("<rect width=\"100\" height=\"100\"/>", 1000)}</g></svg>",
        };
    }
}
