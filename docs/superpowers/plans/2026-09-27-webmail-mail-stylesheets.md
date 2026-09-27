# Mail stylesheets in the reader — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep a message's own `<style>` blocks (and the `class`/`id` they target) in the reader, safely, so responsive newsletters fit a phone screen and a sender's own dark design is honoured.

**Architecture:** The backend sanitiser stops dropping `<style>`: it moves `<head>` styles into the body, lets Ganss filter each rule with the existing CSS property allowlist, culls escaped declarations before Ganss and every `url()` after it (except an http(s) `@font-face` source), and reports whether the message declares a dark colour scheme. The client keeps `<style>` in the reader's DOMPurify pass only, resolves `prefers-color-scheme` blocks itself, darkens stylesheet colours when it darkens the message, and puts a CSP in the iframe document so nothing remote loads without consent.

**Tech Stack:** .NET 10, Ganss HtmlSanitizer 9.1.949-beta, AngleSharp + AngleSharp.Css, xUnit; React 19 + TypeScript, DOMPurify, Vitest (jsdom).

**Spec:** `docs/history/specs/2026-09-27-webmail-mail-stylesheets-design.md`

## Global Constraints

- Allowed at-rules: `@media` and `@font-face` only. `@import`, `@keyframes`, `@namespace` and every other at-rule disappear.
- `url()` inside a stylesheet survives only in an `@font-face` `src`, and only when every URL there is `https` or `http`; otherwise the whole `@font-face` rule is removed. Everywhere else a declaration holding `url(` is removed.
- Selectors are never altered (`.sm\:px-4` is legitimate).
- A `<style>` whose final text contains `</` is removed whole.
- A stylesheet declaration containing a backslash is removed before Ganss; a stylesheet the scanner cannot split (unclosed string or comment, unbalanced braces) is removed whole.
- Iframe CSP without consent: `default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src 'none'`. With consent: `img-src data: https: http:; font-src https: http:`.
- The composer's DOMPurify policy (`SquireEditor`) is unchanged: `style` stays forbidden there.
- `declaresDarkScheme` is optional on the client (`declaresDarkScheme?: boolean`); absent reads as `false`.
- The two real `.eml` files (`D:\Claude\claude.eml`, `D:\Claude\lotterie-nationale.eml`) hold personal data and are never copied into the repository.
- Backend tests: `cd src && dotnet test` (never `--no-build` when a test file was added). Revert `ApiDocumentation.xml` drift before committing (`git checkout -- '**/ApiDocumentation.xml'` if it shows as modified).
- Frontend checks: `cd src/frontend && npx vitest run <file>`, then `npm run lint` and `npx tsc -b` before each commit.
- Commit messages: two lines max, never beginning or ending with `@`, written with `git commit -F -` and a heredoc, ending with the attribution lines:
  `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>` and `Claude-Session: https://claude.ai/code/session_014pZFNVjMycnMn6Crm84wUR`. Never push.

## Review Focus

1. **A tracker smuggled through a stylesheet** — `url()` in a rule, in a `@media` rule, in a shorthand, spelled with an escape, or in an `@import`: nothing may fetch without consent. Tests in Tasks 1 and 2.
2. **A stylesheet that breaks out of `<style>`** — a `</style>` in a string, raw or CSS-escaped: the result must never contain an element the sanitiser did not allow. Tests in Tasks 1 and 2.
3. **Tailwind-style escaped selectors** (`.sm\:px-4 { padding: 0 !important }`) — the rule must survive the backslash cull, since only declarations are culled. Test in Task 2.
4. **A light-authored mail in dark theme whose stylesheet paints `#ffffff` backgrounds** — must come out dark, like its inline styles. Test in Task 5.
5. **An existing reveal path after the CSP lands** — revealed `<img>`, restored CSS backgrounds and `cid:` images (data URIs) must still render once consent is given. Test in Task 4, measured in Task 6.

---

### Task 1: The backend keeps stylesheets and vets them

**Files:**
- Modify: `src/scotty.microservice/Services/MailHtmlSanitizer.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Services/MailHtmlSanitizerTests.cs`

**Interfaces:**
- Produces: `MailHtmlSanitizer.Sanitize` output may now contain `<style>` elements (at the head of the body, in document order) and `class`/`id` attributes.

- [ ] **Step 1: Update the tests whose premise changes**

In `MailHtmlSanitizerTests.cs`:
- `Sanitize_DropsTheContentOfNonRenderedContainers` (line ~53): replace the `<style>body{color:red}</style>` case with `[InlineData("<template><p>tpl</p></template><p>hi</p>", "tpl")]`.
- `Sanitize_StillStripsHostileContentBuriedPastTheCap` (line ~780): replace the `<style>body{color:red}</style>` case with `[InlineData("<style>.x{position:fixed}</style>", "position")]`.
- `Sanitize_NeverKeepsAUrlInASheet` (line ~362): delete the `@font-face` `InlineData` (it now survives, see Step 2), keep the other two.

- [ ] **Step 2: Write the failing tests**

Add to `MailHtmlSanitizerTests.cs`:

