# Message priority — set it on the way out, read it on the way in

A third link beside Cc and Bcc reveals a `Priority` row in the composer (High / Normal / Low),
which writes the priority headers onto the outgoing message and onto the saved draft. On the way
in, the same headers are read back and shown as a small glyph before the subject in the list row
and as a chip in the reader's header.

## The problem

Nothing in the stack carries a notion of priority today: not `SendMessageRequest`, not
`MailMessageSummary`, not `MailMessageDetail`, and nothing in the frontend. A mail sent from this
webmail is always ordinary, and a mail *received* marked urgent looks exactly like one that is not
— the information arrives and is discarded.

## What this is not

**Not a state on a received message.** A priority is an RFC 5322 header, written once by the
sender. IMAP cannot rewrite a message in place, so "mark this received mail as important" would
need a private IMAP keyword that no other client reads. The star already does that job, and does
it as a flag every client understands.

**Not a search criterion, and not a sort key.** Priority does not enter `MailSearchCriteria` and
does not reorder the list.

**Not a preference.** There is no account-level default priority; every message starts Normal.

**Not a read receipt.** `Disposition-Notification-To` is the feature that usually ships beside this
one. It is a different question — it needs a consent prompt on the reading side — and it is out.

## The model

```csharp
public enum MailPriority { Normal, High, Low }
```

`Normal` is first so that it is `default`, and it is serialised as a string —
`"normal" | "high" | "low"` — the way `QuotePurpose` already is on `PrepareQuoteRequest`.

**`Normal` means no header at all**, not `X-Priority: 3`. A message of ordinary priority says
nothing about its priority, which is what every mail client does and what keeps the default free:
nothing is added to a message that did not ask for it.

## Writing: three headers, because clients read three different ones

`SendMessageRequest` gains `Priority`. `SaveDraftRequest` derives from it, so drafts get the field
without a line of their own.

`OutgoingMessageFactory` gains `ApplyPriorityHeaders`, a private static beside the existing
`ApplyThreadingHeaders` and called from the same place in `BuildMessageAsync`:

| | `X-Priority` | `Importance` | `X-MSMail-Priority` |
|---|---|---|---|
| High | `1 (Highest)` | `high` | `High` |
| Low | `5 (Lowest)` | `low` | `Low` |
| Normal | *(nothing)* | *(nothing)* | *(nothing)* |

Three headers rather than one because there is no single header everyone reads: Outlook and
Exchange read `Importance`, Thunderbird and Roundcube read `X-Priority`, and `X-MSMail-Priority` is
what older Microsoft clients look for. Writing one of the three would work in some recipients'
clients and silently not in others, which is worse than not offering the feature.

The values are the conventional spellings, not inventions: `1 (Highest)` is what the wire actually
carries in the field, comment included, and a bare `1` is read identically by everything that
parses it.

### The draft round trip

`OpenedDraft` gains `Priority`, filled by the same reader the inbound path uses, reading the
message that was appended to the drafts folder.

Without it the field dies in silence: saving a draft writes the headers correctly, reopening it
seeds the composer with a fresh `Normal`, and sending from there drops a priority the user set and
watched being saved. This is the same class of bug as a From-only edit not counting as a change —
the setting is invisible in the resumed composer, so nothing tells the user it is gone.

### A reply never inherits it

Answering an urgent message does not make the answer urgent. `PreparedQuote` is unchanged and the
composer opens at `Normal` for reply, forward and edit-as-new alike.

## Reading: one reader, one wiring point

`MailPriorityReader` — a pure static class in `Services/`, the shape `MailSpamScoreReader` and
`MailHeaderDetailsReader` already have. It takes a MailKit `HeaderList` and returns a
`MailPriority`, so one signature serves both the summary path (`IMessageSummary.Headers`) and the
detail path (`MimeMessage.Headers`).

Precedence — first readable header wins, and a header that is present but unparsable falls through
to the next:

1. `X-Priority` — leading integer, comment ignored. `1`–`2` High, `3` Normal, `4`–`5` Low.
2. `Importance` — `high` / `normal` / `low`, case-insensitive.
3. `X-MSMail-Priority` — `High` / `Normal` / `Low`.
4. `Priority` (RFC 2156) — `urgent` / `normal` / `non-urgent`.
5. Nothing found → `Normal`.

**An explicit `3` stops the chain.** A sender who wrote `X-Priority: 3` said Normal, and consulting
`Importance` behind their back would let a stale header from a mailing-list rewriter overrule them.

**First occurrence wins**, the rule the other two header readers follow. It matters less here —
these are written once by the sender, not appended by each relay the way `Authentication-Results`
is — but a message carrying two `X-Priority` lines has to resolve to one answer, and "the first" is
the answer the rest of this codebase gives.

