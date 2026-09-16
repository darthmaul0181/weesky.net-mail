# The schema, and why it is shaped this way

[`install/install.sql`](../install/install.sql) is the schema. This page is the reasoning behind
it — the choices that look arbitrary until you know what they are avoiding. Read it before
"correcting" a column, an index or a collation.

Rescued from the per-slice DDL notes, which were removed once `install.sql` became the single
definition of the schema.

---

## Conventions that hold across the database

**One database, separate from `dovecot`.** `dovecot` belongs to Dovecot and can be rebuilt by the
mail server's provisioning, which would take the webmail's data with it; the two also have
different backup and retention policies. **No foreign key crosses between them** — such a
constraint would recreate exactly the coupling the separation exists to avoid.

**Collation is mixed, on purpose.** Tables are `utf8mb4_bin`; only columns holding *human text*
carry `utf8mb4_unicode_ci`.

- Binary where the value is an identifier: IMAP folder paths are case-sensitive and must compare
  byte for byte — `Archive` and `archive` are two folders. `uid` is opaque. `address` is stored
  canonical, and a case-insensitive collation would merge two values the code treats as distinct.
- Case-insensitive for names and descriptions: it is human text, and a binary `LIKE` would be
  useless if a server-side search ever appeared. Today sorting and filtering happen client-side,
  so this collation does nothing yet — it avoids being wrong later.
- `utf8mb4_unicode_ci`, not `utf8mb4_0900_ai_ci`: the database is MariaDB.

**A foreign key requires both columns to share a collation.** Every `CHAR(36)` key column is
therefore `utf8mb4_bin`, matching `users.id`. Getting this wrong does not degrade anything — the
`CREATE TABLE` simply fails.

**`DATETIME` when the code writes the value, `TIMESTAMP` when the schema should.** A `TIMESTAMP`
travels through the session's time zone, and every date the code sets is already UTC. There is
also a MariaDB trap: the **first** `TIMESTAMP` column of a table silently receives
`DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP`, so a tombstone whose `sync_sequence` was
bumped would have its deletion date rewritten. `contacts.updated_at` and
`calendar_events.updated_at` *are* `TIMESTAMP ... ON UPDATE CURRENT_TIMESTAMP`, deliberately:
they are witnesses the schema must keep current on **every** write. Neither is the basis of an
ETag — that is `card_hash` and `ics_hash`.

**Reserved words are avoided, not quoted.** `sort_order` rather than `order`, `seq` rather than
`sequence` (a MariaDB keyword since 10.3). A column that only exists between back-quotes is a
production error waiting to happen in a project where the SQL is written by hand.

**No row means the default applies.** `app_settings` and `user_preferences` are empty on a fresh
install; the defaults live in the code (`Models/AppSettings.cs`, `Models/UserPreferences.cs`), not
in the schema.

---

## Settings and preferences

**`app_settings`** holds the instance's settings, not an account's — hence no `user_id` and no
foreign key. At most a handful of rows: the install-as-an-app switch and the two names the web
manifest needs.

**`user_preferences` is key/value, not a column per option.** A column per option would mean an
`ALTER TABLE` **run by hand on the server** for every new setting, and there will be more. Here,
adding a preference is a code change alone. The price, accepted: the database can validate neither
the key nor the value. The registry does it — an unknown key is refused with a 400 before it
reaches the table, and a row whose value is no longer accepted falls back to the default rather
than being served.

---

## Users, identities and trusted senders

**`sending_identities` is replaced whole, never merged.** `PUT /api/Identities` replaces the
account's entire set in one transaction: no row-by-row PATCH, no intermediate state for the client
to reconcile.

**`trusted_senders`** records the senders whose remote images the user accepted once and for all,
one click at a time from the reader. `last_used` is refreshed at most once a day. What actually
bounds the table is not the daily sweep over `TrustedSenders:RetentionDays` (365) but the ceiling
of 1 000 rows per account, enforced by `TrustedSenderStore`.