```csharp
    // A responsive newsletter adapts to a phone through its own @media rules, and they need
    // !important to beat the inline widths they override.
    [Fact]
    public void Sanitize_KeepsAHeadStylesheetAtTheHeadOfTheBody()
    {
        var result = _sut.Sanitize(
            "<html><head><style>@media only screen and (max-width: 499px) { .full-width { width: 100% !important } }</style></head>"
            + "<body><table class=\"full-width\" id=\"main\"><tr><td>hi</td></tr></table></body></html>").Html;

        Assert.StartsWith("<style>", result);
        Assert.Contains("@media only screen and (max-width: 499px)", result);
        Assert.Contains("width: 100% !important", result);
        Assert.Contains("class=\"full-width\"", result);
        Assert.Contains("id=\"main\"", result);
    }

    [Fact]
    public void Sanitize_KeepsHeadStylesheetsInDocumentOrder()
    {
        var result = _sut.Sanitize(
            "<html><head><style>.a { color: red }</style><style>.b { color: blue }</style></head><body><p>hi</p></body></html>").Html;

        Assert.True(result.IndexOf(".a", StringComparison.Ordinal) < result.IndexOf(".b", StringComparison.Ordinal));
    }

    [Fact]
    public void Sanitize_KeepsChildCombinatorsInASheet()
    {
        var result = _sut.Sanitize("<style>td > p.a { color: red }</style><p class=\"a\">hi</p>").Html;

        Assert.Contains("td>p.a", result.Replace(" ", string.Empty));
    }

    [Theory]
    [InlineData("position")]
    [InlineData("z-index")]
    [InlineData("float")]
    public void Sanitize_AppliesThePropertyAllowlistInsideASheet(string property)
    {
        var result = _sut.Sanitize(
            $"<style>@media (max-width: 499px) {{ .x {{ {property}: 1; color: red }} }}</style><p>hi</p>").Html;

        Assert.DoesNotContain(property, result);
        Assert.Contains("color", result);
    }

    [Theory]
    [InlineData("@import url(https://t.example/a.css);", "t.example")]
    [InlineData("@keyframes k { from { color: red } }", "keyframes")]
    [InlineData("@namespace svg url(http://www.w3.org/2000/svg);", "namespace")]
    public void Sanitize_DropsEveryOtherAtRule(string rule, string forbidden)
    {
        var result = _sut.Sanitize($"<style>{rule} .x {{ color: red }}</style><p>hi</p>").Html;

        Assert.DoesNotContain(forbidden, result);
        Assert.Contains(".x", result);
    }

    // No consent surface exists for a url() in a rule: it would fetch on render.
    [Theory]
    [InlineData(".x { background-image: url(https://t.example/p.gif); color: red }")]
    [InlineData("@media (max-width: 499px) { .x { background-image: url(https://t.example/p.gif); color: red } }")]
    [InlineData(".x { background: url(https://t.example/p.gif) #fff; color: red }")]
    [InlineData(".x { background-image: url(cid:logo@mail); color: red }")]
    public void Sanitize_DropsAUrlDeclarationFromASheetRule(string css)
    {
        var result = _sut.Sanitize($"<style>{css}</style><p>hi</p>").Html;

        Assert.DoesNotContain("url(", result);
        Assert.Contains("color", result);
    }

    // A font is gated by the reader's CSP until consent, like an image.
    [Fact]
    public void Sanitize_KeepsAnHttpsFontFace()
    {
        var result = _sut.Sanitize(
            "<style>@font-face { font-family: Brand; src: url(https://fonts.example/b.woff2) format(\"woff2\"), local(\"Arial\"); font-display: swap }</style><p>hi</p>").Html;

        Assert.Contains("@font-face", result);
        Assert.Contains("https://fonts.example/b.woff2", result);
    }

    [Theory]
    [InlineData("url(javascript:alert(1))")]
    [InlineData("url(data:font/woff2;base64,AAAA)")]
    [InlineData("url(https://fonts.example/b.woff2), url(file:///etc/passwd)")]
    public void Sanitize_DropsAFontFaceWithANonHttpSource(string source)
    {
        var result = _sut.Sanitize($"<style>@font-face {{ font-family: Brand; src: {source} }} .x {{ color: red }}</style><p>hi</p>").Html;

        Assert.DoesNotContain("@font-face", result);
        Assert.Contains(".x", result);
    }

    [Fact]
    public void Sanitize_NeverLetsASheetCloseItsOwnElement()
    {
        var result = _sut.Sanitize(
            "<style>.x { font-family: \"a</style><img src=x onerror=alert(1)>\" } .y { color: red }</style><p>hi</p>").Html;

        Assert.DoesNotContain("onerror", result);
        Assert.Equal(result.Split("<style").Length - 1, result.Split("</style>").Length - 1);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd src && dotnet test --filter "FullyQualifiedName~MailHtmlSanitizerTests"`
Expected: the new tests FAIL (the `<style>` is dropped today); the three edited ones PASS.

- [ ] **Step 4: Implement**

In `MailHtmlSanitizer.cs`:

1. Remove `"style"` from `DropWithContent` (keep it in `RawTextTags`).
2. In the constructor, add `"style"` to the allowed tags array, `"class"` and `"id"` to the allowed attributes, `"src"`, `"font-display"` and `"unicode-range"` to the CSS properties, then set the at-rules:

```csharp
        // A rule is filtered by the same property allowlist as an inline style; only the two
        // at-rules a mail needs survive — @import would fetch a sheet nobody here has vetted.
        _sanitizer.AllowedAtRules.Clear();
        foreach (var rule in new[] { CssRuleType.Style, CssRuleType.Media, CssRuleType.FontFace })
            _sanitizer.AllowedAtRules.Add(rule);
```

(add `using AngleSharp.Css.Dom;` and `using AngleSharp.Css.Parser;`).

3. In `Sanitize`, right after `var pre = _parser.ParseDocument(bounded);`:

```csharp
        // Only the body crosses Ganss, and a newsletter's stylesheet lives in the head.
        pre.Body!.Prepend([.. pre.Head!.QuerySelectorAll("style")]);
```

4. Replace `var document = _parser.ParseDocument(cleaned);` with:

```csharp
        // In body mode: a leading <style> parsed as a whole document would be hoisted into the
        // head, and only the body is serialised.
        var document = _parser.ParseDocument("<body>" + cleaned);
        VetStyleSheets(document);
```

5. Add the post-Ganss pass (field `private readonly CssParser _cssParser = new();` next to `_parser`):

```csharp
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

            if (css.Contains("</", StringComparison.Ordinal)) style.Remove();
            else style.TextContent = css;
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

    private static bool IsHttpFontSource(string source)
    {
        var urls = CssUrl.Matches(source).Select(m => m.Groups["u"].Value.Trim()).ToList();
        return urls.Count == CountUrlFunctions(source)
            && urls.All(url => Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps));
    }
```

