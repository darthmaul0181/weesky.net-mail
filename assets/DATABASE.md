# Database — `dovecot`

MariaDB database used by Dovecot for mail authentication and by the `snoopy.microservice` API for account/domain management.

## Ecosystem role

The `dovecot` database is the **single source of truth** for the entire weesky.net mail stack. Every component — from mail routing to authentication to administration — derives its decisions from this database. No component maintains its own copy of this data.

```
                        ┌──────────────────────────────────────┐
                        │           dovecot (MariaDB)          │
                        │   domains · users · aliases          │
                        │   domains_ownerships                 │
                        └────────────┬─────────────────────────┘
                                     │
               ┌─────────────────────┼──────────────────────┐
               │                     │                      │
               ▼                     ▼                      ▼
         ┌──────────┐          ┌──────────┐         ┌─────────────────┐
         │ Postfix  │          │ Dovecot  │         │    snoopy       │
         │ (MTA)    │          │ (IMAP /  │         │ microservice    │
         │          │          │  LDA)    │         │ (ASP.NET REST)  │
         └──────────┘          └──────────┘         └─────────────────┘
```

### Postfix

Postfix queries the database via MySQL maps at every stage of message processing. See [`POSTFIX.md`](POSTFIX.md) for the exact SQL.

| What Postfix asks | Table(s) queried | Purpose |
|---|---|---|
| Is this domain mine? | `domains` | Accept or reject the RCPT TO domain (`virtual_mailbox_domains`) |
| Does this mailbox exist? | `users`, `domains` | Confirm delivery routing is possible (`virtual_mailbox_maps`) |
| Where does this alias point? | `aliases`, `users`, `domains` | Rewrite the recipient before delivery (`virtual_alias_maps`) |
| Who is allowed to send as this address? | `users`, `aliases`, `domains` | Authorize SMTP submission (`smtpd_sender_login_maps`) |

> [!NOTE]
> Postfix never touches the filesystem to resolve a mailbox path — it delegates actual delivery to Dovecot via LMTP. The database is therefore the > only thing standing between an incoming message and its destination.


### Dovecot

Dovecot queries the database for every authentication attempt and every mailbox operation. See [`DOVECOT.md`](DOVECOT.md) for the exact SQL.

| What Dovecot asks | Table(s) queried | Purpose |
|---|---|---|
| What is the stored password? | `users`, `domains` | IMAP login authentication (`passdb`) |
| What are this user's mailbox details and quota? | `users`, `domains`, `aliases` | Resolve identity and enforce storage limits (`userdb`) |
| List all active mailboxes | `users`, `domains` | Batch operations via `doveadm` (e.g. quota recalculation) |

Dovecot's `passdb` is also called by Postfix during SMTP SUBMISSION: when a client sends a mail through port 587, Postfix delegates credential validation to Dovecot (SASL), which in turn reads the hashed password from the `users` table. The same password hash therefore gates both IMAP access and authenticated outbound sending.

The `userdb` alias branch is queried by the Postfix quota-status plugin **before** alias resolution — at that point Postfix only knows the alias address, so the `UNION ALL` branch resolves it to the real mailbox and its quota on the fly.

### snoopy microservice

The `snoopy.microservice` (ASP.NET Core REST API) is the **only component that writes** to the database. Postfix and Dovecot are read-only consumers; `snoopy` is the sole administration path.

Through its React web frontend, `snoopy` exposes:

- Domain management (create / delete primary and virtual domains, assign ownerships)
- Account management (create / update / delete mailboxes, change password, set quota, toggle active state)
- Alias management (create / delete aliases across owned domains)

Because the database is shared with live Postfix and Dovecot processes, every write performed by `snoopy` takes effect immediately — there is no configuration reload step.

---

## Tables

### `domains`

Stores every domain the system knows about — both primary and virtual (alias) domains.

| Column | Type | Notes |
|--------|------|-------|
| `id` | `char(3)` | Short uppercase identifier, e.g. `WSY`, `EXT`. Primary key. |
| `name` | `varchar(30)` | Fully qualified domain name, e.g. `weesky.be`. Indexed (`idx_name`) — queried by name on every Postfix/Dovecot lookup. |

