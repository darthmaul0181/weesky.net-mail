# OAuth for connected accounts — a second way to prove who you are, not a second way to reach a server

A connected mailbox is attached today by handing us its password, which we verify, encrypt under a
key derived from the user's own password, and replay on every IMAP or SMTP login. This adds a
second credential shape beside it: an OAuth 2.0 refresh token, exchanged for a short-lived access
token and presented over SASL `XOAUTH2`. Everything else about a connected account — who curates
the endpoints, who owns the secret, which errors mean what — stays exactly as it is.

Microsoft is the first provider wired, because it is the one where OAuth is not optional: basic
authentication is gone from Outlook, Hotmail and Office 365, personal accounts included, so a
password can no longer attach one of those mailboxes at all.

## The problem

`MailAccountConnection` carries a `Password`, `MailConnectionFactory.OpenAsync` calls
`AuthenticateAsync(username, password)`, and that is the only way this application can prove an
identity to a mail server. Every provider that has retired basic authentication is therefore
unreachable, whatever endpoints an admin curates for it.

The gap is narrow and the shape of the code is already right for it: endpoints come from an
admin-written `external_domains` row, a user supplies only a credential, and the credential is
composed into a connection record in exactly one place. What is missing is a credential that is not
a password, and the machinery to keep it fresh.

## What this is not

**Not a change to who decides where a connection goes.** A user still supplies no host. The
authorization and token endpoints join the IMAP and SMTP ones on the admin-curated domain row, for
the same reason those are there: no request field may ever become the address of an outbound
connection, and a client secret is not something an end user should hold.

**Not a replacement for password authentication.** The home server, every local shared mailbox and
every external domain an admin leaves in `Password` mode keep working unchanged, byte for byte. A
Gmail mailbox attached today with an application password keeps working after this ships.

**Not Gmail.** Full IMAP and SMTP access to a Gmail mailbox needs the `https://mail.google.com/`
scope, which Google classifies as restricted: publishing an application that requests it requires an
annual paid third-party security assessment, and the only exemption is an *Internal* application in
a Google Workspace domain. The mechanism built here is provider-neutral and Google is one
`external_domains` row away, but wiring that row is a separate decision with a cost attached, and it
is out of scope. Application passwords remain the answer for Gmail in the meantime.

**Not background access.** Nothing in this service runs outside a request — there is no
`BackgroundService` and no hosted service — so a token is only ever refreshed while the user's
session key is in hand. This is what lets the refresh token be encrypted the same way a password is,
and it is a constraint to respect rather than an accident: the day a background job needs a mailbox,
the encryption key becomes the question again.

**Not a token vault.** No access token is ever written to the database. It lives in process memory
for its hour and dies with the process.

**Not MSAL.** The Microsoft authentication library manages its own token cache and its own storage,
which is precisely the part we are not delegating. The exchange is two `HttpClient` calls against
documented endpoints, and MailKit 4.17 already ships `SaslMechanismOAuth2`. **No new NuGet package.**

## The credential

`MailAccountConnection.Password` becomes `MailAccountConnection.Credential`, a closed record
hierarchy:

```csharp
internal abstract record MailCredential;
internal sealed record PasswordCredential(string Password) : MailCredential;
internal sealed record OAuthCredential(string AccessToken) : MailCredential;
```

`MailConnectionFactory.OpenAsync` switches on it, and that switch is the entire protocol change:

```csharp
await (connection.Credential switch
{
    OAuthCredential o =>
        client.AuthenticateAsync(new SaslMechanismOAuth2(connection.Username, o.AccessToken), ct),
    PasswordCredential p => client.AuthenticateAsync(connection.Username, p.Password, ct),
    _ => throw new UnreachableException()
});
```

A flag beside the existing `Password` field would have been a smaller diff. The type is worth the
difference because it makes the compiler visit every consumer, and one of them —
`ManageSieveClient`, which builds a SASL PLAIN payload out of the password by hand — can never do
OAuth and must say so rather than send an access token where a password is expected. See
*ManageSieve* below.