If `ICssGroupingRule`, `RemoveAt` or `ICssFontFaceRule.Source` do not compile under this AngleSharp.Css version, find the equivalent member in the package's `ICssMediaRule`/`ICssStyleSheet`/`ICssFontFaceRule` metadata — the behaviour required is the one above. A prototype of exactly this walk ran green against 9.1.949-beta on 2026-09-27 using `ICssMediaRule`, `ICssStyleRule`, `ICssFontFaceRule.Source`, `RemoveProperty` and `ToCss()`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd src && dotnet test --filter "FullyQualifiedName~MailHtmlSanitizerTests"`
Expected: all PASS. Then the whole suite: `cd src && dotnet test` — all PASS.

- [ ] **Step 6: Update rule 6 of `src/scotty.microservice/CLAUDE.md`**

Append to rule 6, after *(c)*, a *(d)*:

```markdown
*(d)* **A message's `<style>` is kept, and judged in two passes of its own.** The head's
stylesheets are moved to the head of the body before Ganss (only the body crosses it), Ganss filters
each rule with the same property allowlist as an inline style and keeps `@media` and `@font-face`
alone — `@import` would fetch a sheet nobody has vetted — and `VetStyleSheets` then removes every
declaration holding a `url(` from a rule and every `@font-face` whose sources are not all http(s):
a sheet has no consent surface, so a font is held back by the reader's CSP instead. A sheet whose
final text holds `</` is removed whole, since that is the only way out of the element. The second
parse runs in body mode (`"<body>" + cleaned`): a leading `<style>` parsed as a whole document is
hoisted into the head and silently lost.
```

- [ ] **Step 7: Commit**

```bash
git add src/scotty.microservice/Services/MailHtmlSanitizer.cs src/scotty.microservice/scotty.microservice.Tests/Services/MailHtmlSanitizerTests.cs src/scotty.microservice/CLAUDE.md
git commit -F - <<'EOF'
Keep a message's own stylesheets, vetted like its inline styles

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014pZFNVjMycnMn6Crm84wUR
EOF
```

---

### Task 2: Escaped declarations in a stylesheet are culled before Ganss

**Files:**
- Modify: `src/scotty.microservice/Services/MailHtmlSanitizer.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Services/MailHtmlSanitizerTests.cs`

**Interfaces:**
- Consumes: Task 1's pipeline (`<style>` now survives Ganss).
- Produces: `internal static string? CullEscapedSheetDeclarations(string css)` — the sheet without any declaration holding a backslash, or `null` when it cannot be split.

- [ ] **Step 1: Write the failing tests**

```csharp
    // AngleSharp resolves `\75 rl(` into `url(` while parsing, so the cull has to read the source.
    [Theory]
    [InlineData(".x { background-image: \\75 rl(https://t.example/e.gif); color: red }")]
    [InlineData(".x { font-family: \"\\3c/style\\3e<img src=x onerror=alert(1)>\"; color: red }")]
    [InlineData("@\\69 mport url(https://t.example/a.css); .x { color: red }")]
    public void Sanitize_CullsAnEscapedDeclarationInASheet(string css)
    {
        var result = _sut.Sanitize($"<style>{css}</style><p>hi</p>").Html;

        Assert.DoesNotContain("t.example", result);
        Assert.DoesNotContain("onerror", result);
        Assert.Contains("color", result);
    }

    // Generated mail escapes its selectors; only declarations are culled.
    [Fact]
    public void Sanitize_KeepsAnEscapedSelector()
    {
        var result = _sut.Sanitize(
            "<style>@media (max-width: 600px) { .sm\\:px-4 { padding-left: 16px !important } }</style><p class=\"sm:px-4\">hi</p>").Html;

        Assert.Contains(".sm\\:px-4", result);
        Assert.Contains("padding-left: 16px !important", result);
    }

    [Theory]
    [InlineData(".x { color: red } /* unterminated")]
    [InlineData(".x { font-family: \"unterminated }")]
    [InlineData(".x { color: red } }")]
    [InlineData(".x { color: red")]
    public void CullEscapedSheetDeclarations_RefusesASheetItCannotSplit(string css)
    {
        Assert.Null(MailHtmlSanitizer.CullEscapedSheetDeclarations(css + " .y { width: 1px\\31 }"));
    }

    [Fact]
    public void CullEscapedSheetDeclarations_DropsOnlyTheEscapedDeclaration()
    {
        Assert.Equal(".x { color: red;}",
            MailHtmlSanitizer.CullEscapedSheetDeclarations(".x { color: red; width: \\31 px}"));
    }
```

The test project already sees internals if `MailHtmlSanitizer` is `internal` and the other tests construct it; if `InternalsVisibleTo` is missing, the `new MailHtmlSanitizer()` field at the top of the file would not compile today, so it is present.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src && dotnet test --filter "FullyQualifiedName~MailHtmlSanitizerTests"`
Expected: FAIL (`CullEscapedSheetDeclarations` does not exist).

- [ ] **Step 3: Implement**

Add next to `CullEscapedDeclarations`:

```csharp
    // The sheet's twin of CullEscapedDeclarations: a declaration holding a backslash goes while its
    // source is still readable. A prelude — a selector, an at-rule's condition — is kept, since
    // generated mail escapes its class names and a selector fetches nothing.
    private static void CullEscapedStyleSheets(AngleSharp.Dom.IElement root)
    {
        foreach (var style in root.QuerySelectorAll("style").ToList())
        {
            if (!style.TextContent.Contains('\\')) continue;
            if (CullEscapedSheetDeclarations(style.TextContent) is { } kept) style.TextContent = kept;
            else style.Remove();
        }
    }

    /// <summary>
    /// The sheet without any declaration holding a backslash, comments removed, or null when an
    /// unclosed string or comment or an unbalanced brace leaves it impossible to split — a sheet we
    /// cannot split is a sheet we cannot vet.
    /// </summary>
    internal static string? CullEscapedSheetDeclarations(string css)
    {
        var output = new StringBuilder(css.Length);
        var segment = new StringBuilder();
        var depth = 0;
        var quote = '\0';

        for (var i = 0; i < css.Length; i++)
        {
            var c = css[i];
            if (quote != '\0')
            {
                segment.Append(c);
                if (c == '\\' && i + 1 < css.Length) segment.Append(css[++i]);
                else if (c == quote) quote = '\0';
                else if (c == '\n') return null;
                continue;
            }

            if (c == '/' && i + 1 < css.Length && css[i + 1] == '*')
            {
                var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) return null;
                i = end + 1;
                continue;
            }

            switch (c)
            {
                case '"' or '\'':
                    quote = c;
                    segment.Append(c);
                    break;
                case '{':
                    output.Append(segment).Append(c);
                    segment.Clear();
                    depth++;
                    break;
                case ';':
                    Flush();
                    output.Append(c);
                    break;
                case '}':
                    if (--depth < 0) return null;
                    Flush();
                    output.Append(c);
                    break;
                default:
                    segment.Append(c);
                    break;
            }
        }

        if (quote != '\0' || depth != 0) return null;
        Flush();
        return output.ToString();

        void Flush()
        {
            if (!segment.ToString().Contains('\\')) output.Append(segment);
            segment.Clear();
        }
    }
```

Note the expected output in `CullEscapedSheetDeclarations_DropsOnlyTheEscapedDeclaration`: the culled segment is `" width: \31 px"`, so what remains is `.x { color: red;}`.

In `Sanitize`, call it beside the inline cull:

```csharp
        UnwrapDisallowedTags(pre.Body!);
        CullEscapedDeclarations(pre.Body!);
        CullEscapedStyleSheets(pre.Body!);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd src && dotnet test --filter "FullyQualifiedName~MailHtmlSanitizerTests"` then `cd src && dotnet test`
Expected: all PASS.

- [ ] **Step 5: Extend rule 6 *(d)* in `src/scotty.microservice/CLAUDE.md`**

Append to *(d)*: `A declaration holding a backslash is culled from a sheet before Ganss, like an inline one (CullEscapedStyleSheets); a selector is not, since generated mail escapes its class names, and a sheet the scanner cannot split is removed whole.`

- [ ] **Step 6: Commit**

```bash
git add src/scotty.microservice/Services/MailHtmlSanitizer.cs src/scotty.microservice/scotty.microservice.Tests/Services/MailHtmlSanitizerTests.cs src/scotty.microservice/CLAUDE.md
git commit -F - <<'EOF'
Cull escaped declarations from a message's stylesheet before parsing

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014pZFNVjMycnMn6Crm84wUR
EOF
```

---

### Task 3: The message says whether it has a dark design

**Files:**
- Modify: `src/scotty.microservice/Models/Mail/SanitizedHtml.cs`, `src/scotty.microservice/Models/Mail/MailMessageDetail.cs`, `src/scotty.microservice/Services/ImapMessageCommands.cs:474-495`, `src/scotty.microservice/Services/MailHtmlSanitizer.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Services/MailHtmlSanitizerTests.cs`, `src/scotty.microservice/scotty.microservice.Tests/Services/ImapSessionMessageFetchTests.cs`

**Interfaces:**
- Produces: `SanitizedHtml.DeclaresDarkScheme` and `MailMessageDetail.DeclaresDarkScheme` (`bool`), serialised as `declaresDarkScheme`.

- [ ] **Step 1: Write the failing tests**

In `MailHtmlSanitizerTests.cs`:

```csharp
    [Theory]
    [InlineData("<html><head><meta name=\"color-scheme\" content=\"light dark\"></head><body><p>hi</p></body></html>")]
    [InlineData("<html><head><meta name=\"supported-color-schemes\" content=\"light dark\"></head><body><p>hi</p></body></html>")]
    [InlineData("<html><head><META NAME=\"Color-Scheme\" CONTENT=\"dark\"></head><body><p>hi</p></body></html>")]
    [InlineData("<html><head><style>:root { color-scheme: light dark; }</style></head><body><p>hi</p></body></html>")]
    public void Sanitize_ReportsADeclaredDarkScheme(string html)
    {
        Assert.True(_sut.Sanitize(html).DeclaresDarkScheme);
    }

    // A dark block without the declaration is not a promise: prefers-color-scheme alone is how a
    // sender reacts to a client, not how it says the design was made for both.
    [Theory]
    [InlineData("<html><head><style>@media (prefers-color-scheme: dark) { .x { color: #fff } }</style></head><body><p>hi</p></body></html>")]
    [InlineData("<html><head><meta name=\"color-scheme\" content=\"light\"></head><body><p>hi</p></body></html>")]
    [InlineData("<p>hi</p>")]
    public void Sanitize_ReportsNoDarkSchemeWithoutTheDeclaration(string html)
    {
        Assert.False(_sut.Sanitize(html).DeclaresDarkScheme);
    }
```

In `ImapSessionMessageFetchTests.cs`, find the test that sets up `sanitizer.Setup(s => s.Sanitize(It.IsAny<string>()))` (line ~41) and add a sibling that returns `new SanitizedHtml { Html = "<p>x</p>", DeclaresDarkScheme = true }` and asserts the detail's `DeclaresDarkScheme` is `true`, copying the arrange/act of that existing test verbatim.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src && dotnet test --filter "FullyQualifiedName~MailHtmlSanitizerTests|FullyQualifiedName~ImapSessionMessageFetchTests"`
Expected: build FAILS (`DeclaresDarkScheme` does not exist).

- [ ] **Step 3: Implement**

`SanitizedHtml.cs`, after `Truncated`:

```csharp
    /// <summary>
    /// True when the message declares a dark colour scheme (a color-scheme or supported-color-schemes
    /// meta, or a color-scheme declaration naming dark). The reader then renders the sender's own
    /// dark design instead of recolouring the light one.
    /// </summary>
    public bool DeclaresDarkScheme { get; set; }
```

`MailMessageDetail.cs`, after `Truncated`, the same property with the summary `Whether the sender designed this body for a dark scheme too; see SanitizedHtml.`

`ImapMessageCommands.cs`, in the initialiser after `Truncated = sanitized.Truncated,`: `DeclaresDarkScheme = sanitized.DeclaresDarkScheme,`.

`MailHtmlSanitizer.cs`:

```csharp
    // `prefers-color-scheme: dark` is excluded by the look-behind: reacting to a client is not
    // declaring a design.
    private static readonly Regex DarkSchemeDeclaration = new(
        @"(?<![\w-])(?:supported-color-schemes|color-scheme)\s*:[^;}]*\bdark\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool DeclaresDarkScheme(AngleSharp.Dom.IDocument document) =>
        document.QuerySelectorAll("meta[name]").Any(meta =>
            meta.GetAttribute("name")!.Trim() is var name
            && (name.Equals("color-scheme", StringComparison.OrdinalIgnoreCase)
                || name.Equals("supported-color-schemes", StringComparison.OrdinalIgnoreCase))
            && Regex.IsMatch(meta.GetAttribute("content") ?? string.Empty, @"\bdark\b", RegexOptions.IgnoreCase))
        || document.QuerySelectorAll("style").Any(style => DarkSchemeDeclaration.IsMatch(style.TextContent));
```

In `Sanitize`, right after `var pre = _parser.ParseDocument(bounded);` (before the head styles move): `var darkScheme = DeclaresDarkScheme(pre);`, and set `DeclaresDarkScheme = darkScheme` in the returned `SanitizedHtml`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `cd src && dotnet test`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/scotty.microservice
git commit -F - <<'EOF'
Report whether a message declares a dark colour scheme

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014pZFNVjMycnMn6Crm84wUR
EOF
```

(Revert `ApiDocumentation.xml` first if `git status` shows it modified.)

---

### Task 4: The reader keeps `<style>`, and the iframe gets a CSP

**Files:**
- Modify: `src/frontend/src/modules/mail/sanitizePolicy.ts`, `src/frontend/src/modules/mail/reader/sanitizeBody.ts`, `src/frontend/src/modules/mail/compose/SquireEditor.tsx:4,63`, `src/frontend/src/modules/mail/api/mailTypes.ts:247`
- Test: `src/frontend/src/modules/mail/reader/sanitizeBody.test.ts`, `src/frontend/src/modules/mail/compose/SquireEditor.test.tsx` (if it exists; otherwise the policy test below lives in `sanitizeBody.test.ts`)

**Interfaces:**
- Produces: `READER_FORBID_TAGS`, `COMPOSER_FORBID_TAGS`, `FORBID_ATTR` from `sanitizePolicy.ts`; `renderBodyDocument(fragment, { dark?, senderDark?, remote?, narrow? })`; `MailMessageDetail.declaresDarkScheme?: boolean`.

- [ ] **Step 1: Write the failing tests**

In `sanitizeBody.test.ts`, inside `describe('sanitizeBody')`:

```ts
  // A newsletter's @media rules are what fit it to a phone; the iframe is the barrier here.
  it('keeps a leading stylesheet and the classes and ids it targets', () => {
    const out = sanitizeBody('<style>@media (max-width: 499px) { .w { width: 100% !important } }</style><table class="w" id="m"><tr><td>x</td></tr></table>')
    expect(out).toContain('<style>@media (max-width: 499px)')
    expect(out).toContain('class="w"')
    expect(out).toContain('id="m"')
  })
```

Add a policy test (in `sanitizeBody.test.ts`, top-level):

```ts
import { COMPOSER_FORBID_TAGS, READER_FORBID_TAGS } from '../sanitizePolicy'

describe('sanitize policies', () => {
  // The editor is a div in the SPA document: a stylesheet there would restyle the whole app.
  it('forbids style in the composer and allows it in the reader', () => {
    expect(COMPOSER_FORBID_TAGS).toContain('style')
    expect(READER_FORBID_TAGS).not.toContain('style')
  })
})
```

Inside `describe('renderBodyDocument')`:

```ts
  describe('content security policy', () => {
    const cspOf = (document: string) => /http-equiv="Content-Security-Policy" content="([^"]*)"/.exec(document)?.[1] ?? ''

    it('loads nothing remote without consent', () => {
      const csp = cspOf(renderBodyDocument('<p>x</p>'))
      expect(csp).toContain("default-src 'none'")
      expect(csp).toContain("style-src 'unsafe-inline'")
      expect(csp).toContain('img-src data:;')
      expect(csp).toContain("font-src 'none'")
    })

    it('admits remote images and fonts once the images are shown', () => {
      const csp = cspOf(renderBodyDocument('<p>x</p>', { remote: true }))
      expect(csp).toContain('img-src data: https: http:')
      expect(csp).toContain('font-src https: http:')
    })

    // Before the body's own <style>, or the body's rules could load under no policy at all.
    it('is the first thing in the head', () => {
      expect(renderBodyDocument('<p>x</p>')).toMatch(/<head><meta http-equiv="Content-Security-Policy"/)
    })
  })

  it('does not dim images when the sender designed the dark version', () => {
    expect(renderBodyDocument('<p>x</p>', { dark: true, senderDark: true })).not.toContain('brightness')
    expect(renderBodyDocument('<p>x</p>', { dark: true, senderDark: true })).toContain('color-scheme: dark')
  })
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/frontend && npx vitest run src/modules/mail/reader/sanitizeBody.test.ts`
Expected: FAIL (style stripped, policies not exported, no CSP).

- [ ] **Step 3: Implement**

`sanitizePolicy.ts`:

```ts
// Shared by the reader's iframe and the composer's editable div so paste and render cannot drift.
// The composer is the more dangerous one: a plain div in the SPA document, not a sandboxed iframe,
// which is why it alone forbids <style> — in the reader a message's stylesheet stays inside the frame.
const FORBID_ANYWHERE = ['script', 'iframe', 'object', 'embed', 'form', 'base', 'link']
export const READER_FORBID_TAGS = FORBID_ANYWHERE
export const COMPOSER_FORBID_TAGS = ['style', ...FORBID_ANYWHERE]
export const FORBID_ATTR = ['srcset', 'formaction', 'ping']
```

`SquireEditor.tsx`: import `COMPOSER_FORBID_TAGS` instead of `FORBID_TAGS` and pass `FORBID_TAGS: COMPOSER_FORBID_TAGS` at line 63.

`sanitizeBody.ts`: import `READER_FORBID_TAGS`; in `sanitizeBody`, pass `FORBID_TAGS: READER_FORBID_TAGS` and add `FORCE_BODY: true` with the comment `// Without it a leading <style> is parsed into the head and dropped.`

