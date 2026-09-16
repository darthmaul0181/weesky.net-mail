# Contacts backfill — giving pre-4a contacts a vCard

One-off operation, run **once** per deployment. It gives a vCard, a `card_hash` and a projection
to every contact stored **before** the 4a slice. Without it those contacts keep `card_hash = ''`:
no screen breaks, but they have no card to synchronise and the model is incomplete.

It is **idempotent**: running it again breaks nothing and redoes nothing.

> **A fresh installation never needs this.** Only a deployment that held contacts before the 4a
> slice has anything to convert.

---

## 1. When

In this order, skipping nothing:

1. The 4a tables and columns exist — [`../../install/install.sql`](../../install/install.sql)
   creates them.
2. The backend that knows them is **deployed and started**.
3. Only then, the backfill.

A backend that projects before the tables exist falls over on its first write. The other way
round, running the backfill against the old backend does nothing at all — the route does not exist
yet.

Run it on every environment holding contacts older than 4a.

---

## 2. How

The route is `POST /api/Contacts/Backfill`, **administrators only** (policy `Admin`, the same as
`PUT /api/AppSettings`). A non-admin account gets a `403`.

It works **in batches** and sweeps **every user**: this is an operator's gesture over the whole
table, not over one address book. Each call answers:

```json
{ "processed": 200, "remaining": 1340 }
```

- `processed` — contacts converted by **this** call;
- `remaining` — contacts left to convert **after** this call.

Batch size is `?batchSize=N`, clamped into `1..1000`. **Pass `batchSize=500`**: the default of 200
is cautious, but the selection sweeps the table on every call, so fewer calls cost less.

### Open a session

```bash
BASE=https://mail.example.net          # the target environment
JAR=$(mktemp)

curl -s -c "$JAR" -X POST "$BASE/api/Login" \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@example.net","password":"…"}'
```

### The loop

Keep going while `remaining > 0`:

```bash
while :; do
  OUT=$(curl -s -b "$JAR" -X POST "$BASE/api/Contacts/Backfill?batchSize=500")
  echo "$OUT"
  PROCESSED=$(echo "$OUT" | grep -o '"processed":[0-9]*' | cut -d: -f2)
  REMAINING=$(echo "$OUT" | grep -o '"remaining":[0-9]*'  | cut -d: -f2)
  [ "$REMAINING" = "0" ] && { echo "Done."; break; }
  [ "$PROCESSED" = "0" ] && { echo "Stuck: see section 4."; break; }
done
```

The second guard matters. `remaining > 0` with `processed = 0` means the head of the queue is
being refused over and over; without the guard the loop never ends.

One more call after the end answers `{ "processed": 0, "remaining": 0 }` — that is the check that
everything went through.

---

## 3. Logging

Every call writes one line to the application log (Serilog, `Information`):

```
Contacts backfill: 500 processed, 1340 remaining
```

A `processed` below the requested `batchSize` with a non-zero `remaining` signals refused contacts
(section 4). A silent operation nobody can tell has finished is an operation people re-run just in
case; this line exists so that they do not have to.

---

## 4. A refused contact

The only possible refusal is a card over the **1 MB** ceiling — typically a huge `PHOTO` on a card
imported before the slice. The contact is left **untouched**: `vcard_raw` byte for byte,
`card_hash` still empty. So it comes back in the next batch, indefinitely. That is what the
`processed == 0` guard above stops.

### The decision first

**A refused contact can be left as it is.** That is the exact state every contact was in before
this backfill: no screen breaks, the contact displays, reads, edits and exports as before. It
simply will not have a card to synchronise. Two or three contacts in that state do **not** justify
staying up at 3 a.m. — note their `id`, stop the loop, deal with it on a working day.

Finding them:

```sql
SELECT id, user_id, LENGTH(vcard_raw) AS bytes
FROM contacts
WHERE card_hash = ''
ORDER BY bytes DESC;
```

`LENGTH(vcard_raw)` ranks; it does not decide. The ceiling is measured on the **recomposed** card,
after the `UID` is inserted, a `REV` added and long lines folded — a few tens of kilobytes more
than the column shows. A contact at 1 020 000 bytes in the database can therefore be refused. What
the query does give you for certain is the order: the culprits are at the top.

### If you really want to convert it

A 1 MB `vcard_raw` is not edited by hand, and certainly not in SQL: the value is a whole vCard in
one column, whose `PHOTO` line alone is 99 % of the weight. Two routes, in order of preference:

1. **Through the application, touching nothing in the database.** Export the contact
   (`GET /api/Contacts/Export` for that user's address book), then re-save it from the webmail's
   contact editor without its photo. The edit goes through the composer, which rewrites
   `vcard_raw` **and** sets the hash. The contact leaves the queue by the normal door and the
   backfill never sees it again.
2. **With a script**, if the contact belongs to a user you cannot ask: strip the `PHOTO` property
   from `vcard_raw` — it runs from `PHOTO;` to the first following line that does **not** begin
   with a space or a tab, since it is a folded value — leave `card_hash` empty, then run the loop
   again. Back the row up first.

**Never** "unblock" a contact by setting a bogus `card_hash`: that takes it out of the queue
without giving it a card, which is to say it loses the information that the work is still
outstanding.

---

## 5. Re-running after a fix to the engine

The work queue *is* the `card_hash` column: an empty hash means outstanding, a set hash is
**never** revisited. To put a scope back through, empty it and run the loop again.

Always **back the table up first** — a re-run rewrites `vcard_raw` and rebuilds the four child
tables.

```sql
-- The whole table
UPDATE contacts SET card_hash = '';

-- One user
UPDATE contacts SET card_hash = '' WHERE user_id = 'xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx';

-- One contact
UPDATE contacts SET card_hash = '' WHERE id = 'xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx';
```

Empty **only** what the fix concerns: on a contact edited since the backfill, a re-run goes back
through reconciliation and will do the right thing, but it also rewrites `updated_at`.

---

## 6. What it does, and what it does not

For each contact in the queue, in this order:

1. **No card** — a new card is composed from the columns.
2. **Existing card** — the card is **reconciled**, not re-projected: the current columns reset
   `N`, `FN`, `NICKNAME` and the `EMAIL` block, **and nothing else**. Every `TEL`, `ADR`, `ORG`,
   `BDAY`, `NOTE`, `PHOTO` and off-model property that only the card carried is kept as is. Within
   `N`, only the given and family names are reset: a middle name and an honorific the card carried
   in components 3 and 4 survive, and `FN` is recomputed **counting them** — `Jean Pierre Dupont`
   stays `Jean Pierre Dupont`, it does not become `Jean Dupont`.
3. A `UID` is added if the card lacked one (pre-4a cards have none).
4. The `card_hash` is computed, and the card is **projected** into the columns and child tables.

The order is the point. Contacts that were imported **and then edited** carry a card whose name
and address are stale: projecting that card first would rewrite the columns with the old name and
the old address — erasing the user's edit. That is why the operation reconciles before it
projects, and why reconciliation is **bounded** to the four fields that could have drifted.

What it does not do: it creates, merges and deletes **no** contact, and touches neither `uid` nor
`source` nor `is_favorite`. It does write `updated_at` on converted contacts — the card really did
change.
