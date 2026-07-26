# Restorable CSS background images — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A remote image declared in CSS is withheld like an `<img src>` and restored on the same consent, instead of being deleted before the reader can ask for it.

**Architecture:** The display sanitiser stops culling `background-image` outright. A remote `url()` moves into a `data-blocked-bg` attribute and counts toward `blockedImageCount`; a `cid:` one stays in the CSS for the client to resolve into a data URI; anything else is culled as today. The reader restores the withheld URLs by appending a `background-image` declaration when the user consents, and resolves `cid:` backgrounds through the inline-image path it already runs for `<img>`.

**Tech Stack:** ASP.NET Core 10 + Ganss.Xss + AngleSharp (backend), React + DOMPurify + Vitest (frontend).

## Global Constraints

- The design spec is `docs/superpowers/specs/2026-07-26-webmail-css-background-images-design.md`. Read it before Task 1.
- Only `background-image` is in scope. `list-style-image`, `border-image`, `mask-image`, `cursor` keep the current cull.
- Only `http:` and `https:` URLs are withheld. Every other scheme that reaches the pass is culled, and `data:` never reaches it — Ganss removes it first.
- A declaration containing a backslash is culled outright, with no attribute, whatever its property.
- `data-blocked-bg` must NOT be added to `_sanitizer.AllowedAttributes`: keeping it out is what stops a message from smuggling a pre-set one, since only our own post-Ganss pass may create it.
- Both sides validate the scheme independently. DOMPurify does not — measured, it passes `background-image: url(javascript:alert(1))` through untouched.
- Backend: `dotnet test` from `src/snoopy.microservice` (not `--no-build`, new test files appear here).
- Frontend: `npx vitest run <path>` from `src/frontend`; `npm run lint` and `npm run typecheck` before the last commit.
- C# style: file-scoped namespace, `internal` by default, expression bodies where they read better, no comment restating the code.

---

### Task 1: Narrow the cull from the letters `url` to the function `url(`

**Files:**
- Modify: `src/snoopy.microservice/Services/MailHtmlSanitizer.cs:101-112`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailHtmlSanitizerTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: the loop over `[style]` in `Sanitize`, now testing `url(` rather than `url`. Task 2 rewrites the body of that same loop.

- [ ] **Step 1: Write the failing test**

Add to `MailHtmlSanitizerTests.cs`, next to the other CSS tests:

```csharp
// The cull targets the url( function. A declaration merely containing those three letters —
// a font really named Curly — is not a fetch and must survive.
[Fact]
public void Sanitize_KeepsADeclarationWhoseValueMerelyContainsTheLettersUrl()
{
    var result = _sut.Sanitize("<div style=\"font-family: Curly\">x</div>").Html;

    Assert.Contains("Curly", result, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `cd src/snoopy.microservice && dotnet test --filter "FullyQualifiedName~MailHtmlSanitizerTests.Sanitize_KeepsADeclarationWhoseValueMerelyContainsTheLettersUrl"`
Expected: FAIL — the declaration is culled, so `Curly` is absent.

- [ ] **Step 3: Narrow the predicate**

In `MailHtmlSanitizer.Sanitize`, replace the two `Contains("url", …)` tests with `Contains("url(", …)`:

```csharp
foreach (var styled in document.QuerySelectorAll("[style]"))
{
    var style = styled.GetAttribute("style")!;
    if (!style.Contains("url(", StringComparison.OrdinalIgnoreCase) && !style.Contains('\\')) continue;

    var kept = style.Split(';').Where(declaration =>
        !declaration.Contains("url(", StringComparison.OrdinalIgnoreCase)
        && !declaration.Contains('\\'));
    styled.SetAttribute("style", string.Join(';', kept));
}
```

- [ ] **Step 4: Run the whole sanitiser suite**

Run: `cd src/snoopy.microservice && dotnet test --filter "FullyQualifiedName~MailHtmlSanitizerTests"`
Expected: PASS, including the existing `Sanitize_NeverKeepsACssUrl` and `Sanitize_NeverKeepsAUrlInASheet`.

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Services/MailHtmlSanitizer.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/MailHtmlSanitizerTests.cs
git commit -m "Cull CSS on the url( function, not on the letters url"
```