`renderBodyDocument`:

```ts
export function renderBodyDocument(
  fragment: string,
  options: { dark?: boolean; senderDark?: boolean; remote?: boolean; narrow?: boolean } = {},
): string {
```

- `images` becomes `options.dark && !options.senderDark ? 'filter: brightness(0.85) saturate(0.9);' : ''` with the comment line `// A sender's own dark design already chose its images.`
- add, before the `return`:

```ts
  // Whatever the sanitisers let through, nothing remote loads before consent: the message's own
  // stylesheet can name a font or an image, and this is the barrier that does not read it.
  const remote = options.remote ? ' https: http:' : ''
  const csp = `default-src 'none'; style-src 'unsafe-inline'; img-src data:${remote}; font-src${remote || " 'none'"}`
```

- the head becomes `<head><meta http-equiv="Content-Security-Policy" content="${csp}"><meta charset="utf-8"><style>`.

`mailTypes.ts`, after `blockedImageCount: number`:

```ts
  /** The sender designed the body for a dark scheme too. Absent from an API that predates it. */
  declaresDarkScheme?: boolean
```

`MessageReader.tsx:365`: `srcDoc={renderBodyDocument(body, { dark: inverted, remote: showImages, narrow })}` (Task 5 adds `senderDark`).

- [ ] **Step 4: Add the reader-level CSP test**

