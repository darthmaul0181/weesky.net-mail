# The record

Why the webmail is built the way it is, slice by slice. Everything here is **dated and never
revised**: a document describes the state of the world on its date, not today's.

[`specs/`](specs) holds the design agreed before a slice was built. It is the one thing to read
before undoing a decision without knowing its price.

What is *not* here, because it is not history:

- **Known defects** — [`../known-issues/`](../known-issues), a live backlog.
- **Why the schema is shaped as it is** — [`../schema-notes.md`](../schema-notes.md).
- **How to install or operate** — [`../../install/`](../../install) and
  [`../operations/`](../operations).

Most of the older material is in French, the later in English; the language column says which.
Nothing is translated after the fact — a record rewritten is a record you can no longer trust.

---

## 1 — The shell

| Slice | Spec | Lang |
|---|---|---|
| The application shell | [webmail-shell](specs/2026-07-18-webmail-shell-design.md) | FR |

## 2 — Mail

| Slice | Spec | Lang |
|---|---|---|
| 2a — folders and reading | [mail-2a](specs/2026-07-18-webmail-mail-2a-design.md) | FR |
| 2a.5 — system folders | [mail-2a5-system-folders](specs/2026-07-19-webmail-mail-2a5-system-folders-design.md) | FR |
| 2b1 — flags | [flags-2b1](specs/2026-07-22-webmail-flags-2b1-design.md) | FR |
| 2b2 — message actions | [actions-2b2](specs/2026-07-22-webmail-actions-2b2-design.md) | FR |
| 2b3 — multi-selection | [multiselect-2b3](specs/2026-07-23-webmail-multiselect-2b3-design.md) · [taller action band](specs/2026-07-23-webmail-bulk-actions-taller-band-design.md) | FR |
| 2b4 — IMAP search | [search-2b4](specs/2026-07-23-webmail-search-2b4-design.md) | FR |
| 2c1 — composing | [compose-2c1](specs/2026-07-23-webmail-compose-2c1-design.md) | FR |
| 2c2a — sending identities | [identities-2c2a](specs/2026-07-24-webmail-identities-2c2a-design.md) | FR |
| 2c2b — reply and forward | [reply-forward-2c2b](specs/2026-07-25-webmail-reply-forward-2c2b-design.md) | FR |
| 2c3a — drafts | [drafts-2c3a](specs/2026-07-25-webmail-drafts-2c3a-design.md) | FR |
| 2d — multiple accounts | [multi-accounts-2d](specs/2026-07-29-webmail-multi-accounts-2d-design.md) | FR |
| — the `users` table and GUID keys | [users-table](specs/2026-07-24-webmail-users-table-design.md) | FR |

## 3 — Contacts, first pass

| Slice | Spec | Lang |
|---|---|---|
| 3a + 3b — the module | [contacts-3a3b](specs/2026-07-27-webmail-contacts-3a3b-design.md) | FR |
| 3c — capturing recipients | [contacts-3c](specs/2026-07-27-webmail-contacts-3c-design.md) | FR |
| 3d — CSV import and export | [contacts-3d](specs/2026-07-27-webmail-contacts-3d-design.md) | FR |
| — bulk actions | [contacts-bulk-actions](specs/2026-08-12-contacts-bulk-actions-design.md) | FR |

## 4 — Contacts: the vCard model and CardDAV

| Slice | Spec | Known issues | Lang |
|---|---|---|---|
| 4a — full model and vCard engine | [contacts-4a](specs/2026-08-14-webmail-contacts-4a-vcard-model-design.md) | [12 open](../known-issues/contacts-4a-residuals.md) | FR |
| 4b — the extended editor | [contacts-4b](specs/2026-08-22-webmail-contacts-4b-editor-design.md) | | FR |
| 4c — the CardDAV server | [contacts-4c](specs/2026-08-23-webmail-contacts-4c-carddav-design.md) · [what 4c-ii-c decided](specs/2026-08-30-carddav-4c-ii-c-decisions.md) | | FR |
| 4d — client conformance | [contacts-4d](specs/2026-08-31-webmail-contacts-4d-conformance-design.md) | | FR |
| 4e — groups | [contacts-4e](specs/2026-08-31-webmail-contacts-4e-groups-design.md) | [a scenario never run](../known-issues/contacts-4e-groups.md) | FR |
| 4f — writing the photo | [contacts-4f](specs/2026-09-03-webmail-contacts-4f-photo-design.md) | | FR |

## 5 — Calendar

| Slice | Spec | Known issues | Lang |
|---|---|---|---|
| 5 — project framing | [calendar-5 overview](specs/2026-09-04-webmail-calendar-5-overview-design.md) | | FR |
| 5a — foundations | *(in the overview)* | [19 open](../known-issues/calendar-5a-residuals.md) | FR |
| 5b — the screens | [calendar-5b](specs/2026-09-05-webmail-calendar-5b-screens-design.md) | [11 open](../known-issues/calendar-5b-residuals.md) | FR |
| 5c — the CalDAV server | [calendar-5c](specs/2026-09-06-webmail-calendar-5c-caldav-design.md) | [22 open](../known-issues/calendar-5c-residuals.md) | FR |
| 5d — client conformance | [calendar-5d](specs/2026-09-07-webmail-calendar-5d-conformance-design.md) | [21 open](../known-issues/calendar-5d-residuals.md) | FR |
| 5e — invitations (5e1, 5e2) | [calendar-5e](specs/2026-09-12-webmail-calendar-5e-invitations-design.md) | [5e1](../known-issues/calendar-5e1-residuals.md) · [5e2](../known-issues/calendar-5e2-residuals.md) | FR |
| 5e3 — replies applied at delivery | [calendar-5e3](specs/2026-09-14-webmail-calendar-5e3-delivery-replies-design.md) | [17 open](../known-issues/calendar-5e3-residuals.md) | FR |

