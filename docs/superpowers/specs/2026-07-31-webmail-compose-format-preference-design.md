# A default composing format — `mail.composeFormat`

**Date** — 2026-07-31
**Scope** — `src/frontend` and `src/snoopy.microservice`, one new preference.

A setting in Settings › General › Composing chooses whether the composer opens in HTML or in plain
text. It governs every composer opened from scratch or from a message — new, reply, reply-all, forward,
edit-as-new — and leaves a resumed draft in whatever format that draft was saved in.

## Why this exists, and what it supersedes

Plain-text composing already ships: a toggle at the right end of `EditorToolbar` switches the composer
between `SquireEditor` and a `<textarea>`, and the message really leaves as `text/plain`
(`docs/superpowers/specs/2026-07-31-webmail-plain-text-compose-design.md`). That spec states, under
"What this is not":

> **Not a preference.** There is no account-level "compose in plain text by default".

**This document supersedes that paragraph.** It was a scope decision, not a reasoned objection — the
older spec gives no argument against a preference, it simply excludes one from its slice. Nothing in the
machinery it built resists this; the conversions it introduced are what makes this cheap.

The reason to add it now is visible in `composeSeed.ts`. `buildComposeSeed` writes `text: null`
literally, three times — for `editAsNew` (`:85`), `forward` (`:108`) and reply/reply-all (`:126`). A
reply therefore opens in HTML **even when replying to a plain-text message**, and the one case plain
text is most often wanted for — answering a mailing list, a ticket system, someone reading in a terminal
— is exactly the case that forces markup today. A preference that covered new messages only would leave
that untouched and would promise more than it delivers.

## The preference

| | |
|---|---|
| Key | `mail.composeFormat` |
| Values | `html`, `text` |
| Default | `html` |

`mail.`, not `compose.`: the registry's own rule (`UserPreferences.cs:33-39`) names a key after the
effect rather than the trigger, and the effect is on the mail composer.

The default is what the composer does today, so an account that never opens the setting sees no change
at all — the principle `mail.rowActions` states in its own comment.

**An enumeration, not a boolean.** `mail.composeInPlainText: true|false` would read as a negation at
every call site, `mail.readingPane` already sets the precedent for a small closed set, and an enum
leaves room for a third value — "follow the message being replied to" — without a key migration. That
third value was considered and declined for this slice: it needs `PrepareQuote` to report the source
message's format, which is microservice work this slice does not otherwise require.

Three declarations, all in places that already exist:

- `UserPreferences.MailComposeFormat` plus its entry in `All` (`UserPreferences.cs:43-57`).
- `PREFERENCE_KEYS.composeFormat` (`usePreferences.ts:13-22`).
- `composeFormatOf(preferences): 'html' | 'text'` beside the other accessors, falling back to `html`
  for an absent or unrecognised value.

## Where it applies

### One pure function

`composeSeed.ts` gains a second exported function beside `buildComposeSeed`:

```ts
applyComposeFormat(seed: ComposeSeed | null, format: 'html' | 'text'): ComposeSeed | null
```

The parameter is nullable because `ComposeView`'s seed is: `mailtoSeedFrom` answers
`ComposeSeed | null` and the compose button supplies no seed at all. `null` in, `null` out — the
caller keeps one expression instead of a branch.

In `html` mode, on `null`, or on a seed whose `action` is `'draft'`, it returns its input untouched.
Otherwise it returns a copy in which:

- `text` is `htmlToText(seed.html ?? '')` and `html` is `undefined`. `htmlToText` is the existing
  function from `bodyFormat.ts`, and its `>`-prefixed re-quoting of `<blockquote>` is precisely what a
  plain-text reply needs.
- **every attachment has its `contentId` cleared.** That single field is the whole mechanism:
  `ComposeView` already splits the seed's attachments on `contentId` present or absent
  (`ComposeView.tsx:111-112`), so clearing it drops an inline image into the tray with no other code
  involved. It matches what the manual toggle does through `attachments.adoptInline`.

It lives in `composeSeed.ts` rather than in `ComposeView` because that file is already the one place a
seed's shape is decided, and because a pure function over a seed is testable without mounting a
composer.

### Applied once, at mount, in `ComposeView`

`ComposeView` applies it in a `useMemo` with an empty dependency list — the format is read once, when
the composer opens, and a later change to the setting never reformats a message being written.

