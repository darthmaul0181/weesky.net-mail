# Compose Format Preference Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A setting in Settings › General › Composing that opens the composer in HTML or in plain text, for every composer but a resumed draft.

**Architecture:** One new registry entry (`mail.composeFormat`, `html`|`text`, default `html`) declared in the C# preference registry and mirrored in the frontend's key map with a `composeFormatOf` accessor. One new pure function, `applyComposeFormat`, transforms a compose seed into the chosen format — converting the quoted HTML with the existing `htmlToText` and clearing each attachment's `contentId` so an inline image falls into the tray. `ComposeView` applies it once at mount and holds a `LoadingBlock` until preferences arrive, because reading a preference at mount is new here and a late flip would discard what the user typed.

**Tech Stack:** ASP.NET Core (.NET 10) + xUnit for the registry; React 18 + TypeScript + Vitest/jsdom/@testing-library for the frontend.

**Spec:** `docs/superpowers/specs/2026-07-31-webmail-compose-format-preference-design.md` — read it before Task 2. It supersedes the "Not a preference" paragraph of `2026-07-31-webmail-plain-text-compose-design.md`.

## Global Constraints

- **Key `mail.composeFormat`, values `html` and `text`, default `html`.** Exactly these strings; the value is a symbol, not prose, and `HTML` must be refused.
- **`mail.`, not `compose.`** — the registry names a key after the effect, and the effect is on the mail composer.
- **The draft carve-out keys on `seed.action === 'draft'`, never on `seed.text`.** An HTML draft carries `text: null` exactly like a reply, so a `text`-body test cannot see it and would discard the draft's body.
- **The preference path never raises the `losesFormatting` confirm dialog.** The user chose in Settings; asking again on every reply is noise.
- **Await preference-derived assertions with `findBy`/`waitFor`, never `settle()`.** `settle()` drains a single macrotask and has already raced this query on CI while passing locally. This slice adds a *mount-time* dependency on it, which is the strongest form of that hazard.
- **Commit messages: two lines max, never beginning or ending with `@`.** Use `git commit -F -` with a heredoc, never a PowerShell here-string.
- **`dotnet test`, not `dotnet test --no-build`** — Task 1 touches an existing test file but the rule is cheap to keep.
- **Do not push.** Pushing deploys.
- **`ApiDocumentation.xml` is a versioned artefact `dotnet test` regenerates with unrelated churn.** Revert it before committing if it appears in `git status`.

## File Structure

| File | Responsibility |
|---|---|
| `src/snoopy.microservice/Models/UserPreferences.cs` | Gains `MailComposeFormat` and its `All` entry. |
| `src/snoopy.microservice/snoopy.microservice.Tests/Models/UserPreferencesTests.cs` | Gains the key, default and validity cases. |
| `src/frontend/src/hooks/usePreferences.ts` | Gains `PREFERENCE_KEYS.composeFormat`, the `ComposeFormat` type and `composeFormatOf`. |
| `src/frontend/src/hooks/usePreferences.test.tsx` | Gains the accessor's cases. |
| `src/frontend/src/modules/mail/compose/composeSeed.ts` | Gains `applyComposeFormat`, pure. |
| `src/frontend/src/modules/mail/compose/composeSeed.test.ts` | Gains its cases, the two carve-out assertions among them. |
| `src/frontend/src/modules/mail/compose/ComposeView.tsx` | Applies it at mount; holds `LoadingBlock` until preferences land. |
| `src/frontend/src/modules/mail/compose/ComposeView.test.tsx` | Gains the two mount cases. |
| `src/frontend/src/modules/settings/general/GeneralPage.tsx` | Gains the row in the Composing section. |
| `src/frontend/src/modules/settings/general/GeneralPage.test.tsx` | Gains the row's case. |
| `src/frontend/CLAUDE.md` | Gains the contract note. |

---

### Task 1: Declare the preference, end to end

The key exists, the backend validates it, the frontend can read it. Nothing consumes it yet, which is what makes this reviewable on its own: `GET /api/Preferences` answers the new key with `html` and no screen changes.

