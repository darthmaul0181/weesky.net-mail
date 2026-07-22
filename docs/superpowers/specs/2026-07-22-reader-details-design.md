# Reader header — expanded details (Gmail parity)

Validated design, 2026-07-22. Mockup approved by the user (artifact f44b042a).

## Goal

A chevron at the end of the reader's "From" line expands the header into a Gmail-style
label/value grid with the message's delivery details. Clicking again returns to the
current compact display. One-shot per message: no persistence, no setting.

## Backend — 5 new fields on `MailMessageDetail`

Extracted in `ImapSession.GetMessageDetailAsync` from `message.Headers` (already fetched,
no extra IMAP round-trip) via a new `MailHeaderDetailsReader`, modelled on
`MailAuthenticationReader` / `MailSpamScoreReader`. Rule 7 applies: topmost header only.

| Field | Source | Notes |
|---|---|---|
| `MailingList` (string?) | `List-Id` | Shown verbatim, e.g. `<news.example.org>` |
| `SentBy` (string?) | `smtp.mailfrom=` domain in Authentication-Results → `Return-Path` domain → `Sender` domain | Gmail's "Mailed by" (envelope domain). Nearly always present |
| `SignedBy` (string?) | `header.d=` in Authentication-Results → `d=` of `DKIM-Signature` | Gmail's "Signed by". Hidden on an explicit `dkim=fail` (no fallback either); a passing signature outranks a failing one |
| `UnsubscribeUrl` (string?) | `List-Unsubscribe`: first `https:`/`http:` URL, else `mailto:` | Scheme whitelist only — anything else is dropped |
| `TlsReceived` (bool?) | Topmost `Received`: `ESMTPS` / `TLS` / cipher mention | `null` when no usable Received → row hidden |

Mirror the fields in `mailTypes.ts` (`MailMessageDetail`).

## Frontend

- **Chevron** at the end of `.reader-from`, after the date: house idiom (`aria-expanded`,
  `is-open` class, 90° CSS rotation) as in `FolderTree`. 20×20 hit target, `.details-toggle`.
- **State**: `detailsOpen` via `useState` in `MessageReader`, reset in the existing
  per-message reset effect. No persistence, no preference.
- **Open**: compact To/Cc lines are replaced by a new `ReaderDetails` component — a
  `<dl>` grid (`max-content 1fr`, right-aligned muted labels): **From** (name +
  `<address>`), **To**, **Cc**, **Date** (long format), **Mailing list**, **Mailed by**,
  **Signed by**, **Unsubscribe** (link), **Security**. A row renders only when its datum
  exists. Subject stays in the `h1` — no redundant row. Spam gauge keeps its place below.
- **Unsubscribe**: `target="_blank" rel="noopener"` for http(s); plain `mailto:` link otherwise.
- **Security**: lock icon + "Standard encryption (TLS)" when `tlsReceived` is true,
  warning + "No encryption" when false, row hidden when null. No "learn more" link.
- **Styles** in `mail.css`, role tokens only, no literal colours. UI text in English.

## Tests

- Backend (xUnit): `MailHeaderDetailsReader` — each field, fallback chains, topmost-header
  rule, unsafe URL schemes rejected, absent headers → nulls.
- Frontend (Vitest): toggle opens/closes and swaps compact lines for the grid,
  `aria-expanded`, conditional rows, state resets on message change.