---

### Task 2: Withhold remote `background-image` URLs into `data-blocked-bg`

**Files:**
- Modify: `src/snoopy.microservice/Services/MailHtmlSanitizer.cs` (the `[style]` loop, and a new private helper)
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailHtmlSanitizerTests.cs` (new tests, plus an edit to `Sanitize_NeverKeepsACssUrl`)

**Interfaces:**
- Consumes: the narrowed loop from Task 1.
- Produces: the wire contract every later task reads — `data-blocked-bg="<url> <url>"`, space-separated because a serialised URL cannot contain a raw space; `SanitizedHtml.BlockedImageCount` counting one per withheld URL; `url(cid:…)` left untouched inside the `style` attribute.

- [ ] **Step 1: Write the failing tests**

`Sanitize_NeverKeepsACssUrl` currently asserts that a `background`/`background-image` URL disappears entirely; that is exactly what stops being true. Replace its two background rows so it covers only the properties still culled:

```csharp
// A url() in a property outside the withholding rule would fetch without consent.
[Theory]
[InlineData("<div style=\"border-image: url(http://evil.example/pix.gif)\">x</div>")]
[InlineData("<div style=\"list-style-image: url(http://evil.example/pix.gif)\">x</div>")]
public void Sanitize_NeverKeepsACssUrl(string html)
{
    var result = _sut.Sanitize(html).Html;

    Assert.DoesNotContain("evil.example", result);
    Assert.Contains("x", result);
}
```

Then add the new behaviour:

```csharp
// Withheld like an <img src>: the URL survives inert, out of the CSS, and counts toward the
// banner. Nothing fetches until the reader consents.
[Fact]
public void Sanitize_MovesARemoteBackgroundToDataBlockedBgAndCountsIt()
{
    var result = _sut.Sanitize(
        "<div style=\"background-image: url(https://cdn.example/logo.png); background-size: contain\">x</div>");

    Assert.Equal(1, result.BlockedImageCount);
    Assert.Contains("data-blocked-bg=\"https://cdn.example/logo.png\"", result.Html);
    Assert.DoesNotContain("url(", result.Html);
    Assert.Contains("background-size", result.Html);
}

// The shorthand reaches this pass already expanded by Ganss, so quoting is whatever it
// serialised; the rule must read every form it can produce.
[Fact]
public void Sanitize_WithholdsAQuotedBackgroundUrl()
{
    var result = _sut.Sanitize(
        "<div style=\"background: url('https://cdn.example/logo.png') center / contain no-repeat #fff\">x</div>");

    Assert.Equal(1, result.BlockedImageCount);
    Assert.Contains("data-blocked-bg=\"https://cdn.example/logo.png\"", result.Html);
}

// Today the gradient dies with the image it shares a declaration with.
[Fact]
public void Sanitize_KeepsAGradientSharingTheDeclarationWithAWithheldLayer()
{
    var result = _sut.Sanitize(
        "<div style=\"background-image: linear-gradient(to right, #000, #fff), url(https://cdn.example/l.png)\">x</div>");

    Assert.Contains("linear-gradient", result.Html);
    Assert.Contains("data-blocked-bg=\"https://cdn.example/l.png\"", result.Html);
}

[Fact]
public void Sanitize_WithholdsEveryLayerInOrder()
{
    var result = _sut.Sanitize(
        "<div style=\"background-image: url(https://a.example/1.png), url(https://b.example/2.png)\">x</div>");

    Assert.Equal(2, result.BlockedImageCount);
    Assert.Contains("data-blocked-bg=\"https://a.example/1.png https://b.example/2.png\"", result.Html);
}

// The bytes never leave the mailbox, so there is nothing to consent to: the client resolves it.
[Fact]
public void Sanitize_LeavesACidBackgroundInTheCss()
{
    var result = _sut.Sanitize("<div style=\"background-image: url(cid:logo@mail)\">x</div>");

    Assert.Equal(0, result.BlockedImageCount);
    Assert.Contains("cid:logo@mail", result.Html);
    Assert.DoesNotContain("data-blocked-bg", result.Html);
}

