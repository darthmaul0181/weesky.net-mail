# Webmail — sound and desktop notification on new mail

**Goal:** two independent settings, both off by default — play a sound, and raise a desktop
notification, when mail arrives in the inbox.

**Scope decision that sets the size of this slice:** notifications fire **while a webmail tab is
open**, including a background or minimised one. That is what Rainloop does and what this design
builds. Notification with the browser *closed* would need a service worker, Web Push with VAPID
keys, a subscription endpoint, and above all a server watching every mailbox continuously — a
standing IMAP `IDLE` per user or a server-side poll loop. That contradicts the no-standing-
connection decision taken twice already (no pooling, polling over `IDLE`) and is a sub-project of
its own. Recorded in *Out of scope*.

**What this slice does not have to build:** the trigger. The folder poll from the previous slice
already reports `uidNext` per folder, every minute, in one `LIST`+`STATUS`.

---

## The two settings

Two registry entries, both defaulting to `"false"` — an application that makes noise nobody asked
for is a defect:

```csharp
public const string MailNotifySound = "mail.notifySound";
public const string MailNotifyDesktop = "mail.notifyDesktop";
```

They join the General settings tab under the two existing rows, as `.field-h.is-setting` rows with
`.toggle-switch` controls, like `mail.showPreview`.

## Permission is not a setting

The desktop toggle has **two states, not one**: our stored preference, and the browser permission,
which is not ours to write. Conflating them produces the lying switch — on, and silent.

**Enabling requests permission inside the click gesture** (Safari requires it; it is good practice
everywhere). Then:

- **granted** → the preference is written `true`.
- **denied** → the preference **stays `false`**, and a line explains the browser is blocking and
  where to reopen it. Storing a `true` that produces nothing would be the lying switch.
- **already denied in an earlier session** → `requestPermission()` resolves `denied` immediately
  without prompting. Same handling, same message — otherwise the click looks ignored.

The symmetric case: granted yesterday, revoked today in browser settings, preference still `true`.
On mount the app reads `Notification.permission`; if it is no longer `granted` the toggle renders
off with the explanation. **The permission always wins** — it is the truth, the preference is only
a wish.

**Browser coverage.** Same API and same permission prompt on Chrome, Edge, Firefox and Safari
(macOS). Two exceptions worth stating in the UI rather than failing silently: **HTTPS is required**
(satisfied; `localhost` is exempt for development), and on **iOS/iPadOS notifications work only for
a site added to the home screen** as a web app — in an ordinary Safari tab they are impossible, no
matter what the setting says.

The sound setting needs no permission, but has its own trap — see *Autoplay* below.

## Waking the poll

The poll currently pauses when the tab loses focus, and a background tab costs nothing. A
notification is useful **only** in that state. So `refetchIntervalInBackground` becomes
**conditional on either notification setting being on**: whoever enables nothing keeps today's
behaviour and pays nothing; whoever enables pays exactly what they asked for. The cost becomes an
explicit user choice rather than one taken on their behalf.

Known limit, true of any option here: browsers throttle background-tab timers to roughly once a
minute after a few minutes hidden. The interval is already 60 s, so this holds — but a
notification may run up to a minute late. Only Web Push removes that, and Web Push is out of scope.

## What fires, and when

**A hook of its own, `useMailNotifications`,** separate from `useListRefresh`: that one watches the
*displayed* folder, this one watches the **inbox whatever you are looking at**. Both read the same
polled folders query, so there is no extra request.

It lives in **`AppShell`, not `MailLayout`** — otherwise nothing would fire while the user sits in
Settings or Calendar.

**Only `uidNext` triggers.** Not `total`, not `unread`, not `highestModSeq`: a deletion, or a
message read on a phone, must not ring. The baseline rule of the poll applies here too — the first
observation never notifies, or opening the webmail would ring every time.

The inbox is found through the **role resolution chain**, never by matching the name `INBOX`, so it
keeps working against the arbitrary servers of tranche 2d.

**Identifying the new messages, not the first ones.** The `uidNext` delta gives the count. For
sender and subject the inbox's first page is fetched — a cost paid on arrival, a few dozen times a
day, never per tick. The new messages are then selected by **`uid >= the previous uidNext`**, not
by taking the top rows: the server sorts by `Date` header, so a message delivered late lands
mid-list and "the first row" would name the wrong sender. Note the boundary is `>=`, not `>`:
`UIDNEXT` is the *next* UID the folder will assign, so a single arrival sits exactly on it, and a
`>` would miss the commonest case of all.

