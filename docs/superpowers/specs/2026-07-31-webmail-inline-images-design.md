# Inline images — paste one in, drop one in, size it

An image pasted or dropped into the message body becomes part of the body rather than a file
appended to it: it is staged with a Content-ID, shown at the caret, and packed into the outgoing
`multipart/related` as a `cid:` resource. Clicking it opens a floating bar offering three display
widths.

## The problem

The inbound half of this already works and has since 2c2b. `QuotePreparer` stages the inline parts
of a quoted or forwarded message and rewrites their `cid:` references into staged content URLs;
`OutgoingMessageFactory` (`OutgoingMessageFactory.cs:118-147`) turns those URLs back into `cid:` on
the way out, packs the parts as linked resources, and drops any the final body no longer names.

What is missing is the user's own way in. `POST /api/Mail/Attachments` never assigns a Content-ID,
so a file the composer uploads is structurally an attachment and can never be anything else. Pasting
a screenshot produces nothing at all, and a dropped image lands in the tray.

## What this is not

**Not a toolbar button.** Insertion is by paste and by drop, deliberately. The cost is stated
rather than hidden: there is no keyboard path and no touch path to inserting an image, and a file
that can be neither copied nor dragged has no door in. A button can be added later without changing
anything below it — the insertion path it would call is the same one.

**Not a resize on upload.** The bytes always leave whole. The three widths are a display choice
written into the markup; nothing is re-encoded, so a recipient can still open the full image.

**Not a second list.** An inline image never appears in the attachment tray. It is removed by
selecting it in the editor, the way Gmail and Thunderbird do it, and the tray keeps meaning "what
the recipient receives as attachments" — which is also what the existing code already does with
quoted inline images, whose ids never enter `items`.

**Not a change to sending, drafts, or reopening.** Those paths already carry inline parts end to
end. This slice adds a producer to them and nothing else.

## The wire

`POST /api/Mail/Attachments` gains one form field:

```csharp
[FromForm] bool inline = false
```

When it is set, the controller generates a Content-ID with `MimeUtils.GenerateMessageId()` and
hands it to `staged.SaveAsync`, whose `contentId` parameter is already optional and already tested
(`StagedAttachmentStore.cs:39`). `StagedAttachmentInfo` already carries `ContentId`, so the response
shape does not move.

**A non-image content type is refused with 400.** An inline part that cannot be displayed has
nowhere to be shown: it would ride in the `multipart/related` referenced by nothing, which is the
exact condition the send path's referenced-only pruning exists to prevent.

Nothing else on the backend changes.

## Getting an image in

**Paste.** A `paste` listener on the editor root in the capture phase, ahead of Squire's own. When
`clipboardData.files` holds an image it takes the event, uploads with `inline=true`, and inserts at
the caret. No `data:` URI ever enters the editor — the `<img>` points at the staged content URL,
byte for byte the same shape a quoted inline image already has, so one rendering path serves both.

**Drop.** `.compose-drop-overlay` is `position: absolute; inset: 0; z-index: 30` with no
`pointer-events` rule (`mail.css:1472`), so the moment a drag starts it covers the editor and the
body can never receive `dragenter` or `drop`. It becomes `pointer-events: none`, and the editor root
takes its own drag handlers with a depth counter of its own — the same reason the surface has one,
`dragleave` firing at every child boundary.

Two zones, two labels: over the body, "Drop image into the message"; anywhere else on the composer,
today's "Drop files to attach". The surface counter stays the "a drag is happening" signal and the
editor's boolean picks the wording.

**A non-image dropped on the body falls through to the tray** rather than being refused. The user
aimed at the message; attaching it is the useful reading of that, and refusing the drop where they
aimed teaches them nothing.

## Sizing

Clicking an `<img>` in the editor selects it and opens a floating bar anchored to it: **Small**,
**Best fit**, **Original**. Each writes a `width` attribute (320, 640, or none) alongside a
`style="max-width: 100%"` that is always present.

**The choice lives in the markup, not in React state.** That is what carries it through a draft save
and reopen with no code: the outgoing sanitiser keeps both `width` and `style` — Ganss's defaults
are untouched there, only the scheme list is tightened (`OutgoingMailSanitizer.cs:22-31`).