// An escape can spell the same function past a naive reader, so the row is not worth an exception.
[Fact]
public void Sanitize_CullsABackgroundDeclarationCarryingABackslash()
{
    var result = _sut.Sanitize(
        "<div style=\"background-image: \\75 rl(https://cdn.example/l.png)\">x</div>");

    Assert.Equal(0, result.BlockedImageCount);
    Assert.DoesNotContain("cdn.example", result.Html);
    Assert.DoesNotContain("data-blocked-bg", result.Html);
}

// Only our own post-Ganss pass may create the attribute; a message cannot arrive carrying one.
[Fact]
public void Sanitize_DropsADataBlockedBgTheMessageBrought()
{
    var result = _sut.Sanitize("<div data-blocked-bg=\"https://evil.example/p.gif\">x</div>");

    Assert.DoesNotContain("evil.example", result.Html);
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `cd src/snoopy.microservice && dotnet test --filter "FullyQualifiedName~MailHtmlSanitizerTests"`
Expected: the six new `data-blocked-bg` / gradient / cid tests FAIL; `Sanitize_DropsADataBlockedBgTheMessageBrought` already passes (Ganss strips the unknown attribute), which is the point of pinning it.

- [ ] **Step 3: Implement the rule**

Add the constant beside `BlockedSrcAttribute` at the top of `MailHtmlSanitizer`:

```csharp
private const string BlockedBackgroundAttribute = "data-blocked-bg";

// Every serialisation AngleSharp can hand us: quoted either way, or bare.
private static readonly Regex CssUrl = new(
    @"url\(\s*(?:""(?<u>[^""]*)""|'(?<u>[^']*)'|(?<u>[^)\s]*))\s*\)",
    RegexOptions.IgnoreCase | RegexOptions.Compiled);
```

Replace the `[style]` loop with:

```csharp
foreach (var styled in document.QuerySelectorAll("[style]"))
{
    var style = styled.GetAttribute("style")!;
    if (!style.Contains("url(", StringComparison.OrdinalIgnoreCase) && !style.Contains('\\')) continue;

    var withheld = new List<string>();
    var kept = new List<string>();

    foreach (var declaration in style.Split(';'))
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
```

`blocked` must therefore be declared **above** this loop rather than below it — move the existing `var blocked = 0;` up.

Add the two helpers, after `Sanitize`:

```csharp
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
    var kept = new List<string>();

    foreach (var layer in SplitLayers(declaration[(colon + 1)..]))
    {
        var match = CssUrl.Match(layer);
        if (!match.Success) { kept.Add(layer.Trim()); continue; }

        var url = match.Groups["u"].Value.Trim();
        if (url.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)) { kept.Add(layer.Trim()); continue; }
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
            withheld.Add(url);
    }

    return kept.Count == 0 ? null : $"{declaration[..colon]}:{string.Join(", ", kept)}";
}

/// <summary>Top-level comma split: a comma inside url(…) or a gradient's parentheses is not one.</summary>
private static IEnumerable<string> SplitLayers(string value)
{
    var depth = 0;
    var start = 0;
    for (var i = 0; i < value.Length; i++)
    {
        if (value[i] == '(') depth++;
        else if (value[i] == ')') depth--;
        else if (value[i] == ',' && depth == 0)
        {
            yield return value[start..i];
            start = i + 1;
        }
    }
    yield return value[start..];
}
```

Add `using System.Text.RegularExpressions;` at the top of the file.

- [ ] **Step 4: Run the suite**

Run: `cd src/snoopy.microservice && dotnet test`
Expected: PASS, all of it. If `Sanitize_WithholdsAQuotedBackgroundUrl` fails on the attribute value, print the actual `result.Html` in the test to see what Ganss serialised, and widen `CssUrl` — do not change the assertion to match a broken value.

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice
git commit -m "Withhold remote background images instead of culling them"
```

---

### Task 3: Keep the attribute through DOMPurify and restore it on consent

**Files:**
- Modify: `src/frontend/src/modules/mail/reader/sanitizeBody.ts`
- Test: `src/frontend/src/modules/mail/reader/sanitizeBody.test.ts`

**Interfaces:**
- Consumes: `data-blocked-bg="<url> <url>"` from Task 2.
- Produces: `revealBlockedImages(html)` restoring `background-image: url("…")`; `sanitizeBody` preserving `data-blocked-bg`.

- [ ] **Step 1: Write the failing tests**

Add to `sanitizeBody.test.ts`:

```ts
describe('revealBlockedImages — backgrounds', () => {
  it('appends the withheld background to the style', () => {
    const html = '<div data-blocked-bg="https://cdn.example/l.png" style="background-size: contain"></div>'

    const revealed = revealBlockedImages(html)

    expect(revealed).toContain('background-size: contain')
    expect(revealed).toContain('background-image: url("https://cdn.example/l.png")')
  })

  it('restores every layer in order', () => {
    const html = '<div data-blocked-bg="https://a.example/1.png https://b.example/2.png"></div>'

    expect(revealBlockedImages(html)).toContain(
      'background-image: url("https://a.example/1.png"), url("https://b.example/2.png")')
  })

  // The backend already refuses these, and the client refuses them again: DOMPurify does not.
  it('restores nothing for a scheme that is not http(s)', () => {
    const html = '<div data-blocked-bg="javascript:alert(1)"></div>'

    expect(revealBlockedImages(html)).not.toContain('background-image')
  })

  // A forged URL must not be able to close the url() and append declarations of its own.
  it('encodes the quotes and backslashes of a forged url', () => {
    const html = '<div data-blocked-bg="https://x.example/a&quot;);position:fixed;a:url(&quot;"></div>'

    const revealed = revealBlockedImages(html)

    expect(revealed).not.toContain('position:fixed')
    expect(revealed).toContain('%22')
  })

  it('leaves a body carrying no withheld background untouched', () => {
    const html = '<p>Bonjour</p>'

    expect(revealBlockedImages(html)).toBe(html)
  })
})

it('keeps data-blocked-bg through the sanitising pass', () => {
  const html = '<div data-blocked-bg="https://cdn.example/l.png"></div>'

  expect(sanitizeBody(html)).toContain('data-blocked-bg')
})
```

- [ ] **Step 2: Run them and watch them fail**

Run: `cd src/frontend && npx vitest run src/modules/mail/reader/sanitizeBody.test.ts`
Expected: FAIL — no background is ever appended, and DOMPurify strips the attribute.

- [ ] **Step 3: Implement**

In `sanitizeBody.ts`, add `data-blocked-bg` to `ADD_ATTR`:

```ts
    ADD_ATTR: ['data-blocked-src', 'data-blocked-bg', 'target'],
```

and replace `revealBlockedImages`:

```ts
const BLOCKED_BACKGROUND = 'data-blocked-bg'

/**
 * A withheld URL is only ever re-entered inside url("…") after its scheme is checked and its
 * quotes and backslashes are encoded: DOMPurify validates neither — measured, it passes
 * url(javascript:…) straight through — so this is the only gate on the client side.
 */
function restorable(raw: string): string | null {
  let parsed: URL
  try {
    parsed = new URL(raw)
  } catch {
    return null
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') return null
  return parsed.href.replace(/["\\]/g, encodeURIComponent)
}

/**
 * Restores remote images, on explicit user consent only. Runs before sanitising, so the
 * restored URLs are subject to the same pass as everything else.
 *
 * Backgrounds are appended rather than merged into the declaration they came from: the last
 * declaration wins, so the image comes back without re-assembling a value we never parsed.
 */
export function revealBlockedImages(html: string): string {
  const revealed = html.replace(/data-blocked-src=/g, 'src=')
  if (!revealed.includes(BLOCKED_BACKGROUND)) return revealed

  const doc = new DOMParser().parseFromString(revealed, 'text/html')
  for (const element of doc.querySelectorAll(`[${BLOCKED_BACKGROUND}]`)) {
    const layers = (element.getAttribute(BLOCKED_BACKGROUND) ?? '')
      .split(/\s+/)
      .map(restorable)
      .filter((url): url is string => url !== null)
    if (layers.length === 0) continue

    const style = element.getAttribute('style')?.trim() ?? ''
    const image = layers.map(url => `url("${url}")`).join(', ')
    const separator = style === '' || style.endsWith(';') ? '' : ';'
    element.setAttribute('style', `${style}${separator}background-image: ${image}`)
  }
  return doc.body.innerHTML
}
```

- [ ] **Step 4: Run the tests**

Run: `cd src/frontend && npx vitest run src/modules/mail/reader/sanitizeBody.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/reader/sanitizeBody.ts src/frontend/src/modules/mail/reader/sanitizeBody.test.ts
git commit -m "Restore withheld background images on consent"
```

---

### Task 4: Resolve `cid:` backgrounds into data URIs

**Files:**
- Modify: `src/frontend/src/modules/mail/reader/inlineImages.ts`
- Test: `src/frontend/src/modules/mail/reader/inlineImages.test.ts`

**Interfaces:**
- Consumes: `url(cid:…)` left in the `style` attribute by Task 2.
- Produces: `referencedCids` and `substituteInlineImages` covering both `img[src]` and `style` backgrounds. `useInlineImages.bodyInlineParts` and the reader's attachment filter get the new coverage for free, since both already call these two.

- [ ] **Step 1: Write the failing tests**

Add to `inlineImages.test.ts`:

```ts
it('reports a cid referenced from a css background', () => {
  const html = '<div style="background-image: url(cid:logo@mail)"></div>'

  expect(referencedCids(html)).toEqual(['logo@mail'])
})

it('reports a quoted cid background', () => {
  const html = `<div style="background-image: url('cid:logo@mail')"></div>`

  expect(referencedCids(html)).toEqual(['logo@mail'])
})

it('deduplicates a cid referenced by both an img and a background', () => {
  const html = '<img src="cid:logo@mail"><div style="background-image: url(cid:logo@mail)"></div>'

  expect(referencedCids(html)).toEqual(['logo@mail'])
})

it('substitutes a cid background with the data uri', () => {
  const html = '<div style="background-size: contain; background-image: url(cid:logo@mail)"></div>'

  const result = substituteInlineImages(html, { 'logo@mail': 'data:image/png;base64,AAA' })

  expect(result).toContain('url("data:image/png;base64,AAA")')
  expect(result).toContain('background-size: contain')
  expect(result).not.toContain('cid:')
})

it('leaves a background whose cid the map does not carry', () => {
  const html = '<div style="background-image: url(cid:missing@mail)"></div>'

  expect(substituteInlineImages(html, { 'other@mail': 'data:image/png;base64,AAA' })).toBe(html)
})
```

- [ ] **Step 2: Run them and watch them fail**

Run: `cd src/frontend && npx vitest run src/modules/mail/reader/inlineImages.test.ts`
Expected: FAIL — `referencedCids` returns `[]` for a background, and `substituteInlineImages` leaves the `cid:` in place.

- [ ] **Step 3: Implement**

In `inlineImages.ts`, add beside `cidOf`:

```ts
/** `url(cid:X)` in a style attribute, quoted either way or bare. */
const CSS_CID = /url\(\s*(?:"cid:([^"]*)"|'cid:([^']*)'|cid:([^)\s]*))\s*\)/gi

const cidsInStyle = (style: string): string[] =>
  [...style.matchAll(CSS_CID)].map(match => match[1] ?? match[2] ?? match[3]).filter(Boolean)
```

Extend `referencedCids` to walk styled elements as well as images:

```ts
export function referencedCids(html: string): string[] {
  if (!html) return []

  const doc = parse(html)
  const cids = new Set<string>()
  for (const img of doc.querySelectorAll('img')) {
    const cid = cidOf(img.getAttribute('src'))
    if (cid) cids.add(cid)
  }
  for (const styled of doc.querySelectorAll('[style]'))
    for (const cid of cidsInStyle(styled.getAttribute('style') ?? '')) cids.add(cid)

  return [...cids]
}
```

and `substituteInlineImages` to rewrite them:

```ts
  for (const styled of doc.querySelectorAll('[style]')) {
    const style = styled.getAttribute('style') ?? ''
    if (!style.includes('cid:')) continue

    const rewritten = style.replace(CSS_CID, (whole, quoted, single, bare) => {
      const uri = dataUriByCid[quoted ?? single ?? bare]
      return typeof uri === 'string' ? `url("${uri}")` : whole
    })
    if (rewritten === style) continue

    styled.setAttribute('style', rewritten)
    substituted = true
  }
```

placed after the existing `img` loop and before the `return`.

- [ ] **Step 4: Run the reader suite**

Run: `cd src/frontend && npx vitest run src/modules/mail/reader/`
Expected: PASS, including `useInlineImages` and `MessageReader` — both consume these two helpers unchanged.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/reader/inlineImages.ts src/frontend/src/modules/mail/reader/inlineImages.test.ts
git commit -m "Resolve cid backgrounds like cid images"
```

---

### Task 5: Pin the reader's end-to-end behaviour and ship

**Files:**
- Test: `src/frontend/src/modules/mail/reader/MessageReader.test.tsx`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing new — this task only pins the composed behaviour and runs the gates.

- [ ] **Step 1: Write the failing test**

Add to `MessageReader.test.tsx`, beside the other attachment tests:

```tsx
// A part used only as a CSS background is displayed by the body, so it is not a chip either —
// the rule the attachment row already applies to a cid image, reaching its second producer.
it('hides an attachment used only as a css background', async () => {
  mocks.getMailMessage.mockResolvedValue({
    ...detail,
    htmlBody: '<div style="background-image: url(cid:logo@mail)">Bonjour</div>',
    attachments: [
      {
        part: '3', fileName: 'logo.png', contentType: 'image/png', size: 10,
        isInline: false, contentId: 'logo@mail',
      },
      {
        part: '4', fileName: 'joint.pdf', contentType: 'application/pdf', size: 10,
        isInline: false, contentId: null,
      },
    ],
  })

  render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
  await screen.findByRole('button', { name: /joint\.pdf/ })

  expect(screen.queryByRole('button', { name: /logo\.png/ })).not.toBeInTheDocument()
})
```

- [ ] **Step 2: Run it**

Run: `cd src/frontend && npx vitest run src/modules/mail/reader/MessageReader.test.tsx -t "css background"`
Expected: PASS — Task 4 is what makes it pass. If it fails, the fault is in Task 4's `referencedCids`, not here.

- [ ] **Step 3: Run every gate**

Run, from `src/frontend`: `npx vitest run` then `npm run lint` then `npm run typecheck`
Run, from `src/snoopy.microservice`: `dotnet test`
Expected: all green; lint reports its 3 pre-existing warnings and no error.

- [ ] **Step 4: Verify in the browser, not only in the tests**

Push the branch, wait for the dev deploy, then open `https://snoopy-dev.mail.weesky.net/mail?folder=INBOX&uid=15679`, click "Show images", and confirm the Anthropic logo fills the 32px circle beside "Anthropic, PBC". Before consent, confirm the circle is still empty and the body carries no remote URL:

```js
const s = document.querySelector('iframe').getAttribute('srcdoc')
console.log(/url\(\s*['"]?https?:/i.test(s))   // must be false before consent
```

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/reader/MessageReader.test.tsx
git commit -m "Pin the chip rule for a background-only attachment"
```

---

## Self-review

**Spec coverage.** Narrowed cull → Task 1. Server withholding table, layers, gradient, backslash, smuggled attribute → Task 2. `ADD_ATTR`, restore, validation, escaping → Task 3. `cid:` backgrounds and the chip consequence → Tasks 4 and 5. Banner count needs no task: `blockedImageCount` already drives it and Task 2 feeds it.

**Type consistency.** `data-blocked-bg` is space-separated in Task 2 and split on `/\s+/` in Task 3. `CssUrl` (C#, named group `u`) and `CSS_CID` (TS, three alternates) are separate expressions for separate jobs and are not shared. `bodyInlineParts` keeps the signature Task 4 leaves untouched.

**Known risk.** Task 2 asserts the exact serialisation Ganss produces for a quoted URL. Step 4 says what to do if it differs: widen the regex, never relax the assertion.
