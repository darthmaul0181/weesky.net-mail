# Trusted senders — always show images from this one address

A per-sender allowance for remote images: the "Show images" button gains a chevron whose single
entry approves the sender once and for all, and the reader stops asking for that address.

## The problem

`mail.alwaysShowImages` is all-or-nothing. A reader who wants their bank's statements to render
but still wants a stranger's tracking pixel blocked has no middle setting, so they click "Show
images" on the same handful of senders forever. The per-message consent is the right default —
loading a remote image tells the sender the message was opened — but it should be possible to
answer the question once for an address that has already earned it.

## What this is not

**No management screen.** No Settings tab listing approved senders, deliberately. The list is
built one click at a time and revoked the same way, from the message that raised the question. A
screen for it would be the first brick of the "everything has a panel" webmail this project is
trying not to become.

## Where the check lives

**Client-side, in the reader, exactly like `mail.alwaysShowImages`.** The sanitiser keeps blocking
every remote image and keeps culling CSS declarations containing `url(`; the trusted list never
enters the sanitising pipeline.

That is not a stylistic preference, it is what keeps the message cache sound. A body sanitised
against the account's list would be a different document per account, and revoking a sender would
have to invalidate every cached message from them. Deriving in the reader keeps one document for
everyone and makes revocation a re-render:

```ts
const senderTrusted = trustedSenders.has(canonical(data.fromAddress))
const showImages = imagesShown || alwaysShowImages || senderTrusted
```

`imagesShown` keeps meaning "the user clicked Show images on *this* message" and nothing else, so
the per-message reset effect is untouched — the same reasoning that shaped
`2026-07-21-webmail-always-show-images-design.md`.

## The table

`trusted_senders` in `snoopy_webmail`, on the pattern of the three tables already there: GUID FK
onto `users`, cascade delete, `utf8mb4_bin`.

```sql
CREATE TABLE `trusted_senders` (
  `user_id`   CHAR(36)     NOT NULL,
  `address`   VARCHAR(320) NOT NULL COMMENT 'Forme canonique minuscule ; 320 = max RFC 5321',
  `last_used` DATETIME     NOT NULL COMMENT 'UTC ; posée par le code, jamais par le schéma',
  PRIMARY KEY (`user_id`, `address`),
  CONSTRAINT `fk_trusted_senders_user`
    FOREIGN KEY (`user_id`) REFERENCES `users`(`id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;