`MessageReader` is not touched. It calls `buildComposeSeed` and knows nothing about a composing format;
threading the preference through the reader would put a compose concern in the reading pane for no gain.

### The draft carve-out keys on `action`, and nothing else works

**A resumed draft must reopen in the format it was saved in**, whatever the preference says. Getting the
discriminator right is the single subtlety of this slice, and two plausible readings are both wrong:

- *"Skip a seed that already carries a `text` body."* An HTML draft carries `text: null`, exactly like a
  reply or a `mailto:` seed, so this rule cannot see it and would convert it.
- *"If there is a seed, the seed decides."* `composeSeed.ts` writes `text: null` literally for reply,
  forward and edit-as-new, so this rule would apply the preference to nothing but a bare compose click.

The discriminator that does work is `seed.action`. `ComposeAction` is
`'reply' | 'replyAll' | 'forward' | 'editAsNew' | 'draft'`, and only `'draft'` describes a body that
already exists somewhere else in a chosen format. `ComposeView` already uses that same test for whether
the composer opens dirty (`ComposeView.tsx:138`, `seed?.action !== 'draft'`), so this reuses an
established boundary rather than inventing one.

A `mailto:` seed needs no special case: `mailtoSeed.ts:70` declares it `action: 'editAsNew'`, so it takes
the preference like any other new message. Its `html` is empty, so the conversion yields an empty text
body — which is the correct outcome, an empty composer in text mode.

The state initialiser then needs the preference only where there is no seed at all.
`ComposeView.tsx:105` currently reads `useState<string | null>(seed?.text ?? null)`. The obvious edit —
`seed?.text ?? (format === 'text' ? '' : null)` — is **wrong and must not be written**: with
`applyComposeFormat` already having done its work, that `??` would fire on every HTML seed it
deliberately left alone, the draft among them. The seeded cases are settled before the initialiser runs;
it only has to answer the no-seed case.

### The confirmation modal stays out of the way

The manual toggle gates on `losesFormatting` and raises a confirm dialog (`ComposeView.tsx:330-333`)
before discarding rich formatting. **The preference path must not reach that gate.** The user already
expressed the choice in Settings; a dialog asking them to confirm it on every reply would be noise.
`applyComposeFormat` performs the conversion directly, the way `switchToPlainText` does once confirmed.

## The mount-order constraint

This is the part that is easy to miss and expensive to get wrong.

`usePreferences()` answers asynchronously, and the project keeps no client-side copy of the defaults —
a consumer waits for the answer rather than guessing. `ComposeView` already calls it
(`ComposeView.tsx:79`) but only reads `captureRecipientsOf` at send time, by which point it has long
since resolved. Reading a preference **at mount** is new here.

Without a gate, the composer opens in HTML and flips to a `<textarea>` a moment later — under the
fingers of someone who may already be typing, discarding what they wrote. `ComposeView` therefore
renders `LoadingBlock` while `preferences` is `undefined`, the same way `MessageList` holds at
"Loading messages…" until they arrive.

## Testing

**Pure, in `composeSeed.test.ts`** — a reply seed converted to text quotes with `> `; an inline
attachment comes back with no `contentId`; `html` mode returns the input by identity; `null` in gives
`null` out. Two assertions carry the carve-out, and both must exist because they fail on *different*
mistakes: an **HTML** draft seed (`action: 'draft'`, `text: null`) is returned unchanged, which a
`text`-body test would miss, and a **text** draft seed is returned unchanged too.

**In `ComposeView.test.tsx`** — with the preference at `text`, a new composer renders a `<textarea>`
and no formatting toolbar; with the preference at `text`, a resumed HTML draft still renders the HTML
editor; the confirm dialog never appears on either path.

**In the microservice** — `UserPreferences.IsValid` accepts `html` and `text` for the new key and
refuses anything else, alongside the existing per-key cases.

Anything a test asserts that derives from a preference must be awaited with `findBy`/`waitFor`, never
with `settle()`. `settle()` drains a single macrotask; it has already raced the preferences query on CI
while passing on a developer machine, and this slice adds a mount-time dependency on that query, which
is the strongest form of the same hazard.

## Non-goals

A third "follow the original" value; a per-identity or per-recipient format; any change to reading, to
the outgoing sanitiser, or to the manual toolbar toggle, which stays exactly as it is and remains the
way to override the preference for one message.
