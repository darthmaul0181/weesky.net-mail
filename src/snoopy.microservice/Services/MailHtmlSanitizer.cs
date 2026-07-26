using System.Text.RegularExpressions;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

internal sealed class MailHtmlSanitizer : IMailHtmlSanitizer
{
    private const string BlockedSrcAttribute = "data-blocked-src";
    private const string BlockedBackgroundAttribute = "data-blocked-bg";

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

        // Unwrap pass first: a bpost mail wrapped its whole 62 KB body in one <center>, and the
        // sanitiser deletes a disallowed tag with its subtree, rendering the message empty.
        var pre = _parser.ParseDocument(html);
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
            Html = document.Body?.InnerHtml ?? string.Empty,
            BlockedImageCount = blocked
        };
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
