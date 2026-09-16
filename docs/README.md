# Documentation

| Where | What | Read it when |
|---|---|---|
| [`../install/`](../install) | How to install the webmail | Setting up a new deployment |
| [`schema-notes.md`](schema-notes.md) | Why the schema is shaped the way it is | Before changing a column, an index or a collation |
| [`known-issues/`](known-issues) | Defects found, measured and left open | Before reporting a bug, or touching the code one names |
| [`operations/`](operations) | Gestures against a database already running | After a restore, or for a one-off migration |
| [`history/`](history) | Why it was built this way — dated, never revised | Before undoing a decision without knowing its price |

Architecture documentation is **not** here. It lives next to the code it describes:

- [`../README.md`](../README.md) — the repository and its components
- [`../src/scotty.microservice/DESIGN.md`](../src/scotty.microservice/DESIGN.md) — the backend
- [`../DESIGN-rules.md`](../DESIGN-rules.md) — the Sieve rules feature
- the `CLAUDE.md` of each component; the frontend splits its own across `src/frontend/docs/`

---

## Installing

[`../install/README.md`](../install/README.md) is the whole path, in five steps. Two things worth
knowing before you start:

- **There are no EF migrations.** The schema is created once by
  [`../install/install.sql`](../install/install.sql) and the service is never granted the right to
  alter it.
- **Three of the five steps are enforced.** Without the database, the Data Protection key ring or
  the reverse proxy's address, the service refuses to start and says which one is missing. That is
  deliberate: a silently inert feature is worse than a failure to start.

## Operating

- [`operations/restore-sync-epoch-rotation.md`](operations/restore-sync-epoch-rotation.md) — **run
  after restoring any backup of the webmail database**, before letting clients back in. Skipping it
  produces no error while phones drift silently out of sync.
- [`operations/contacts-vcard-backfill.md`](operations/contacts-vcard-backfill.md) — one-off, for
  deployments that held contacts before the vCard model existed.

## Known issues

[`known-issues/README.md`](known-issues/README.md) indexes about 160 defects that were measured and
deliberately left open, slice by slice. It is a backlog in the present tense, not an archive.

## The record

[`history/README.md`](history/README.md) is the table of contents of the design specs, organised by
slice, with the language of each document.