The selection clears on `pathChange` and on blur. Delete removes the image, as in any editor; the
bar carries no remove button of its own.

## Staged-id bookkeeping

`inlineIds` derives from the seed alone today (`ComposeView.tsx:112`), and `useStagedAttachments`
syncs it through a passive effect whose comment states the invariant it relies on: those ids come
from the seed the composer mounted on, so they cannot change between a render and a handler after
it. An insertion breaks that.

`useStagedAttachments` therefore gains **`addInline({ id, fileName, size })`, which writes
`inlineRef` synchronously**, the way `apply` keeps `itemsRef` authoritative at every instant just
above it. An image inserted and then abandoned in the same tick must still be released; routed
through the prop it would not be, and the bytes would fall to the TTL sweeper in silence.

`ComposeView` keeps the inserted entries beside `seedInline`, so:

- `buildPayload`'s `[...inlineIds, ...attachments.ids]` is unchanged — the inserted ids are inline
  ids like any other.
- The switch to plain text passes the widened list to `adoptInline`, and an inserted image becomes
  an ordinary attachment exactly as a quoted one already does.
- An insertion marks the form dirty, so the leave guard covers it.

## Scope and coverage

| Zone | Files | What its tests claim |
|---|---|---|
| Staging | `Controllers/MailController.cs` | `inline=true` stages a Content-ID, a non-image is refused 400, the default still stages none |
| Insertion | `compose/SquireEditor.tsx`, `compose/ComposeView.tsx` | a pasted image uploads inline and inserts an `<img>` on the staged URL; a drop on the body inserts where a drop on the surface attaches; a non-image on the body attaches |
| Sizing | `compose/ImageSizeBar.tsx` (new), `styles/mail.css` | clicking an image opens the bar; each width writes the attribute and keeps `max-width` |
| Bookkeeping | `compose/useStagedAttachments.ts`, `compose/ComposeView.tsx` | an inserted id rides the payload, is released on discard, and moves to the tray on the switch to plain text |

## Checked in a browser, not in jsdom

Two things here cannot be caught by any test in this repo, and both are the reason a defect would
ship: **the overlay's `pointer-events`** — jsdom has no hit testing, so a drop target covered by its
own overlay looks identical to a working one — and **the floating bar's placement**, which has to
stay inside a composer column that is `overflow: hidden`, against an image scrolled to the top and
to the bottom of the editor.

## Amendment — 2026-07-31: the Sizing section is superseded

**The `ImageSizeBar` was built, shipped in Task 4, and removed.** Resizing is now Squire's own eight
handles; the three widths (Small / Best fit / Original) are gone, and free resize replaces them —
a trade-off accepted knowingly by the human partner. Everything above about sizing describes code
that no longer exists; it is left in place as the record of what was designed, not as a description
of the product.

Why, so nobody re-adds it: `squire-rte` 2.4.8 constructs an `ImageResizer` unconditionally
(`squire-raw.mjs:2630` — there is no config flag to disable it). A click on an `<img>` therefore
draws Squire's own handles over any bespoke selection UI, and it appends
`.squire-image-resize-container` **into the editor root**, which Squire's MutationObserver reports
as an `input` — and `ComposeView.touchBody` answered every `input` by closing the bar, so it never
painted. Dragging the native handles writes `style.width` / `height: auto`, which overrides the
bar's `width` attribute in any case. No test in this repo could see any of it: jsdom has no
geometry and `ComposeView.test.tsx` stubs the editor.

Two consequences kept from this amendment. The outgoing sanitiser must keep the **inline width
style**, not a `width` attribute — asserted by
`OutgoingMailSanitizerTests.Prepare_KeepsTheInlineSizeStyleAResizeWrites`, and the resumed-draft
path runs through that same sanitiser (`QuotePreparer`). And `SquireEditor` compares `getHTML()`
before forwarding `onChange`: `getHTML` omits the handle container, so an unchanged body means a
click rather than an edit, and clicking an image in a resumed draft no longer prompts to save it.
