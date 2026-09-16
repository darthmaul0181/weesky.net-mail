<p align="center">
  <img src="assets/brand/banner.png" alt="Scotty webmail — mail, contacts and calendar for a mail server you already run" width="900">
</p>

<p align="center">
  <a href="https://github.com/darthmaul0181/weesky.net-mail/actions/workflows/deploy.yml"><img src="https://github.com/darthmaul0181/weesky.net-mail/actions/workflows/deploy.yml/badge.svg" alt="CI/CD"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <img src="https://img.shields.io/badge/React-18-61DAFB" alt="React 18">
  <img src="https://img.shields.io/badge/MariaDB-schema%20by%20hand-003545" alt="MariaDB">
</p>

---

## What this is

A **webmail for a mail server you already run**. Point it at a Dovecot/Postfix host and it gives
that host a browser client — mail, an address book and a calendar — without moving a single
mailbox anywhere.

Two things set it apart from a mail client you install on your machine:

- **It keeps no copy of your mail.** Every folder, message and flag is read from IMAP at the
  moment you ask for it. Scotty's own database holds only what IMAP has nowhere to put —
  preferences, sending identities, contacts, calendars.
- **It never learns your password.** Signing in *is* an IMAP login: the mail server decides. The
  password is then kept in a cookie the service encrypts, and thrown away when you sign out. There
  is no password column to leak, because there is no password column.

And because your phone does not speak IMAP for contacts and calendars, Scotty **is** the CardDAV
and CalDAV server for them. The same address book you edit in the browser is the one that syncs to
iOS, DAVx⁵ and Thunderbird.

## What it does

**Mail** — folders with their system roles resolved (Sent, Drafts, Trash, Junk, Archive), a message
list that can group conversations, flags and the usual actions, multi-select, IMAP search. Compose
in HTML or plain text, with drag-and-drop attachments, inline images and a priority flag. Drafts,
reply, reply-all, forward, and a curated list of sending identities. Remote images are withheld
until you allow them, once or for that sender for good. New mail can ring and raise a desktop
notification.

**Several mailboxes at once** — attach another account to the same session, on your own server or
an external domain an administrator allowed, with a password or through OAuth.

**Contacts** — a complete vCard model rather than a handful of columns, so nothing an imported card
carried is lost on the way in or out. Photos, groups, CSV import and export, bulk actions, and
addresses captured from the people you write to.

**Calendar** — day, week, month and a list view, recurring events, several calendars with their own colours.
Invitations go out to guests and come back in; a guest's reply is applied **as the mail is
delivered**, so the calendar is right before you open anything.

**Mail rules** — the Sieve script your server already runs, edited from the browser over ManageSieve.

**The interface** — English and French, light and dark, eight colour palettes, installable as an
application, and usable on a phone.

## Screens

<table>
<tr>
<td width="50%"><img src="assets/screenshots/mail.png" alt="The mail view: folder column, message list and reading pane"><br><sub><b>Mail</b> — folders, list and reader, side by side</sub></td>
<td width="50%"><img src="assets/screenshots/compose.png" alt="The composer, with recipient tokens and a formatting toolbar"><br><sub><b>Composing</b> — recipient tokens, a formatting toolbar, attachments by drag and drop</sub></td>
</tr>
<tr>
<td width="50%"><img src="assets/screenshots/calendar.png" alt="The calendar in month view, with the new-calendar dialog open"><br><sub><b>Calendar</b> — several calendars, each with its own colour, served over CalDAV</sub></td>
<td width="50%"><img src="assets/screenshots/contacts.png" alt="The contact editor, with addresses and phone numbers"><br><sub><b>Contacts</b> — a full vCard behind the form, served over CardDAV</sub></td>
</tr>
</table>

<sub>Names, messages and folders in these screenshots are made up; the interface is the real one.</sub>

## Installing it

The short version, for a server that already runs Dovecot and Postfix:

```bash
# 1. The database — edit the two placeholders at the top of the file first
mysql -u root -p < install/install.sql

# 2. The service
cp install/scotty.microservice.service /etc/systemd/system/
install -m 0600 -D install/scotty.microservice.env /etc/scotty/scotty.microservice.env
#    …edit both, then
systemctl daemon-reload && systemctl enable --now scotty.microservice
```

Two more steps — the reverse proxy's address, and one Apache line so that reloading a page does not
404 — are in the guide. **Read [`install/README.md`](install/README.md) before running anything**:
it is five steps in order, and three of them are enforced by the service refusing to start rather
than running half-configured.

Worth knowing up front: **this project has no EF migrations.** `install/install.sql` creates the 26
tables once, and the service is never granted the right to alter them.

## Documentation

| | |
|---|---|
| [`install/README.md`](install/README.md) | **Start here to deploy.** Five steps, with an example systemd unit and environment file |
| [`docs/README.md`](docs/README.md) | Where everything else lives |
| [`docs/schema-notes.md`](docs/schema-notes.md) | Why the schema is shaped the way it is — read before changing a column or an index |
| [`docs/known-issues/`](docs/known-issues) | Defects found, measured and deliberately left open |
| [`docs/operations/`](docs/operations) | What to run after restoring a backup, and one-off migrations |
| [`docs/history/`](docs/history) | The design spec of every slice, dated and never revised |
| [`src/scotty.microservice/DESIGN.md`](src/scotty.microservice/DESIGN.md) | Backend architecture |
| [`DESIGN-rules.md`](DESIGN-rules.md) | The Sieve rules feature |
| [`assets/DATABASE.md`](assets/DATABASE.md) | The `dovecot` database the mail stack shares |

## Repository layout

```
weesky.net-mail/
├── install/                       # Everything a new deployment needs
│   ├── install.sql                #   the 26 tables, the database, the MySQL account
│   ├── README.md                  #   the five steps
│   └── optional/                  #   OAuth mailboxes, calendar replies at delivery
├── src/
│   ├── scotty.microservice/       # The core REST API — ASP.NET Core, .NET 10
│   ├── scotty.microservice.host/  #   composes the API with one platform provider
│   ├── scotty.providers.weesky/   #   the weesky provider: account and alias administration
│   └── frontend/                  # React + Vite SPA
├── docs/                          # Schema notes, known issues, operations, the record
├── assets/                        # Server-side notes, the SQL run on the mail host, the artwork
└── tools/                         # Brand icons, palette preview, the CalDAVTester harness
```

### The two platforms

The backend is split so the webmail does not depend on how accounts are managed. `Platform=weesky`
adds the administration screens for accounts, aliases and domains backed by the Dovecot database;
`Platform=generic` runs against any IMAP server, with no directory to administer. Mail, contacts and
calendar are identical either way.

### Stack

ASP.NET Core (.NET 10), EF Core with the Pomelo MySQL provider, MailKit/MimeKit, Ical.Net, Serilog,
Swashbuckle. React 18 with Vite, TypeScript, TanStack Query and react-i18next. MariaDB for both the
`dovecot` database and Scotty's own.

## Licence

The code is under the **GNU Affero General Public License v3.0** — see [`LICENSE`](LICENSE).

AGPL rather than plain GPL because of what a webmail is likely to become: a service someone runs
for other people. The GPL asks nothing of an operator who never hands out a copy, and hosting is
not handing out a copy. Section 13 of the AGPL closes exactly that gap — offering a modified
version to users over a network counts as distribution, so whoever hosts a fork owes those users
its source.

The name **Scotty** and the artwork in [`assets/brand/`](assets/brand) are not covered by it: they
are the project's identity rather than its code, so a fork takes a name and a face of its own.

Copyright (C) 2026 [darthmaul0181](https://github.com/darthmaul0181)
