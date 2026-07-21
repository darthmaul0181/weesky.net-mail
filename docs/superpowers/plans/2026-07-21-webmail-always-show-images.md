# Always show remote images — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A per-account setting that loads remote images in every message, so the reader never shows the "N remote images were blocked" banner.

**Architecture:** One new key in the backend preference registry, one accessor beside the existing ones, one derived boolean in `MessageReader` replacing the per-message consent state at its two readers, and one toggle row in `GeneralPage`. The sanitising pipeline is untouched — the setting pre-applies the reveal the "Show images" button already performs, client-side.

**Tech Stack:** ASP.NET Core 10 / xUnit for the registry; React 18 + TypeScript, TanStack Query, Vitest + `@testing-library/react` for the frontend.

## Global Constraints

- The stored value is the string `"true"` or `"false"`; the default is `"false"`.
- The preference key is `mail.alwaysShowImages`, spelled identically on both sides.
- UI copy is English (the app's UI language), regardless of the conversation language.
- The accessor is off unless the stored value is exactly `'true'` — a missing or malformed row blocks images.
- No change to `MailHtmlSanitizer`, to `sanitizeBody.ts`, or to any query key. CSS `url()` culling stays as it is.
- Comments only where the code does not speak for itself; three lines maximum (repo CLAUDE.md).

---

### Task 1: The preference key on the backend

**Files:**
- Modify: `src/snoopy.microservice/Models/UserPreferences.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Models/UserPreferencesTests.cs`

**Interfaces:**
- Produces: `UserPreferences.MailAlwaysShowImages` — a `const string` equal to `"mail.alwaysShowImages"`, registered in `UserPreferences.All` with default `"false"` and the shared `Booleans` allowed list.

- [ ] **Step 1: Write the failing tests**

In `UserPreferencesTests.cs`, add the new key to the existing `All_CarriesTheKeysTheClientOffers` fact:

```csharp
        Assert.Contains(UserPreferences.All, p => p.Key == UserPreferences.MailAlwaysShowImages);
```

Add one `InlineData` line to `Default_IsTheValueAnAccountWithNoRowsGets`:

```csharp
    [InlineData(UserPreferences.MailAlwaysShowImages, "false")]
```

Add two `InlineData` lines to `IsValid_AcceptsOnlyTheOfferedValues`:

```csharp
    [InlineData(UserPreferences.MailAlwaysShowImages, "true", true)]
    [InlineData(UserPreferences.MailAlwaysShowImages, "yes", false)]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run from `src/snoopy.microservice`: `dotnet test --filter UserPreferencesTests`

Expected: FAIL — compile error, `'UserPreferences' does not contain a definition for 'MailAlwaysShowImages'`.

- [ ] **Step 3: Register the key**

In `Models/UserPreferences.cs`, beside the other constants:

```csharp
    public const string MailAlwaysShowImages = "mail.alwaysShowImages";
```

and in the `All` collection expression, after `MailShowPreview` so the registry reads in the same order the settings page does:

```csharp
        new(MailAlwaysShowImages, "false", Booleans),
```

- [ ] **Step 4: Run the tests to verify they pass**

Run from `src/snoopy.microservice`: `dotnet test --filter UserPreferencesTests`

Expected: PASS. `Effective_FillsInEveryDefault` asserts `effective.Count == UserPreferences.All.Count`, so it follows the new entry on its own.

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Models/UserPreferences.cs src/snoopy.microservice/snoopy.microservice.Tests/Models/UserPreferencesTests.cs
git commit -m "Register the always-show-images preference"
```

---

### Task 2: The client accessor

**Files:**
- Modify: `src/frontend/src/hooks/usePreferences.ts`
- Test: `src/frontend/src/hooks/usePreferences.test.tsx`

**Interfaces:**
- Consumes: the key registered in Task 1.
- Produces: `PREFERENCE_KEYS.alwaysShowImages` (the string `'mail.alwaysShowImages'`) and `alwaysShowImagesOf(preferences: Preferences): boolean`.

- [ ] **Step 1: Write the failing test**

In `usePreferences.test.tsx`, import `alwaysShowImagesOf` in the existing import block from `'./usePreferences'`, then add this case to the `describe('the accessors', …)` block, after the `notifyDesktop` one:

```tsx
  // The mirror of notifySoundOf: an absent or malformed row must keep blocking, never reveal.
  it.each([
    ['true', true],
    ['false', false],
    ['yes', false],
    [undefined, false],
  ])('reads alwaysShowImages %s as %s', (stored, expected) => {
    const preferences: Record<string, string> =
      stored === undefined ? {} : { [PREFERENCE_KEYS.alwaysShowImages]: stored }

    expect(alwaysShowImagesOf(preferences)).toBe(expected)
  })
```

- [ ] **Step 2: Run the test to verify it fails**

Run from `src/frontend`: `npm run test -- src/hooks/usePreferences.test.tsx`

Expected: FAIL — `alwaysShowImagesOf is not a function` (and a TypeScript error on `PREFERENCE_KEYS.alwaysShowImages`).

- [ ] **Step 3: Add the key and the accessor**

In `usePreferences.ts`, add to `PREFERENCE_KEYS` after `showPreview`:

```ts
  alwaysShowImages: 'mail.alwaysShowImages',
```

and add the accessor after `showPreviewOf`:

```ts
/** Off unless explicitly on: a key the backend has not sent yet must keep images blocked. */
export function alwaysShowImagesOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.alwaysShowImages] === 'true'
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run from `src/frontend`: `npm run test -- src/hooks/usePreferences.test.tsx`

Expected: PASS, whole file green.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/hooks/usePreferences.ts src/frontend/src/hooks/usePreferences.test.tsx
git commit -m "Read the always-show-images preference"
```

---

### Task 3: The reader honours the preference

**Files:**
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx`
- Test: `src/frontend/src/modules/mail/reader/MessageReader.test.tsx`

**Interfaces:**
- Consumes: `alwaysShowImagesOf` and `usePreferences` from Task 2.
- Produces: nothing other tasks depend on.

**Note:** `MessageReader.test.tsx` mocks `'../../../api.js'` with an `api` object holding only `getMailMessage`. Once the component calls `usePreferences()`, that query calls `api.getPreferences` — undefined in the mock, so *every* test in the file would fail. Step 1 adds it to the mock, which is why the first test run below is expected to be a single failure and not a red file.

- [ ] **Step 1: Extend the mock and write the failing tests**

In `MessageReader.test.tsx`, add `getPreferences` to the hoisted mocks and to the `api` object:

```tsx
const mocks = vi.hoisted(() => ({
  getMailMessage: vi.fn(),
  getPreferences: vi.fn(),
  requestBlob: vi.fn(),
  mailAttachmentUrl: vi.fn((folder: string, uid: number, part: string) =>
    `/api/Mail/Messages/Attachment?folder=${folder}&uid=${uid}&part=${part}`),
}))

vi.mock('../../../api.js', () => ({
  api: { getMailMessage: mocks.getMailMessage, getPreferences: mocks.getPreferences },
  requestBlob: mocks.requestBlob,
  mailAttachmentUrl: mocks.mailAttachmentUrl,
}))
```

Give every existing test the blocking default by replacing the `beforeEach` line:

```tsx
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', 'mail.alwaysShowImages': 'false' })
  })
