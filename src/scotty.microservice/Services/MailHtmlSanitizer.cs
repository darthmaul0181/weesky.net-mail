using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;
using weesky.Scotty.Microservice.Models.Mail;

namespace weesky.Scotty.Microservice.Services;

internal sealed class MailHtmlSanitizer : IMailHtmlSanitizer
{
    private const string BlockedSrcAttribute = "data-blocked-src";
    private const string BlockedBackgroundAttribute = "data-blocked-bg";
    private const string BreakWordAttribute = "data-break-word";

    // Four ceilings, one per dimension the pipeline's cost rides on. It builds three DOM trees
    // plus two serialisations over bodies ImapSession takes straight off the wire, and each
    // dimension alone reaches minutes of CPU per message. All four are far above real mail: the
    // densest newsletter measured here is 90 KB, ~1 100 elements, nested 8 deep, in 133 ms.
    // Characters and stylesheets are held here, depth and nodes in HtmlStructureBound.

    // Characters. Left where it was found: halving it bought 1.85 s of worst case down to 1.04 s
    // and in exchange cut bodies between 1 and 2M that had always rendered whole. Truncating a
    // real message is the worse failure, and the element ceiling below is the cheaper lever.
    internal const int MaxInputLength = 2 * 1024 * 1024;

    // Stylesheets, which the other three do not bound: each is parsed three more times by
    // AngleSharp.Css, a recursive descent parser. Past ~2 000 nested blocks it overflows the
    // stack, which kills the process rather than throwing — so nesting is capped on the raw text,
    // before any CSS parse. Size and count because 2 MB of CSS measured 13-16 s and 19 000
    // `<style media>` 9.4 s; a real newsletter carries a few sheets, a few tens of KB, 3 deep.
    private const int MaxStyleSheetNesting = 16;
    private const int MaxStyleSheetLength = 256 * 1024;
    private const int MaxStyleSheets = 64;

    // Every serialisation AngleSharp can hand us: quoted either way, or bare.
    private static readonly Regex CssUrl = new(
        @"url\(\s*(?:""(?<u>[^""]*)""|'(?<u>[^']*)'|(?<u>[^)\s]*))\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // What a media query list is spelled with. No brace, semicolon, quote, backslash or asterisk
    // — so no comment opener either: any of them could end the prelude or hide what follows.
    private static readonly Regex MediaQueryList = new(
        @"^[a-z0-9\s(),:.\-/]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Containers whose inner text is not rendered content — dropped with their subtree, while
    // every other disallowed tag is unwrapped. DOMPurify's FORBID_CONTENTS draws the same line.
    private static readonly HashSet<string> DropWithContent = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "title", "head", "template", "textarea", "select", "option",
        "iframe", "frame", "frameset", "object", "embed", "applet",
        "noscript", "noembed", "noframes", "xmp", "plaintext", "listing",
        "svg", "math", "annotation-xml", "mi", "mn", "mo", "ms", "mtext", "foreignobject", "desc",
        "audio", "video", "colgroup"
    };

    // `prefers-color-scheme: dark` is excluded by the look-behind: reacting to a client is not
    // declaring a design. The value is one to three keywords; left unbounded, the scan after
    // each `color-scheme:` of a hostile sheet ran to its end, 110 s over 1.5 MB.
    private static readonly Regex DarkSchemeDeclaration = new(
        @"(?<![\w-])(?:supported-color-schemes|color-scheme)\s*:[^;}]{0,64}\bdark\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BreakWordDeclaration = new(
        @"(?:^|;)\s*word-break\s*:\s*break-word\s*(?:!\s*important\s*)?(?:;|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HtmlSanitizer _sanitizer;
    private readonly HtmlParser _parser = new();
    private readonly CssParser _cssParser = new();

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
            "table", "thead", "tbody", "tfoot", "tr", "td", "th", "caption", "style"
        }) _sanitizer.AllowedTags.Add(tag);