## Features and refinements

Standalone slices, in the order they were designed.

| Date | Spec | Lang |
|---|---|---|
| 2026-07-21 | [Reader header](specs/2026-07-21-reader-header-design.md) | FR |
| 2026-07-21 | [Always show remote images](specs/2026-07-21-webmail-always-show-images-design.md) | EN |
| 2026-07-21 | ["All" messages per page, loaded in blocks](specs/2026-07-21-webmail-message-stream-design.md) | EN |
| 2026-07-21 | [Sound and desktop notification on new mail](specs/2026-07-21-webmail-new-mail-notifications-design.md) | EN |
| 2026-07-21 | [Four more palettes, and a preview for each](specs/2026-07-21-webmail-palettes-design.md) | EN |
| 2026-07-21 | [Periodic refresh of the mail view](specs/2026-07-21-webmail-periodic-refresh-design.md) | EN |
| 2026-07-22 | [Reader action zone](specs/2026-07-22-reader-actions-design.md) | FR |
| 2026-07-22 | [Expanded reader details](specs/2026-07-22-reader-details-design.md) | EN |
| 2026-07-22 | [Reading pane position](specs/2026-07-22-reading-pane-position-design.md) | EN |
| 2026-07-22 | [Spam score in the reader header](specs/2026-07-22-spam-score-design.md) | FR |
| 2026-07-23 | [Drag and drop to folders — framing note](specs/2026-07-23-drag-drop-messages-earmark.md) | FR |
| 2026-07-25 | [Attachments UX](specs/2026-07-25-webmail-attachments-ux-design.md) | FR |
| 2026-07-26 | [CSS background images](specs/2026-07-26-webmail-css-background-images-design.md) · [known issues](../known-issues/webmail-dark-mode-and-css-backgrounds-known-issues.md) | EN |
| 2026-07-26 | [Trusted senders](specs/2026-07-26-webmail-trusted-senders-design.md) | EN |
| 2026-07-28 | [Installable webmail (PWA)](specs/2026-07-28-webmail-pwa-design.md) | FR |
| 2026-07-30 | [View source](specs/2026-07-30-webmail-view-source-design.md) | EN |
| 2026-07-31 | [A default composing format](specs/2026-07-31-webmail-compose-format-preference-design.md) | EN |
| 2026-07-31 | [Content-sized modals](specs/2026-07-31-webmail-content-sized-modals-design.md) | EN |
| 2026-07-31 | [Inline images](specs/2026-07-31-webmail-inline-images-design.md) | EN |
| 2026-07-31 | [Message priority](specs/2026-07-31-webmail-message-priority-design.md) | EN |
| 2026-07-31 | [Plain-text composing](specs/2026-07-31-webmail-plain-text-compose-design.md) | EN |
| 2026-08-04 | [OAuth for connected accounts](specs/2026-08-04-oauth-connected-accounts-design.md) · [setup](../../install/optional/oauth-providers.md) | EN |
| 2026-08-06 | [Localisation](specs/2026-08-06-webmail-localisation-design.md) | EN |
| 2026-08-09 | [Mobile and tablet](specs/2026-08-09-webmail-mobile-responsive-design.md) | EN |
| 2026-08-12 | [Decoupling the webmail from the weesky plugin](specs/2026-08-12-webmail-decoupling-design.md) | FR |
| 2026-08-13 | [Grouped conversations](specs/2026-08-13-grouped-conversations-design.md) | FR |
| 2026-08-14 | [Message row exit animation](specs/2026-08-14-message-row-exit-animation-design.md) | FR |
| 2026-08-20 | [Reusing IMAP connections](specs/2026-08-20-webmail-imap-connection-pool-design.md) | FR |

---

## What was removed, and how to get it back

**The implementation plans.** Each slice had one — a step-by-step brief for whoever wrote the
code. Their output is the code itself, which is in git, so they went rather than stay as four
megabytes of scaffolding. Where a plan had arbitrated something the spec left open, that section
moved into the spec first; see the appendix of
[calendar-5e](specs/2026-09-12-webmail-calendar-5e-invitations-design.md) for the one case.

**The per-slice DDL notes.** Their SQL became a second copy of the schema once
[`../../install/install.sql`](../../install/install.sql) existed, and two copies drift. The
reasoning they carried — why a collation, why a redundant column, why no foreign key there — was
lifted into [`../schema-notes.md`](../schema-notes.md).

**Campaign reports that had expired** — two conformance runs, three CardDAV development
verifications, one manual test pass and four code reviews from July and August 2026. Each recorded
the result of a run on a given day.

All of it is recoverable: `git log --diff-filter=D --name-only` finds the file,
`git show <commit>^:<path>` brings it back.