**The cache is never read — an earlier draft of this document said it should be, and was wrong.**
Reading the inbox's first page from the query cache "when the list refresh just fetched it" looks
free and is worse than useless: that sibling refresh is *asynchronous* in both modes, while this
watcher reads synchronously in the same effect pass, so the cached page is always the one from
**before** the arrival. `newSince` then finds nothing, the notification degrades to "1 new
message" instead of naming the sender, and clicking it stops opening the message — all of it
precisely when the user has the inbox open, the case the optimisation was written for. Always
fetch.

If the filter does not yield exactly one message, the notification falls back to the count —
honest rather than wrong.

**One notification per tick.** One message → sender and subject. Several → "3 new messages".
Clicking it focuses the window and, when a single message is known, opens it.

**Two tabs, two different mechanisms.** For the bubble the API already has the answer: an identical
`tag` makes the browser *replace* rather than stack, so duplicates collapse natively. For the sound
there is no equivalent — a `localStorage` guard records the last announced arrival, and a tab
finding a value already current stays quiet. Stated plainly: this is not an atomic lock; the two
tabs poll on independent clocks and practically never land in the same millisecond, and the worst
case is one duplicate beep.

**The guard stores `{ uidValidity, uidNext }`, not a bare number.** A bare monotone number is a
trap in both directions. A rebuilt folder raises `uidValidity` and can send `uidNext` leaping —
the watcher would announce thousands and bank that inflated figure — or restarting near 1, where a
previously banked 30,000 would refuse every genuine arrival in every tab, for good. Recording the
numbering the claim belongs to makes a foreign `uidValidity` mean "no claim" rather than a
permanent gag.

**Notifications fire even when the tab is focused.** Many clients suppress in that case. "Stays
quiet depending on what I am looking at" is a rule a user cannot predict; "I turned the sound on,
it makes a sound" needs no documentation.

## The sound

**RainLoop's own `new-mail` sound**, the one this webmail is being compared against throughout.
Synthesising a chime was considered and dropped in its favour: the point of reference is the
sound the user already knows.

**Licence — checked, not assumed.** RainLoop Webmail is **MIT**, not AGPL as first suspected:
*"Copyright (c) 2022 RainLoopTeam"*, standard MIT text. MIT permits reuse provided the copyright
notice travels with it, so this carries **no licence obligation onto this service** — unlike AGPL,
which for a network-served application would have reached the whole source. The notice goes in
`src/frontend/THIRD-PARTY.md`; without it the reuse is not licensed.

One limit stated rather than glossed: the sound files carry **no attribution metadata** (only an
ffmpeg encoder tag), and the upstream directory has no attribution file, so where RainLoop
originally got the sound cannot be verified. What is verifiable is that RainLoop ships it under
MIT.

**MP3 only, not both formats.** Upstream ships `new-mail.mp3` and `new-mail.ogg` — a split that
dates from when Firefox refused MP3 and Safari refused Vorbis. Every current browser decodes MP3,
so the second file is 15 KB of history. Verified: MP3 with an ID3v2.4 header, OGG Vorbis stereo
44.1 kHz, **1.91 s**, ~15 KB each.

The asset lives in `src/frontend/src/assets/` beside the images, hashed and bundled by Vite like
any other.

### Autoplay: the rule nobody sees coming

Browsers block sound from a page the user has never interacted with. A notification sound is by
nature played when nobody is interacting — squarely inside the blocked case.

What saves it: **enabling the setting is an interaction**, used twice. The `Audio` element is
created and played inside the click gesture, and **the sound plays immediately as confirmation** —
proving it works in front of the user and registering the origin engagement that Chrome and Edge
then remember. One click is both the demonstration and the unlock. The same element is kept and
replayed later, rather than constructed per notification.

It does not cover everything: **Safari is strictest** and may still refuse in a background tab. A
rejected `play()` is swallowed in silence — never a toast: a notification that failed must not
produce a second interruption announcing the failure.

## Testing

The decision is a pure function — two folder snapshots plus the two settings in, a decision or
`null` out — carrying the fine cases: baseline, deletion only, read-flip only, settings off,
cross-tab guard already set, delta greater than one. Around it, fakes for `Notification`,
`Audio` and `localStorage` cover permission denied, permission revoked between sessions, and
blocked audio (a rejected `play()` promise must be swallowed, and a test pins that).

## Out of scope

- **Web Push with the browser closed** — the sub-project described at the top.
- **Per-folder opt-in.** Only the inbox notifies. Mail filed by a Sieve rule was explicitly
  declared not worth an interruption — ringing for it would undo the rule the user wrote.
- **Unread count in the tab title.**
- **A user-chosen sound.** One sound, RainLoop's, shipped as an asset.