```

Then add these two tests after the existing `'offers to show blocked images and reveals them on demand'`:

```tsx
  const blocked = {
    ...detail,
    blockedImageCount: 2,
    htmlBody: '<img data-blocked-src="https://t.example/p.gif">',
  }

  // The whole point of the setting: no banner, no button, nothing to click per message.
  it('shows the images and no banner when the account always shows them', async () => {
    mocks.getMailMessage.mockResolvedValue(blocked)
    mocks.getPreferences.mockResolvedValue({ 'mail.alwaysShowImages': 'true' })

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
    await screen.findByText('Re: facture')

    // Asserted on the absence of the attribute, not on `src="…"`: `data-blocked-src="…"`
    // contains that substring verbatim, so a positive match alone proves nothing.
    await waitFor(() => expect(container.querySelector('iframe')!.getAttribute('srcdoc'))
      .not.toContain('data-blocked-src'))
    expect(container.querySelector('iframe')!.getAttribute('srcdoc'))
      .toContain('src="https://t.example/p.gif"')
    expect(screen.queryByText(/remote images were blocked/i)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /show images/i })).not.toBeInTheDocument()
  })

  it('keeps blocking when the account has not asked for it', async () => {
    mocks.getMailMessage.mockResolvedValue(blocked)

    const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })

    expect(await screen.findByText(/2 remote images were blocked/i)).toBeInTheDocument()
    expect(container.querySelector('iframe')!.getAttribute('srcdoc'))
      .toContain('data-blocked-src')
  })
```

- [ ] **Step 2: Run the tests to verify they fail**

Run from `src/frontend`: `npm run test -- src/modules/mail/reader/MessageReader.test.tsx`

Expected: `'shows the images and no banner when the account always shows them'` FAILS (the banner is still there and the `srcdoc` still carries `data-blocked-src`). Every other test in the file PASSES.

- [ ] **Step 3: Derive the consent**

In `MessageReader.tsx`, add the import beside the existing ones:

```tsx
import { alwaysShowImagesOf, usePreferences } from '../../../hooks/usePreferences'
```

Add the hook next to the others at the top of the component, above the `useState` calls (hooks must sit ahead of the early returns already in this component):

```tsx
  const { data: preferences } = usePreferences()
```

Below the early returns, next to the existing `inverted` line, derive rather than seed state — `imagesShown` keeps meaning "clicked on *this* message", so the per-message reset effect stays untouched:

```tsx
  const showImages = imagesShown || (!!preferences && alwaysShowImagesOf(preferences))
```

Then replace `imagesShown` at its two readers. The body reveal:

```tsx
  const revealed = showImages ? revealBlockedImages(data.htmlBody) : data.htmlBody