```

Creation is manual, like every other table here — no EF migrations. A companion prerequisite doc
(`docs/superpowers/webmail-trusted-senders-table.md`) carries the script for **both**
`snoopy_webmail` and `snoopy_webmail_dev`, to be applied before the backend ships.

**Addresses are stored canonical**, through `IdentityResolver.Canonical`
(`Trim().ToLowerInvariant()`) — reused, not reimplemented. The collation is binary, so a casing
difference would split one sender into two entries and the second would silently do nothing.

**No `DEFAULT CURRENT_TIMESTAMP` on `last_used`**, for the same reason `users.creation_date` has
none: the code owns the value, so a read can never move it.

### The cap is what bounds the table, not the TTL

**1 000 rows per account, refused with 400 beyond.** A TTL deletes after the fact and bounds
nothing in the meantime; a cap bounds. It is also the honest answer to the growth question, which
on the arithmetic is not really a question at all: a row is roughly 200 bytes with its index, so a
thousand approved senders cost 200 KB per account.

## The API

`TrustedSendersController`, shaped like `IdentitiesController` — webmail data, not mail-server
data, so no IMAP session and no credentials cookie.

| Verb | Answer |
|---|---|
| `GET /api/TrustedSenders` | `string[]` of canonical addresses. No `last_used`: no screen shows it. |
| `POST /api/TrustedSenders` | `{ address }` → 204. Upsert. 400 if the address does not parse, or if the cap is reached. |
| `DELETE /api/TrustedSenders?address=…` | **Always 204**, unknown address included. |

The delete is idempotent for the reason `DELETE /api/Mail/Attachments/{id}` is: a 404 would
confirm which addresses a given account has approved, and the caller has nothing useful to do with
the distinction anyway.

## `last_used`, and when it is written

Written **server-side, piggybacked on `GET /Mail/Messages/Detail`**. The reader is already
fetching that message; a dedicated client call would buy a second round trip per open for nothing.

The address the message came from is canonicalised, looked up on the primary key, and the date is
written **only if it is not already today's**. Day granularity is ample against a 365-day
retention, and the test spares the `UPDATE` on every reopen within a day. A sender who is not
approved costs one `SELECT` that finds nothing.

**The write is never fatal.** It follows rule 5 of the microservice's CLAUDE.md — IMAP first,
bookkeeping second; a failed bookkeeping write degrades, it never fails the user's operation.

Two consequences, both accepted:

- TanStack caches the message detail, so reopening a message inside the cache window touches
  nothing. Irrelevant at this granularity.
- The row is touched even while `mail.alwaysShowImages` makes the list dormant. Correct: the entry
  *would* apply, and the global setting is not a decision about this sender.

## The sweep

`TrustedSenderSweeper : BackgroundService`, one tick a day, deleting rows whose `last_used` is
older than the retention. Built on `StagedAttachmentSweeper`'s pattern, including the log line on
every tick — zero removed included, since the line doubles as the sweeper's heartbeat — and the
`catch` that stops a failed sweep from taking the host down with it.

**One difference that will not compile its way to your attention.** `StagedAttachmentSweeper`
injects its store directly because the staged store is a singleton over the filesystem. The
`DbContext` here is scoped, so this sweeper takes an `IServiceScopeFactory` and opens its own
scope per tick. Injecting the store directly compiles and throws on the first tick.

Retention: `TrustedSenders:RetentionDays` in `appsettings.json`, default `365`.

## The reader

`useTrustedSenders()` lives in `modules/mail/queries.ts` beside `useIdentities` — one query, a
`select` building the `Set` once, invalidated by both mutations.

### Granting: the split button

The banner keeps its current condition (`blockedImageCount > 0 && !showImages`), unchanged,
because `senderTrusted` enters through `showImages`. A property falls out of that for free: **the
chevron can only ever grant trust, never revoke it**, since an approved sender has no banner at
all to hang a chevron from.

The split reuses `.attachment-split`'s shape, with `direction="down"`. One difference: there both
halves are neutral chips, here both carry the accent fill, so the seam between them is
`--action-primary-fg` at 32% alpha rather than a `--border` hairline — no new token, and it works
on either ground.

The menu holds one entry and no separator:

> **Always show images from this sender**

Spelled out in full deliberately. It is alone in its menu so no width constrains it, and it is the
moment of consent — the place to be explicit.

### Revoking: the kebab

An entry appended to the reader's existing kebab after a separator, shown only when
`senderTrusted && !alwaysShowImages`:

> **Block sender's images**

Two things decided this wording. It fits within `.dropdown-menu`'s `min-width: 180px` — the app is
`box-sizing: border-box` (`index.css:1`), so that floor is the whole box, icon and padding
included — which means **the kebab keeps exactly the width it has today**. And it reuses the word
the banner has already taught the reader: *"3 remote images were **blocked**"*. Blocking is a
return to the default, not a new concept.

The fit is a criterion, not a measurement: `--font` is `system-ui`, so the label renders in Segoe
UI on Windows, SF on macOS, and whatever the desktop supplies on Linux. It was checked against the
floor in the mockup on one machine; the label is short enough to have margin on all three, but the
rule to hold on to is "no longer than the widest existing entry plus a word", not a pixel count.

Rejected: *"Stop trusting this sender"* — "trusting" is the data model's vocabulary, never the
screen's, which speaks only of images shown or blocked. And *"Block this sender"*, the shortest of
all, which reads as "stop delivering their mail"; ambiguity about a destructive-sounding action is
not a saving.

**The two labels are deliberately asymmetric**, and never on screen together: one exists only for
an unapproved sender, the other only for an approved one.

Hiding the entry under `alwaysShowImages` matters: with the global on, revoking changes nothing
visible, and a menu entry whose effect cannot be seen is worse than an absent one. The list is not
destroyed meanwhile — it sleeps, and turning the global back off finds it intact.

### Icons to create

- `ChevronDownIcon` — mirror of the existing one, `M4.5 7.5l5.5 6 5.5-6`.
- `ImageOffIcon` — a struck-through frame. The kebab's five other entries all carry an icon; a
  sixth without one reads as a rendering fault.

## The setting

A `ToggleRow` labelled **Trust my contacts** in `GeneralPage`, `disabled` permanently and
unchecked, with a `.settings-note` beneath it: *Available once Contacts ships.*

**No preference key is declared.** Nothing can write it while the row is disabled, and an entry in
the `UserPreferences` registry that nothing can reach is dead code carrying dead validation and
dead tests.

### The inclusion rule, for the Contacts slice

The three scopes nest rather than compete:

```
Always show remote images  ⊃  Trust my contacts  ⊃  the per-sender list
```

So the greying goes one way only. With "Always show remote images" on, everything narrower is
without effect and greys out. The converse does not hold: "Trust my contacts" on does not make the
global redundant, since the global adds every sender who is *not* a contact — greying it there
would trap the user in the narrower setting.

When Contacts ships, this becomes one preference key, one clause in the predicate
(`|| (trustContacts && contacts.has(address))`), and `disabled = alwaysShowImages` on the row.

## Tests

- **Store** — upsert is idempotent; canonicalisation folds case and surrounding space; the cap
  refuses the 1 001st; deleting an unknown address is not an error; two accounts cannot see each
  other's rows.
- **Controller** — 400 on an unparsable address, 400 at the cap, 204 deleting an unknown address,
  401 unauthenticated.
- **Touch** — sets the date on an existing row; creates nothing for an unapproved sender; does not
  write twice in one day; a write that throws does not fail `Detail`.
- **Sweeper** — removes rows past the retention, spares those inside it, and opens a scope per
  tick.
- **`MessageReader`** — approved sender: no banner, no button, `srcDoc` carries restored `src`
  attributes, and the kebab holds the revocation entry. Unapproved: today's behaviour intact.
  Global on: the revocation entry is **absent**. Choosing the chevron's entry reveals the images
  without changing message.
- **`GeneralPage`** — the row is present, disabled, and its note is with it.

## Prerequisite

`docs/superpowers/webmail-trusted-senders-table.md` — the DDL, to run on `snoopy_webmail` **and**
`snoopy_webmail_dev` before the backend is deployed. Without the table every call answers 500 and
the reader's list query fails, which degrades to "no sender is trusted" rather than to a broken
reader, but the banner would then never stop coming back.