A domain is **primary** when at least one user has their mailbox hosted on it (`users.domain = domains.id`). It is a **virtual (alias) domain** when no user has their primary mailbox on it — it exists purely to receive mail that gets forwarded to owners' primary mailboxes via the `aliases` table.

---

### `users`

One row per mailbox.

| Column | Type | Notes |
|--------|------|-------|
| `id` | `int` | Auto-increment primary key. |
| `username` | `varchar(128)` | Local part of the email address (before `@`). Unique with `domain`. |
| `domain` | `char(3)` | FK → `domains.id`. The domain hosting this mailbox. |
| `password` | `varchar(128)` | **Stored as plaintext by the application.** Two MariaDB triggers (`INSERT_PASSWORD`, `UPDATE_PASSWORD`) automatically encrypt the value to SHA-512 crypt (`$6$…`) before it is persisted. Never hash server-side. |
| `quota_mb` | `int` | Mailbox quota in MB. `0` = unlimited. |
| `fullname` | `varchar(100)` | Display name. |
| `lastupdate` | `datetime` | Set automatically by the `UPDATE_PASSWORD` trigger when the password changes. |
| `active` | `enum('Y','N')` | Whether Dovecot should accept login for this account. |
| `admin` | `enum('Y','N')` | Whether this user can access the admin API endpoints. |

A user's email address is reconstructed as `username@domain_name` (joining `domains.name` on `domain`).

---

### `aliases`

Maps an alias address to a destination mailbox. Dovecot reads this table to forward incoming mail.

| Column | Type | Notes |
|--------|------|-------|
| `id` | `int` | Auto-increment primary key. |
| `source_addr` | `varchar(30)` | Local part of the alias (before `@`). Unique with `source_domain`. |
| `source_domain` | `char(3)` | FK → `domains.id` (CASCADE delete). The domain the alias lives on. |
| `destination_user` | `int` | FK → `users.id` (CASCADE delete). The mailbox that receives the mail. |

An alias is always tied to a specific domain. A user can create aliases on any domain they own (primary or virtual). When the owning user is deleted all their aliases are removed automatically.

---

### `domains_ownerships`

Associates one or more users with a **virtual domain**, granting them the right to create aliases on that domain.

| Column | Type | Notes |
|--------|------|-------|
| `domainId` | `varchar(3)` | FK → `domains.id` (CASCADE delete). The virtual domain. |
| `userId` | `int` | FK → `users.id` (CASCADE delete). The owner. |

Composite primary key `(domainId, userId)` — a domain can have multiple owners, and a user can own multiple virtual domains.

When a domain is deleted all its ownership records are removed automatically. When a user is deleted all their ownerships are removed automatically.

---

## Concepts

### Primary domain

A domain for which at least one `users` row exists (`users.domain = domains.id`). Users have a real mailbox hosted on this domain. Mail sent to `username@primary-domain` lands directly in the mailbox.

### Virtual (alias) domain

A domain with no associated mailboxes — no `users` row has `domain = this domain id`. Its sole purpose is to receive mail and route it to owners' primary mailboxes via aliases. Ownership is managed through `domains_ownerships`.

Example flow: `info@extra.com` is an alias (`source_domain = 'EXT'`, `source_addr = 'info'`) pointing to `alice` on `weesky.be`. Alice owns the `EXT` domain via a `domains_ownerships` row.

### Password encryption

The application writes the password in cleartext. The `INSERT_PASSWORD` / `UPDATE_PASSWORD` triggers on `users` convert it to `$6$<salt>$<hash>` (SHA-512 crypt) transparently before the row is persisted. The salt is derived from `SHA2(CONCAT(UUID(), RAND(), NOW(6)), 256)` — combining a UUID, a random value, and a microsecond timestamp — giving sufficient entropy to prevent rainbow-table attacks. Dovecot validates login against that stored hash. Any server-side pre-hashing would cause double-encryption and break authentication.
