using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

internal sealed class MailHtmlSanitizer : IMailHtmlSanitizer
{
    private const string BlockedSrcAttribute = "data-blocked-src";

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
            "a", "img",
            "table", "thead", "tbody", "tfoot", "tr", "td", "th", "caption"
        }) _sanitizer.AllowedTags.Add(tag);

        _sanitizer.AllowedAttributes.Clear();
        foreach (var attribute in new[]
        {
            "href", "src", "alt", "title", "style",
            "colspan", "rowspan", "align", "valign", "width", "height",
            "cellpadding", "cellspacing", "border", "bgcolor", "dir",
            BlockedSrcAttribute
        }) _sanitizer.AllowedAttributes.Add(attribute);

        // Inline styles only. Positional properties stay excluded (position, z-index, float):
        // even sandboxed, a message overlaying itself invites phishing. Dimension and shape are
        // what table-based mail layouts ride on — dropping them collapsed real messages.
        _sanitizer.AllowedCssProperties.Clear();
        foreach (var property in new[]
        {
            "color", "background", "background-color",
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

        var cleaned = _sanitizer.Sanitize(pre.Body?.InnerHtml ?? string.Empty);

        // Second pass on the already-sanitised markup, using the same parser the sanitiser
        // uses so the two cannot disagree about the tree.
        var document = _parser.ParseDocument(cleaned);

        var blocked = 0;
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
