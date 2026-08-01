using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

internal sealed class MailHtmlSanitizer : IMailHtmlSanitizer
{
    private const string BlockedSrcAttribute = "data-blocked-src";
    private const string BlockedBackgroundAttribute = "data-blocked-bg";

    // Three ceilings, one per dimension the pipeline's cost rides on. It builds three DOM trees
    // plus two serialisations over bodies ImapSession takes straight off the wire, and each
    // dimension alone reaches minutes of CPU per message. All three are far above real mail: the
    // densest newsletter measured here is 90 KB, ~1 100 elements, nested 8 deep, in 133 ms.

    // Characters. Halved from 2M once the element ceiling below landed: with elements bounded,
    // this became the binding constraint on the worst case — 2M characters of gradient-bearing
    // style attributes measured 1.69 s, and no legitimate body approaches even a tenth of it.
    internal const int MaxInputLength = 1024 * 1024;

    // Depth, capped on the raw text before the first parse, because AngleSharp's tree
    // construction is itself superlinear in it — measured here: 50 000 levels 6.5 s,
    // 100 000 levels 43.6 s — so a guard on the parsed tree has already paid the cost.
    private const int MaxNestingDepth = 1024;

    // Elements, which is what the cost is actually proportional to and what a character ceiling
    // does not bound: 2M characters still hold ~175 000 of them, measured between 22.7 s and
    // 71.5 s. Every pass here is per-element, so this is the ceiling that sets the worst case.
    private const int MaxElementCount = 10_000;

    // Every serialisation AngleSharp can hand us: quoted either way, or bare.
    private static readonly Regex CssUrl = new(
        @"url\(\s*(?:""(?<u>[^""]*)""|'(?<u>[^']*)'|(?<u>[^)\s]*))\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Containers whose inner text is not rendered content — dropped with their subtree, while
    // every other disallowed tag is unwrapped. DOMPurify's FORBID_CONTENTS draws the same line.
    private static readonly HashSet<string> DropWithContent = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "title", "head", "template", "textarea", "select", "option",
        "iframe", "frame", "frameset", "object", "embed", "applet",
        "noscript", "noembed", "noframes", "xmp", "plaintext", "listing",
        "svg", "math", "annotation-xml", "mi", "mn", "mo", "ms", "mtext", "foreignobject", "desc",
        "audio", "video", "colgroup"
    };

