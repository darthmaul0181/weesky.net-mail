# Optional — connecting external mailboxes over OAuth

Lets a user attach an Office 365 or Outlook.com mailbox to the webmail without ever typing a
password into it. Skip this page unless you want that: nothing else depends on it, and a
deployment that never enables it behaves exactly as before.

Everything below is done once, by an operator. The tables `install.sql` created already carry the
columns this needs.

> **The client secret is entered in the Administration screen, never written into the column.**
> The API passes the pasted value through Data Protection on the way in, so
> `external_domains.oauth_client_secret` holds protected bytes, not text. A plaintext value pasted
> straight into the column will simply never open again.

---

## 1. Register the application with Microsoft

Azure portal → App registrations → New registration.

**Supported account types:** *Accounts in any organizational directory and personal Microsoft
accounts*. That is what lets the same registration serve an Office 365 mailbox and an Outlook.com
one.

**Redirect URI, type Web** — the **API's** callback, not the webmail's. Microsoft redirects to the
microservice, which exchanges the code server-side. Register both; they coexist on one
registration:

```
https://api.mail.example.net/api/ConnectedAccounts/OAuth/Callback
https://api-dev.mail.example.net/api/ConnectedAccounts/OAuth/Callback
```

It must match `Mail__OAuthRedirectUri` (step 2) byte for byte: same scheme, same case in the path,
no trailing slash. Microsoft compares it literally.

**Certificates & secrets → New client secret.** Copy it immediately — the portal shows it once.
Note its expiry: Microsoft caps it at 24 months, and mailboxes stop refreshing the day it expires.

**API permissions, from two different APIs.** This is the step that trips people up, because the
mail scopes are not Graph scopes and do not appear in the list the portal offers first:

- *APIs my organization uses* → **Office 365 Exchange Online** → Delegated permissions →
  `IMAP.AccessAsUser.All` and `SMTP.Send`. The service asks for them by full URI,
  `https://outlook.office.com/IMAP.AccessAsUser.All` and `https://outlook.office.com/SMTP.Send`.
- *Microsoft Graph* → Delegated permissions → `offline_access`, `openid`, `email`, `profile`.

---

## 2. Two settings on the service

The provider hands the authorization code **to the server**, never to the browser — it is a
secret, and a page's URL is read far too easily. So Microsoft redirects to the API, while the user
must end up on the webmail's settings page, which is a different host. The service needs both
addresses:

- **`OAuthRedirectUri`** — what the service *announces to Microsoft*, in the authorization request
  and again at the code exchange. It is not an address to reach; it is a string to match against
  step 1.
- **`WebmailBaseUrl`** — where the service *sends the browser back* once the code is exchanged. It
  knows only its own address. The service appends `/settings/accounts` itself, so give the root
  only, with no trailing slash.

These do not go in `appsettings.json`. Put them in the `EnvironmentFile` the systemd unit already
uses, one unit per environment, then `systemctl restart`:

```ini
Mail__WebmailBaseUrl=https://account.mail.example.net
Mail__OAuthRedirectUri=https://api.mail.example.net/api/ConnectedAccounts/OAuth/Callback
```

Left empty, `WebmailBaseUrl` produces a relative redirect that only works when the API and the SPA
share an origin. They do not here: the user would land back on the API and see an error page
instead of their settings.

---

## 3. Create the domain row in Administration

Sign in as an administrator, then Settings → Administration → External domains → Add.

- IMAP `outlook.office365.com` port `993` `SSL/TLS`; SMTP `smtp.office365.com` port `587`
  `STARTTLS`. Leave the Sieve fields empty — Outlook offers no ManageSieve.
- Authentication: **OAuth 2.0**. The provider fields appear:

| Field | Value |
|---|---|
| Authorization URL | `https://login.microsoftonline.com/common/oauth2/v2.0/authorize` |
| Token URL | `https://login.microsoftonline.com/common/oauth2/v2.0/token` |
| Scopes | `offline_access openid email profile https://outlook.office.com/IMAP.AccessAsUser.All https://outlook.office.com/SMTP.Send` |
| Client id | the Application (client) ID from step 1 |
| Client secret | the secret copied in step 1, pasted once |

The secret is **write-only**. After saving, the edit dialog says only that a secret is stored:
leaving the field empty on a later edit keeps it, entering a new value replaces it — which is also
how you rotate an expired Entra secret. Both URLs must be https, and the API refuses to save an
OAuth 2.0 domain with a provider field missing. A row that saves is a row the consent flow
accepts.

Once saved, the domain's tile carries an `OAuth` label, and its connection form offers
"Sign in with &lt;name&gt;" in place of a password field.

---

## Do not switch an existing Password domain to OAuth over live rows

A connected account's `auth_mode` is fixed at creation, deliberately. Rows attached while the
domain was in Password mode go on replaying their stored password after the switch. The provider
refuses it, every mail request on that mailbox answers 502, and the row in settings still looks
healthy — `credentialsValid` says only that the ciphertext opens.

There is no repair in place for such a row: the Reconnect consent belongs to OAuth rows, and
re-entering a password reaches a server that no longer accepts one. Each affected user must
**disconnect the mailbox and attach it again** through the provider sign-in the switched domain
now offers.