        _sanitizer.AllowedAttributes.Clear();
        foreach (var attribute in new[]
        {
            "href", "src", "alt", "title", "style", "class", "id",
            "colspan", "rowspan", "align", "valign", "width", "height",
            "cellpadding", "cellspacing", "border", "bgcolor", "dir", "face", "size", "color",
            // Written by MarkBreakWord; a message bringing its own only lets its text wrap.
            BreakWordAttribute
        }) _sanitizer.AllowedAttributes.Add(attribute);
        // data-blocked-src / -bg are deliberately absent: both are written by our own post-Ganss
        // pass, and allowing them would let a message forge withheld images the banner then counts.

        // Inline styles and sheet style rules alike (not @font-face descriptors: VetStyleSheets
        // judges their src). Positional properties stay excluded (position, z-index, float): even
        // sandboxed, a message overlaying itself invites phishing. Dimension and shape are what
        // table-based mail layouts ride on — dropping them collapsed real messages.
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

        // A rule is filtered by the same property allowlist as an inline style; only the two
        // at-rules a mail needs survive — @import would fetch a sheet nobody here has vetted.
        _sanitizer.AllowedAtRules.Clear();
        foreach (var rule in new[] { CssRuleType.Style, CssRuleType.Media, CssRuleType.FontFace })
            _sanitizer.AllowedAtRules.Add(rule);

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
        var (bounded, tooManyElements) = HtmlStructureBound.Apply(html);
        truncated |= tooManyElements;

        // Unwrap pass first: a bpost mail wrapped its whole 62 KB body in one <center>, and the
        // sanitiser deletes a disallowed tag with its subtree, rendering the message empty.
        var pre = _parser.ParseDocument(bounded);
        // An empty body is what the reader shows the text alternative for.
        if (HtmlStructureBound.Exceeds(pre)) return new SanitizedHtml { Truncated = true };
        // Read before CullEscapedStyleSheets can remove a sheet: an unsafe escape does not undo
        // a design promise the sheet already made.
        var darkScheme = DeclaresDarkScheme(pre);
        // Bounded before the move below, whose cost is quadratic in the sheets it moves.
        CullEscapedStyleSheets(pre.DocumentElement);
        BoundStyleSheets(pre.DocumentElement);
        // Only the body crosses Ganss, and a newsletter's stylesheet lives in the head.
        pre.Body!.Prepend([.. pre.Head!.QuerySelectorAll("style")]);
        foreach (var style in pre.Body.QuerySelectorAll("style[media]").ToList())
            CarryMediaIntoText(style);
        UnwrapDisallowedTags(pre.Body);
        CullEscapedDeclarations(pre.Body);
        MarkBreakWord(pre.Body);

        // Bounded again: a tree the unwrap reshaped (a paragraph inside a paragraph) multiplies
        // its formatting once its text is parsed back, which the sanitiser does first.
        var (reshaped, multiplied) = HtmlStructureBound.Apply(pre.Body.InnerHtml);
        var cleaned = _sanitizer.Sanitize(reshaped);

        // Second pass on the already-sanitised markup, using the same parser the sanitiser
        // uses so the two cannot disagree about the tree. In body mode: a leading <style> parsed
        // as a whole document would be hoisted into the head, and only the body is serialised.
        var document = _parser.ParseDocument("<body>" + cleaned);
        if (HtmlStructureBound.Exceeds(document)) return new SanitizedHtml { Truncated = true };
        truncated |= multiplied;
        VetStyleSheets(document);

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
            // A relative link would resolve against the page showing the message: the webmail's.
            var href = link.GetAttribute("href")!.Trim();
            if (href.StartsWith("//", StringComparison.Ordinal)) href = "https:" + href;
            if (!IsHttpUrl(href, out var site) && !(Uri.TryCreate(href, UriKind.Absolute, out site) && site.Scheme == Uri.UriSchemeMailto))
            {
                link.RemoveAttribute("href");
                continue;
            }

