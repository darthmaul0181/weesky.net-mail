using System.Text;
using weesky.Scotty.Microservice.Models.Mail;

namespace weesky.Scotty.Microservice.Services;

/// <summary>
/// Bounds the shape of a message body on its raw text, before any parse: what the parser builds
/// is what <see cref="MailHtmlSanitizer"/> then pays for.
/// </summary>
internal static class HtmlStructureBound
{
    // Depth, capped on the raw text before the first parse, because AngleSharp's tree
    // construction is itself superlinear in it — measured here: 50 000 levels 6.5 s,
    // 100 000 levels 43.6 s — so a guard on the parsed tree has already paid the cost.
    private const int MaxNestingDepth = 1024;

    // Nodes, which is what the cost is actually proportional to and what a character ceiling does
    // not bound: 2M characters hold ~175 000 elements, measured between 22.7 s and 71.5 s, or
    // ~233 000 comments, measured at 55 s. Comments count because the parser builds a node for
    // each and removing them is quadratic in siblings — an element-only ceiling never saw them.
    // 20 000 is 18× the densest newsletter measured and ~3× the heaviest body plausible at all,
    // which is the margin a hard cut earns: past it the reader sees an amputated message.
    private const int MaxNodeCount = 20_000;

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

    /// <summary>
    /// One pre-parse pass over the raw text bounding both dimensions the pipeline's cost rides on.
    /// Tags nesting past <see cref="MaxNestingDepth"/> are elided and their content kept — rule
    /// 6(b)'s unwrap applied to depth — and the text is cut at the <see cref="MaxNodeCount"/>th
    /// node, degrading as the width ceiling does. Returns whether it cut, for
    /// <see cref="SanitizedHtml.Truncated"/>. What survives crosses the whole pipeline, so a
    /// miscount here costs layout, never safety.
    /// </summary>
    internal static (string Html, bool Truncated) Apply(string html)
    {
        StringBuilder? capped = null;
        var copied = 0;
        var depth = 0;
        var nodes = 0;
        var cut = -1;
        var i = 0;

        while (i < html.Length)
        {
            var open = html.IndexOf('<', i);
            if (open < 0) break;

            var (name, isEnd, end) = ReadTag(html, open);
            i = end;

            if (name.Length == 0)
            {
                // A comment or a declaration is a node the parser builds and no element ceiling
                // would ever see. `</1>` is one, and 233 000 of them measured 55 s where the same
                // count of elements measured 0.5 s. A stray '<' is text, and text merges.
                if (end > open + 1 && ++nodes > MaxNodeCount) { cut = open; break; }
                continue;
            }

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

            // Only what survives the depth cap reaches the parser, so only that is counted. An end
            // tag builds nothing of its own.
            if (!isEnd && ++nodes > MaxNodeCount)
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
    /// a declaration, a bogus comment or a stray <c>&lt;</c> — and the index just past the token.
    /// </summary>
    /// <remarks>
    /// Every boundary here is the tokeniser's, because the two must agree on where a name ends for
    /// every input, not merely for well-formed ones. Reading a name as shorter than the tokeniser
    /// does is what let <c>&lt;script_x&gt;</c> pass as a raw-text <c>script</c>, whose skip then
    /// swallowed the document the parser went on to build. The same shortening made
    /// <c>&lt;br_x&gt;</c> read as a void tag and <c>&lt;p_x&gt;</c> as a peer, both uncounted,
    /// and <c>&lt;/1&gt;</c> — a bogus comment — read as an end tag closing a level nothing opened.
    /// </remarks>
    private static (string Name, bool IsEnd, int End) ReadTag(string html, int open)
    {
        var i = open + 1;
        if (i >= html.Length) return (string.Empty, false, html.Length);

        if (html.AsSpan(i).StartsWith("!--")) return (string.Empty, false, EndOfComment(html, i + 3));

        // Doctype or processing instruction: consumed to the next '>', nothing emitted.
        if (html[i] is '!' or '?') return (string.Empty, false, EndOfDeclaration(html, i));

        var isEnd = html[i] == '/';
        if (isEnd) i++;

        // Only an ASCII letter opens a tag. Anything else after '<' is literal text, and after
        // '</' a bogus comment — neither reaches the tree, so neither may move the counters.
        if (i >= html.Length || !char.IsAsciiLetter(html[i]))
            return (string.Empty, false, isEnd ? EndOfDeclaration(html, i) : open + 1);

        var start = i;
        while (i < html.Length && !IsHtmlSpace(html[i]) && html[i] is not ('/' or '>')) i++;

        return (html[start..i], isEnd, EndOfTag(html, i));
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
            else if (!IsHtmlSpace(c)) afterEquals = false;
        }

        return html.Length;
    }

    // A comment closes on `-->` and on `--!>`, and the degenerate `<!-->` / `<!--->` on their own
    // '>'. Missing any of them would skip to the next `-->` — past markup the parser still builds.
    private static int EndOfComment(string html, int i)
    {
        if (i < html.Length && html[i] == '>') return i + 1;
        if (i + 1 < html.Length && html[i] == '-' && html[i + 1] == '>') return i + 2;

        for (var d = html.IndexOf("--", i, StringComparison.Ordinal); d >= 0;
             d = html.IndexOf("--", d + 1, StringComparison.Ordinal))
        {
            if (d + 2 < html.Length && html[d + 2] == '>') return d + 3;
            if (d + 3 < html.Length && html[d + 2] == '!' && html[d + 3] == '>') return d + 4;
        }

        return html.Length;
    }

    // A doctype, a processing instruction or a bogus comment: consumed to the next '>' with
    // nothing special inside it, quotes included.
    private static int EndOfDeclaration(string html, int i)
    {
        var close = html.IndexOf('>', i);
        return close < 0 ? html.Length : close + 1;
    }

    /// Stops on the element's own end tag rather than past it, so the caller still counts it down.
    /// Raw text closes only on <c>&lt;/name</c> followed by whitespace, '/' or '>', so
    /// <c>&lt;/scriptx&gt;</c> does not close a script: stopping there would read the rest of the
    /// script's text as markup, and a sender could spend `&lt;/div&gt;` in it to cancel real depth.
    private static int SkipRawText(string html, string name, int from)
    {
        var close = $"</{name}";

        for (var i = html.IndexOf(close, from, StringComparison.OrdinalIgnoreCase); i >= 0;
             i = html.IndexOf(close, i + 2, StringComparison.OrdinalIgnoreCase))
        {
            var after = i + close.Length;
            if (after >= html.Length || IsHtmlSpace(html[after]) || html[after] is '/' or '>') return i;
        }

        return html.Length;
    }

    // The tokeniser's whitespace, not Unicode's: a no-break space does not separate attributes,
    // so treating it as one would let a quote open a value where the tokeniser reads a character.
    private static bool IsHtmlSpace(char c) => c is '\t' or '\n' or '\f' or '\r' or ' ';
}