```

and the banner condition, which takes the button with it:

```tsx
      {data.blockedImageCount > 0 && !showImages && (
```

Leave the `useState`, the reset effect and the `setImagesShown(true)` click handler exactly as they are.

- [ ] **Step 4: Run the tests to verify they pass**

Run from `src/frontend`: `npm run test -- src/modules/mail/reader/MessageReader.test.tsx`

Expected: PASS, whole file green — including the untouched per-message "Show images" test.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/reader/MessageReader.tsx src/frontend/src/modules/mail/reader/MessageReader.test.tsx
git commit -m "Skip the blocked-images banner when the account always shows them"
```

---

### Task 4: The toggle in General settings

**Files:**
- Modify: `src/frontend/src/modules/settings/general/GeneralPage.tsx`
- Test: `src/frontend/src/modules/settings/general/GeneralPage.test.tsx`

**Interfaces:**
- Consumes: `PREFERENCE_KEYS.alwaysShowImages` and `alwaysShowImagesOf` from Task 2.
- Produces: nothing other tasks depend on.

- [ ] **Step 1: Write the failing tests**

In `GeneralPage.test.tsx`, add after the existing preview-toggle tests:

```tsx
  it('shows the images toggle off by default and on when it is stored', async () => {
    renderPage()
    expect(await screen.findByLabelText('Always show remote images')).not.toBeChecked()
  })

  it('saves the images toggle and warns about what it costs', async () => {
    renderPage()

    fireEvent.click(await screen.findByLabelText('Always show remote images'))

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.alwaysShowImages', 'true'))
    expect(await screen.findByText(/remote images will always load/i)).toBeInTheDocument()
  })

  // The warning the banner used to carry has to survive somewhere, and the moment of choosing
  // is the one place it is useful.
  it('carries the privacy note only while it is on', async () => {
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'true', 'mail.alwaysShowImages': 'true' })

    expect(await screen.findByLabelText('Always show remote images')).toBeChecked()
    expect(screen.getByText(/tells the sender you opened the message/i)).toBeInTheDocument()
  })
```

- [ ] **Step 2: Run the tests to verify they fail**

Run from `src/frontend`: `npm run test -- src/modules/settings/general/GeneralPage.test.tsx`

Expected: the three new tests FAIL with `Unable to find a label with the text of: Always show remote images`. The rest of the file PASSES.

- [ ] **Step 3: Add the row and its note**

In `GeneralPage.tsx`, add `alwaysShowImagesOf` to the existing import from `'../../../hooks/usePreferences'`, then insert this immediately after the `show-preview` `ToggleRow` and before the `notify-sound` one — both rows above say how mail is displayed, the ones below are about notifications:

```tsx
          <ToggleRow
            id="always-show-images"
            label="Always show remote images"
            checked={alwaysShowImagesOf(preferences)}
            disabled={setPreference.isPending}
            onChange={on => save(PREFERENCE_KEYS.alwaysShowImages, String(on),
              on ? 'Remote images will always load' : 'Remote images stay blocked until you ask')}
          />

          {alwaysShowImagesOf(preferences) && (
            <p className="settings-note">
              Loading them tells the sender you opened the message.
            </p>
          )}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run from `src/frontend`: `npm run test -- src/modules/settings/general/GeneralPage.test.tsx`

Expected: PASS, whole file green.

- [ ] **Step 5: Run the whole suite and the typecheck**

Run from `src/frontend`: `npm run test && npm run typecheck`

Expected: every test file green, `tsc --noEmit` silent.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/modules/settings/general/GeneralPage.tsx src/frontend/src/modules/settings/general/GeneralPage.test.tsx
git commit -m "Offer the always-show-remote-images setting"
```

---

### Task 5: Record it where the next reader will look

**Files:**
- Modify: `src/frontend/CLAUDE.md`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

- [ ] **Step 1: Extend the two paragraphs that would otherwise be wrong**

In `src/frontend/CLAUDE.md`, in the "Rendering message HTML — three independent barriers" paragraph, replace the closing sentence:

> Remote images arrive as `data-blocked-src` and are only restored on explicit user consent, per message — loading them tells the sender the message was opened.

with:

> Remote images arrive as `data-blocked-src` and are only restored on consent — per message by default, or once and for all via `mail.alwaysShowImages`, which suppresses the banner entirely. **The preference never enters the sanitising pipeline**: it is read in `MessageReader` and only pre-applies the reveal the button performs, so a body is the same document for every account and the message cache does not depend on it. CSS `url()` stays culled either way — the button does not restore background images and neither does the setting.

In the "Preferences" paragraph, add after the sentence about the two notification keys:

> `alwaysShowImagesOf` follows them: off unless the stored value is exactly `'true'`, since a key the backend has not sent yet must keep images blocked. `MessageReader` **derives** consent (`imagesShown || alwaysShowImagesOf`) rather than seeding its state from the preference — seeding would have to be re-synchronised when the query resolves and re-applied on every message change, and the per-message reset effect would have to learn about the preference.

In the settings-module file list, replace the `general/` line with:

> - `general/` — `GeneralPage.tsx` (messages per page, message-list preview, always-show-remote-images, new-mail sound and desktop notification toggles)

- [ ] **Step 2: Commit**

```bash
git add src/frontend/CLAUDE.md
git commit -m "Document the always-show-remote-images setting"
```
