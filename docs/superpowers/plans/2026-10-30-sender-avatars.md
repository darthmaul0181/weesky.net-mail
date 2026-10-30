# Sender avatars Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a 40 px sender avatar (contact photo, else Gravatar, else initials) in the message list, behind two account settings.

**Architecture:** Two new preferences; a server-side Gravatar proxy (`GET /api/Avatars`) that refuses to run while the setting is off; a frontend `SenderAvatar` inside the row's content cell whose image comes from the contact book first and the proxy second, loaded only for rows near the viewport.

**Tech Stack:** ASP.NET Core 10 / xUnit / Moq; React 19, TanStack Query, Vitest, i18next.

**Spec:** `docs/superpowers/specs/2026-10-30-sender-avatars-design.md`

## Global Constraints

- Keys and defaults: `mail.showAvatars` `'true'|'false'` default `false`; `mail.avatarColor` `'mono'|'sender'` default `mono`.
- Frontend reads: `showAvatarsOf` is on only for exactly `'true'`; `avatarColorOf` falls back to `mono` for anything but `'sender'`.
- Avatar: rounded square 40 px, `--radius-md`, hairline `--border`; wide skin 28 px (to be measured).
- Gravatar: fixed host `gravatar.com`, `?s=80&d=404`, SHA-256 of the lower-cased trimmed address, no redirect, 5 s budget, body ≤ 64 KB, content type in {`image/jpeg`,`image/png`,`image/gif`,`image/webp`}, never SVG.
- Server cache: hit 7 days, miss 24 h, failure 5 min; one upstream call per address however many callers.
- Response headers: `X-Content-Type-Options: nosniff`, `Cache-Control: private, max-age=86400`.
- The browser never contacts Gravatar. No third-party URL is ever user-controlled.
- No colour is hard-coded: tokens only; the sender tint is hue-only with per-theme lightness constants.
- UI strings in English and French (`parity.test.ts`: U+00A0 before `; : ? !`, `’` not `'`).
- C#: primary constructors, records for DTOs, `ILogger` structured logging, `CancellationToken` on every async method, no try/catch that only logs and rethrows.
- Run `dotnet test` (never `--no-build`) after adding test files; revert `src/scotty.microservice/ApiDocumentation.xml` before committing (it drifts ~855 unrelated lines).
- Commits: two lines max, never begin or end with `@`, end with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`; use a heredoc with `git commit -F -`. Never push.
- The Edit tool writes LF on this CRLF repo: for multi-line replacements use a python/pwsh script and check `git diff --stat` for phantom changes.
- Geometry is measured in `src/frontend/probes/mobile-layout.html`, never asserted in jsdom.

## Review Focus

1. Gravatar answers 200 with an oversize body, an HTML/SVG body or a wrong content type: treated as a miss, never relayed.
2. Setting off: the client sends no `/api/Avatars` request, and the server answers 404 without any upstream call.
3. A sender with no address, an address-only sender, a one-word name, an emoji or non-Latin name: initials never crash, never split a surrogate pair; no address gives `?`.
4. Ten rows asking for the same address at once: one upstream call and one client request.
5. A contact whose photo was removed or replaced after the list loaded: the row stops showing the old picture and falls back to Gravatar, then initials.

---

## File Structure

Backend (`src/scotty.microservice/`)
- `Models/UserPreferences.cs` (modify) · `Models/AvatarImage.cs` (create) · `Services/IAvatarService.cs`, `Services/GravatarService.cs` (create) · `Controllers/AvatarsController.cs` (create) · `Configuration/ApplicationServicesConfiguration.cs`, `Configuration/SecurityConfiguration.cs` (modify)

Frontend (`src/frontend/src/`)
- `hooks/usePreferences.ts` (modify) · `modules/settings/general/MailGeneralPage.tsx` (modify) · `locales/{en,fr}/*` (modify)
- `modules/mail/list/avatar/senderAvatar.ts` (create: pure helpers) · `useSenderAvatarUrl.ts` (create) · `SenderAvatar.tsx` (create)
- `modules/mail/list/MessageRow.tsx`, `MessageList.tsx` (modify) · `styles/mail.css` (modify) · `api.ts` (modify) · `modules/contacts/contactTypes.ts`, `modules/contacts/queries.ts` (modify)
- `modules/whatsnew/catalog.ts`, `locales/{en,fr}/whatsNew.json`, `VERSION` (modify)

---

### Task 1: Preferences registry (backend)

**Files:**
- Modify: `src/scotty.microservice/Models/UserPreferences.cs`
- Test: `src/scotty.microservice/scotty.microservice.Tests/Models/UserPreferencesTests.cs`

**Interfaces:**
- Produces: constants `UserPreferences.MailShowAvatars = "mail.showAvatars"` and `UserPreferences.MailAvatarColor = "mail.avatarColor"`.

- [ ] **Step 1: Write the failing tests** in `UserPreferencesTests`: `Effective_GivesAvatarDefaults` (`Effective([])` has `mail.showAvatars == "false"` and `mail.avatarColor == "mono"`); `IsValid_AcceptsOnlyRegisteredAvatarValues` (`true`/`false` for the first, `mono`/`sender` for the second; `"yes"`, `""`, `"Sender"` refused).
- [ ] **Step 2: Run** `cd src && dotnet test --filter UserPreferencesTests` — expected FAIL (constants missing).
- [ ] **Step 3: Declare both keys** next to `MailShowFolderIcons`, with `Booleans` and `["mono", "sender"]`, and a one-line comment on why the default is off (a new setting changes nothing for an account that never opens it).
- [ ] **Step 4: Run** the same filter — expected PASS.
- [ ] **Step 5: Commit** `Add the sender avatar preferences`.

---

### Task 2: Gravatar service (backend)

**Files:**
- Create: `Models/AvatarImage.cs`, `Services/IAvatarService.cs`, `Services/GravatarService.cs`
- Modify: `Configuration/ApplicationServicesConfiguration.cs` (next to the `bing` client)
- Test: `scotty.microservice.Tests/Services/GravatarServiceTests.cs` (copy `DailyImageServiceTests`' `Factory`/fake-handler/fake-clock approach)

**Interfaces:**
- Produces: `public sealed record AvatarImage(byte[] Bytes, string ContentType)`; `public interface IAvatarService { Task<AvatarImage?> GetAsync(string address, CancellationToken cancellationToken); }`; `GravatarService.ClientName = "gravatar"` and `internal static string GravatarService.HashOf(string address)` (lower-case hex SHA-256 of the trimmed, lower-cased address).
- Consumed by: Task 3.

- [ ] **Step 1: Write the failing tests** (fake `HttpMessageHandler`, fake `TimeProvider`):
  - `HashOf_IgnoresCaseAndSurroundingSpace`: `HashOf(" Bob@Example.COM ") == HashOf("bob@example.com")`, 64 hex chars.
  - `Get_RequestsFixedHostWithSizeAndNotFoundDefault`: the single upstream request URL is `https://gravatar.com/avatar/<hash>?s=80&d=404`.
  - `Get_ReturnsBytesAndContentTypeOnAPng`.
  - `Get_ReturnsNullOn404_AndDoesNotAskAgainWithin24Hours_ButAsksAfter`.
  - `Get_CachesAHitForSevenDays`.
  - `Get_TreatsAnOversizeBodyAsAMiss` (declared length > 64 KB, and a body that outgrows it while streaming).
  - `Get_RefusesNonImageContentTypes` (`text/html`, `image/svg+xml`).
  - `Get_DoesNotFollowARedirect` (302 → null, no second request).
  - `Get_OnTimeoutOrServerError_ReturnsNullAndRetriesAfterFiveMinutes`.
  - `Get_ConcurrentCallsForOneAddress_MakeOneUpstreamRequest` (20 parallel callers, handler counts 1).
- [ ] **Step 2: Run** `dotnet test --filter GravatarServiceTests` — expected FAIL.
- [ ] **Step 3: Implement `GravatarService(IHttpClientFactory clients, TimeProvider clock, ILogger<GravatarService> logger) : IAvatarService`** (internal sealed). Follow `DailyImageService` for the capped streaming read, the whole-fetch budget token and the "visitor's cancellation is not a failure" rule. Cache: a size-limited `MemoryCache` (32 MB, size = byte count, a miss or failure costs 64) holding hits 7 d, misses 24 h, failures 5 min; single-flight per hash (a concurrent dictionary of in-flight tasks, removed when done). Log with structured properties and never the full address (log the hash).
- [ ] **Step 4: Register**: `AddHttpClient(GravatarService.ClientName, client => client.Timeout = TimeSpan.FromSeconds(5))` with `AllowAutoRedirect = false` (as the Bing client), and `AddSingleton<IAvatarService, GravatarService>()`.
- [ ] **Step 5: Run** the filter — expected PASS. **Commit** `Add the Gravatar lookup service`.

---

### Task 3: Avatars endpoint and rate limit (backend)

**Files:**
- Create: `Controllers/AvatarsController.cs`
- Modify: `Configuration/SecurityConfiguration.cs` (`AddRateLimiters`)
- Test: `scotty.microservice.Tests/Controllers/AvatarsControllerTests.cs` (use `ControllerTestHelpers.CreateAuthenticatedContext`); extend `MailRouteSurfaceTests`' sibling route test if one lists all controllers

**Interfaces:**
- Consumes: `IAvatarService.GetAsync`, `IUserPreferenceStore.GetAsync(webmailUid, ct)` (as `PreferencesController` uses it), `UserPreferences.Effective`.
- Produces: `GET /api/Avatars?address=` → 200 image / 400 / 401 / 404; policy name `AvatarsPolicy = "avatars"`.

- [ ] **Step 1: Write the failing tests**:
  - `Get_WhenAvatarsAreOff_Answers404_WithoutCallingTheService` (`Mock<IAvatarService>` `Times.Never`).
  - `Get_WithAnInvalidAddress_Answers400` for `""`, `"nope"`, `"a@b@c"`, a 255-char address, and `"Bob <bob@x.com>"` (only a bare address is accepted).
  - `Get_WhenTheServiceHasNoImage_Answers404`.
  - `Get_OnAHit_Answers200WithBytes_ContentType_NoSniff_AndPrivateCache`.
  - `Get_ForwardsTheAddressAsGiven_NotTheCallersIdentity` (the service receives only the address).
- [ ] **Step 2: Run** `dotnet test --filter AvatarsControllerTests` — expected FAIL.
- [ ] **Step 3: Implement `AvatarsController(IUserPreferenceStore preferences, IAvatarService avatars) : ApiBaseController`** with `[Route("api/[controller]")] [ApiController] [Authorize] [EnableRateLimiting(SecurityConfiguration.AvatarsPolicy)]`; address validated by a `[FromQuery]` request record with `[Required, StringLength(254)]` plus a bare-address check (`MailAddress.TryCreate` and `Address == input.Trim()`); preference read first, answering `NotFoundEnveloppe` when `mail.showAvatars` is not `"true"`.
- [ ] **Step 4: Add the `avatars` policy**: a per-user fixed window (partition on the authenticated `Upn`, falling back to the remote address), 600 requests per minute, no queue; the default 429 rejection suffices.
- [ ] **Step 5: Run** `cd src && dotnet test`. Revert `ApiDocumentation.xml`. Add the controller to the list in `src/scotty.microservice/CLAUDE.md`. **Commit** `Add the avatars endpoint`.

---

### Task 4: Pure helpers (frontend)

**Files:**
- Create: `src/frontend/src/modules/mail/list/avatar/senderAvatar.ts`
- Test: `.../avatar/senderAvatar.test.ts`

**Interfaces:**
- Produces:
  - `avatarInitials(name: string | undefined, address: string | undefined): string` (≤ 2 grapheme clusters, upper-cased; `?` with neither).
  - `avatarHue(address: string | undefined): number` (integer 0–359, stable, from `canonicalAddress`).
  - `type AvatarColor = 'mono' | 'sender'`.

- [ ] **Step 1: Write the failing tests**: `avatarInitials('Léa Martin','x@y.z') === 'LM'`; `('Karim', …) === 'K'`; `('Jean-Pierre Le Gall', …) === 'JL'` (first letters of the first two words); `(undefined,'noreply@lumen.com') === 'N'`; `('', '') === '?'`; name equal to its own address is treated as no name (`('bob@x.com','bob@x.com') === 'B'`); `('😀 Smile', …) === '😀S'` (no split surrogate); `('李 小龍', …)` keeps two whole characters; `avatarHue('Bob@X.com ') === avatarHue('bob@x.com')`, in `[0,360)`, and differs for two sample addresses.
- [ ] **Step 2: Run** `npx vitest run src/modules/mail/list/avatar` — expected FAIL.
- [ ] **Step 3: Implement** with `Intl.Segmenter` for graphemes and a 32-bit string hash (FNV-1a) modulo 360 over `canonicalAddress(address)` (`src/lib/canonicalAddress.ts`).
- [ ] **Step 4: Run** — PASS. **Commit** `Add the avatar initials and hue helpers`.

---

### Task 5: Settings (frontend)

**Files:**
- Modify: `hooks/usePreferences.ts`, `modules/settings/general/MailGeneralPage.tsx`, the `en`/`fr` catalogue holding the `general.*` keys (grep `general.preview.label`)
- Test: `hooks/usePreferences.test.ts` (exists or create), `modules/settings/general/MailGeneralPage.test.tsx`

**Interfaces:**
- Produces: `PREFERENCE_KEYS.showAvatars`, `PREFERENCE_KEYS.avatarColor`; `showAvatarsOf(preferences: Preferences): boolean`; `avatarColorOf(preferences: Preferences): AvatarColor`.

- [ ] **Step 1: Write the failing tests**: accessor tests (`showAvatarsOf({})` false, `{'mail.showAvatars':'true'}` true, `'TRUE'` false; `avatarColorOf` `{}`/garbage → `mono`, `'sender'` → `sender`). Page tests (await with `findBy`, never `settle()`): the row *Show sender avatars* toggles and `PUT`s `mail.showAvatars=true`; the child row *Avatar colours* is indented (`is-child`), `is-covered` and disabled while the parent is off, enabled when on; choosing *One colour per sender* `PUT`s `mail.avatarColor=sender`; the value is kept while the parent is off.
- [ ] **Step 2: Run** the two files — FAIL.
- [ ] **Step 3: Implement** the accessors; in the *Message list* section, after `show-folder-icons`, one `ToggleRow` (`id="show-avatars"`, `nested` absent) and one child row using `field-h is-setting is-child` + `MenuSelect` (`id="avatar-color"`) with options *Monochrome* and *One colour per sender*, `covered={!showAvatarsOf(...)}`, `disabled={saving || !showAvatarsOf(...)}`. Hint under the switch states that the sender's address is checked against Gravatar through the server. Add `general.avatars.*` and `general.avatarColor.*` (label, hint, on, off, options, toasts) in `en` and `fr`, spelling each `t('…')` key as a literal.
- [ ] **Step 4: Run** the two files plus `parity.test.ts` and `keys.test.ts` — PASS. **Commit** `Add the sender avatar settings`.

---

### Task 6: Image sources (frontend)

**Files:**
- Modify: `api.ts` (add `getAvatar`), `modules/contacts/contactTypes.ts` (add `hasPhoto: boolean` to `Contact`: the API already sends it), `modules/contacts/queries.ts` (add `contactKeys.avatar`)
- Create: `modules/mail/list/avatar/useSenderAvatarUrl.ts`
- Test: `.../avatar/useSenderAvatarUrl.test.tsx`

**Interfaces:**
- Consumes: `useContacts()`, `api.getContactPhoto(id)`, `canonicalAddress`.
- Produces: `api.getAvatar(address: string): Promise<Blob | null>` (`null` on 404); `useSenderAvatarUrl(address: string | undefined, enabled: boolean): string | null` — an object URL revoked with its blob; `null` until resolved and when no image exists. `enabled` is false until the row is near the viewport **and** the setting is on.

- [ ] **Step 1: Write the failing tests** (mock `api`, prime the query client, `findBy`/`waitFor`):
  - disabled → `api.getContactPhoto` and `api.getAvatar` never called.
  - address of a contact with `hasPhoto` → contact photo fetched, Gravatar route not called.
  - address of a contact without a photo, or of no contact → `getAvatar` called once with the trimmed address.
  - `getAvatar` resolving `null` → hook returns `null` (initials) and a rerender does not call it again.
  - ten hook instances for the same address → one `getAvatar` call.
  - contact photo that 404s falls through to `getAvatar`.
  - the object URL is revoked on unmount.
  - contacts invalidation after a photo change refetches the active avatar query (`hasPhoto` flips false → no photo request, Gravatar tried).
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement.** Build the address→contact map once per `useContacts` result (`useMemo`, keyed by `canonicalAddress` of every address). Queries: contact photo under `contactKeys.avatar(accountId, contactId)` (prefix of `contactKeys.all`, so every contact write refreshes an active one), Gravatar under `['avatar', canonicalAddress]` with `staleTime` 24 h; both `queryFn`s turn an `ApiError` 404 into `null`. Reuse the object-URL pattern of `useContactPhotoUrl`.
- [ ] **Step 4: Run** — PASS. **Commit** `Add the avatar image sources`.

---

### Task 7: The avatar component (frontend)

**Files:**
- Create: `modules/mail/list/avatar/SenderAvatar.tsx`; a `useInView` hook in `src/hooks/` only if none exists (grep first)
- Modify: `styles/mail.css`
- Test: `.../avatar/SenderAvatar.test.tsx`, plus a contrast sweep `.../avatar/avatarContrast.test.ts`

**Interfaces:**
- Consumes: Task 4 helpers, Task 6 hook.
- Produces: `SenderAvatar({ name, address, color }: { name?: string; address?: string; color: AvatarColor })` rendering `<div className="message-row-avatar is-photo|is-mono|is-sender" aria-hidden="true" style={{ '--avatar-h': hue }}>`. Marker rule: the box is always rendered at its final size, so nothing shifts when an image arrives.

- [ ] **Step 1: Write the failing tests**: initials shown at rest; photo `<img>` replaces initials when the hook returns a URL; `is-mono` vs `is-sender` class follows `color`; `--avatar-h` set only in sender mode; no network hook enabled until the element is in view (mock `IntersectionObserver`; when it is undefined the component counts as in view); `aria-hidden`. Contrast sweep: read the light and `[data-theme='dark']` constants of `.message-row-avatar.is-sender` from `mail.css` text, and for hue 0…359 assert text/background ≥ 4.5:1 in both themes (write the HSL→sRGB luminance helper in the test).
- [ ] **Step 2: Run** — FAIL.
- [ ] **Step 3: Implement the component and CSS.** `.message-row-avatar`: 40 px, `--radius-md`, `border: 1px solid var(--border)`, `flex: none`, 600-weight 14 px initials, `<img>` `object-fit: cover`. `.is-mono`: `--folders-bg` background, `--pane-item-active-fg` text. `.is-sender`: `hsl(var(--avatar-h) <s> <l>)` background and ink from numeric constants declared once in the block, with a `[data-theme='dark']` override (these are numbers, not colours). Observe visibility with `IntersectionObserver`, `rootMargin` equal to about 20 rows of 85 px (`1700px 0px`), disconnecting after the first hit.
- [ ] **Step 4: Run** — PASS. **Commit** `Add the sender avatar component`.

---

### Task 8: Wire into the row (frontend)

**Files:**
- Modify: `modules/mail/list/MessageRow.tsx`, `modules/mail/list/MessageList.tsx`, `styles/mail.css`, `src/frontend/probes/mobile-layout.html`
- Test: `MessageRow.test.tsx`, `MessageList.test.tsx`, `src/a11y.test.tsx`

**Interfaces:**
- Consumes: `showAvatarsOf`, `avatarColorOf`, `SenderAvatar`.
- Produces: new `MessageRowProps.avatar: AvatarColor | null` (`null` = off); `MessageList` computes it from preferences (`null` while preferences load).

- [ ] **Step 1: Write the failing tests**: `avatar={null}` → no `.message-row-avatar`; `avatar="mono"` → one, with the sender's initials; in a drafts folder the first recipient's initials/hue; a collapsed conversation head uses the head's sender; the row still has exactly four `role="gridcell"` children and the `aria-label` is byte-identical with and without the avatar; `MessageList` passes `null` before preferences resolve and `'sender'` once `mail.showAvatars=true` and `mail.avatarColor=sender` (use `findBy`).
- [ ] **Step 2: Run** the three files — FAIL.
- [ ] **Step 3: Implement.** Render the avatar as the first child of `.message-row-content` in both skins; the person is `drafts ? message.to[0] : { name: message.fromName, address: message.fromAddress }`. CSS: narrow skin raises `padding-left` by avatar width plus 10 px gap and keeps `--pad-y`; the content stays a block (the avatar is absolutely positioned at `left: 34px, top: var(--pad-y)` — keep one idiom with the checkbox); wide skin (`is-line`) uses the 28 px size as an in-flow flex item centred on the line. `MessageRow` stays memoised: `avatar` is a primitive.
- [ ] **Step 4: Measure** in `probes/mobile-layout.html` (Blink, 360×640, 1024×768; read `clipped`, the row `height`): a narrow row with preview, with and without the avatar, and a wide row. The row height with avatar must not exceed the preview row's current height by more than the spec's 6 px; the content's ellipsis still works. Record the measured numbers in the plan's commit message body (one line).
- [ ] **Step 5: Run** the `src/frontend` suite (`npm test`, `npm run lint`, `npm run build`) — PASS. **Commit** `Show the sender avatar in the message list`.

---

### Task 9: What's new, version, docs

**Files:**
- Modify: `modules/whatsnew/catalog.ts`, `locales/{en,fr}/whatsNew.json`, `src/frontend/VERSION`, the settings row (`WhatsNewDot` on each anchor of the path, `WhatsNewTarget` around the new block, mirroring the `daily-image` entry), `src/frontend/docs/architecture-mail.md`, `docs/architecture-settings.md`
- Test: `modules/whatsnew/catalog.test.ts` (reads the real catalogue)

- [ ] **Step 1: Run** `npx vitest run src/modules/whatsnew` after adding the catalogue entry alone — expected FAIL (texts, dot, target missing).
- [ ] **Step 2: Add** entry `id: 'sender-avatars'`, version = `src/frontend/VERSION` bumped by one minor, `path` ending on the Mail settings page, `target: 'settings:show-avatars'` (check the exact anchor names against the existing entries); EN and FR `items.sender-avatars.title/body`; the dots and the target in `MailGeneralPage` and wherever the path's anchors live.
- [ ] **Step 3: Docs.** One paragraph each in `architecture-mail.md` (the avatar, its sources and the 24 h negative cache) and `architecture-settings.md` (the two rows).
- [ ] **Step 4: Run** `npm test`, `npm run lint`, `npm run build` — PASS. **Commit** `Announce the sender avatars and document them`.

---

## Self-review notes

- Spec coverage: settings (T1, T5), avatar shape and initials (T4, T7, T8), whose avatar (T8), sources and fallbacks (T2, T3, T6), performance (T2 single-flight, T6 dedupe, T7 viewport), security (T2, T3), What's new (T9), tests per spec list (T1–T8); wide skin size measured in T8.
- Spec gap closed here: the list `Contact` type did not declare `hasPhoto` though the API sends it (T6); the server refuses lookups while the setting is off (T3), which the spec implied by "switching avatars on enables it".
- My calls (not in the spec): 32 MB server cache bound, 5-minute failure cache, 600 requests/minute per user, viewport margin of about 20 rows, avatar positioned absolutely like the checkbox.
