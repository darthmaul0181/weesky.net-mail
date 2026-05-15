# Dovecot — SQL queries

Queries used by Dovecot for authentication (`passdb`), user lookup (`userdb`), and mailbox iteration against the `dovecot` MariaDB database.

## passdb

Retrieves the stored password for authentication.
Falls back to `weesky.be` when no domain is provided in the login string.

```sql
SELECT u.password
FROM users u
JOIN domains d ON u.domain = d.id
  AND d.name = COALESCE(NULLIF('%{user | domain}', ''), 'weesky.be')
WHERE u.username = '%{user | username}' AND u.active = 'Y'
```

---

## userdb

Retrieves the mailbox address and quota for a given identity — either a primary user or an alias address.

```sql
SELECT CONCAT(u.username, '@', d.name)  AS user,
       CONCAT(u.quota_mb, 'M')          AS quota_storage_size,
       ROUND(u.quota_mb * 0.10)         AS quota_storage_grace
FROM users u
JOIN domains d ON u.domain = d.id AND d.name = '%{user | domain}'
WHERE u.username = '%{user | username}' AND u.active = 'Y'

UNION ALL

SELECT CONCAT(u.username, '@', d.name)  AS user,
       CONCAT(u.quota_mb, 'M')          AS quota_storage_size,
       ROUND(u.quota_mb * 0.10)         AS quota_storage_grace
FROM users u
JOIN domains d ON u.domain = d.id
JOIN aliases a ON a.destination_user = u.id
JOIN domains ad ON a.source_domain = ad.id AND ad.name = '%{user | domain}'
WHERE a.source_addr = '%{user | username}' AND u.active = 'Y'
```

`UNION ALL` is used instead of `UNION` because the same address cannot be both a direct user and an alias target on the same domain simultaneously.

> [!NOTE]
> The alias branch is required because Dovecot's `userdb` is queried **before** Postfix resolves aliases. The Postfix quota-status plugin asks Dovecot whether the destination mailbox has enough space prior to accepting the message — at that point Postfix only knows the original recipient address, not the resolved one. The lookup therefore arrives with the alias address, and the `UNION ALL` branch resolves it to the real mailbox and its quota. The flow looks like this:
>
> ```
> alias@example.com arrives
>        │
>        ▼
> Postfix → quota-status → Dovecot userdb(alias@example.com)
>        │                        ← UNION ALL alias branch resolves here
>        ▼
> Postfix resolves alias → user@primary-domain.com
>        │
>        ▼
> Postfix → LMTP → Dovecot userdb(user@primary-domain.com)
>                         ← direct branch matches here; alias branch returns nothing
> ```
>
> The alias branch is harmless on the LMTP delivery path (it simply returns no rows), but it is essential for the quota pre-check.

---

## iterate

Enumerates all active mailboxes (used by `doveadm` for batch operations such as quota recalculation).

```sql
SELECT CONCAT(u.username, '@', d.name) AS user
FROM users u
INNER JOIN domains d ON d.id = u.domain
```
