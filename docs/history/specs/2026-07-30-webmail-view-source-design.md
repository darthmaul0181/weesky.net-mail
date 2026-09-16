# View source — the message as it arrived, in its own tab

A "View source" entry at the foot of the reader's kebab opens `/mail/source?folder=…&uid=…` in a
new browser tab: a chrome-less page carrying a synthesis table — Message-ID, date, From, To,
Subject, SPF / DKIM / DMARC — above the verbatim RFC822 source.

## The problem

The reader distils headers and hides the rest. The auth badge paints green or red, the chevron
opens a details grid with mailing list, mailed by, signed by and TLS, and the spam gauge draws one
number. All of that is a reading of the headers, and none of it answers "what actually arrived".
Diagnosing a routing loop, a broken DKIM signature or a forged `From` needs the bytes, and today
the only way to them is another mail client.

## What this is not

**No "Download original", no "Copy to clipboard".** Gmail offers both beside its source view; both
are out of scope. What is wanted here is reading, and a browser already selects and copies text.

**No management of what the page shows.** No header filter, no fold/unfold, no syntax colouring,
no search box. The browser's own Ctrl+F searches a `<pre>` perfectly well.

**No back button, no logo, no way home.** Closing the tab is the return, the way it is in Gmail. A
reloaded or bookmarked URL is therefore a dead end, and that is accepted: the page is reached from
a message, and the mailbox is one tab away in the tab strip.

## Where it opens, and why that decides the rest

**A route sibling of `AppShell`, not a child of it** — under `RequireAuth`, beside the shell rather
than inside it:

```
RequireAuth
├── AppShell → mail, contacts, settings…
└── mail/source          ← new, lazy(), no shell
```

That single placement is what removes the rail and the folder column. It is not only a visual
choice: a child of `AppShell` would mount `FolderTree`, and with it `useFolders`, which polls
every 60 seconds — a folder tree refreshing itself forever in a tab whose only job is to display a
text file. `/ConnectedAccounts` would fire there too. The sibling route mounts neither.

`folder` and `uid` travel in search params, not in route segments, for the reason `/mail` does: a
folder path may contain the IMAP server's hierarchy separator.

The page lives in a new file, `src/modules/mail/source/MessageSourceView.tsx`. **No state is added
to `MessageReader`**, already the largest file in the module.

**A new tab costs a second SPA boot**, and that is the honest price of this design. It is bounded:
`hasSession()` reads `localStorage` synchronously so `RequireAuth` renders on the first frame, and
`activeAccountId` — the value behind the `X-Account-Id` header (`api.js:82`) — is seeded from
`localStorage` at mount rather than from the `/ConnectedAccounts` answer, so the source request
leaves before the accounts list lands. The tab shows the bundle's blank frame, then the page; it
never waits on a round-trip it could have skipped.

## The menu entry is a link, not a button

`MenuItem` gains an optional `href`. An item carrying one renders as
`<a role="menuitem" target="_blank" rel="noopener">` instead of `<button>`; everything else about
the item — label, icon, disabled, the key — is unchanged, and `DropdownMenu` closes on activation
exactly as it does for a button.

A `window.open()` inside `onSelect` would have been zero lines of `DropdownMenu`, and is the wrong
answer: middle-click, Ctrl+click and the browser's own "open in new window" all do nothing on a
`<button>`, and a control that navigates while looking like a command teaches the wrong thing about
the menu. `rel="noopener"` is not optional — without it the opened tab gets a handle on
`window.opener`.

The entry sits in a third group under its own separator, below Archive / Report as junk / Move to…
/ Copy to…. The existing rule holds: a separator is drawn only between two populated groups.

## The backend contract

`GET /api/Mail/Messages/Source?folder=…&uid=…` → `MailMessageSource`:

```
Subject, MessageId, Date, FromName, FromAddress, To[]
Authentication { Spf, Dkim, Dmarc, Raw }
Source, TotalBytes, Truncated
```

**One request, not two.** The synthesis rides with the source rather than being fetched from
`Messages/Detail` in parallel: that endpoint parses and sanitises the whole HTML body, which this
screen never displays.

**DMARC is a third verdict on the existing reader.** `MailAuthenticationReader.Parse` already
distils SPF and DKIM out of the topmost `Authentication-Results` through MimeKit's
`AuthenticationResults.TryParse`, which parses arbitrary methods; DMARC is one more
`Verdict(parsed.Results, "dmarc")` call and one more field on `MailAuthentication`. The field is
nullable like the other two, and the reader's existing contract carries over unchanged: an
absent or ambiguous verdict is rendered as absent, never as a failure.