            link.SetAttribute("href", href);
            link.SetAttribute("target", "_blank");
            link.SetAttribute("rel", "noopener noreferrer");
        }

        return new SanitizedHtml
        {
            // Ganss's formatter escapes < and > in attribute values; AngleSharp's default
            // (InnerHtml) does not, which would undo that hardening on this last serialise.
            Html = document.Body is { } body ? body.ChildNodes.ToHtml(HtmlFormatter.Instance) : string.Empty,
            BlockedImageCount = blocked,
            Truncated = truncated,
            DeclaresDarkScheme = darkScheme
        };
    }

    private static bool DeclaresDarkScheme(AngleSharp.Dom.IDocument document) =>
        document.QuerySelectorAll("meta[name]").Any(meta =>
            meta.GetAttribute("name")!.Trim() is var name
            && (name.Equals("color-scheme", StringComparison.OrdinalIgnoreCase)
                || name.Equals("supported-color-schemes", StringComparison.OrdinalIgnoreCase))
            && Regex.IsMatch(meta.GetAttribute("content") ?? string.Empty, @"\bdark\b", RegexOptions.IgnoreCase))
        || document.QuerySelectorAll("style").Any(style => DarkSchemeDeclaration.IsMatch(style.TextContent));

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
            if (style.Contains('\\'))
                style = TrySplitTopLevel(style, ';', out var declarations)
                    ? string.Join(';', declarations.Where(d => !d.Contains('\\')))
                    : string.Empty;

            // Ganss parses an inline style with the same recursive parser as a sheet.
            styled.SetAttribute("style", NestsTooDeep(style) ? string.Empty : style);
        }
    }

    // Browsers read the legacy `word-break: break-word` as `overflow-wrap: anywhere`, which lets a
    // long token wrap inside a table cell; AngleSharp knows neither spelling and Ganss drops it, so
    // the element carries a marker the reader's own stylesheet turns back into the declaration.
    private static void MarkBreakWord(AngleSharp.Dom.IElement root)
    {
        foreach (var styled in root.QuerySelectorAll("[style]"))
            if (BreakWordDeclaration.IsMatch(styled.GetAttribute("style")!))
                styled.SetAttribute(BreakWordAttribute, string.Empty);
    }

    // Three rounds of a hand-rolled CSS tokeniser each closed one bypass and opened another —
    // an escaped `url(`, a quote inside an unquoted one, a comment gluing two tokens together,
    // an `@font-face` block treated as a grouping rule — so this stops trying to parse the sheet
    // at all: a sheet holding any escape that is not plainly punctuation is removed whole.
    private static void CullEscapedStyleSheets(AngleSharp.Dom.IElement root)
    {
        foreach (var style in root.QuerySelectorAll("style").ToList())
            if (HoldsUnsafeEscape(style.TextContent)) style.Remove();
    }

    /// <summary>
    /// Holds each message to <see cref="MaxStyleSheets"/> sheets and <see cref="MaxStyleSheetLength"/>
    /// characters of CSS, kept in document order until either is spent, and removes a sheet nested
    /// past <see cref="MaxStyleSheetNesting"/> or typed as anything but CSS — Ganss strips `type`,
    /// so a `text/template` block would otherwise go live. Runs after the escape cull, which
    /// <see cref="NestsTooDeep"/> relies on, and before <see cref="CarryMediaIntoText"/> parses.
    /// </summary>
    private static void BoundStyleSheets(AngleSharp.Dom.IElement root)
    {
        var kept = 0;
        var budget = MaxStyleSheetLength;

        foreach (var style in root.QuerySelectorAll("style").ToList())
        {
            var css = style.TextContent;
            if (!IsCssType(style.GetAttribute("type")) || NestsTooDeep(css)
                || kept == MaxStyleSheets || (budget -= css.Length) < 0)
                style.Remove();
            else
                kept++;
        }
    }

    private static bool IsCssType(string? type) =>
        type?.Trim() is not { Length: > 0 } trimmed || trimmed.Equals("text/css", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="css"/> nests blocks, functions or brackets past
    /// <see cref="MaxStyleSheetNesting"/>, pairing them as the CSS tokeniser does: an escape, a
    /// string or a comment hides its own. Wherever this reading and the parser's could part — a
    /// string or comment left open, an unquoted url() holding a quote or a comment opener — it
    /// answers true: a miscount must drop a sheet, never pass one.
    /// </summary>
    internal static bool NestsTooDeep(string css)
    {
        Span<char> closers = stackalloc char[MaxStyleSheetNesting];
        var depth = 0;

        for (var i = 0; i < css.Length; i++)
        {
            switch (css[i])
            {
                case '\\':
                    i++;
                    break;
                case '"' or '\'':
                    i = EndOfCssString(css, i);
                    if (i < 0) return true;
                    break;
                case '/' when i + 1 < css.Length && css[i + 1] == '*':
                    i = css.IndexOf("*/", i + 2, StringComparison.Ordinal) + 1;
                    if (i <= 0) return true;
                    break;
                case '(' when IsUnquotedUrl(css, i) && UrlHidesAStringOrComment(css, i):
                    return true;
                case '(' or '[' or '{' when depth == MaxStyleSheetNesting:
                    return true;
                case '(':
                    closers[depth++] = ')';
                    break;
                case '[':
                    closers[depth++] = ']';
                    break;
                case '{':
                    closers[depth++] = '}';
                    break;
                case ')' or ']' or '}' when depth > 0 && closers[depth - 1] == css[i]:
                    depth--;
                    break;
            }
        }

        return false;
    }

    // Index of the closing quote, or -1 for a string a newline or the end of the text cuts short:
    // the tokeniser reads those as a bad string, whose recovery this does not model.
    private static int EndOfCssString(string css, int open)
    {
        for (var i = open + 1; i < css.Length; i++)
        {
            if (css[i] == '\\') i++;
            else if (css[i] == css[open]) return i;
            else if (css[i] is '\n' or '\r' or '\f') return -1;
        }

        return -1;
    }

    // `url(` not followed by a quote is one token up to the next `)`, not a function. Read as a
    // function instead, a bracket inside can only over-count; a quote or `/*` would hide text.
    private static bool IsUnquotedUrl(string css, int paren)
    {
        if (paren < 3 || !css.AsSpan(paren - 3, 3).Equals("url", StringComparison.OrdinalIgnoreCase)) return false;
        var next = css.AsSpan(paren + 1).TrimStart(" \t\n\r\f");
        return next.IsEmpty || next[0] is not ('"' or '\'');
    }

    private static bool UrlHidesAStringOrComment(string css, int paren)
    {
        for (var i = paren + 1; i < css.Length && css[i] != ')'; i++)
        {
            if (css[i] == '\\') i++;
            else if (css[i] is '"' or '\'' || css[i] == '/' && i + 1 < css.Length && css[i + 1] == '*') return true;
        }

        return false;
    }

    // What generated mail escapes in a class name (`.sm\:px-4`, `.w-1\/2`, `.\!mt-0`) and nothing
    // else: none of these can spell a letter, a hex digit (so never `url(`), a quote, a brace, a
    // semicolon, whitespace or another backslash, so none can open a string, a block or a fetch.
    // No `*`: `\*` (as in `\/\*`) reopens a real `/*` comment once Ganss serialises it back
    // unescaped. The rest can still come back as a bare `(` or `[` (`a\(`): the second parse drops
    // such a declaration in a style rule, and VetStyleSheets' fixpoint check removes any sheet
    // whose written text would not re-read to itself.
    private const string SafeEscapedPunctuation = ":/.%@!#,+[]()=~^$|&";

    /// <summary>
    /// Whether <paramref name="css"/> holds a backslash not immediately followed by one of
    /// <see cref="SafeEscapedPunctuation"/> — including a backslash with nothing after it. A sheet
    /// with only safe escapes, or none, is kept untouched: no declaration-level surgery at all.
    /// </summary>
    internal static bool HoldsUnsafeEscape(string css)
    {
        for (var i = css.IndexOf('\\'); i >= 0; i = css.IndexOf('\\', i + 2))
            if (i + 1 >= css.Length || !SafeEscapedPunctuation.Contains(css[i + 1]))
                return true;
        return false;
    }

    // Ganss vets properties and at-rules but not what a url() in a rule would fetch, nor a font
    // source's scheme. A url() has no consent surface in a sheet, except a font the reader's CSP
    // holds back until the images are shown.
    private void VetStyleSheets(AngleSharp.Dom.IDocument document)
    {
        foreach (var style in document.QuerySelectorAll("style").ToList())
        {
            var sheet = _cssParser.ParseStyleSheet(style.TextContent);
            VetRules(sheet.Rules, sheet.RemoveAt);
            var css = sheet.ToCss();

            // A safe escape can serialise back bare (`a\(` → `a(`), so the written text may re-read
            // differently; keeping only a sheet that re-reads to itself means the browser gets
            // exactly what VetRules judged.
            if (css.Contains("</", StringComparison.Ordinal) || _cssParser.ParseStyleSheet(css).ToCss() != css)
                style.Remove();
            else
                style.TextContent = css;
        }
    }

    private static void VetRules(ICssRuleList rules, Action<int> removeAt)
    {
        for (var i = rules.Length - 1; i >= 0; i--)
        {
            switch (rules[i])
            {
                case ICssGroupingRule group:
                    VetRules(group.Rules, group.RemoveAt);
                    break;
                case ICssStyleRule rule:
                    foreach (var name in rule.Style.Select(d => d.Name).ToList())
                        if (rule.Style.GetPropertyValue(name).Contains("url(", StringComparison.OrdinalIgnoreCase))
                            rule.Style.RemoveProperty(name);
                    break;
                case ICssFontFaceRule face when !IsHttpFontSource(face.Source):
                    removeAt(i);
                    break;
            }
        }
    }

    private static bool IsHttpFontSource(string source) =>
        TryReadUrls(source, out var urls) && urls.All(url => IsHttpUrl(url, out _));

    // Ganss strips `media`, so its condition is carried into the sheet as an @media wrapper. The
    // value is spliced into CSS text, hence the character allowlist; a wrapper the parser does not
    // read back as one @media rule (a bad query, a text closing it early) takes the sheet with it.
    private void CarryMediaIntoText(AngleSharp.Dom.IElement style)
    {
        var media = style.GetAttribute("media")!.Trim();
        style.RemoveAttribute("media");
        if (media.Length == 0 || media.Equals("all", StringComparison.OrdinalIgnoreCase)) return;

        // The wrapper is one level more, so the nesting ceiling is checked again before the parse.
        var wrapped = $"@media {media} {{ {style.TextContent} }}";
        var rules = MediaQueryList.IsMatch(media) && !NestsTooDeep(wrapped)
            ? _cssParser.ParseStyleSheet(wrapped).Rules
            : null;

        if (rules is { Length: 1 } && rules[0] is ICssMediaRule rule && CanEverApply(rule.Media)) style.TextContent = wrapped;
        else style.Remove();
    }

    // The parser reads a query it cannot parse as `not all`.
    private static bool CanEverApply(IMediaList media) =>
        media.MediaText.Split(',').Any(query => !query.Trim().Equals("not all", StringComparison.OrdinalIgnoreCase));

    // False when a url( could not be read: a value we cannot account for would leave an unvetted
    // fetch in the CSS.
    private static bool TryReadUrls(string value, out List<string> urls)
    {
        urls = CssUrl.Matches(value).Select(m => m.Groups["u"].Value.Trim()).ToList();
        return urls.Count == CountUrlFunctions(value);
    }

    private static bool IsHttpUrl(string url, [NotNullWhen(true)] out Uri? parsed) =>
        Uri.TryCreate(url, UriKind.Absolute, out parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

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
            if (!TryReadUrls(layer, out var urls)) continue;

            // Only a layer whose every url() stays inside the message may be kept verbatim.
            if (urls.All(url => url.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)))
            {
                kept.Add(layer.Trim());
                continue;
            }

            // AbsoluteUri, not the raw text: a quoted url() can carry a raw space, which the
            // space-separated attribute would read back as two URLs.
            foreach (var url in urls)
                if (IsHttpUrl(url, out var parsed)) withheld.Add(parsed.AbsoluteUri);
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
