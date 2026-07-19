using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services
{
    public class MailHtmlSanitizer : IMailHtmlSanitizer
    {
        private const string BlockedSrcAttribute = "data-blocked-src";

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
                BlockedSrcAttribute
            }) _sanitizer.AllowedAttributes.Add(attribute);

            // Inline styles only, and only properties email clients actually honour. Anything
            // positional is excluded: it would let a message escape its container and overlay
            // the surrounding interface.
            _sanitizer.AllowedCssProperties.Clear();
            foreach (var property in new[]
            {
                "color", "background-color",
                "font-family", "font-size", "font-style", "font-weight",
                "text-align", "text-decoration",
                "margin", "margin-top", "margin-bottom", "margin-left", "margin-right",
                "padding", "padding-top", "padding-bottom", "padding-left", "padding-right",
                "border", "border-collapse", "border-color", "border-style", "border-width",
                "list-style-type", "line-height", "vertical-align"
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

            var cleaned = _sanitizer.Sanitize(html);

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
    }
}
