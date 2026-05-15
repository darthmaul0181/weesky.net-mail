# Postfix — SQL queries

Queries used by Postfix to resolve aliases, domains, and mailboxes against the `dovecot` MariaDB database.

## domains (virtual_mailbox_domains)

Checks whether a domain is handled by this server.
`%s` = domain name.

```sql
SELECT name FROM domains WHERE name = '%s'
```

---

## mailbox-maps (virtual_mailbox_maps)

Checks whether a mailbox exists and is active (used for delivery routing).
`%u` = local part, `%d` = domain name.

```sql
SELECT 1
FROM users u
JOIN domains d ON u.domain = d.id AND d.name = '%d'
WHERE u.username = '%u' AND u.active = 'Y'
LIMIT 1
```

> [!NOTE]
> Postfix delegates actual mail delivery to Dovecot via LMTP (Dovecot acts as the LDA). Postfix therefore only needs to confirm that a mailbox exists — it never resolves a filesystem path itself. Returning a constant `1` is sufficient; the value is never used, only the presence or absence of a row matters.

---

## aliases (virtual_alias_maps)

Resolves the destination mailbox address for an incoming alias.
`%u` = local part, `%d` = domain name.

```sql
SELECT CONCAT(u.username, '@', ud.name)
FROM aliases a
JOIN domains ad ON a.source_domain = ad.id AND ad.name = '%d'
JOIN users u ON u.id = a.destination_user
JOIN domains ud ON ud.id = u.domain
WHERE a.source_addr = '%u'
```

`ad` is the alias's domain; `ud` is the destination user's primary domain (may differ from `ad`).


---

## mailbox-login (smtpd_sender_login_maps)

Resolves the canonical mailbox address for a login attempt — either a primary user or the destination of an alias.
`%u` = local part, `%d` = domain name.

```sql
SELECT CONCAT(u.username, '@', d.name)
FROM users u
JOIN domains d ON u.domain = d.id AND d.name = '%d'
WHERE u.username = '%u' AND u.active = 'Y'

UNION ALL

SELECT CONCAT(u.username, '@', d.name)
FROM users u
JOIN domains d ON u.domain = d.id
JOIN aliases a ON a.destination_user = u.id
JOIN domains ad ON a.source_domain = ad.id AND ad.name = '%d'
WHERE a.source_addr = '%u' AND u.active = 'Y'
```

`UNION ALL` is used instead of `UNION` because the same address cannot be both a direct user and an alias target on the same domain simultaneously.

> [!NOTE]
> This query is also used as `smtpd_sender_login_maps` to authorize outbound sending. When a user authenticates and sends a mail, Postfix resolves the `MAIL FROM` address through this map. If the sender address matches an alias whose `destination_user` resolves to the authenticated user (via `mailbox-login`), Postfix allows the submission. In practice this means a user can send mail using any alias they own as the `From` address, not just their primary mailbox address.