The reader is wired in exactly two places: `ImapSession.FillSummary`, which the message list and
the search results already share precisely so the two cannot drift apart, and the detail mapping.
`MailMessageSummary` and `MailMessageDetail` each gain a `Priority` property.

### The cost: four header fields on every list FETCH

`SummaryItems` cannot answer this on its own — the priority headers are not in the envelope. Beside
it goes a `SummaryHeaders` array, and the three `FetchAsync` call sites that use `SummaryItems`
(the SORT page, the sequence-window fallback, and the search-result fill) switch to the MailKit
overload that takes header names.

On the wire that adds one `BODY.PEEK[HEADER.FIELDS (X-PRIORITY IMPORTANCE X-MSMAIL-PRIORITY
PRIORITY)]` item to a FETCH that already asks for envelope, flags, size, bodystructure, internal
date and preview text. It is **one more item in the same round trip, not a second request** — but
on a 100-message streaming block it is 100 more header parses, and that is the whole price of this
feature. It is spent knowingly: the marker in the list row is what makes a priority useful, and
without the fetch the priority could only be seen after opening the message.

## The composer

`ComposeView` gains `priority` state and the row that edits it.

The link joins `.compose-cc-links`, the strip that already holds Cc and Bcc, and behaves the way
they do: shown while the row is hidden, gone once it is revealed. This is the composer's own
established idiom for a field that most messages do not need — "revealed on demand rather than
shown by default" — so the third one costs no new vocabulary.

The revealed row is a `.field-h` carrying a `DropdownMenu` (High / Normal / Low), the component the
formatting toolbar now uses for font, size and alignment.

**The row cannot be re-hidden while the value is not `Normal`.** Cc and Bcc are safe to collapse
because collapsing them is not the same as emptying them — the tokens stay on screen. A priority
row that folded away would take a live setting off the screen while it kept riding on the message.
Setting the value back to Normal is what folds it.

**Changing the priority makes the draft dirty.** `ComposeView`'s `dirty` means "changed since open
or since the last save", and a priority-only edit is a real change for the same reason a From-only
edit is: leaving without the guard firing would discard a setting the user made.

## The list row and the reader

**The list row** gets a 12px glyph *inside* the subject line, inline before the text. No reserved
column and no new flex slot: a Normal-priority row — every row, nearly always — is byte for byte
what it is today. High takes `--danger`, Low takes `--text-muted`.

Two icons, `PriorityHighIcon` and `PriorityLowIcon`, drawn as a **matched pair**: a double chevron
pointing up and one pointing down. The design sketch used a flag for High and an arrow for Low;
that is being corrected here, because two unrelated marks read as two unrelated facts, where one
scale read up and down reads as one.

**The reader** gets a chip inline after the subject in the header stack — `High priority` /
`Low priority`. It sits inside the stack, so it does not touch `ReaderActions`, which is a sibling
of the stack rather than part of it.

**Both say whose claim this is.** `X-Priority` is written by the sender and is freely forged; spam
sets it constantly. So the marker stays a glyph and never becomes a coloured band, and it carries
the originating header in a hover text. The reader's chip uses `Tooltip` with `bottom-left`
placement, the one direction that cannot be clipped by that column's `overflow: hidden`. The list
glyph uses a plain `title` instead: `Tooltip` always mounts its bubble, and a hundred extra bubbles
in a streaming list buys nothing a `title` does not already give at this size.

This restraint follows the auth badge's rule, already validated in this module — no signal on an
ambiguous result, because a badge that cries wolf teaches the reader to ignore it.

## Tests

**Backend**

- `OutgoingMessageFactoryTests` — High writes the three headers with their exact values, Low
  likewise, Normal writes none of the three.
- `MailPriorityReaderTests`, new — each header at each level; `1 (Highest)` parsed past its
  comment; an explicit `X-Priority: 3` returns Normal without consulting `Importance`; an
  unparsable `X-Priority` falls through to `Importance`; two `X-Priority` headers resolve to the
  first; no header at all returns Normal.
- `MailControllerTests` — the priority reaches the factory from both `Send` and `Drafts`.
- The draft round trip — save at High, reopen, `OpenedDraft.Priority` is High.

**Frontend**

- `ComposeView.test.tsx` — the link reveals the row and disappears; the row refuses to fold at High
  and folds at Normal; the chosen value rides in the send payload and in the draft payload; a
  priority-only change arms the leave guard.
- `MessageList.test.tsx` — the glyph renders for High and for Low, and **not at all** for Normal.
- `MessageReader.test.tsx` — the chip renders with its header name, and is absent at Normal.

Per the repo's testing rule, jsdom sees no layout: that the glyph does not push the subject's
ellipsis around, and that the reader chip does not collide with the actions zone, are checked in a
browser rather than asserted.