---

## Connected accounts

`external_domains` is the registry of external domains an administrator allows;
`connected_accounts` links a user to each of their attached mailboxes, the secret encrypted with
AES-256-GCM under a per-user derived key — a password on a `Password` row, an OAuth refresh token
on an `OAuth2` row. `users.kdf_salt` carries the salt of that derivation.

`folder_role_overrides` and `sending_identities` re-scope per account through an `account_id`
column with a **sentinel value**: `NOT NULL DEFAULT ''` means "the main account", anything else is
a `connected_accounts` GUID.

**Three things the schema cannot enforce, and the application must:**

- **The multi-NULL hole in `uq_connected_accounts_target`.** Two identical local accounts
  (`domain_id` NULL) for the same user and the same `email` do not collide in the unique index —
  MariaDB never collides two NULLs. The code must refuse the duplicate before the INSERT.
- **Cascading the `account_id` rows.** Neither `folder_role_overrides` nor `sending_identities`
  carries a foreign key on `account_id` — the `''` sentinel forbids one. Deleting a connected
  account must therefore delete its rows in both tables explicitly.
- **The sentinel's consistency.** Nothing guarantees `account_id` is either `''` or an existing
  GUID, except the code that writes it.

---

## Contacts and CardDAV

**`dav_credentials` hashes the DAV secret with a plain SHA-256, not a KDF.** This is the reverse of
the usual rule, and the reason is written here so that nobody "fixes" it later. A slow KDF exists
to make a dictionary attack expensive against a secret a *human* chose. Here the entropy is ours:
20 base32 characters, about 100 bits, beyond exhaustive search at any hashing speed. And a DAV
client re-authenticates on **every** request — a PBKDF2 at 100 000 iterations would be a
denial of service we inflict on ourselves, triggerable at will by unauthenticated requests. The
per-row salt stays: it stops the same generated string being recognisable twice in the table, and
costs nothing, since a row is found by its key and never by its digest.

**Two distinct states, which must not be confused:**

| State | Meaning | What the edge answers |
|---|---|---|
| No row | Never enabled | `401` |
| `carddav_enabled = 0` | Off but configured — the secret survives, so switching back on reconfigures no device | `403`, but only after a **successful** digest comparison, otherwise the answer would be an account-enumeration oracle |

The `DEFAULT 1` describes the state a row is born in — it only exists once the user turned the
switch on — not a policy applied to someone who asked for nothing.

**`contacts.uid` is unique per user**, because an address book is one collection per user.

**`contacts.dav_name` is nullable**, unlike its calendar counterpart: the column arrived after
contacts already existed, and since MySQL's uniqueness ignores `NULL`, it could stay empty until
the backfill ran.

---

## Calendar and CalDAV

**`calendar_events.user_id` is redundant with `calendars.user_id`, and that is the point.** The
calendar screen always asks the same question — every event of this user between the 1st and the
30th, across all calendars. Without the column, every month view needs a join; with it,
`ix_calendar_events_window` answers alone.

**`calendar_events.uid` is unique per calendar, not per user.** RFC 4791 § 4.1: two calendars may
legitimately hold the same event — an invitation accepted and filed twice — and per-user
uniqueness would refuse the second copy. This is the difference from `contacts`.

**`calendar_events.dav_name` is NOT NULL**, unlike the contacts one: no row pre-exists here. Every
event is born with its resource name, and leaving the column nullable would permit exactly one
thing — forgetting it.

**`last_occurrence` uses a cut-off date, not NULL.** A rule of "every Monday, forever" has no last
occurrence. `NULL` would fall outside every `BETWEEN` and so vanish from the month view; the value
written is `2100-01-01`. The event stays in range, the index stays a plain interval scan, and
nobody needs to understand recurrence rules to write the query.

