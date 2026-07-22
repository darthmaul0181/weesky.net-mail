# Reader Header Expanded Details Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A chevron on the reader's "From" line expands the header into a Gmail-style label/value grid (mailing list, mailed by, signed by, unsubscribe, TLS), backed by 5 new backend fields.

**Architecture:** A new static `MailHeaderDetailsReader` (modelled on `MailAuthenticationReader`) extracts the five values from the already-fetched `message.Headers` in `ImapSession.GetMessageDetailAsync`; they travel as flat nullable fields on `MailMessageDetail`. The frontend adds a `detailsOpen` boolean to `MessageReader` and a `ReaderDetails` grid component that replaces the compact To/Cc lines while open.

**Tech Stack:** .NET 10 / MimeKit / xUnit — React 18 / TypeScript / Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-07-22-reader-details-design.md`

## Global Constraints

- **Topmost header only** (backend rule 7): every header read takes the first (topmost) occurrence; a value missing there is never borrowed from a lower occurrence.
- **UI text in English** (the site's UI language; conversation language is irrelevant).
- **No literal colours in `mail.css`** — role tokens only (`--text-muted`, `--action-primary`, `--surface-sunken`, `--radius-sm`…).
- **No persistence, no setting**: `detailsOpen` is per-message component state, reset on message change.
- Code comments only where the code can't say it; 3 lines max (repo rule).
- Backend: file-scoped namespaces, `sealed`, records for DTO values, one type per file.
- Run backend tests with `dotnet test` (never `--no-build` — new test files are added here).

---

### Task 1: Backend — `MailHeaderDetails` record + `MailHeaderDetailsReader`

**Files:**
- Create: `src/snoopy.microservice/Models/Mail/MailHeaderDetails.cs`
- Create: `src/snoopy.microservice/Services/MailHeaderDetailsReader.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailHeaderDetailsReaderTests.cs`

**Interfaces:**
- Consumes: `MimeKit.HeaderList`, `MimeKit.Cryptography.AuthenticationResults` (same API `MailAuthenticationReader` uses).
- Produces: `MailHeaderDetailsReader.Parse(HeaderList headers)` returning `MailHeaderDetails(string? MailingList, string? SentBy, string? SignedBy, string? UnsubscribeUrl, bool? TlsReceived)` — Task 2 wires this into `ImapSession`.

- [ ] **Step 1: Write the failing tests**

Create `src/snoopy.microservice/snoopy.microservice.Tests/Services/MailHeaderDetailsReaderTests.cs`:

```csharp
using MimeKit;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class MailHeaderDetailsReaderTests
{
    private static HeaderList Headers(params (string Field, string Value)[] entries)
    {
        var headers = new HeaderList();
        foreach (var (field, value) in entries) headers.Add(new Header(field, value));
        return headers;
    }

    [Fact]
    public void Parse_ReturnsAllNullsOnAMessageWithoutTheHeaders()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(("Subject", "hello")));

        Assert.Null(result.MailingList);
        Assert.Null(result.SentBy);
        Assert.Null(result.SignedBy);
        Assert.Null(result.UnsubscribeUrl);
        Assert.Null(result.TlsReceived);
    }

    [Fact]
    public void Parse_ReadsTheMailingListVerbatim()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(("List-Id", "Weesky news <news.weesky.net>")));

        Assert.Equal("Weesky news <news.weesky.net>", result.MailingList);
    }

    [Fact]
    public void Parse_ReadsSentByFromTheAuthenticatedEnvelope()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Authentication-Results", "mx.weesky.net; spf=pass smtp.mailfrom=bounce@a547955.bnc3.mailjet.com")));

        Assert.Equal("a547955.bnc3.mailjet.com", result.SentBy);
    }

    [Fact]
    public void Parse_FallsBackToReturnPathForSentBy()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(("Return-Path", "<bounce@list.example.org>")));

        Assert.Equal("list.example.org", result.SentBy);
    }

    [Fact]
    public void Parse_FallsBackToSenderForSentBy()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(("Sender", "Weesky News <news@sender.example>")));

        Assert.Equal("sender.example", result.SentBy);
    }

    [Fact]
    public void Parse_PrefersTheAuthenticatedEnvelopeOverReturnPath()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Authentication-Results", "mx.weesky.net; spf=pass smtp.mailfrom=bounce@authentic.example"),
            ("Return-Path", "<bounce@other.example>")));

        Assert.Equal("authentic.example", result.SentBy);
    }

    // Every header below the topmost was written upstream — or forged. Same rule as
    // MailAuthenticationReader: nothing is ever borrowed from a lower occurrence.
    [Fact]
    public void Parse_ReadsOnlyTheTopmostAuthenticationResults()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Authentication-Results", "mx.weesky.net; dmarc=pass"),
            ("Authentication-Results", "relay.evil.example; spf=pass smtp.mailfrom=a@evil.example; dkim=pass header.d=evil.example")));

        Assert.Null(result.SentBy);
        Assert.Null(result.SignedBy);
    }

    [Fact]
    public void Parse_ReadsSignedByFromTheTopmostAuthenticationResults()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Authentication-Results", "mx.weesky.net; dkim=pass header.d=google.com header.s=s1")));

        Assert.Equal("google.com", result.SignedBy);
    }

    [Fact]
    public void Parse_FallsBackToTheDkimSignatureForSignedBy()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("DKIM-Signature", "v=1; a=rsa-sha256; d=fondation-patrimoine.org; s=mailjet; h=from:to")));

        Assert.Equal("fondation-patrimoine.org", result.SignedBy);
    }

    [Fact]
    public void Parse_PicksTheHttpsUnsubscribeLinkOverMailto()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("List-Unsubscribe", "<mailto:unsub@x.be>, <https://x.be/unsub?id=1>")));

        Assert.Equal("https://x.be/unsub?id=1", result.UnsubscribeUrl);
    }

    [Fact]
    public void Parse_KeepsTheMailtoUnsubscribeWhenItIsAllThereIs()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(("List-Unsubscribe", "<mailto:unsub@x.be>")));

        Assert.Equal("mailto:unsub@x.be", result.UnsubscribeUrl);
    }

    // The value is sender-controlled and lands in an <a href>; anything but http(s)/mailto is dropped.
    [Fact]
    public void Parse_DropsAnUnsubscribeCarryingNoSafeScheme()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(("List-Unsubscribe", "<javascript:alert(1)>")));

        Assert.Null(result.UnsubscribeUrl);
    }

    [Fact]
    public void Parse_ReportsTlsFromTheTopmostReceived()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Received", "from out.mailjet.com by mx.weesky.net with ESMTPS id abc123")));

        Assert.True(result.TlsReceived);
    }

    [Fact]
    public void Parse_ReportsNoTlsOnAPlainSmtpHop()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Received", "from out.mailjet.com by mx.weesky.net with ESMTP id abc123")));

        Assert.False(result.TlsReceived);
    }

    // The topmost Received is the hop into our own server — the only one we wrote ourselves.
    [Fact]
    public void Parse_LetsTheTopmostReceivedWin()
    {
        var result = MailHeaderDetailsReader.Parse(Headers(
            ("Received", "from relay by mx.weesky.net with ESMTP id a"),
            ("Received", "from origin by relay with ESMTPS id b")));

        Assert.False(result.TlsReceived);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

From `src/snoopy.microservice`: `dotnet test --filter MailHeaderDetailsReaderTests`
Expected: build FAILS — `MailHeaderDetailsReader` and `MailHeaderDetails` do not exist.

- [ ] **Step 3: Write the implementation**

Create `src/snoopy.microservice/Models/Mail/MailHeaderDetails.cs`:

```csharp
namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>Delivery details for the reader's expanded header. Any field is null when its header is absent.</summary>
public sealed record MailHeaderDetails(
    string? MailingList,
    string? SentBy,
    string? SignedBy,
    string? UnsubscribeUrl,
    bool? TlsReceived);
```

Create `src/snoopy.microservice/Services/MailHeaderDetailsReader.cs`:

```csharp
using MimeKit;
using MimeKit.Cryptography;
using weesky.Snoopy.Microservice.Models.Mail;

namespace weesky.Snoopy.Microservice.Services;

/// <summary>Reads the reader's expanded-header details out of a message's headers. Topmost occurrence only.</summary>
internal static class MailHeaderDetailsReader
{
    public static MailHeaderDetails Parse(HeaderList headers)
    {
        var auth = TopmostAuthenticationResults(headers);

        return new MailHeaderDetails(
            Topmost(headers, "List-Id"),
            SentBy(headers, auth),
            SignedBy(headers, auth),
            UnsubscribeUrl(Topmost(headers, "List-Unsubscribe")),
            TlsReceived(Topmost(headers, "Received")));
    }

    // HeaderList preserves message order and relays prepend, so the first match is the topmost.
    private static string? Topmost(HeaderList headers, string field)
    {
        foreach (var header in headers)
            if (string.Equals(header.Field, field, StringComparison.OrdinalIgnoreCase)) return header.Value.Trim();

        return null;
    }

    private static AuthenticationResults? TopmostAuthenticationResults(HeaderList headers)
    {
        foreach (var header in headers)
        {
            if (!string.Equals(header.Field, "Authentication-Results", StringComparison.OrdinalIgnoreCase)) continue;
            return AuthenticationResults.TryParse(header.RawValue, out var parsed) ? parsed : null;
        }

        return null;
    }

    // Gmail's "mailed by": the envelope domain. The authenticated smtp.mailfrom is the most
    // trustworthy source; Return-Path (written by our own MTA) and Sender come after.
    private static string? SentBy(HeaderList headers, AuthenticationResults? auth)
        => DomainOf(Property(auth, "spf", "smtp", "mailfrom"))
           ?? DomainOf(Topmost(headers, "Return-Path"))
           ?? DomainOf(Topmost(headers, "Sender"));

    private static string? SignedBy(HeaderList headers, AuthenticationResults? auth)
        => Property(auth, "dkim", "header", "d") ?? DkimSignatureDomain(Topmost(headers, "DKIM-Signature"));

    private static string? Property(AuthenticationResults? auth, string method, string ptype, string name)
    {
        if (auth is null) return null;

        foreach (var result in auth.Results)
        {
            if (!string.Equals(result.Method, method, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var property in result.Properties)
                if (string.Equals(property.PropertyType, ptype, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(property.Property, name, StringComparison.OrdinalIgnoreCase))
                    return property.Value;
        }

        return null;
    }

    private static string? DkimSignatureDomain(string? value)
    {
        if (value is null) return null;

        foreach (var segment in value.Split(';'))
        {
            var trimmed = segment.Trim();
            if (trimmed.StartsWith("d=", StringComparison.OrdinalIgnoreCase)) return trimmed[2..].Trim();
        }

        return null;
    }

    // Accepts "a@b.c", "<a@b.c>" or "Name <a@b.c>" — the part after the last @.
    private static string? DomainOf(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;

        var cleaned = address.Trim().TrimStart('<').TrimEnd('>');
        var at = cleaned.LastIndexOf('@');
        var domain = at >= 0 ? cleaned[(at + 1)..] : cleaned;
        return domain.Length > 0 ? domain : null;
    }

    // Sender-controlled and rendered as a link: only http(s) and mailto survive, https first.
    private static string? UnsubscribeUrl(string? value)
    {
        if (value is null) return null;

        string? mailto = null;
        foreach (var entry in value.Split(','))
        {
            var url = entry.Trim().TrimStart('<').TrimEnd('>');
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return url;
            if (mailto is null && url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) mailto = url;
        }

        return mailto;
    }

    // ESMTPS (covers ESMTPSA) is the with-TLS SMTP dialect; "TLS" catches version/cipher notes.
    private static bool? TlsReceived(string? value)
        => value is null
            ? null
            : value.Contains("ESMTPS", StringComparison.OrdinalIgnoreCase) || value.Contains("TLS", StringComparison.Ordinal);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

From `src/snoopy.microservice`: `dotnet test --filter MailHeaderDetailsReaderTests`
Expected: 15 PASS. Then run the full suite: `dotnet test` — everything green.

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Models/Mail/MailHeaderDetails.cs src/snoopy.microservice/Services/MailHeaderDetailsReader.cs src/snoopy.microservice/snoopy.microservice.Tests/Services/MailHeaderDetailsReaderTests.cs
git commit -m "Add MailHeaderDetailsReader for the reader's expanded header"
```

---

### Task 2: Backend DTO + `ImapSession` wiring + frontend types

**Files:**
- Modify: `src/snoopy.microservice/Models/Mail/MailMessageDetail.cs` (after the `SpamScore` property, line 23)
- Modify: `src/snoopy.microservice/Services/ImapSession.cs:392-408` (the `MailMessageDetail` initialiser)
- Modify: `src/frontend/src/modules/mail/api/mailTypes.ts:75-92` (`MailMessageDetail` interface)

**Interfaces:**
- Consumes: `MailHeaderDetailsReader.Parse(HeaderList)` → `MailHeaderDetails` (Task 1).
- Produces: 5 flat fields on the `Detail` endpoint's JSON — `mailingList`, `sentBy`, `signedBy`, `unsubscribeUrl` (all `string | null`), `tlsReceived` (`boolean | null`) — which Tasks 3 and 4 consume.

- [ ] **Step 1: Add the DTO properties**

In `MailMessageDetail.cs`, after the `SpamScore` property:

```csharp
    /// <summary>Expanded-header details (List-Id, envelope domain, DKIM domain, unsubscribe link, TLS). Each null when absent.</summary>
    public string? MailingList { get; set; }

    public string? SentBy { get; set; }
    public string? SignedBy { get; set; }
    public string? UnsubscribeUrl { get; set; }
    public bool? TlsReceived { get; set; }
```

- [ ] **Step 2: Wire the reader into `ImapSession.GetMessageDetailAsync`**

In `ImapSession.cs`, above the `var detail = new MailMessageDetail` line (after line 390) add:

```csharp
            var headerDetails = MailHeaderDetailsReader.Parse(message.Headers);
```

and inside the initialiser, after `SpamScore = MailSpamScoreReader.Parse(message.Headers)` add:

```csharp
                MailingList = headerDetails.MailingList,
                SentBy = headerDetails.SentBy,
                SignedBy = headerDetails.SignedBy,
                UnsubscribeUrl = headerDetails.UnsubscribeUrl,
                TlsReceived = headerDetails.TlsReceived
```

(keep the comma after the `SpamScore` line).

- [ ] **Step 3: Mirror the fields in `mailTypes.ts`**

In the `MailMessageDetail` interface, after `spamScore: MailSpamScore | null`:

```ts
  /** Expanded-header details — each null when the message carries no such header. */
  mailingList: string | null
  sentBy: string | null
  signedBy: string | null
  unsubscribeUrl: string | null
  tlsReceived: boolean | null
```

- [ ] **Step 4: Verify both sides build**

From `src/snoopy.microservice`: `dotnet test` — Expected: all PASS.
From `src/frontend`: `npx tsc -b` (or `npm run build`) — Expected: no type errors.

- [ ] **Step 5: Commit**

```bash
git add src/snoopy.microservice/Models/Mail/MailMessageDetail.cs src/snoopy.microservice/Services/ImapSession.cs src/frontend/src/modules/mail/api/mailTypes.ts
git commit -m "Expose expanded-header details on the message Detail endpoint"
```

---

### Task 3: Frontend — `ReaderDetails` grid + `LockIcon` + CSS

**Files:**
- Create: `src/frontend/src/modules/mail/reader/ReaderDetails.tsx`
- Create: `src/frontend/src/icons/LockIcon.tsx`
- Modify: `src/frontend/src/styles/mail.css` (after `.reader-recipients`, line 480)
- Test: `src/frontend/src/modules/mail/reader/ReaderDetails.test.tsx`

**Interfaces:**
- Consumes: `MailMessageDetail` (Task 2 shape), `AddressList` from `./AddressLabel` (`{ addresses: MailAddressInfo[] }`), `formatReaderDate(iso: string): string`.
- Produces: `export default function ReaderDetails({ message }: { message: MailMessageDetail })` — Task 4 mounts it in `MessageReader`.

- [ ] **Step 1: Write the failing tests**

Create `src/frontend/src/modules/mail/reader/ReaderDetails.test.tsx`:

```tsx
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import ReaderDetails from './ReaderDetails'
import type { MailMessageDetail } from '../api/mailTypes'

const message: MailMessageDetail = {
  uid: 1, folderPath: 'INBOX', uidValidity: 1,
  subject: 'News', fromName: 'Weesky News', fromAddress: 'news@weesky.net',
  to: [{ name: '', address: 'mick@weesky.be' }],
  cc: [{ name: 'Bob', address: 'bob@x.be' }],
  date: '2026-07-02T10:03:00Z',
  authentication: null, spamScore: null,
  mailingList: '<news.weesky.net>', sentBy: 'a547955.bnc3.mailjet.com', signedBy: 'weesky.net',
  unsubscribeUrl: 'https://news.weesky.net/unsub', tlsReceived: true,
  htmlBody: '', textBody: '', blockedImageCount: 0, attachments: [],
}

describe('ReaderDetails', () => {
  it('shows every row when the message carries every datum', () => {
    render(<ReaderDetails message={message} />)

    expect(screen.getByText('Weesky News')).toBeInTheDocument()
    expect(screen.getByText('<news@weesky.net>')).toBeInTheDocument()
    expect(screen.getByText('mick@weesky.be')).toBeInTheDocument()
    expect(screen.getByText('Bob')).toBeInTheDocument()
    expect(screen.getByText(/2026/)).toBeInTheDocument()
    expect(screen.getByText('<news.weesky.net>')).toBeInTheDocument()
    expect(screen.getByText('a547955.bnc3.mailjet.com')).toBeInTheDocument()
    expect(screen.getByText('weesky.net')).toBeInTheDocument()
    expect(screen.getByText(/standard encryption/i)).toBeInTheDocument()
  })

  it('drops the rows whose datum is absent, leaving no empty labels', () => {
    render(<ReaderDetails message={{
      ...message, cc: [], mailingList: null, sentBy: null, signedBy: null,
      unsubscribeUrl: null, tlsReceived: null,
    }} />)

    expect(screen.queryByText('Cc:')).not.toBeInTheDocument()
    expect(screen.queryByText('Mailing list:')).not.toBeInTheDocument()
    expect(screen.queryByText('Mailed by:')).not.toBeInTheDocument()
    expect(screen.queryByText('Signed by:')).not.toBeInTheDocument()
    expect(screen.queryByText('Unsubscribe:')).not.toBeInTheDocument()
    expect(screen.queryByText('Security:')).not.toBeInTheDocument()
  })

  it('shows the bare address alone when the sender has no display name', () => {
    render(<ReaderDetails message={{ ...message, fromName: 'news@weesky.net' }} />)

    expect(screen.getByText('news@weesky.net')).toBeInTheDocument()
    expect(screen.queryByText('<news@weesky.net>')).not.toBeInTheDocument()
  })

  it('opens an http unsubscribe link in a new tab, without a referrer', () => {
    render(<ReaderDetails message={message} />)

    const link = screen.getByRole('link', { name: /unsubscribe/i })
    expect(link).toHaveAttribute('href', 'https://news.weesky.net/unsub')
    expect(link).toHaveAttribute('target', '_blank')
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')
  })

  it('links a mailto unsubscribe in place, not in a new tab', () => {
    render(<ReaderDetails message={{ ...message, unsubscribeUrl: 'mailto:unsub@x.be' }} />)

    const link = screen.getByRole('link', { name: /unsubscribe/i })
    expect(link).toHaveAttribute('href', 'mailto:unsub@x.be')
    expect(link).not.toHaveAttribute('target')
  })

  it('says so plainly when the last hop was unencrypted', () => {
    render(<ReaderDetails message={{ ...message, tlsReceived: false }} />)

    expect(screen.getByText(/no encryption/i)).toBeInTheDocument()
    expect(screen.queryByText(/standard encryption/i)).not.toBeInTheDocument()
  })
})
```

- [ ] **Step 2: Run the tests to verify they fail**

From `src/frontend`: `npx vitest run src/modules/mail/reader/ReaderDetails.test.tsx`
Expected: FAIL — `Cannot find module './ReaderDetails'`.

- [ ] **Step 3: Write the component and icon**

Create `src/frontend/src/icons/LockIcon.tsx`:

```tsx
export default function LockIcon({ size = 11 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6">
      <rect x="3" y="7" width="10" height="7" rx="1.5" />
      <path d="M5.5 7V5a2.5 2.5 0 0 1 5 0v2" />
    </svg>
  )
}
```

Create `src/frontend/src/modules/mail/reader/ReaderDetails.tsx`:

```tsx
import type { MailMessageDetail } from '../api/mailTypes'
import LockIcon from '../../../icons/LockIcon'
import { AddressList } from './AddressLabel'
import { formatReaderDate } from './formatReaderDate'

interface Props {
  message: MailMessageDetail
}

/** The grid the header chevron expands into. A row whose datum is absent renders nothing. */
export default function ReaderDetails({ message }: Props) {
  const named = message.fromName && message.fromName !== message.fromAddress
  const isMailto = message.unsubscribeUrl?.startsWith('mailto:')

  return (
    <dl className="reader-details">
      <dt>From:</dt>
      <dd>
        {named
          ? <>{message.fromName} <span className="detail-muted">&lt;{message.fromAddress}&gt;</span></>
          : message.fromAddress}
      </dd>
      {message.to.length > 0 && <><dt>To:</dt><dd><AddressList addresses={message.to} /></dd></>}
      {message.cc.length > 0 && <><dt>Cc:</dt><dd><AddressList addresses={message.cc} /></dd></>}
      <dt>Date:</dt>
      <dd>{formatReaderDate(message.date)}</dd>
      {message.mailingList && <><dt>Mailing list:</dt><dd>{message.mailingList}</dd></>}
      {message.sentBy && <><dt>Mailed by:</dt><dd>{message.sentBy}</dd></>}
      {message.signedBy && <><dt>Signed by:</dt><dd>{message.signedBy}</dd></>}
      {message.unsubscribeUrl && (
        <>
          <dt>Unsubscribe:</dt>
          <dd>
            <a href={message.unsubscribeUrl} {...(isMailto ? {} : { target: '_blank', rel: 'noopener noreferrer' })}>
              Unsubscribe from this mailing list
            </a>
          </dd>
        </>
      )}
      {message.tlsReceived !== null && (
        <>
          <dt>Security:</dt>
          <dd>
            {message.tlsReceived
              ? <span className="reader-security"><LockIcon /> Standard encryption (TLS)</span>
              : 'No encryption'}
          </dd>
        </>
      )}
    </dl>
  )
}
```

- [ ] **Step 4: Add the styles**

In `src/frontend/src/styles/mail.css`, directly after the `.reader-recipients` rule (line 480):

```css
.details-toggle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  flex: none;
  padding: 0;
  border: 0;
  border-radius: var(--radius-sm);
  background: none;
  color: var(--text-muted);
  cursor: pointer;
}

.details-toggle:hover { color: var(--action-primary); background: var(--surface-sunken); }
.details-toggle.is-open { transform: rotate(90deg); }

.reader-details {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 5px 14px;
  margin-top: 4px;
}

.reader-details dt { text-align: right; }
.reader-details dd { margin: 0; color: var(--text); overflow-wrap: break-word; min-width: 0; }
.reader-details a { color: var(--action-primary); }
.detail-muted { color: var(--text-muted); }
.reader-security { display: inline-flex; align-items: center; gap: 5px; }
```

(`.details-toggle` belongs to Task 4's markup but ships with the rest of this block so the CSS lands once.)

- [ ] **Step 5: Run the tests to verify they pass**

From `src/frontend`: `npx vitest run src/modules/mail/reader/ReaderDetails.test.tsx`
Expected: 6 PASS.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src/modules/mail/reader/ReaderDetails.tsx src/frontend/src/modules/mail/reader/ReaderDetails.test.tsx src/frontend/src/icons/LockIcon.tsx src/frontend/src/styles/mail.css
git commit -m "Add the ReaderDetails grid for the expanded header"
```

---

### Task 4: Frontend — chevron toggle in `MessageReader`

**Files:**
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx` (imports, state at line 25-27, reset effect at line 31-35, header markup at line 70-83)
- Test: `src/frontend/src/modules/mail/reader/MessageReader.test.tsx` (fixture at line 33-43, new describe block)

**Interfaces:**
- Consumes: `ReaderDetails` (Task 3), `ChevronRightIcon` from `../../../icons/ChevronRightIcon` (`{ size?: number }`), the Task 2 fields on `data`.
- Produces: the user-facing behaviour — nothing downstream consumes this.

- [ ] **Step 1: Write the failing tests**

In `MessageReader.test.tsx`, add the new fields to the `detail` fixture (line 33-43) so it mirrors the real payload:

```tsx
  date: '2026-07-18T09:00:00Z', authentication: null,
  spamScore: null,
  mailingList: null, sentBy: null, signedBy: null, unsubscribeUrl: null, tlsReceived: null,
```

Then add this describe block before the final closing `})`:

```tsx
  describe('the expanded details', () => {
    const detailed = {
      ...detail,
      mailingList: '<news.weesky.net>',
      sentBy: 'a547955.bnc3.mailjet.com',
      signedBy: 'weesky.net',
      unsubscribeUrl: 'https://news.weesky.net/unsub',
      tlsReceived: true,
    }

    it('starts collapsed, showing exactly the compact header', async () => {
      mocks.getMailMessage.mockResolvedValue(detailed)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      await screen.findByText('Re: facture')

      expect(screen.getByRole('button', { name: 'Show details' })).toHaveAttribute('aria-expanded', 'false')
      expect(screen.getByText(/^To:/)).toBeInTheDocument()
      expect(screen.queryByText('Mailing list:')).not.toBeInTheDocument()
    })

    it('expands into the details grid, replacing the compact lines', async () => {
      mocks.getMailMessage.mockResolvedValue(detailed)

      const { container } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      fireEvent.click(await screen.findByRole('button', { name: 'Show details' }))

      expect(screen.getByText('Mailing list:')).toBeInTheDocument()
      expect(screen.getByText('a547955.bnc3.mailjet.com')).toBeInTheDocument()
      expect(screen.getByRole('link', { name: /unsubscribe/i })).toBeInTheDocument()
      expect(container.querySelector('.reader-recipients')).toBeNull()
      expect(screen.getByRole('button', { name: 'Hide details' })).toHaveAttribute('aria-expanded', 'true')
    })

    it('collapses back on a second click', async () => {
      mocks.getMailMessage.mockResolvedValue(detailed)

      render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      fireEvent.click(await screen.findByRole('button', { name: 'Show details' }))
      fireEvent.click(screen.getByRole('button', { name: 'Hide details' }))

      expect(screen.queryByText('Mailing list:')).not.toBeInTheDocument()
      expect(screen.getByText(/^To:/)).toBeInTheDocument()
    })

    // One-shot per message, like the image consent and the colour choice.
    it('resets to collapsed when another message is opened', async () => {
      mocks.getMailMessage.mockResolvedValue(detailed)

      const { rerender } = render(<MessageReader folderPath="INBOX" uid={2} />, { wrapper })
      fireEvent.click(await screen.findByRole('button', { name: 'Show details' }))

      mocks.getMailMessage.mockResolvedValue({ ...detailed, uid: 3, subject: 'Autre' })
      rerender(<MessageReader folderPath="INBOX" uid={3} />)
      await screen.findByText('Autre')

      expect(screen.getByRole('button', { name: 'Show details' })).toBeInTheDocument()
      expect(screen.queryByText('Mailing list:')).not.toBeInTheDocument()
    })
  })
```

- [ ] **Step 2: Run the tests to verify they fail**

From `src/frontend`: `npx vitest run src/modules/mail/reader/MessageReader.test.tsx`
Expected: the 4 new tests FAIL (`Unable to find role button "Show details"`); every pre-existing test still PASSES.

- [ ] **Step 3: Implement the toggle**

In `MessageReader.tsx`:

Add the imports:

```tsx
import ChevronRightIcon from '../../../icons/ChevronRightIcon'
import ReaderDetails from './ReaderDetails'
```

Add the state next to `originalColours` (line 26):

```tsx
  const [detailsOpen, setDetailsOpen] = useState(false)
```

Add the reset to the per-message effect (line 31-35):

```tsx
    setDetailsOpen(false)
```

In the header markup, append the chevron to `.reader-from` after the date span (line 74):

```tsx
              <button
                type="button"
                className={`details-toggle${detailsOpen ? ' is-open' : ''}`}
                aria-expanded={detailsOpen}
                aria-label={detailsOpen ? 'Hide details' : 'Show details'}
                onClick={() => setDetailsOpen(open => !open)}
              >
                <ChevronRightIcon size={12} />
              </button>
```

Replace the To/Cc block (lines 76-81) with:

```tsx
            {detailsOpen ? (
              <ReaderDetails message={data} />
            ) : (
              <>
                {data.to.length > 0 && (
                  <div className="reader-recipients">To: <AddressList addresses={data.to} /></div>
                )}
                {data.cc.length > 0 && (
                  <div className="reader-recipients">Cc: <AddressList addresses={data.cc} /></div>
                )}
              </>
            )}
```

(the `SpamGauge` line stays where it is, after this block.)

- [ ] **Step 4: Run the full frontend suite**

From `src/frontend`: `npx vitest run`
Expected: all PASS — the new tests and every pre-existing one.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/mail/reader/MessageReader.tsx src/frontend/src/modules/mail/reader/MessageReader.test.tsx
git commit -m "Expand the reader header into the details grid on demand"
```