    // Void elements open no level. Every other start tag does, `<div/>` included: HTML has no
    // self-closing syntax outside this list, so honouring a trailing slash would be a way past
    // the cap uncounted.
    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr"
    };

    // Their content is text, not markup: `if (a<b)` inside a script must not read as nesting.
    private static readonly HashSet<string> RawTextTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "textarea", "title", "xmp", "iframe", "noembed", "noframes"
    };

    // These close a peer instead of nesting under it, so a counter that has no tree cannot tell
    // <p>a<p>b from two levels. None can appear without a counted ancestor between two of them —
    // a td needs a table — so ignoring them undercounts real depth by a bounded factor only.
    private static readonly HashSet<string> PeerTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "li", "td", "th", "tr", "dt", "dd", "option"
    };

    private readonly HtmlSanitizer _sanitizer;
    private readonly HtmlParser _parser = new();

    public MailHtmlSanitizer()
    {
        _sanitizer = new HtmlSanitizer();

        _sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
        {
            "p", "br", "hr", "div", "span",
            "strong", "b", "em", "i", "u", "s", "sub", "sup",
            "h1", "h2", "h3", "h4", "h5", "h6",
            "ul", "ol", "li", "blockquote", "pre", "code",
            "a", "img", "center", "font",
            "table", "thead", "tbody", "tfoot", "tr", "td", "th", "caption"
        }) _sanitizer.AllowedTags.Add(tag);

        _sanitizer.AllowedAttributes.Clear();
        foreach (var attribute in new[]
        {
            "href", "src", "alt", "title", "style",
            "colspan", "rowspan", "align", "valign", "width", "height",
            "cellpadding", "cellspacing", "border", "bgcolor", "dir", "face", "size", "color"
        }) _sanitizer.AllowedAttributes.Add(attribute);
        // data-blocked-src / -bg are deliberately absent: both are written by our own post-Ganss
        // pass, and allowing them would let a message forge withheld images the banner then counts.

        // Inline styles only. Positional properties stay excluded (position, z-index, float):
        // even sandboxed, a message overlaying itself invites phishing. Dimension and shape are
        // what table-based mail layouts ride on — dropping them collapsed real messages.
        _sanitizer.AllowedCssProperties.Clear();
        foreach (var property in new[]
        {
            "color", "background", "background-color", "background-image",
            // The -x/-y longhands are rule 6(a) again: AngleSharp expands both shorthands into
            // them, so the name alone lets the position and the repeat fall out of a restored
            // background — the logo painting at 0% 0% and tiling.
            "background-repeat", "background-repeat-x", "background-repeat-y",
            "background-position", "background-position-x", "background-position-y",
            "background-size",
            "font", "font-family", "font-size", "font-style", "font-weight",
            "text-align", "text-decoration", "text-decoration-line", "text-decoration-style",
            "text-decoration-color", "text-transform", "letter-spacing", "white-space",
            "word-break", "overflow-wrap", "direction",
            "margin", "margin-top", "margin-bottom", "margin-left", "margin-right",
            "padding", "padding-top", "padding-bottom", "padding-left", "padding-right",
            "border", "border-collapse", "border-color", "border-style", "border-width",
            "border-top", "border-right", "border-bottom", "border-left",
            "border-radius", "border-top-left-radius", "border-top-right-radius",
            "border-bottom-left-radius", "border-bottom-right-radius",
            "border-spacing", "table-layout", "box-sizing",
            "width", "min-width", "max-width", "height", "min-height", "max-height",
            "display", "list-style-type", "line-height", "vertical-align"
        }) _sanitizer.AllowedCssProperties.Add(property);

        // Per-side longhands: real mail writes them directly, and the parser also expands the
        // allowed shorthands into them. A name allowlist must carry both forms or drop both.
        foreach (var side in new[] { "top", "right", "bottom", "left" })
            foreach (var aspect in new[] { "width", "style", "color" })
                _sanitizer.AllowedCssProperties.Add($"border-{side}-{aspect}");

        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("mailto");
        _sanitizer.AllowedSchemes.Add("cid");
    }

    public SanitizedHtml Sanitize(string html)
    {
        if (string.IsNullOrEmpty(html)) return new SanitizedHtml();

        // The cut happens before any parse so the kept part crosses the whole pipeline; a cut
        // after would let truncation reopen what the passes had closed.
        var truncated = html.Length > MaxInputLength;
        if (truncated)
            html = html[..(char.IsHighSurrogate(html[MaxInputLength - 1]) ? MaxInputLength - 1 : MaxInputLength)];

        // The structural bound runs after the width cut, never before: the cut is what bounds the
        // text it walks, and a cut landing mid-tag ends its scan exactly where it ends the
        // parser's own tokenisation — both give up on the same unterminated tag.
        var (bounded, tooManyElements) = BoundStructure(html);
        truncated |= tooManyElements;

        // Unwrap pass first: a bpost mail wrapped its whole 62 KB body in one <center>, and the
        // sanitiser deletes a disallowed tag with its subtree, rendering the message empty.
        var pre = _parser.ParseDocument(bounded);
        UnwrapDisallowedTags(pre.Body!);
        CullEscapedDeclarations(pre.Body!);

        var cleaned = _sanitizer.Sanitize(pre.Body?.InnerHtml ?? string.Empty);

        // Second pass on the already-sanitised markup, using the same parser the sanitiser
        // uses so the two cannot disagree about the tree.
        var document = _parser.ParseDocument(cleaned);

        var blocked = 0;

        // A url() outside background-image is culled by value: it would fetch without consent.
        // A background-image one is withheld instead, like an <img src>, so the reader can
        // restore it on demand.
        foreach (var styled in document.QuerySelectorAll("[style]"))
        {
            var style = styled.GetAttribute("style")!;
            if (!style.Contains("url(", StringComparison.OrdinalIgnoreCase) && !style.Contains('\\')) continue;

            // A style we cannot tokenise is a style we cannot vet: dropping it loses a background,
            // keeping it risks leaving a fetch behind.
            if (!TrySplitTopLevel(style, ';', out var declarations))
            {
                styled.SetAttribute("style", string.Empty);
                continue;
            }

            var withheld = new List<string>();
            var kept = new List<string>();

            foreach (var declaration in declarations)
            {
                if (declaration.Contains('\\')) continue;
                if (!declaration.Contains("url(", StringComparison.OrdinalIgnoreCase))
                {
                    kept.Add(declaration);
                    continue;
                }
                if (!IsBackgroundImage(declaration)) continue;

                var remaining = WithholdRemoteLayers(declaration, withheld);
                if (remaining != null) kept.Add(remaining);
            }

            styled.SetAttribute("style", string.Join(';', kept));
            if (withheld.Count > 0)
            {
                styled.SetAttribute(BlockedBackgroundAttribute, string.Join(' ', withheld));
                blocked += withheld.Count;
            }
        }

        foreach (var image in document.QuerySelectorAll("img"))
        {
            var src = image.GetAttribute("src");
            if (string.IsNullOrEmpty(src) || src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)) continue;

            image.SetAttribute(BlockedSrcAttribute, src);
            image.RemoveAttribute("src");
            blocked++;
        }

        foreach (var link in document.QuerySelectorAll("a[href]"))
        {
            link.SetAttribute("target", "_blank");
            link.SetAttribute("rel", "noopener noreferrer");
        }

        return new SanitizedHtml
        {
            // Ganss's formatter escapes < and > in attribute values; AngleSharp's default
            // (InnerHtml) does not, which would undo that hardening on this last serialise.
            Html = document.Body is { } body ? body.ChildNodes.ToHtml(HtmlFormatter.Instance) : string.Empty,
            BlockedImageCount = blocked,
            Truncated = truncated
        };
    }

    /// <summary>
    /// One pre-parse pass over the raw text bounding both dimensions the pipeline's cost rides on.
    /// Tags nesting past <see cref="MaxNestingDepth"/> are elided and their content kept — rule
    /// 6(b)'s unwrap applied to depth — and the text is cut at the <see cref="MaxElementCount"/>th
    /// element, degrading as the width ceiling does. Returns whether it cut, for
    /// <see cref="SanitizedHtml.Truncated"/>. What survives crosses the whole pipeline, so a
    /// miscount here costs layout, never safety.
    /// </summary>
    private static (string Html, bool Truncated) BoundStructure(string html)
    {
        StringBuilder? capped = null;
        var copied = 0;
        var depth = 0;
        var elements = 0;
        var cut = -1;
        var i = 0;

        while (i < html.Length)
        {
            var open = html.IndexOf('<', i);
            if (open < 0) break;

            var (name, isEnd, end) = ReadTag(html, open);
            i = end;
            if (name.Length == 0) continue;

            var opensLevel = !VoidTags.Contains(name) && !PeerTags.Contains(name);
            var elide = opensLevel
                && (isEnd ? depth > 0 && depth-- > MaxNestingDepth : ++depth > MaxNestingDepth);

            // A raw-text element goes with its content or not at all: eliding only its start tag
            // would spill a script's source as visible text, the leak DropWithContent prevents.
            if (!isEnd && RawTextTags.Contains(name)) i = SkipRawText(html, name, end);

            if (elide)
            {
                capped ??= new StringBuilder(html.Length);
                capped.Append(html, copied, open - copied);
                copied = i;
                continue;
            }

            // Only what survives the depth cap reaches the parser, so only that is counted.
            if (!isEnd && ++elements > MaxElementCount)
            {
                cut = open;
                break;
            }
        }

        var stop = cut < 0 ? html.Length : cut;
        return (capped is null ? html[..stop] : capped.Append(html, copied, stop - copied).ToString(),
            cut >= 0);
    }

    /// <summary>
    /// Reads the tag opening at <paramref name="open"/>, returning its name — empty for a comment,
    /// a doctype or a stray <c>&lt;</c> — and the index just past the token.
    /// </summary>
    private static (string Name, bool IsEnd, int End) ReadTag(string html, int open)
    {
        var i = open + 1;
        if (i >= html.Length) return (string.Empty, false, html.Length);

        if (html.AsSpan(i).StartsWith("!--"))
        {
            var close = html.IndexOf("-->", i, StringComparison.Ordinal);
            return (string.Empty, false, close < 0 ? html.Length : close + 3);
        }

        if (html[i] is '!' or '?') return (string.Empty, false, EndOfTag(html, i));

        var isEnd = html[i] == '/';
        if (isEnd) i++;

        var start = i;
        while (i < html.Length && (char.IsLetterOrDigit(html[i]) || html[i] == '-')) i++;

        return i == start
            ? (string.Empty, false, open + 1)
            : (html[start..i], isEnd, EndOfTag(html, i));
    }

    /// Index just past the '>' closing the tag, skipping quoted attribute values. A quote opens
    /// one only right after '=', as in HTML tokenisation: reading a stray apostrophe as a
    /// delimiter would let one tag swallow the document, and its nesting with it.
    private static int EndOfTag(string html, int i)
    {
        var quote = '\0';
        var afterEquals = false;

        for (; i < html.Length; i++)
        {
            var c = html[i];

            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }

            if (afterEquals && c is '"' or '\'')
            {
                quote = c;
                afterEquals = false;
                continue;
            }

            if (c == '>') return i + 1;
            if (c == '=') afterEquals = true;
            else if (!char.IsWhiteSpace(c)) afterEquals = false;
        }

        return html.Length;
    }

    // Stops on the closing tag rather than past it, so the caller still counts it down.
    private static int SkipRawText(string html, string name, int from)
    {
        var close = html.IndexOf($"</{name}", from, StringComparison.OrdinalIgnoreCase);
        return close < 0 ? html.Length : close;
    }

    // The CSS parser resolves escapes before the withholding pass sees them, so `\75 rl(` would
    // reach it spelled plainly and be treated as a genuine url(). Dropping such a declaration
    // while its source is still readable keeps the escape route closed whatever the parser does.
    // This is wider than it needs to be — `font-family: \41 rial` was rendering before and no
    // longer does — which the constraint accepts: an escape is rare in mail, a leak is not.
    private static void CullEscapedDeclarations(AngleSharp.Dom.IElement root)
    {
        foreach (var styled in root.QuerySelectorAll("[style]"))
        {
            var style = styled.GetAttribute("style")!;
            if (!style.Contains('\\')) continue;

            styled.SetAttribute("style", TrySplitTopLevel(style, ';', out var declarations)
                ? string.Join(';', declarations.Where(d => !d.Contains('\\')))
                : string.Empty);
        }
    }

    private static bool IsBackgroundImage(string declaration)
    {
        var colon = declaration.IndexOf(':');
        return colon > 0
            && declaration[..colon].Trim().Equals("background-image", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Moves each http(s) layer of a background-image into <paramref name="withheld"/> and returns
    /// what is left of the declaration — gradients and cid layers — or null when nothing remains.
    /// A layer whose scheme is neither is dropped: nothing to restore is nothing to validate later.
    /// </summary>
    private static string? WithholdRemoteLayers(string declaration, List<string> withheld)
    {
        var colon = declaration.IndexOf(':');
        if (!TrySplitTopLevel(declaration[(colon + 1)..], ',', out var layers)) return null;

        var kept = new List<string>();

        foreach (var layer in layers)
        {
            var urls = CssUrl.Matches(layer).Select(m => m.Groups["u"].Value.Trim()).ToList();

            // A url( the pattern could not read is a layer we cannot account for; keeping it
            // would leave an unvetted fetch in the CSS.
            if (urls.Count != CountUrlFunctions(layer)) continue;

            // Only a layer whose every url() stays inside the message may be kept verbatim.
            if (urls.All(url => url.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)))
            {
                kept.Add(layer.Trim());
                continue;
            }

            // AbsoluteUri, not the raw text: a quoted url() can carry a raw space, which the
            // space-separated attribute would read back as two URLs.
            foreach (var url in urls)
                if (Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                    && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
                    withheld.Add(parsed.AbsoluteUri);
        }

        return kept.Count == 0 ? null : $"{declaration[..colon]}:{string.Join(", ", kept)}";
    }

    private static int CountUrlFunctions(string value)
    {
        var count = 0;
        for (var i = value.IndexOf("url(", StringComparison.OrdinalIgnoreCase); i >= 0;
             i = value.IndexOf("url(", i + 4, StringComparison.OrdinalIgnoreCase)) count++;
        return count;
    }

    /// <summary>
    /// Splits on <paramref name="separator"/> at top level: one inside a CSS string, inside url(…)
    /// or inside a gradient's parentheses separates nothing. Returns false when the value cannot be
    /// tokenised — an unclosed string or unbalanced parentheses — which the caller must cull rather
    /// than guess at, since both are how a URL smuggles a separator past a naive reader.
    /// </summary>
    private static bool TrySplitTopLevel(string value, char separator, out List<string> parts)
    {
        parts = [];
        var depth = 0;
        var quote = '\0';
        var start = 0;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
            }
            else if (c is '"' or '\'') quote = c;
            else if (c == '(') depth++;
            else if (c == ')' && --depth < 0) return false;
            else if (c == separator && depth == 0)
            {
                parts.Add(value[start..i]);
                start = i + 1;
            }
        }

        if (quote != '\0' || depth != 0) return false;

        parts.Add(value[start..]);
        return true;
    }

    // Reverse document order, so children are handled before their parent is unwrapped.
    private void UnwrapDisallowedTags(AngleSharp.Dom.IElement root)
    {
        foreach (var element in root.QuerySelectorAll("*").Reverse())
        {
            if (element.Parent is not { } parent) continue;
            if (DropWithContent.Contains(element.LocalName))
            {
                element.Remove();
            }
            else if (!_sanitizer.AllowedTags.Contains(element.LocalName))
            {
                while (element.FirstChild is { } child) parent.InsertBefore(child, element);
                element.Remove();
            }
        }
    }
}