**Files:**
- Modify: `src/snoopy.microservice/Models/UserPreferences.cs`
- Modify: `src/snoopy.microservice/snoopy.microservice.Tests/Models/UserPreferencesTests.cs`
- Modify: `src/frontend/src/hooks/usePreferences.ts`
- Modify: `src/frontend/src/hooks/usePreferences.test.tsx`

**Interfaces:**
- Consumes: nothing.
- Produces: `UserPreferences.MailComposeFormat` (C# const, `"mail.composeFormat"`); `PREFERENCE_KEYS.composeFormat`; `export type ComposeFormat = 'html' | 'text'`; `export function composeFormatOf(preferences: Preferences): ComposeFormat`. Tasks 2 and 3 use the last three by these exact names.

- [ ] **Step 1: Write the failing C# cases**

In `UserPreferencesTests.cs`, add the key to the existing `All_CarriesTheKeysTheClientOffers` fact:

```csharp
        Assert.Contains(UserPreferences.All, p => p.Key == UserPreferences.MailComposeFormat);
```

Add the default to the existing `Default_IsTheValueAnAccountWithNoRowsGets` theory:

```csharp
    [InlineData(UserPreferences.MailComposeFormat, "html")]
```

Add a validity theory of its own beneath the existing ones:

```csharp
    [Theory]
    [InlineData("html", true)]
    [InlineData("text", true)]
    [InlineData("HTML", false)]      // the value is a symbol, not prose
    [InlineData("plain", false)]     // the toolbar's label is not the stored value
    [InlineData("", false)]
    public void ComposeFormat_AcceptsTheTwoEditors(string value, bool expected)
    {
        Assert.Equal(expected, UserPreferences.IsValid(UserPreferences.MailComposeFormat, value));
    }
```

- [ ] **Step 2: Run them and watch them fail**

```bash
cd src/snoopy.microservice && dotnet test --filter UserPreferencesTests
```
Expected: compile error — `MailComposeFormat` does not exist.

- [ ] **Step 3: Declare it in the registry**

In `src/snoopy.microservice/Models/UserPreferences.cs`, add the constant beside `MailRowActions`:

```csharp
    public const string MailComposeFormat = "mail.composeFormat";
```

and the entry in `All`, after the `MailRowActions` line:

```csharp
        // Governs every composer but a resumed draft, which reopens in the format it was saved in.
        // An enumeration rather than a boolean: it leaves room for a "follow the original" value
        // without a key migration.
        new(MailComposeFormat, "html", ["html", "text"]),
```

- [ ] **Step 4: Run the C# tests**

```bash
cd src/snoopy.microservice && dotnet test --filter UserPreferencesTests
```
Expected: PASS.

- [ ] **Step 5: Write the failing frontend cases**

In `src/frontend/src/hooks/usePreferences.test.tsx`, add:

```tsx
describe('composeFormatOf', () => {
  it('reads the stored editor', () => {
    expect(composeFormatOf({ 'mail.composeFormat': 'text' })).toBe('text')
    expect(composeFormatOf({ 'mail.composeFormat': 'html' })).toBe('html')
  })

  // An older backend does not send the key at all, and today's composer is the HTML one.
  it('falls back to html on an absent or unrecognised value', () => {
    expect(composeFormatOf({})).toBe('html')
    expect(composeFormatOf({ 'mail.composeFormat': 'plain' })).toBe('html')
  })
})
```

Add `composeFormatOf` to that file's existing import from `./usePreferences`.

- [ ] **Step 6: Run it and watch it fail**

```bash
cd src/frontend && npx vitest run src/hooks/usePreferences.test.tsx
```
Expected: FAIL — `composeFormatOf` is not exported.

- [ ] **Step 7: Add the key and the accessor**

In `src/frontend/src/hooks/usePreferences.ts`, add to `PREFERENCE_KEYS`, after `rowActions`:

```ts
  composeFormat: 'mail.composeFormat',
```

and the accessor beside `readingPaneOf`, which it mirrors:

```ts
export type ComposeFormat = 'html' | 'text'

/** Which editor a composer opens in. `html` unless the account explicitly chose otherwise, so an
    unrecognised value from a newer build never leaves the user in an editor they did not pick. */
export function composeFormatOf(preferences: Preferences): ComposeFormat {
  return preferences[PREFERENCE_KEYS.composeFormat] === 'text' ? 'text' : 'html'
}
```

- [ ] **Step 8: Run the frontend checks**

```bash
cd src/frontend && npx vitest run src/hooks/usePreferences.test.tsx && npm run lint && npm run typecheck
```
Expected: all pass.

- [ ] **Step 9: Commit**

```bash
git status --short   # revert ApiDocumentation.xml if dotnet test regenerated it
git add src/snoopy.microservice/Models/UserPreferences.cs \
        src/snoopy.microservice/snoopy.microservice.Tests/Models/UserPreferencesTests.cs \
        src/frontend/src/hooks/usePreferences.ts src/frontend/src/hooks/usePreferences.test.tsx
git commit -F - <<'EOF'
Declare mail.composeFormat

Registry entry, key and accessor. Nothing reads it yet.
EOF
```

---

### Task 2: The composer honours it

The behaviour. A pure transform over a seed, applied once at mount, behind a gate that stops the editor rendering before the preference is known.

**Files:**
- Modify: `src/frontend/src/modules/mail/compose/composeSeed.ts`
- Modify: `src/frontend/src/modules/mail/compose/composeSeed.test.ts`
- Modify: `src/frontend/src/modules/mail/compose/ComposeView.tsx` — around `:79` (preferences), `:90-91` (the seed memo), `:105` (the `text` state)
- Modify: `src/frontend/src/modules/mail/compose/ComposeView.test.tsx`

**Interfaces:**
- Consumes: `composeFormatOf`, `ComposeFormat` from Task 1.
- Produces: `export function applyComposeFormat(seed: ComposeSeed | null, format: ComposeFormat): ComposeSeed | null`. Task 3 does not use it.

**`MessageReader` is not touched.** It calls `buildComposeSeed` and must not learn what a composing format is; the transform belongs to the composer that consumes the seed, not to the screen that builds it.

- [ ] **Step 1: Read the spec's two middle sections**

Read "Where it applies" and "The mount-order constraint" in the spec. The `seed.action` discriminator and the `LoadingBlock` gate are both cases where the obvious implementation is wrong in a way no type error catches.

- [ ] **Step 2: Write the failing pure tests**

In `src/frontend/src/modules/mail/compose/composeSeed.test.ts`, add:

```ts
import { applyComposeFormat, type ComposeSeed } from './composeSeed'

// Mirrors ComposeSeed at composeSeed.ts:20. `html` is a required string, `nameHints` a record,
// and StagedAttachmentInfo (mailTypes.ts:169) spells the file name `fileName` with `contentId`
// a required `string | null` — none of the four is optional.
function seedOf(over: Partial<ComposeSeed> = {}): ComposeSeed {
  return {
    action: 'reply', to: [], cc: [], bcc: [], subject: 'Re: hi',
    html: '<p>mine</p><blockquote><p>yours</p></blockquote>', text: null,
    fromAddress: null, attachments: [], inReplyTo: null, references: [],
    priority: 'normal', draftRef: null, nameHints: {}, ...over,
  }
}

describe('applyComposeFormat', () => {
  it('returns its input untouched in html mode', () => {
    const seed = seedOf()
    expect(applyComposeFormat(seed, 'html')).toBe(seed)
  })

  it('answers null for null', () => {
    expect(applyComposeFormat(null, 'text')).toBeNull()
  })

  it('converts the quote, prefixing it', () => {
    const out = applyComposeFormat(seedOf(), 'text')!
    expect(out.text).toContain('> yours')
    expect(out.text).toContain('mine')
    expect(out.html).toBe('')
  })

  // The one field that moves an inline image into the tray: ComposeView splits on it already.
  it('nulls every contentId so an inline image becomes an attachment', () => {
    const out = applyComposeFormat(seedOf({
      attachments: [{ id: 'a', fileName: 'logo.png', size: 1, contentType: 'image/png', contentId: 'cid1' }],
    }), 'text')!
    expect(out.attachments[0].contentId).toBeNull()
  })

  // Two carve-out assertions, because they fail on different mistakes. The HTML draft is the one a
  // `seed.text` test cannot see: it carries text: null exactly like a reply does.
  it('leaves an html draft alone', () => {
    const draft = seedOf({ action: 'draft', text: null, html: '<p>saved</p>' })
    expect(applyComposeFormat(draft, 'text')).toBe(draft)
  })

  it('leaves a text draft alone', () => {
    const draft = seedOf({ action: 'draft', text: 'saved', html: '' })
    expect(applyComposeFormat(draft, 'text')).toBe(draft)
  })
})
```

- [ ] **Step 3: Run them and watch them fail**

```bash
cd src/frontend && npx vitest run src/modules/mail/compose/composeSeed.test.ts
```
Expected: FAIL — `applyComposeFormat` is not exported.

- [ ] **Step 4: Write the pure function**

In `src/frontend/src/modules/mail/compose/composeSeed.ts`, add at the end, importing `htmlToText` from `./bodyFormat` and `ComposeFormat` from `../../../hooks/usePreferences`:

```ts
/**
 * The account's chosen editor, applied to a seed before the composer mounts.
 *
 * A resumed draft is exempt and the test is `action`, never `text`: an HTML draft carries
 * `text: null` exactly like a reply does, so a body test cannot tell the two apart and would
 * discard what the draft holds. `ComposeView` draws the same boundary for its dirty flag.
 *
 * Clearing `contentId` is the whole of the attachment handling: the composer already splits the
 * seed's attachments on that field, so an inline image lands in the tray with nothing else to do.
 */
export function applyComposeFormat(
  seed: ComposeSeed | null, format: ComposeFormat,
): ComposeSeed | null {
  if (seed === null || format === 'html' || seed.action === 'draft') return seed

  return {
    ...seed,
    html: '',
    text: htmlToText(seed.html),
    // `contentId` is a required `string | null`, so it is nulled, never dropped — and the
    // composer's split tests it for falsiness, which null satisfies.
    attachments: seed.attachments.map(a => ({ ...a, contentId: null })),
  }
}
```

`html: ''` rather than `undefined`: `ComposeSeed.html` is a required `string`.

- [ ] **Step 5: Run them and watch them pass**

```bash
cd src/frontend && npx vitest run src/modules/mail/compose/composeSeed.test.ts
```
Expected: PASS.

- [ ] **Step 6: Write the failing mount tests**

In `src/frontend/src/modules/mail/compose/ComposeView.test.tsx`, add two cases. Preferences reach the view through the API mock the file already declares — `mocks.getPreferences` — so a case sets `mocks.getPreferences.mockResolvedValue({ 'mail.composeFormat': 'text' })` before rendering, and the file's `beforeEach` supplies the baseline. Reuse the file's own render helper rather than writing a new one.

```tsx
it('opens a new message in the text editor when the account chose text', async () => {
  mocks.getPreferences.mockResolvedValue({ 'mail.composeFormat': 'text' })
  renderCompose()
  expect(await screen.findByRole('textbox', { name: /message body/i }))
    .toBeInstanceOf(HTMLTextAreaElement)
})

// The carve-out, at the level a user would notice it.
it('reopens an html draft in the html editor whatever the account chose', async () => {
  mocks.getPreferences.mockResolvedValue({ 'mail.composeFormat': 'text' })
  renderCompose({ action: 'draft', html: '<p>saved</p>', text: null })
  await waitFor(() => expect(document.querySelector('.compose-editor')).toBeTruthy())
  expect(screen.queryByRole('textbox', { name: /message body/i })).toBeNull()
})
```

Adapt the two `renderCompose` calls to the file's real helper signature — it takes a seed, and the
preference is set on the mock rather than passed in.

`findBy`/`waitFor`, never `settle()` — this asserts something derived from the preferences query at mount.

If the textarea carries no accessible name today, give it one (`aria-label="Message body"`) as part of Step 8 rather than asserting on a class; a role query that survives a refactor is worth the attribute.

- [ ] **Step 7: Run them and watch them fail**

```bash
cd src/frontend && npx vitest run src/modules/mail/compose/ComposeView.test.tsx
```
Expected: both new cases FAIL — the composer opens in HTML regardless.

- [ ] **Step 8: Wire it into `ComposeView`**

Three edits, in `src/frontend/src/modules/mail/compose/ComposeView.tsx`.

Import the accessor alongside the existing one at `:8`:

```tsx
import { captureRecipientsOf, composeFormatOf, usePreferences } from '../../../hooks/usePreferences'
```

Replace the seed memo (`:90-91`) so the format is applied once, at mount. The empty dependency list is deliberate — changing the setting must never reformat a message being written:

```tsx
  const rawSeed = useMemo(
    () => state?.seed ?? mailtoSeedFrom(location.search), [state?.seed, location.search])
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const seed = useMemo(
    () => applyComposeFormat(rawSeed, preferences ? composeFormatOf(preferences) : 'html'), [])
```

Replace the `text` initialiser (`:105`). `applyComposeFormat` has already settled every seeded case, so this line only answers the no-seed one:

```tsx
  const [text, setText] = useState<string | null>(
    seed ? seed.text : (preferences && composeFormatOf(preferences) === 'text' ? '' : null))
```

Add the gate immediately before the component's `return`, importing `LoadingBlock` from `../../../components/LoadingBlock`:

```tsx
  // Read at mount, unlike captureRecipientsOf which is read at send. Without this the composer
  // opens in HTML and flips to a textarea a moment later, under whoever has started typing.
  if (!preferences) return <LoadingBlock />
```

That `if` must sit after every hook call in the component — an early return before one changes the hook order between renders and React throws.

- [ ] **Step 9: Run the compose suite**

```bash
cd src/frontend && npx vitest run src/modules/mail/compose && npm run lint && npm run typecheck
```
Expected: all pass, the two new cases among them.

- [ ] **Step 10: Run the whole suite**

```bash
cd src/frontend && npm test
```
Expected: PASS. The `LoadingBlock` gate is the likely breaker — any existing `ComposeView` test that renders without resolving preferences now sees a spinner. Fix those by resolving the query, never by removing the gate.

- [ ] **Step 11: Commit**

```bash
git add src/frontend/src/modules/mail/compose
git commit -F - <<'EOF'
Open the composer in the account's chosen editor

applyComposeFormat converts a seed once at mount; a resumed draft is
exempt on its action. The view waits for preferences before rendering.
EOF
```

---

### Task 3: The setting, and the note

The surface, plus the documentation the next reader needs.

**Files:**
- Modify: `src/frontend/src/modules/settings/general/GeneralPage.tsx` — the Composing section at `:303-315`
- Modify: `src/frontend/src/modules/settings/general/GeneralPage.test.tsx`
- Modify: `src/frontend/CLAUDE.md`

**Interfaces:**
- Consumes: `PREFERENCE_KEYS.composeFormat` and `composeFormatOf` from Task 1.
- Produces: nothing.

- [ ] **Step 1: Write the failing test**

In `src/frontend/src/modules/settings/general/GeneralPage.test.tsx`, add, following the file's existing render helper and save assertions:

```tsx
it('saves the chosen composing editor', async () => {
  renderPage({ 'mail.composeFormat': 'html' })
  const plain = await screen.findByRole('radio', { name: /plain text/i })
  expect(plain).not.toBeChecked()
  fireEvent.click(plain)
  await waitFor(() =>
    expect(mocks.setPreference).toHaveBeenCalledWith('mail.composeFormat', 'text'))
})
```

`setPreference` takes **two positional arguments**, not an options object — that is how every other assertion in this file spells it (`mocks.setPreference).toHaveBeenCalledWith('mail.pageSize', 'all')`). Adapt `renderPage` to the file's own helper name and signature.

- [ ] **Step 2: Run it and watch it fail**

```bash
cd src/frontend && npx vitest run src/modules/settings/general/GeneralPage.test.tsx
```
Expected: FAIL — no radio named "Plain text".

- [ ] **Step 3: Add the row**

In `src/frontend/src/modules/settings/general/GeneralPage.tsx`, add the constant beside `READING_PANES` (`:45`):

```tsx
const COMPOSE_FORMATS: { value: ComposeFormat; label: string; toast: string }[] = [
  { value: 'html', label: 'Formatted', toast: 'New messages will open in the formatted editor' },
  { value: 'text', label: 'Plain text', toast: 'New messages will open in the plain-text editor' },
]
```

Add `ComposeFormat`, `composeFormatOf` to the existing import from `../../../hooks/usePreferences` (`:7-8`).

Add the row inside the Composing section, **above** the existing `ToggleRow` — a choice of editor precedes what happens to the recipients:

```tsx
            <div className="field-h is-setting is-stacked">
              <span className="setting-label">
                <span id="compose-format-label">Default editor</span>
                <span className="setting-hint">
                  Applies to new messages, replies and forwards. A saved draft reopens in the
                  editor it was written in, and the toolbar switches any one message.
                </span>
              </span>
              <div className="layout-cards" role="radiogroup" aria-labelledby="compose-format-label">
                {COMPOSE_FORMATS.map(({ value, label, toast }) => (
                  <label key={value} className="layout-card">
                    <span className="layout-card-name">
                      <input
                        type="radio"
                        name="compose-format"
                        value={value}
                        checked={composeFormatOf(preferences) === value}
                        disabled={setPreference.isPending}
                        onChange={() => save(PREFERENCE_KEYS.composeFormat, value, toast)}
                      />
                      {label}
                    </span>
                  </label>
                ))}
              </div>
            </div>
```

No glyph: `PaneGlyph` draws three *arrangements*, which is a shape a miniature can carry. Two editors are not, and a decorative square beside each would say nothing the label does not.

- [ ] **Step 4: Run it and watch it pass**

```bash
cd src/frontend && npx vitest run src/modules/settings/general/GeneralPage.test.tsx
```
Expected: PASS.

- [ ] **Step 5: Document the contract**

Add to `src/frontend/CLAUDE.md`, in the `general/` bullet of the Settings module section, where the four headings are already listed — extend the **Composing** parenthetical:

> **Composing** (default editor, then save-new-recipients). **The editor choice is `mail.composeFormat` (`html`|`text`, default `html`) and it governs every composer but a resumed draft** — new, reply, reply-all, forward and edit-as-new all take it, because answering a mailing list is the case plain text exists for and it is the one `composeSeed.ts` used to force into HTML by writing `text: null` literally. `applyComposeFormat` (`compose/composeSeed.ts`) is where it is applied, once, in a `useMemo` with an empty dependency list so changing the setting never reformats a message being written. **Its carve-out tests `seed.action === 'draft'`, never `seed.text`**: an HTML draft carries `text: null` exactly like a reply, so a body test cannot tell them apart and would throw the draft's body away — the same boundary the dirty flag draws. Clearing each attachment's `contentId` is the whole of the inline-image handling, since the composer already splits the seed's attachments on that field. **`ComposeView` renders `LoadingBlock` until preferences arrive**, which nothing else in that file needed: `captureRecipientsOf` is read at send time, this is read at mount, and without the gate the composer opens in HTML and flips to a textarea under whoever has started typing. The preference path never reaches the `losesFormatting` confirm dialog — the choice was already made in Settings.

- [ ] **Step 6: Full verification**

```bash
cd src/frontend && npm run lint && npm run typecheck && npm test && npm run build
```
Expected: all four pass.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/modules/settings/general src/frontend/CLAUDE.md
git commit -F - <<'EOF'
Add the default editor setting

Two cards in General, Composing. Documents the draft carve-out and the
mount-time preferences gate.
EOF
```

---

## Verification the tests cannot do

Two things need a browser, on `account-dev` after a push:

- **The gate does not flash.** Open `/mail/compose` and confirm the spinner gives way to the right editor without an HTML frame in between.
- **A reply in text mode quotes with `> `.** Set the preference to plain text, reply to an HTML message, and read the quoted block. `htmlToText`'s nesting is asserted in `bodyFormat.test.ts`, but that a real message's markup survives it is not.

## Known Minors

Report any that survive, per the project's rule that a minor visible on screen gets fixed:

- A composer that renders the editor before the spinner clears, or clears it twice.
- A reply whose converted quote loses its blank lines between paragraphs.
- The setting's radio staying unchecked for a moment after a save.