In `MessageReader.test.tsx`, after `'shows the images and no banner when the account always shows them'`:

```ts
  it('widens the frame\'s policy to remote images only once they are shown', async () => {
    mocks.getMailMessage.mockResolvedValue(blocked)

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText(/1 remote image was blocked/i)
    expect(container.querySelector('iframe')!.getAttribute('srcdoc')).toContain('img-src data:;')

    fireEvent.click(screen.getByRole('button', { name: /show images/i }))

    await waitFor(() => expect(container.querySelector('iframe')!.getAttribute('srcdoc'))
      .toContain('img-src data: https: http:'))
  })
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd src/frontend && npx vitest run src/modules/mail` then `npm run lint` and `npx tsc -b`
Expected: all PASS, no lint or type error.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/modules/mail
git commit -F - <<'EOF'
Keep a message's stylesheet in the reader and fence the frame with a CSP

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014pZFNVjMycnMn6Crm84wUR
EOF
```

---

### Task 5: Dark mode — the sender's design, or ours applied to the stylesheet too

**Files:**
- Create: `src/frontend/src/modules/mail/reader/messageStyles.ts`
- Modify: `src/frontend/src/modules/mail/reader/darkenColours.ts`, `src/frontend/src/modules/mail/reader/MessageReader.tsx:136-158,365`
- Test: `src/frontend/src/modules/mail/reader/messageStyles.test.ts` (create), `darkenColours.test.ts`, `MessageReader.test.tsx`

**Interfaces:**
- Consumes: `renderBodyDocument`'s `senderDark` option (Task 4), `MailMessageDetail.declaresDarkScheme` (Task 4).
- Produces: `resolveColourScheme(html: string, scheme: 'light' | 'dark'): string`; `forEachStyleRule(sheetText: string, edit: (style: CSSStyleDeclaration) => void): string` in `messageStyles.ts`.

- [ ] **Step 1: Write the failing tests**

`messageStyles.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { forEachStyleRule, resolveColourScheme } from './messageStyles'

