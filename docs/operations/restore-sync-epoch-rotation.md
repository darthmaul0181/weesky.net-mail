# After restoring a backup — rotating the CardDAV and CalDAV sync epochs

**Run this after restoring any backup of the webmail database**, before letting clients back in.
Skip it and a restore produces no visible error at all while one or more phones drift silently and
permanently out of sync.

The gesture ships as two scripts —
[`../../assets/contacts-sync-epoch-rotate.sql`](../../assets/contacts-sync-epoch-rotate.sql) for
the address book and
[`../../assets/calendar-sync-epoch-rotate.sql`](../../assets/calendar-sync-epoch-rotate.sql) for
the calendar — precisely because an instruction you have to find inside a design document during a
restore is an instruction nobody will run. The two are identical bar a table and a key
(`contact_sync_state`/`user_id` against `calendar_sync_state`/`calendar_id`); what follows
describes the address book and holds for the calendar by that substitution alone.

## What and why

A restore rewinds `contact_sync_state.seq`. Refusing a token that sits *ahead* of the current
sequence only catches the clients that ran furthest ahead: a token left below the restored
sequence passes, and covers ranks whose content has since changed. The result is a silent,
permanent divergence on phones that go on syncing without reporting anything.

Running the script makes every token issued by the pre-restore database a stranger to the address
book, and the ctag changes with them. It is idempotent: running it twice is still right.

**Why the epoch and not `pruned_below = seq`.** Both invalidate the tokens, but the second does it
by moving a watermark whose meaning is "those tombstones no longer exist" — lying about one thing
to obtain the effect you want elsewhere. The epoch says one thing and says it in full: this
address book is no longer the one that issued your tokens.

## Apply

```bash
mariadb -u <user> -p scotty_webmail < assets/contacts-sync-epoch-rotate.sql
mariadb -u <user> -p scotty_webmail < assets/calendar-sync-epoch-rotate.sql
```

Repeat for any other environment the restore touched.

Each file also carries, in a comment, a single-row form (`WHERE user_id = …` or
`WHERE calendar_id = …`). That is **not** the restore gesture — it serves incidents that name one
`user_id` or one `calendar_id`, such as the startup check below. After a restore it is the
whole-database form above that must run, and the `<` runs it already.

## Clients do not all recover the same way

Know this before you tell users anything:

- **DAVx⁵ and iOS** read the `403 valid-sync-token` and restart from a full sync by themselves,
  address book and calendar alike.
- **Thunderbird** falls back to a full sync only on a `400`. Its code replays a token refused with
  `403` every cycle, indefinitely. After this rotation, a Thunderbird address book or calendar must
  be **re-paired by hand** — delete it and recreate it.

## What the startup check does not see

`SyncStateConsistencyCheck` compares `MAX(contacts.sync_sequence)` against
`contact_sync_state.seq`, and `MAX(calendar_events.sync_sequence)` against
`calendar_sync_state.seq` per calendar. It detects a restore that rewound one table of a pair
without the other. A *consistent* restore — both tables rewound together, from the same backup —
leaves it silent, the inequality still holding.

So do not use it to decide whether the rotation is needed. It is needed after **every** restore,
whether the check spoke or not.

## The trade against Radicale

This is the one place where this server asks for a human gesture that Radicale does not. Radicale
derives its ctag from the collection's content, so a restore changes it on its own —
self-repairing, at the cost of a recomputation on every state query. Ours is a counter, so `O(1)`
on the path a phone takes every fifteen minutes, but rewindable. That is the right trade for five
thousand contacts, and it is only the right trade if this line actually gets run.
