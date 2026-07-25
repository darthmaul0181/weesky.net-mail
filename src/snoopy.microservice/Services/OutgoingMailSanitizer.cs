using System.Text;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

internal sealed class OutgoingMailSanitizer : IOutgoingMailSanitizer
{
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "li", "tr", "blockquote", "h1", "h2", "h3", "h4", "h5", "h6", "pre", "table", "ul", "ol"
    };

    private readonly HtmlSanitizer _sanitizer;
    private readonly HtmlParser _parser = new();

    public OutgoingMailSanitizer()
    {
        // Ganss defaults are close to right for outgoing: scripts, handlers and bad schemes
        // go, styles stay. Only the scheme list is tightened.
        _sanitizer = new HtmlSanitizer();
        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("mailto");
        // cid: references an embedded part — no file access, no tracker. Without this, Ganss
        // strips the src attribute before the image cull below ever sees it (the first gate).
        _sanitizer.AllowedSchemes.Add("cid");
    }

    public OutgoingBody Prepare(string html)
    {
        var sanitized = _sanitizer.Sanitize(html ?? string.Empty);

        var document = _parser.ParseDocument($"<body>{sanitized}</body>");
        var body = document.Body!;

        // An image with no usable source is noise in the wire format; cid: is usable since 2c2b.
        foreach (var img in body.QuerySelectorAll("img").ToList())
        {
            var src = img.GetAttribute("src") ?? string.Empty;
            if (!IsAllowedImageSource(src)) img.Remove();
        }

        // Ganss's HtmlFormatter escapes < and > in attribute values; AngleSharp's default
        // (InnerHtml) does not, which would undo that hardening on the re-serialize.
        return new OutgoingBody(body.ChildNodes.ToHtml(HtmlFormatter.Instance), ExtractText(body));
    }

    // Ganss has already dropped every scheme but http, https, mailto and cid, so what is left to
    // reject is the schemeless src: relative to nothing once the message leaves us. cid is the
    // second gate — an inline part reference is a legitimate outgoing source since 2c2b.
    private static bool IsAllowedImageSource(string src) =>
        src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || src.StartsWith("cid:", StringComparison.OrdinalIgnoreCase);

    private static string ExtractText(IElement root)
    {
        var builder = new StringBuilder();
        Append(root, builder);
        var lines = builder.ToString().Split('\n').Select(l => l.Trim());
        return string.Join('\n', lines).Trim();
    }

    // Explicit stack, not recursion: ~7 000 nested elements — which a paste can carry and the
    // parser accepts — overflow the call stack, and that kills the process. null is a deferred
    // block boundary, popped once the element's children have been emitted.
    private static void Append(INode root, StringBuilder builder)
    {
        var pending = new Stack<INode?>();
        PushChildren(pending, root);

        while (pending.TryPop(out var node))
        {
            switch (node)
            {
                case null: builder.Append('\n'); break;
                case IText text: builder.Append(text.Data); break;
                case IElement element when element.TagName.Equals("BR", StringComparison.OrdinalIgnoreCase):
                    builder.Append('\n');
                    break;
                case IElement element:
                    if (BlockTags.Contains(element.TagName)) pending.Push(null);
                    PushChildren(pending, element);
                    break;
            }
        }
    }

    // Reversed, so the stack pops the children back in document order.
    private static void PushChildren(Stack<INode?> pending, INode node)
    {
        var children = node.ChildNodes;
        for (var i = children.Length - 1; i >= 0; i--) pending.Push(children[i]);
    }
}
