# Plain-text composing — one button, and the message really is text

A toggle at the right end of the formatting toolbar switches the composer between the HTML editor
and a plain `<textarea>`. In text mode the message leaves as `text/plain` alone, the toolbar folds
away, a quoted reply is re-quoted with `>` prefixes, and an inline image becomes an ordinary
attachment rather than disappearing.

## The problem

The composer has exactly one input format. Everything a user writes goes out as HTML, and the
`text/plain` part the backend derives from it is an alternative nobody chose — a courtesy copy, not
the message. Writing to a mailing list, to a ticket system, or to anyone who reads mail in a
terminal means sending markup they did not ask for and cannot switch off.

Half the machinery is already there and works: `OutgoingMailSanitizer.Prepare` extracts a text
rendering of the HTML body, and `OutgoingMessageFactory` assembles a `multipart/alternative` from
the pair. What is missing is a way to say "this message is text" and have the stack believe it.

## What this is not

**Not a preference.** There is no account-level "compose in plain text by default". The toggle is
per message, and every new composer opens in HTML. Settings › Composing stays as it is.

**Not a `multipart/alternative` with a generated HTML part.** A message composed as text goes out
as `text/plain` and nothing else. Fabricating an HTML twin would defeat the whole point of the
button.

**Not a format flag on a received message.** Reading is untouched: `MessageReader` already prefers
the HTML part and falls back to the text one.

**Not a rich-text-preserving round trip.** Switching HTML → text converts and, when there is
formatting to lose, says so first. It does not keep a shadow copy of the HTML to restore later —
two divergent buffers with no rule for which one wins is a worse behaviour than an honest,
announced loss.

## The surface

The toggle lives at the **right end of `EditorToolbar`**, in a group of its own — it governs the
body it sits above, which the compose header does not. In text mode the toolbar collapses to that
single button, which is also the way back; nothing else in the bar means anything without an HTML
editor under it.

The `<textarea>` **replaces** `SquireEditor`, it does not hide it. Leaving Squire mounted behind a
`display: none` would give the composer two candidate sources for the body, and `buildPayload`
would eventually read the stale one. It carries the same `.compose-editor` box so the column's
band stack keeps its geometry.

The button is a `.compose-tool` like its neighbours, `aria-pressed` carrying the mode, labelled
`Plain text`.

## The conversions

A new pure module, `compose/bodyFormat.ts`, holds the three functions and nothing else:

**`htmlToText(html): string`** — the same block-boundary walk `OutgoingMailSanitizer.ExtractText`
performs, plus the part that matters here: a `<blockquote>` emits its lines prefixed with `> `,
nesting included (`>> `). Without it a switched reply produces a flattened wall of text with no
visible quoting, which is the one case plain text is most often wanted for.

**`textToHtml(text): string`** — escapes, then renders the line structure, exactly as
`QuotePreparer.TextToHtml` does on the server.

**`losesFormatting(html): boolean`** — true when the body carries bold, italic, underline,
strikethrough, a list, a link, an image, a colour, or a font/size declaration. This predicate alone
decides whether the confirmation opens: an empty body, or one carrying nothing but paragraphs of
text, switches with no dialog at all.

Text → HTML never asks anything: there is nothing to lose in that direction.

The confirmation reuses the composer's own leave-guard dialog shape — a `.modal-overlay` holding a
`.modal`, a titled header, and a `.folder-pick-submit` row ending in one `.btn-primary`. Like that
one it has no ✕: a confirmation whose two answers are both on its buttons does not need a third
way out. It names what goes: the formatting, and the inline images that become attachments.

**`htmlToText` deliberately duplicates part of the backend's `ExtractText`.** The two sit on
opposite sides of the wire and cannot be shared: the server derives an alternative part from an
HTML message it is about to send, the client converts what the user is switching. Only the block
walk is common; the quote prefixing exists on the client alone.

## Inline images become attachments

Inline images only ever arrive through a seed — a reply, a forward, or a resumed draft, staged by
`QuotePreparer`. The toolbar has no image button, so nothing else can create one.

On the switch to text, their ids move out of `useStagedAttachments`' `inlineRef` and into the tray.
The hook gains `adoptInline(items)`; `ComposeView` passes the `seed.attachments` entries carrying a
`contentId`, which already hold the file name and size the tray row needs. They then leave as
ordinary attachments.

Without this they would be lost silently: `OutgoingMessageFactory` packs a linked resource only
when the final body still references its `cid:`, and a text body references nothing.

## The wire

`SendMessageRequest` gains one field:

```csharp
public string? TextBody { get; init; }
```

**Non-null means the message is plain text.** One source of truth rather than a format flag that
could contradict the content it describes; `null` is every message composed before this slice and
every HTML message after it, so the existing contract is untouched. `SaveDraftRequest` derives from
`SendMessageRequest` and gains it for free.

`OutgoingMessageFactory.BuildMessageAsync` reads it once, at the top, and three things follow in
the same place:

- `new BodyBuilder { TextBody = request.TextBody }` — no `HtmlBody`, no generated alternative.
- No linked resources: the cid rewrite loop is skipped entirely, and every staged attachment is
  packed as a regular one.
- `IOutgoingMailSanitizer` is not called. There is no markup to judge, and running an HTML
  sanitiser over text would mangle a body containing `<` or `&`.

Everything else — the From resolution, the threading headers, the priority headers, the Bcc
handling — is upstream of the body and unchanged.

## Drafts round-trip

A draft saved as text has no HTML part, so `QuotePreparer` reopens it today through its text-only
branch: `TextToHtml(message.TextBody)`, an HTML composer holding a `<div>`. The format would be
lost on every save-and-resume.

`OpenedDraft` therefore gains `string? TextBody`, filled by the draft-open action when the source
message carries no HTML part, and `composeSeed` opens the composer in text mode when the seed
carries it.

**Only the draft path reads it.** A reply or a forward whose original was text-only must keep
arriving as HTML in an HTML composer — that is `QuotePreparer`'s current, correct behaviour, and
the user can still switch to text from there like on any other message.

## Scope and coverage

| Zone | Files | What its tests claim |
|---|---|---|
| Conversions | `compose/bodyFormat.ts` (new) | nested quotes prefix with `>`/`>>`, lists and `<br>` keep their line structure, escaping round-trips, `losesFormatting` is false on plain paragraphs and on an empty body |
| Composer | `ComposeView.tsx`, `EditorToolbar.tsx`, `useStagedAttachments.ts`, `styles/mail.css` | the toggle swaps the editor, the confirmation opens only when there is formatting to lose, inline ids land in the tray, the payload carries `textBody` and an empty `htmlBody` |
| Wire | `Models/Mail/SendMessageRequest.cs`, `Services/OutgoingMessageFactory.cs` | a plain-text send is `text/plain` with no HTML part, no `LinkedResources`, and every staged file packed as an attachment |
| Drafts | `Models/Mail/OpenedDraft.cs`, `Controllers/MailController.cs`, `compose/composeSeed.ts` | save as text → reopen in text mode, with the body intact |

## Checked in a browser, not in jsdom

The `<textarea>`'s box against the Squire canvas — padding, line height, the height it takes inside
the column's band stack — has to be verified in a real browser. jsdom measures nothing, so no test
in this repo can catch the editor changing height under the toggle.