`MailAccountConnection.ToString` is already redacted and stays redacted. `MailCredential` declares
`public sealed override string ToString() => GetType().Name;` — **sealed, not merely overridden**:
a record generates its own `ToString` unless the base has sealed it, so an unsealed override in the
base would be silently replaced in `PasswordCredential` by one that prints the password.

## The provider record

`external_domains` gains six columns. Only `auth_mode` is non-null, defaulting to `Password`, so
every existing row keeps its exact current meaning.

| column | type | meaning |
|---|---|---|
| `auth_mode` | `VARCHAR(16) NOT NULL DEFAULT 'Password'` | `Password` \| `OAuth2` |
| `oauth_authorization_url` | `VARCHAR(512) NULL` | where the user is sent to consent |
| `oauth_token_url` | `VARCHAR(512) NULL` | where codes and refresh tokens are exchanged |
| `oauth_scopes` | `VARCHAR(1024) NULL` | space-separated, sent verbatim |
| `oauth_client_id` | `VARCHAR(255) NULL` | public |
| `oauth_client_secret` | `VARBINARY(1024) NULL` | Data-Protection-protected |

The Microsoft tenant segment (`common`, `consumers`, `organizations`, a tenant GUID) is part of the
authorization and token URLs, so it needs no column of its own.

**The client secret is protected with the existing Data Protection key ring**, under a purpose
string of its own (`weesky.oauth.clientsecret`), distinct from the credentials cookie's. The key
ring is already persisted — `StateDirectory=` in the systemd unit is a documented prerequisite and
the service refuses to start without it outside Development — so this introduces no new key
material and no new operational failure mode. Losing the key ring already signs every user out; it
would now also require re-entering client secrets, which is an admin action on a handful of rows.

A domain row in `OAuth2` mode with any of the five OAuth columns null is **unusable**, and is
treated exactly as a row whose transport security no longer parses: logged as an administrator
error, answered to the caller as `account_not_found`. `MailConnectionBuilder.TryExternal` grows this
check; the rule that a caller learns nothing about why a domain is unusable is unchanged.

## The account record

`connected_accounts` gains one column and widens another.

**`auth_mode VARCHAR(16) NOT NULL DEFAULT 'Password'`**, frozen at creation. The mode is derivable
from the domain row today, but a row that describes itself cannot be reinterpreted by an admin
flipping a domain from `Password` to `OAuth2` — which would otherwise hand a stored password to a
token endpoint, or feed a refresh token to `AuthenticateAsync` as a password.

**`cipher` widens from `VARBINARY(512)` to `VARBINARY(8192)`.** This is not a precaution: Microsoft
refresh tokens are opaque encrypted blobs that routinely exceed 1 KB, where Google's are around 100
characters. At 512 bytes the column cannot hold one at all. `ConnectedAccountCipher.MaxSecretLength`
follows from the width — `8192 - 1 (version) - 12 (nonce) - 16 (tag) = 8163` — and stays the single
place that arithmetic is written. MariaDB's 65 535-byte row limit is not approached.

**The mode enters the AES-GCM associated data.** `ConnectedAccountCipher.Context` binds a cipher to
the row's identity, its owner, its domain and its login, so that write access to the database
redirects nothing. The mode belongs in that set for the same reason the domain does. The segment is
added **only for OAuth rows, and before the address**, which keeps two properties: every row written
by commit `02f91e9` still opens unchanged, and the address stays last so no two rows can produce the
same context.

```
Password : {accountId}|{userId}|{domainId}|{email}            ← byte-identical to today
OAuth2   : {accountId}|{userId}|{domainId}|oauth2|{email}
```

Nothing else about the cipher changes: same AES-256-GCM, same KEK derived from the user's own
password with PBKDF2, same property that the server alone can never open what it stores.

## The consent flow

The obstacle that shapes this flow is that **both session cookies are `SameSite=Strict`**
(`SessionCookies.cs`, `MailCredentialStore.cs`). The provider's redirect back to us is a cross-site
top-level navigation, so neither cookie is sent: the request that carries the authorization code
cannot identify the user and cannot derive the KEK. Relaxing either cookie to `Lax` for the sake of
one flow was rejected — it weakens the CSRF posture of the whole application.