describe('resolveColourScheme', () => {
  const html = '<style>@media (prefers-color-scheme: dark) { .x { color: #fff } } '
    + '@media screen and (prefers-color-scheme: light) { .y { color: #000 } }</style><p>x</p>'

  // We decide, not the browser: what the iframe inherits from the page is Blink's business.
  it('makes the dark blocks unconditional and the light ones inert for a dark render', () => {
    const out = resolveColourScheme(html, 'dark')
    expect(out).not.toContain('prefers-color-scheme')
    expect(out).toContain('@media (min-width: 0) { .x')
    expect(out).toContain('@media screen and (max-width: 0) and (min-width: 1px) { .y')
  })

  it('does the opposite for a light render', () => {
    const out = resolveColourScheme(html, 'light')
    expect(out).toContain('@media (max-width: 0) and (min-width: 1px) { .x')
    expect(out).toContain('@media screen and (min-width: 0) { .y')
  })

  it('leaves a body with no stylesheet untouched', () => {
    expect(resolveColourScheme('<p>prefers-color-scheme: dark</p>', 'light')).toBe('<p>prefers-color-scheme: dark</p>')
  })
})

describe('forEachStyleRule', () => {
  it('reaches rules nested in a media block and keeps their priority', () => {
    const out = forEachStyleRule('@media (max-width: 499px) { .a { color: #000 !important } } .b { color: red }',
      style => style.setProperty('color', '#123456', style.getPropertyPriority('color')))
    expect(out).toMatch(/\.a \{ color: (#123456|rgb\(18, 52, 86\)) !important; \}/)
    expect(out).toMatch(/\.b \{ color: (#123456|rgb\(18, 52, 86\)); \}/)
  })
})
```

In `darkenColours.test.ts`:

```ts
describe('darkenColours — stylesheets', () => {
  // A light-authored newsletter paints its slabs from the stylesheet as often as inline.
  it('darkens colours declared in a stylesheet, media blocks included', () => {
    const out = darkenColours('<style>.bg { background-color: #ffffff } @media (max-width: 499px) { .t { color: #000000 !important } }</style><p class="t">x</p>')
    expect(out).toMatch(/\.bg \{ background-color: (#212121|rgb\(33, 33, 33\)); \}/)
    expect(out).toMatch(/\.t \{ color: (#e0e0e0|rgb\(224, 224, 224\)) !important; \}/)
  })

  it('darkens the stops of a gradient in a stylesheet', () => {
    const out = darkenColours('<style>.g { background-image: linear-gradient(#ffffff, #ffffff) }</style><p>x</p>')
    expect(out).not.toMatch(/#ffffff|rgb\(255, 255, 255\)/)
  })
})
```

In `MessageReader.test.tsx`, after the background-restore test:

```ts
  const withSheet = {
    ...detail,
    htmlBody: '<style>.bg { background-color: #ffffff } @media (prefers-color-scheme: dark) { .bg { background-color: #262624 !important } }</style><p class="bg">Bonjour</p>',
  }

  it('renders the sender\'s own dark design when the message declares one', async () => {
    theme.isDark = true
    mocks.getMailMessage.mockResolvedValue({ ...withSheet, declaresDarkScheme: true })

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    const srcdoc = container.querySelector('iframe')!.getAttribute('srcdoc')!
    expect(srcdoc).toContain('@media (min-width: 0)')
    expect(srcdoc).toMatch(/\.bg \{ background-color: (#ffffff|rgb\(255, 255, 255\))/)
    expect(srcdoc).not.toContain('brightness')
    theme.isDark = false
  })

  it('recolours a light-only message, stylesheet included, and drops its dark blocks', async () => {
    theme.isDark = true
    mocks.getMailMessage.mockResolvedValue(withSheet)

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    const srcdoc = container.querySelector('iframe')!.getAttribute('srcdoc')!
    expect(srcdoc).toContain('(max-width: 0) and (min-width: 1px)')
    expect(srcdoc).toMatch(/\.bg \{ background-color: (#212121|rgb\(33, 33, 33\))/)
    theme.isDark = false
  })

  it('keeps the light design in the light theme even when a dark one is declared', async () => {
    mocks.getMailMessage.mockResolvedValue({ ...withSheet, declaresDarkScheme: true })

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    expect(container.querySelector('iframe')!.getAttribute('srcdoc'))
      .toContain('(max-width: 0) and (min-width: 1px)')
  })
```

(If the fixture's subject is not `Re: facture`, use the `detail` fixture's subject; `withSheet` spreads it.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd src/frontend && npx vitest run src/modules/mail/reader`
Expected: FAIL (`messageStyles` missing, stylesheet colours untouched).

- [ ] **Step 3: Implement `messageStyles.ts`**

```ts
// A message's own <style> blocks, handled as text in and text out. Parsed through a constructable
// sheet: it is never adopted, so nothing here applies to the page or fetches anything.

// Always true and always false, both valid inside any `and` chain of a media query.
const ALWAYS = '(min-width: 0)'
const NEVER = '(max-width: 0) and (min-width: 1px)'
const SCHEME_FEATURE = /\(\s*prefers-color-scheme\s*:\s*(dark|light)\s*\)/gi

/** Settles each `prefers-color-scheme` condition for the scheme we render, rather than leaving it to
 * whatever the iframe inherits from the page. */
export function resolveColourScheme(html: string, scheme: 'light' | 'dark'): string {
  if (!/prefers-color-scheme/i.test(html)) return html
  return rewriteStyleElements(html, css =>
    css.replace(SCHEME_FEATURE, (_feature, wanted: string) => wanted.toLowerCase() === scheme ? ALWAYS : NEVER))
}

/** Runs `edit` on every style rule's declarations, media blocks included, and serialises the sheet. */
export function forEachStyleRule(sheetText: string, edit: (style: CSSStyleDeclaration) => void): string {
  const sheet = new CSSStyleSheet()
  sheet.replaceSync(sheetText)
  const walk = (rules: CSSRuleList) => {
    for (const rule of Array.from(rules)) {
      if (rule instanceof CSSStyleRule) edit(rule.style)
      else if (rule instanceof CSSGroupingRule) walk(rule.cssRules)
    }
  }
  walk(sheet.cssRules)
  return Array.from(sheet.cssRules, rule => rule.cssText).join('\n')
}

/** Rewrites the text of each <style> in a fragment. */
export function rewriteStyleElements(html: string, rewrite: (css: string) => string): string {
  if (!html.includes('<style')) return html
  const document = new DOMParser().parseFromString(html, 'text/html')
  for (const style of document.body.querySelectorAll('style')) style.textContent = rewrite(style.textContent ?? '')
  return document.body.innerHTML
}
```

If jsdom lacks `CSSGroupingRule` as a global, test with `'cssRules' in rule` instead of `instanceof CSSGroupingRule` — the behaviour required is recursion into any rule that holds rules.

**Note on `DOMParser` and a leading `<style>`:** `parseFromString(html, 'text/html')` hoists a leading `<style>` into the `<head>`, so `document.body.querySelectorAll('style')` misses it. Parse `'<body>' + html` instead, in `rewriteStyleElements` and in `darkenColours` (whose existing `document.body.innerHTML` return then keeps the style). Add a test in `messageStyles.test.ts` proving a style at the very start of the fragment is rewritten and still present in the output.

- [ ] **Step 4: Darken stylesheets in `darkenColours.ts`**

Parse `'<body>' + html` (see note above). After the attribute loop, add:

```ts
  for (const style of document.body.querySelectorAll('style')) {
    style.textContent = forEachStyleRule(style.textContent ?? '', declarations => {
      for (let i = 0; i < declarations.length; i++) {
        const property = declarations.item(i)
        const value = declarations.getPropertyValue(property)
        const darkened = property === 'background-image'
          ? darkenImageColours(value)
          : STYLESHEET_COLOUR.test(property) ? toDarkColour(value, roleOf(property)) : null
        if (darkened && darkened !== value)
          declarations.setProperty(property, darkened, declarations.getPropertyPriority(property))
      }
    })
  }
```

with, near `COLOUR_PROPERTIES`:

```ts
/** The same colour properties, as whole names for a CSSOM declaration. */
const STYLESHEET_COLOUR = /^(color|background-color|border(-[a-z]+)?-color|outline-color)$/i
```

Update the doc comment of `darkenColours` to `/** Rewrites every colour a fragment declares: style attributes, colour attributes and stylesheets. */`.

- [ ] **Step 5: Wire `MessageReader.tsx`**

Replace the `sanitized` memo (lines ~149-153):

```ts
  // The sender's own dark design wins over our recolouring, but only when the message declares one
  // and the reader asked for dark; the colour toggle still brings the light original back.
  const senderDark = inverted && data?.declaresDarkScheme === true
  const sanitized = useMemo(() => {
    const html = data?.htmlBody ?? ''
    const revealed = showImages ? revealBlockedImages(html) : html
    const schemed = resolveColourScheme(revealed, senderDark ? 'dark' : 'light')
    return sanitizeBody(inverted && !senderDark ? darkenColours(schemed) : schemed)
  }, [data?.htmlBody, showImages, inverted, senderDark])
```

and line ~365: `srcDoc={renderBodyDocument(body, { dark: inverted, senderDark, remote: showImages, narrow })}`. Import `resolveColourScheme` from `./messageStyles`.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `cd src/frontend && npx vitest run src/modules/mail` then `npm run lint` and `npx tsc -b`
Expected: all PASS.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/modules/mail/reader
git commit -F - <<'EOF'
Honour a sender's dark design; darken a light one's stylesheet too

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014pZFNVjMycnMn6Crm84wUR
EOF
```

---

### Task 6: Measured in Blink, and documented

**Files:**
- Create: `src/frontend/probes/mail-stylesheets.html`
- Modify: `src/frontend/docs/architecture-mail.md` (the *Rendering message HTML* and *Dark mode recolours* paragraphs), `docs/known-issues/webmail-dark-mode-and-css-backgrounds-known-issues.md`

**Interfaces:**
- Consumes: `sanitizeBody`, `renderBodyDocument`, `resolveColourScheme`, `darkenColours` (Tasks 4-5).

- [ ] **Step 1: Write the probe page**

`probes/mail-stylesheets.html` renders a server-sanitised body through the real client pipeline into a sandboxed iframe (same `sandbox` attribute as `MessageReader`), at a chosen width and theme, and reports:
- `overflow`: the iframe document's `scrollWidth - clientWidth`. The product's frame is unreadable from the parent (no same-origin), so the probe renders a second copy of the same width with `sandbox="allow-same-origin"` — still no `allow-scripts`, so nothing in it runs — purely to measure it. That copy exists only in this local probe, never in the product.
- `darkLogo`: in the measuring copy, whether `.dark-img` elements are displayed (the Lottery's logo swap).

It carries one built-in synthetic fixture (a 600px `<table class="wrapper">` with a head `<style>` holding `@media only screen and (max-width: 600px) { .wrapper { width: 100% !important } }`, a `prefers-color-scheme: dark` block swapping a `.light-img`/`.dark-img` pair, and `declaresDarkScheme` true), and a `<textarea id="fixture">` plus a `declaresDarkScheme` checkbox so a real server-sanitised body can be pasted in for a local run. Controls: width (360, 1024), theme (light, dark), remote (off, on). Import the pipeline from `/src/modules/mail/reader/*.ts` the way `localisation-widths.html` imports from `/src/locales`.

- [ ] **Step 2: Produce the two real bodies, outside the repository**

In the scratchpad (`C:/Users/mick0/AppData/Local/Temp/claude/D--development-repos-weesky-net-mail/4596329b-a759-4edc-b839-24437293be91/scratchpad`), a throwaway console project referencing `src/scotty.microservice/scotty.microservice.core.csproj` extracts the HTML part of each `.eml` with MimeKit and writes `MailHtmlSanitizer.Sanitize(html)` (JSON: `html`, `declaresDarkScheme`) to `claude.sanitized.json` and `lotterie.sanitized.json`. `MailHtmlSanitizer` is `internal`: reach it by reflection (`Type.GetType("weesky.Scotty.Microservice.Services.MailHtmlSanitizer, scotty.microservice.core")`, `Activator.CreateInstance(type, nonPublic: true)`) — never commit an `InternalsVisibleTo` for this.

- [ ] **Step 3: Measure**

Run `npm run dev`, open the probe in Chrome (device-metric emulation, not `--window-size`), and for each real body paste it in and read: at 360 wide, `overflow` must be 0 in both themes (it was the horizontal scroll this whole change exists to remove); at 1024 wide, the body must keep its 600-700px desktop column (no stretch); in dark theme the Lottery's `darkLogo` must be true and the Claude mail's content background `#262624`; with remote off, the Network panel shows no request to a font or image host; with remote on, the images load. Record the numbers in the commit body's second line only if they fit; otherwise in the known-issues file.

- [ ] **Step 4: Document**

`architecture-mail.md`, *Rendering message HTML — three independent barriers*: add that the reader keeps the message's `<style>` (the composer does not: `COMPOSER_FORBID_TAGS`), that `FORCE_BODY` is what keeps a leading one, and that the iframe document opens on a CSP widened by the same `showImages` that reveals images — a font is held back by it, since a sheet has no consent surface of its own. *Dark mode recolours*: add the three-case rule (sender's dark design when `declaresDarkScheme` and dark theme; our recolouring, stylesheets included, otherwise; light design in light theme) and that `resolveColourScheme` settles `prefers-color-scheme` itself rather than trusting what the iframe inherits.

`docs/known-issues/webmail-dark-mode-and-css-backgrounds-known-issues.md`, under *Worth fixing*, add **A quoted message's classes reach the composer**: `PrepareQuote` keeps `class` (the outgoing sanitiser's Ganss defaults) and `SquireEditor`'s DOMPurify keeps it too, so a quoted `class="header"` can pick up a style of the webmail's own inside the editor. Pre-existing and independent of the stylesheet work; not fixed there because Squire relies on its own classes. Under *Known and accepted*, add: a remote font does not raise the blocked-images banner, so a message with no remote image keeps its fallback font unless images are always shown or the sender is trusted.

- [ ] **Step 5: Run the whole frontend suite and commit**

Run: `cd src/frontend && npx vitest run && npm run lint && npx tsc -b`
Expected: all PASS.

```bash
git add src/frontend/probes/mail-stylesheets.html src/frontend/docs/architecture-mail.md docs/known-issues/webmail-dark-mode-and-css-backgrounds-known-issues.md
git commit -F - <<'EOF'
Probe and document the reader's handling of message stylesheets

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_014pZFNVjMycnMn6Crm84wUR
EOF
```