**`calendar_revisions` carries no foreign key to `calendars` or `calendar_events`.** A revision is
written at the very moment the thing it archives is being deleted: `ON DELETE CASCADE` would
delete the archive right after the deletion that created it, and `RESTRICT` would forbid the
deletion outright. Both columns stay populated while the target exists and become orphan
identifiers afterwards — deliberately. Only the foreign key to `users` remains: a deleted account
takes its history with it.

---

## The sync counters, and the one property with no test

`contact_sync_state` is per user; `calendar_sync_state` is per **calendar**, because CalDAV
synchronises each collection separately — each calendar has its own token, so its own counter.

`seq` advances through an `INSERT ... ON DUPLICATE KEY UPDATE seq = seq + 1`. Neither the EF Core
InMemory provider nor SQLite executes that identically, **so no automated test covers it.** It is
the only correctness property of the DAV work that has to be verified by hand — once against a new
database, and again after any restore or schema rework.

Two `mysql` sessions side by side, against a non-production database:

```sql
-- Session A                              -- Session B
START TRANSACTION;
INSERT INTO contact_sync_state
  (user_id, epoch, seq, pruned_below)
VALUES ('<a real user>', UUID(), 1, 0)
ON DUPLICATE KEY UPDATE seq = seq + 1;
                                          START TRANSACTION;
                                          INSERT INTO contact_sync_state
                                            (user_id, epoch, seq, pruned_below)
                                          VALUES ('<the same>', UUID(), 1, 0)
                                          ON DUPLICATE KEY UPDATE seq = seq + 1;
                                          -- ^ MUST BLOCK here, not return
SELECT seq FROM contact_sync_state
 WHERE user_id = '<the same>';            -- (still blocked)
COMMIT;
                                          -- ^ unblocks now
                                          SELECT seq FROM contact_sync_state
                                           WHERE user_id = '<the same>';
                                          COMMIT;
```

| Observation | Expected |
|---|---|
| Session B at its `INSERT` | **blocks**; it does not return |
| B unblocks | at A's `COMMIT`, not before |
| The `seq` read by A, then by B | two **distinct**, consecutive values |
| After both `COMMIT`s | `seq` has advanced by exactly 2 |
| The `epoch` | **unchanged** across both, despite the `UUID()` in each `VALUES` |
| `SELECT` on `user_id` | finds **exactly one row** — a `Guid` parameter bound in a format the `CHAR(36)` column does not recognise would create a second state row instead of updating the first |

**If B does not block, the increment is not under lock and nothing about synchronisation is
safe.** Stop there; do not open the DAV routes.

The same check applies to `calendar_sync_state`, with `calendar_id` in place of `user_id`.

---

## Delivery replies

**`delivery_reply_key` is a single row (`id = 1`), and is deliberately not in `app_settings`** —
that table is read without authentication, so the setting itself would tell all of the internet
whether the door is open.

- **`key_hash`** — SHA-256 of the key, never the key. A database leak does not open the door.
- **`last_call_at`** — the last call whose key was valid; reset to `NULL` on every generation.
- **`enabled`** — the switch. Off, the door answers 404 to everyone.
- **`updated_at` as `DATETIME(6)`** — the concurrency token for the Administration screen's
  writes. `last_call_at` is written without moving it.

**`scheduling_service_account`** is the technical SMTP account that sends calendar invitations when
an appointment with guests is changed from a phone or Thunderbird. Also a single row, also kept
out of `app_settings` for the same reason.

- **`password_cipher`** holds bytes protected by Data Protection under a purpose of its own,
  distinct from the OAuth client secret's — never text. It is written through the Administration
  screen, never into the column. If the key ring is lost the password becomes unreadable: the
  screen says so, the account is treated as absent for sending, and re-entering the password is
  the whole repair.
- **`last_test_at` / `last_test_ok`** — the verdict of the last "Test" on the saved account, reset
  to `NULL` on every save.
- **`updated_at` as `DATETIME(6)`** — a test records its verdict only if the row was not re-saved
  while it ran, and that value is what the comparison uses.