The handshake is therefore three steps, and the middle one is deliberately anonymous.

**1. `POST /api/ConnectedAccounts/OAuth/Start`** — authenticated, rate-limited under the existing
`login` limiter. Body `{ domainId }` to attach a new mailbox, or `{ accountId }` to re-authenticate
one already attached; exactly one of the two, and an `accountId` the caller does not own is the
usual 404. Refuses a domain that is not in `OAuth2` mode. Mints a 128-bit `state` and a PKCE
verifier, records a pending handshake, and answers the authorization URL the client should navigate
to.

**2. `GET /api/ConnectedAccounts/OAuth/Callback?code=&state=`** — `[AllowAnonymous]`, because no
cookie can reach it. It looks the `state` up, exchanges the code at the token endpoint (the client
secret never leaves the server), stores the resulting tokens on the pending handshake, and answers a
302 to the settings page carrying the same `state`. An unknown, expired or already-consumed `state`
redirects to the settings page with a generic error; a provider error redirects the same way.
Nothing here is trusted enough to write a row.

**3. `POST /api/ConnectedAccounts/OAuth/Complete`** — authenticated and same-site, so both cookies
travel and the KEK is available. Body `{ state }`. It loads the pending handshake, **verifies that
its user is the calling user** — the check without which one user could complete another's consent —
encrypts the refresh token under the KEK with the OAuth context, writes the `connected_accounts`
row, consumes the handshake, and answers the same `ConnectedAccountResponse` shape `Connect`
answers today.

The authorization code never appears in the browser's URL bar or history, and never reaches
JavaScript.

### The pending handshake

`IOAuthHandshakeStore` over `IMemoryCache`: created at Start, enriched at Callback, consumed
single-use at Complete, expiring after **10 minutes** either way. The entry holds the user id, the
domain id, the PKCE verifier and — after the callback — the tokens and the address the provider
reported.

**These tokens live in process memory and are never persisted**, which is why they are not
encrypted at rest: there is no rest. That is stronger than protecting a persisted row, and it costs
one restarted consent if the service is restarted mid-handshake, which is acceptable for a
ten-minute window. The deployment is single-instance, so no shared store is required; the day it is
not, this is the piece that moves to the database.

### Which address the account carries

The scopes requested include `openid email profile`, so the token response carries an `id_token`
whose `email` (falling back to `preferred_username`) is the mailbox address. It is read with
`Microsoft.IdentityModel.JsonWebTokens`, already referenced. **The signature is not validated**, and
that is correct rather than lax: the token arrives over TLS on a direct back-channel call to the
token endpoint, which OpenID Connect explicitly recognises as sufficient for a confidential client
in the authorization-code flow. There is no Microsoft Graph dependency.

The address is canonicalised through `IdentityResolver.Canonical` before it is stored, exactly as
`Connect` does with a user-typed one, so the cipher context matches what the row holds. A user
cannot supply it: it is whatever mailbox they actually signed in to, which removes an entire class
of mismatch the password flow has to validate.

The existing duplicate and self-connection rules apply unchanged, and are checked at Complete: a
mailbox already connected, or the caller's own primary address on the home server, is refused with
the messages `Connect` already uses.

**Re-authenticating an existing row refuses a different mailbox.** A handshake started with an
`accountId` must come back with the address that row already holds, or it is a 400 naming the
mismatch. The cipher context is bound to the address, so a token for another mailbox would encrypt
under a context the row can never reproduce — the account would open once, in memory, and never
again. Refusing at the boundary turns a silent corruption into a sentence the user can act on.

## Keeping the token fresh

`IOAuthTokenService.GetAccessTokenAsync(row, domain, kek, ct)`, a typed `HttpClient` service
alongside `DovecotQuotaClient`:

1. **Cache hit** — `IMemoryCache`, keyed on user id *and* account id, returns the access token if
   more than **two minutes** of its life remain. This is the path a normal request takes.
2. **Miss** — a `SemaphoreSlim` per account serialises the refresh, and the cache is re-checked
   inside it, so a burst of parallel mail requests triggers exactly one exchange rather than one per
   request.