`MailMessageDetail` gains the same `Dmarc` field for free, since it embeds the same record —
`mailTypes.ts`'s `MailAuthentication` mirrors it on the client side. The reader's `authVerdict` is
**not** changed to consider it — the badge's green-on-SPF-and-DKIM rule
is a validated decision from the reader spec, and widening it is a separate question.

### The cap is applied at the IMAP fetch, not at the render

`MailOptions.MaxMessageSizeMb` is 25. A source that large — mostly base64 attachment payload —
freezes the tab when handed to a `<pre>`, and pulling 25 MB over IMAP to display the first few
thousand bytes of it wastes the whole transfer.

So the session grows `GetMessageSourceAsync(folder, uid, maxBytes, ct)`, built on MailKit's
`IMailFolder.GetStreamAsync(uid, 0, maxBytes)` — a `BODY[]<0.N>` partial fetch — plus a
`Fetch(MessageSummaryItems.Size)` for `TotalBytes`. It does **not** go through the existing
`GetMimeMessageAsync`, whose whole point is a complete `BODY[]`.

`MAX_SOURCE_BYTES` is **1 MB**, an internal constant rather than a setting. Headers sit at the head
of the file, so what a truncation drops is the tail of the base64 — the part nobody reads. A heavy
HTML newsletter (200–300 KB) still arrives whole.

`Truncated` is `TotalBytes > MAX_SOURCE_BYTES`, computed from the size rather than inferred from
the returned length: a message of exactly 1 MB is complete, and guessing from the byte count alone
would label it truncated.

On the client it is one `api.getMessageSource(folder, uid)` in `api.js`, one `MailMessageSource`
type in `api/mailTypes.ts`, and one `useMessageSource` hook in `modules/mail/queries.ts` keyed
`['mail', accountId, 'source', folder, uid]` — the module's existing key shape.

## The page

While the request is in flight the page shows the house busy state, `LoadingBlock`. A URL missing
`folder` or `uid` renders the same error line as a 404 rather than requesting anything: the page is
reached from a menu, so a malformed URL is a hand-edited one.

Synthesis as a `<dl>` grid on the `ReaderDetails` pattern, then the source in a `<pre>`.

**The source is injected as text, never as markup.** No `dangerouslySetInnerHTML`, so no iframe and
no DOMPurify pass — unlike the message body, this content is displayed rather than parsed, and the
sandboxing that surrounds the body exists to contain a document the browser will interpret. React's
default child rendering already escapes it; the requirement here is that nothing downstream
reintroduces an HTML parse.

The `<pre>` carries `white-space: pre` with the block scrolling horizontally on its own — a
`Received:` line must stay on one line, which is the argument that chose the full-width page over
a modal in the first place.

A truncated source ends on an explicit line rather than on silence:

```
— truncated at 1 MB of 24.3 MB —
```

The tab title takes the subject, so three open sources stay distinguishable in the tab strip.

The synthesis renders each row only when its datum exists, the rule `ReaderDetails` already
follows. SPF / DKIM / DMARC sit on one row, each verdict shown as returned; the raw
`Authentication-Results` header is in the source below and is not repeated in the table.

## Errors

A 404 (the message is gone) and a 502 (the mail server could not be reached) both render a plain
message line above a Retry button, never a blank page — the shape `MessageList` uses when its
first block fails. A 401 needs no handling here: `api.js`'s
existing handler clears the session and `RequireAuth` redirects, in this tab as in any other.

## Tests

- `MessageSourceView.test.tsx` — the synthesis rows, a row omitted when its datum is null, the
  truncation marker present when `truncated` and absent otherwise, the 404 and 502 error lines.
- `DropdownMenu.test.tsx` — an item carrying `href` renders an anchor with `target="_blank"` and
  `rel="noopener"`; an item without one still renders a button.
- `ReaderActions.test.tsx` — the View source entry is present, is a link, and points at
  `/mail/source` carrying the current folder and uid.
- Backend — `MailAuthenticationReader` returns the DMARC verdict, and null when the header carries
  none; `Truncated` is true above the cap and false at exactly the cap.

Per the repo's testing rule, jsdom sees no layout: the `<pre>`'s horizontal scrolling and the
page's full-width claim are checked in a browser, not asserted in a test.
