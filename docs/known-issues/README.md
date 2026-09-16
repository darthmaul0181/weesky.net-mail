# Known issues

Defects that were **found, measured and deliberately left open**, plus paths no test covers. This
is a live backlog, not an archive: read it before reporting a bug as new, and before touching the
code an entry names.

Each entry says first what would be *seen*, then where it lives. Most of them are worth picking up
when the neighbouring code moves, not on their own.

| Area | Entries | Language |
|---|---|---|
| [Contacts 4a — the vCard model](contacts-4a-residuals.md) | 12 | FR |
| [Contacts 4e — groups](contacts-4e-groups.md) | a client scenario never run | EN |
| [Calendar 5a — foundations](calendar-5a-residuals.md) | 19 | FR |
| [Calendar 5b — the screens](calendar-5b-residuals.md) | 11 | FR |
| [Calendar 5c — the CalDAV server](calendar-5c-residuals.md) | 22 | FR |
| [Calendar 5d — client conformance](calendar-5d-residuals.md) | 21 | FR |
| [Calendar 5e1 — receiving invitations](calendar-5e1-residuals.md) | 22 | FR |
| [Calendar 5e2 — inviting guests](calendar-5e2-residuals.md) | 34 | FR |
| [Calendar 5e3 — replies at delivery](calendar-5e3-residuals.md) | 17 | FR |
| [CSS backgrounds and dark mode](webmail-dark-mode-and-css-backgrounds-known-issues.md) | measured, not deduced | EN |

Entries were written at the close of the slice that produced them, so they describe the code as it
stood then. A closed one is not always struck through — check the code before trusting an entry
against today's behaviour.

The design each slice was working to is in [`../history/specs/`](../history/specs), indexed by
[`../history/README.md`](../history/README.md).