3. The refresh token is decrypted under the KEK with the row's context, posted to the token endpoint
   with the client id and the unprotected client secret, and the new access token cached against the
   `expires_in` the provider reports.
4. **Rotation is expected, not exceptional.** Microsoft returns a new refresh token on every
   refresh. When the response carries one, the row's cipher is re-encrypted and updated. This is a
   write on a read path, and it is not best-effort the way `BindCipherAsync` is: dropping a rotated
   token loses the account. It fails the resolution if it fails.
5. **`invalid_grant`** — the consent was withdrawn, the token expired after 90 days of silence, or
   the password change invalidated nothing but our ability to read it — resolves to
   `ConnectedAccountErrors.CredentialsInvalid`, the existing **409**.
6. Any other failure — network, 5xx, a malformed response — resolves to a new
   `ConnectedAccountErrors.ProviderUnavailable`, mapped to **502** in
   `ApiBaseController.ConnectedAccountError`, beside the two constants already there.

The service is called from **`AccountConnectionResolver.ExternalConnection`** and nowhere else: the
single place an external connection record is composed. The password branch is untouched, the
factories learn nothing about OAuth beyond the SASL switch, and no controller acquires a token.

## Errors, and what the user sees

No new HTTP status. The four failure modes documented in the mail rules are unchanged, and OAuth
lands inside them:

- A withdrawn or expired consent is **409 `connected_credentials_invalid`** — the same code a
  connected account whose cipher no longer opens produces today, and for the same reason: the
  session is entirely valid, one attached mailbox needs re-authentication. It must never become a
  401, which the frontend's global handler reads as "sign out".
- A provider that will not answer is **502**, like a mail server that refuses.
- A domain row missing its OAuth configuration is **404 `account_not_found`**, like any other
  unusable domain row.

`credentialsValid` in the account listing keeps its current meaning — *the cipher opens under the
session key* — and no more. Proving a refresh token still lives requires a network round trip per
account, which is exactly what that endpoint avoids by design; a dead token surfaces on first use.
The response record gains **`authMode`**, which is what lets the frontend offer the right repair.

## ManageSieve

`RulesController` resolves a connection and builds a `SieveConnection` whose SASL PLAIN payload is
assembled from the password by hand. An access token is not a password there, and ManageSieve has no
`XOAUTH2` in this implementation.

An OAuth connection is therefore treated **exactly as a domain with no Sieve host**: the rules
editor is hidden and the endpoint refuses. In practice this is invisible — Outlook offers no
ManageSieve, so `sieve_host` is null on that row anyway and the editor is already hidden — but the
refusal is written explicitly at the pattern match rather than left to a null check somewhere else,
which is the point of the closed credential type.

## The frontend

`ConnectAccountForm` branches on the selected domain's `authMode`: a password field as today, or a
single "Sign in with Microsoft" button that calls Start and navigates to the returned URL. The
settings page reads the `state` the callback redirect leaves in the query string, calls Complete,
strips the parameter, and shows the created account or the error.

`ConnectedAccountsPage` shows the same "credentials invalid" state it shows today, with the action
switched by `authMode`: a password field, or a "Reconnect" button that restarts the same handshake
against the existing row. Reconnecting an existing account replaces its cipher — the account keeps
its id, its identities and its folder-role overrides — which is the OAuth twin of
`PUT /{id}/Password`.

`ExternalDomainChoice` gains `authMode`, so the form can branch before anything is submitted. It
still carries no host, no client id and no scope: those stay administrator information.

## Testing

- **`ConnectedAccountCipher`** — the password context is pinned byte-for-byte against today's
  string, so the backward compatibility is a test and not a comment; the OAuth context differs from
  the password one for the same row; an 8163-byte secret round-trips and 8164 throws.
- **`MailConnectionFactory`** — an `OAuthCredential` authenticates through `SaslMechanismOAuth2` and
  a `PasswordCredential` through the user/password overload, following the pattern
  `MailConnectionFactoryCleartextTests` already establishes.
- **`OAuthTokenService`** — over a fake `HttpMessageHandler`: a successful refresh caches and does
  not re-exchange; a rotated refresh token rewrites the row's cipher; a refresh that returns none
  leaves it alone; `invalid_grant` maps to `CredentialsInvalid`; a 500 and a timeout map to
  `ProviderUnavailable`; **twenty concurrent calls produce exactly one HTTP exchange**.
- **`OAuthHandshakeStore`** — expiry, single use, and a completion by a different user refused.
- **`ConnectedAccountsController`** — Start refuses a `Password` domain; Callback with an unknown
  state redirects with an error and writes nothing; Complete by another user answers 404; Complete
  of an already-connected mailbox answers the existing 400.
- **`AccountConnectionResolver`** — an OAuth row resolves to an `OAuthCredential`; a password row is
  unaffected; a token failure maps to 409 and 502 respectively.
- **Frontend** — the form branches on `authMode`; the reconnect button appears on an invalid OAuth
  account and the password field on an invalid password account.

## What an operator must do

**1. Register the application** in Microsoft Entra ID (Azure portal → App registrations → New
registration):

- Supported account types: *Accounts in any organizational directory and personal Microsoft
  accounts* — this is what lets both an Office 365 and an Outlook.com mailbox connect.
- Redirect URI, type **Web**: `https://<api-host>/api/ConnectedAccounts/OAuth/Callback`. It must
  match byte for byte what the service sends.
- Certificates & secrets → New client secret. **Copy it immediately**; the portal shows it once.
  Note its expiry — Microsoft caps it at 24 months, and a mailbox stops refreshing the day it
  lapses.
- API permissions, **from two different APIs** — this is the step that trips people up, because the
  mail scopes are not Graph scopes and are not in the list the portal offers first:
  - *APIs my organization uses* → **Office 365 Exchange Online** → Delegated permissions →
    `IMAP.AccessAsUser.All` and `SMTP.Send`. The service requests them by their full URI,
    `https://outlook.office.com/IMAP.AccessAsUser.All` and
    `https://outlook.office.com/SMTP.Send`.
  - *Microsoft Graph* → Delegated permissions → `offline_access`, `openid`, `email`, `profile`.

**2. Apply the schema change** to `snoopy_webmail`. Database creation is manual in this project, as
`docs/superpowers/mail-2a5-database-prerequisite.md` records; this follows the same route.

```sql
ALTER TABLE external_domains
  ADD COLUMN auth_mode              VARCHAR(16)    NOT NULL DEFAULT 'Password',
  ADD COLUMN oauth_authorization_url VARCHAR(512)  NULL,
  ADD COLUMN oauth_token_url        VARCHAR(512)   NULL,
  ADD COLUMN oauth_scopes           VARCHAR(1024)  NULL,
  ADD COLUMN oauth_client_id        VARCHAR(255)   NULL,
  ADD COLUMN oauth_client_secret    VARBINARY(1024) NULL;

ALTER TABLE connected_accounts
  ADD COLUMN auth_mode VARCHAR(16) NOT NULL DEFAULT 'Password',
  MODIFY COLUMN cipher VARBINARY(8192) NOT NULL;
```

**3. Create the domain row** with:

- authorization URL `https://login.microsoftonline.com/common/oauth2/v2.0/authorize`
- token URL `https://login.microsoftonline.com/common/oauth2/v2.0/token`
- scopes `offline_access openid email profile https://outlook.office.com/IMAP.AccessAsUser.All https://outlook.office.com/SMTP.Send`
- IMAP `outlook.office365.com:993` `SslOnConnect`, SMTP `smtp.office365.com:587` `StartTls`
- Sieve host null.

## Deferred

**Google.** One `external_domains` row — authorization and token endpoints, the
`https://mail.google.com/` scope, `imap.gmail.com:993`, `smtp.gmail.com:587` — and no code. What is
deferred is the decision to pay for the security assessment the restricted scope requires, or to
move the domain to Google Workspace so an *Internal* application is possible.

**Background refresh.** Out of reach by construction while the refresh token is encrypted under the
user's session key. A server-side push-notification feature would have to revisit that choice
first, and migrate every stored cipher.

**Multi-instance.** The access-token cache and the pending handshake store are both process-local.
Both would move to a shared store, and the handshake to the database, before this service is run
more than once.
