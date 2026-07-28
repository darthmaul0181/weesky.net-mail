# Contacts 3c — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A send to an unknown address creates a contact (named from the reply's headers when it can), reversible from a toast; and a contact's remote images can load without asking.

**Architecture:** The capture is client-side, in the composer, which already holds the address book, the recipients and the account's own addresses. Two pure modules carry the decisions — `captureModel.ts` (who to create, how to split a name) and `composeSeed.ts`'s new `nameHints` map (how a header name reaches send time). The backend gains only a `source` column and two preference keys.

**Tech Stack:** ASP.NET Core .NET 10, EF Core (Pomelo MySQL in prod, InMemory in tests), xUnit + Moq, `CSharpFunctionalExtensions.Result<T>` — React 18 + TypeScript, Vite, TanStack Query, Vitest + jsdom + @testing-library/react.

**Spec:** `docs/superpowers/specs/2026-07-27-webmail-contacts-3c-design.md`

## Global Constraints

- **Address identity is `canonicalAddress`** (`trim` + lower-case), the client mirror of `IdentityResolver.Canonical`. Never `fold` from `contactSearch` — `fold` also strips diacritics, which answers a *search* question and would make `josé@x.be` count as already-known because `jose@x.be` is in the book.
- **`contacts.source` is written at creation and never afterwards.** `UpdateAsync` must leave it alone.
- **The site UI is English**; code and comments are English. The user-facing strings in this plan are exact — do not reword them.
- **A token names a role, never a colour.** No literal colour anywhere in this plan's CSS.
- **A capture failure is swallowed silently; an undo failure speaks.**
- **Comments only where the code is not self-evident, 3 lines maximum.**
- **Commit messages: two lines maximum**, and must not begin or end with `@`.
- **Backend style:** file-scoped namespaces, `sealed`, primary constructors, records for DTOs, `CancellationToken` on every async path.
- **Run `dotnet test`, never `dotnet test --no-build`,** in any task that adds a test file.
- **Frontend tests sit beside what they test** (`Foo.ts` → `Foo.test.ts`).

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `docs/superpowers/webmail-contacts-tables.md` | the `ALTER TABLE` to replay on both databases | 1 |
| `src/snoopy.microservice/Data/Preferences/Contact.cs` | `Source` column mapping | 1 |
| `src/snoopy.microservice/Models/Contacts/ContactRequest.cs` | `Source` on the wire | 1 |
| `src/snoopy.microservice/Models/Contacts/ContactWrite.cs` | `Source`, validated | 1 |
| `src/snoopy.microservice/Services/ContactValidator.cs` | normalise `Source` to one of three values | 1 |
| `src/snoopy.microservice/Repositories/ContactStore.cs` | write `Source` on create, never on update | 1 |
| `src/snoopy.microservice/Models/UserPreferences.cs` | the two new preference keys | 2 |
| `src/frontend/src/lib/canonicalAddress.ts` | address identity, shared by mail and contacts | 3 |
| `src/frontend/src/modules/contacts/captureModel.ts` | who to capture, how to split a name — pure | 4 |
| `src/frontend/src/modules/mail/compose/composeSeed.ts` | `nameHints`: header names reaching send time | 5 |
| `src/frontend/src/hooks/useToasts.js` | an optional action on a toast | 6 |
| `src/frontend/src/components/Toasts.jsx` | the action button | 6 |
| `src/frontend/src/index.css` | `.toast-action` | 6 |
| `src/frontend/src/hooks/usePreferences.ts` | the two keys and their accessors | 7 |
| `src/frontend/src/modules/contacts/contactTypes.ts` | `ContactDraft.source` | 7 |
| `src/frontend/src/modules/contacts/queries.ts` | `useContacts(enabled)` | 7 |
| `src/frontend/src/modules/contacts/useCaptureContacts.ts` | create/remove that outlive the composer | 8 |
| `src/frontend/src/modules/mail/compose/ComposeView.tsx` | the wiring, the toast, the undo | 8 |
| `src/frontend/src/modules/mail/reader/MessageReader.tsx` | images of a contact | 9 |
| `src/frontend/src/modules/settings/general/GeneralPage.tsx` | the two rows | 10 |

---

### Task 1: The `source` column

**Files:**
- Modify: `docs/superpowers/webmail-contacts-tables.md`
- Modify: `src/snoopy.microservice/Data/Preferences/Contact.cs`
- Modify: `src/snoopy.microservice/Models/Contacts/ContactRequest.cs`
- Modify: `src/snoopy.microservice/Models/Contacts/ContactWrite.cs`
- Modify: `src/snoopy.microservice/Services/ContactValidator.cs`
- Modify: `src/snoopy.microservice/Repositories/ContactStore.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Services/ContactValidatorTests.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Repositories/ContactStoreTests.cs`

**Interfaces:**
- Produces: `ContactWrite(string? FirstName, string? LastName, string? Nickname, bool IsFavorite, IReadOnlyList<string> Addresses, string Source)`; `Contact.Source` (string, default `"manual"`); wire field `ContactRequest.Source` (`string?`).

- [ ] **Step 1: Add the DDL to the prerequisite document**

Append to `docs/superpowers/webmail-contacts-tables.md`, after the existing `CREATE TABLE` block:

````markdown
## Ajout de la tranche 3c

À rejouer sur les deux bases si les tables existent déjà ; les fiches présentes sont toutes des
saisies manuelles, ce que le défaut leur attribue correctement.

```sql
ALTER TABLE `contacts`
  ADD COLUMN `source` ENUM('manual','captured','imported')
    NOT NULL DEFAULT 'manual'
    COMMENT 'Origine de la fiche ; écrite à la création seulement'
    AFTER `is_favorite`;
```
````

- [ ] **Step 2: Write the failing validator tests**

Append to `ContactValidatorTests.cs`:

```csharp
[Fact]
public void Validate_DefaultsSourceToManual()
{
    var result = ContactValidator.Validate(new ContactRequest { FirstName = "Alice" });

    Assert.True(result.IsSuccess);
    Assert.Equal("manual", result.Value.Source);
}

[Theory]
[InlineData("captured")]
[InlineData("imported")]
[InlineData("manual")]
public void Validate_KeepsAKnownSource(string source)
{
    var result = ContactValidator.Validate(new ContactRequest { FirstName = "Alice", Source = source });

    Assert.True(result.IsSuccess);
    Assert.Equal(source, result.Value.Source);
}

// An unknown value is filed as manual rather than refused: the field is a hint about origin,
// and losing the contact over it would be a worse answer than mis-filing it.
[Theory]
[InlineData("CAPTURED")]
[InlineData("nonsense")]
[InlineData("")]
public void Validate_FallsBackToManualOnAnUnknownSource(string source)
{
    var result = ContactValidator.Validate(new ContactRequest { FirstName = "Alice", Source = source });

    Assert.True(result.IsSuccess);
    Assert.Equal("manual", result.Value.Source);
}
```

- [ ] **Step 3: Run them and watch them fail**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests --filter "FullyQualifiedName~ContactValidatorTests"`
Expected: FAIL — `ContactRequest` has no `Source`, `ContactWrite` has no `Source`.

- [ ] **Step 4: Add `Source` to the wire and to the validated write**

In `ContactRequest.cs`, after `Addresses`:

```csharp
    /// <summary>Where the card came from. Absent or unknown is filed as "manual".</summary>
    public string? Source { get; set; }
```

In `ContactWrite.cs`, extend the record:

```csharp
public sealed record ContactWrite(
    string? FirstName,
    string? LastName,
    string? Nickname,
    bool IsFavorite,
    IReadOnlyList<string> Addresses,
    string Source);
```

- [ ] **Step 5: Normalise `Source` in the validator**

In `ContactValidator.cs`, beside the other constants:

```csharp
    private static readonly string[] KnownSources = ["manual", "captured", "imported"];
```

and a private helper beside `Blank`:

```csharp
    private static string Source(string? raw) =>
        raw != null && KnownSources.Contains(raw, StringComparer.Ordinal) ? raw : "manual";
```

Then pass it where the successful `ContactWrite` is constructed — find the `return Result.Success(new ContactWrite(...))` at the end of `Validate` and add `Source(request.Source)` as the last argument.

- [ ] **Step 6: Run the validator tests**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests --filter "FullyQualifiedName~ContactValidatorTests"`
Expected: PASS.

- [ ] **Step 7: Write the failing store tests**

Append to `ContactStoreTests.cs`, following the fixture style already used there:

```csharp
[Fact]
public async Task Create_StoresTheSource()
{
    await using var context = NewContext();
    var store = new ContactStore(context);
    var userId = Guid.NewGuid();

    var created = await store.CreateAsync(
        userId,
        new ContactWrite("Alice", null, null, false, ["alice@x.be"], "captured"),
        CancellationToken.None);

    var row = await context.Contacts.FindAsync(created.Value);
    Assert.Equal("captured", row!.Source);
}

// The whole point of the column: editing a captured card must not make it pass for a manual one.
[Fact]
public async Task Update_LeavesTheSourceIntact()
{
    await using var context = NewContext();
    var store = new ContactStore(context);
    var userId = Guid.NewGuid();
    var created = await store.CreateAsync(
        userId,
        new ContactWrite("Alice", null, null, false, ["alice@x.be"], "captured"),
        CancellationToken.None);

    await store.UpdateAsync(
        userId,
        created.Value,
        new ContactWrite("Alice", "Dupont", null, false, ["alice@x.be"], "manual"),
        CancellationToken.None);

    var row = await context.Contacts.FindAsync(created.Value);
    Assert.Equal("captured", row!.Source);
    Assert.Equal("Dupont", row.LastName);
}
```

Every other `new ContactWrite(...)` already in this file — and in `ContactsControllerTests.cs` — now misses an argument. Add `"manual"` as the last argument to each; the compiler lists them.

- [ ] **Step 8: Run them and watch them fail**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests --filter "FullyQualifiedName~ContactStoreTests"`
Expected: FAIL — `Contact` has no `Source`.

- [ ] **Step 9: Map the column and write it on create only**

In `Contact.cs`, after `IsFavorite`:

```csharp
    /// <summary>
    /// Where the card came from. Written at creation and never afterwards: editing a captured
    /// contact must not reclassify it as one somebody typed.
    /// </summary>
    [Column("source")]
    public string Source { get; set; } = "manual";
```

In `ContactStore.CreateAsync`, inside the `new Contact { ... }` initialiser, after `IsFavorite = contact.IsFavorite,`:

```csharp
            Source = contact.Source,
```

In `ContactStore.UpdateAsync`, extend the existing "deliberately untouched" comment so the rule is written where someone would break it:

```csharp
        // Uid, VCardRaw and Source are deliberately untouched: the first is the identity a CardDAV
        // client syncs on, the second holds properties this UI cannot show and must not erase, the
        // third records an origin that editing does not change.
```

- [ ] **Step 10: Run the whole backend suite**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests`
Expected: PASS, no new build warnings.

- [ ] **Step 11: Commit**

```bash
git add docs/superpowers/webmail-contacts-tables.md src/snoopy.microservice
git commit -F - <<'EOF'
Record where a contact came from

A source column, written at creation and left alone by every update.
EOF
```

---

### Task 2: The two preference keys

**Files:**
- Modify: `src/snoopy.microservice/Models/UserPreferences.cs`
- Test: `src/snoopy.microservice/snoopy.microservice.Tests/Models/UserPreferencesTests.cs`

**Interfaces:**
- Produces: keys `contacts.captureRecipients` (default `"true"`) and `mail.trustContacts` (default `"false"`), both boolean-valued.

- [ ] **Step 1: Write the failing tests**

Append to `UserPreferencesTests.cs`:

```csharp
[Fact]
public void Effective_CapturesRecipientsByDefault()
{
    var effective = UserPreferences.Effective([]);

    Assert.Equal("true", effective["contacts.captureRecipients"]);
}

// Off by default like alwaysShowImages, and for the same reason: loading a remote image tells the
// sender the message was opened, so nothing turns that on unasked.
[Fact]
public void Effective_DoesNotTrustContactsByDefault()
{
    var effective = UserPreferences.Effective([]);

    Assert.Equal("false", effective["mail.trustContacts"]);
}

[Theory]
[InlineData("contacts.captureRecipients")]
[InlineData("mail.trustContacts")]
public void IsValid_AcceptsBooleansOnly(string key)
{
    Assert.True(UserPreferences.IsValid(key, "true"));
    Assert.True(UserPreferences.IsValid(key, "false"));
    Assert.False(UserPreferences.IsValid(key, "yes"));
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests --filter "FullyQualifiedName~UserPreferencesTests"`
Expected: FAIL — `KeyNotFoundException` on both keys.

- [ ] **Step 3: Declare the keys**

In `UserPreferences.cs`, after `MailReadingPane`:

```csharp
    // contacts., not mail.: the preference governs a write to the address book. The trigger is a
    // send; the effect is what names the key.
    public const string ContactsCaptureRecipients = "contacts.captureRecipients";

    // mail., by the same rule read the other way: the trigger is being in the book, the effect is
    // how the mail reader treats remote images.
    public const string MailTrustContacts = "mail.trustContacts";
```

and two entries at the end of the `All` collection expression:

```csharp
        new(ContactsCaptureRecipients, "true", Booleans),
        new(MailTrustContacts, "false", Booleans),
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests --filter "FullyQualifiedName~UserPreferences"`
Expected: PASS.

- [ ] **Step 5: Run the whole backend suite**

Run: `dotnet test src/snoopy.microservice/snoopy.microservice.Tests`
Expected: PASS. `PreferencesControllerTests` may assert a key count — update the expected number if it does.

- [ ] **Step 6: Commit**

```bash
git add src/snoopy.microservice
git commit -F - <<'EOF'
Declare the recipient-capture and contact-trust preferences

Capture on by default, contact image trust off, both boolean.
EOF
```

---

### Task 3: `canonicalAddress` moves to `src/lib/`

**Files:**
- Move: `src/frontend/src/modules/mail/reader/canonicalAddress.ts` → `src/frontend/src/lib/canonicalAddress.ts`
- Move: `src/frontend/src/modules/mail/reader/canonicalAddress.test.ts` → `src/frontend/src/lib/canonicalAddress.test.ts`
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx` (its only importer)

**Interfaces:**
- Produces: `canonicalAddress(address: string | null | undefined): string` importable from `src/lib/canonicalAddress`.

Mechanical: the file's contents do not change. Contacts needs the same rule, and a contacts module reaching into the mail reader for an address rule is the coupling `useAccountId` moved out of `modules/mail/queries.ts` to avoid.

- [ ] **Step 1: Move both files**

```bash
git mv src/frontend/src/modules/mail/reader/canonicalAddress.ts src/frontend/src/lib/canonicalAddress.ts
git mv src/frontend/src/modules/mail/reader/canonicalAddress.test.ts src/frontend/src/lib/canonicalAddress.test.ts
```

- [ ] **Step 2: Fix the import inside the moved test**

In `src/frontend/src/lib/canonicalAddress.test.ts` the import is already `'./canonicalAddress'` and stays correct. Confirm by reading it.

- [ ] **Step 3: Fix the one importer**

In `MessageReader.tsx`, change the `canonicalAddress` import to:

```ts
import { canonicalAddress } from '../../../lib/canonicalAddress'
```

- [ ] **Step 4: Prove nothing else referenced the old path**

Run: `cd src/frontend && npx tsc --noEmit`
Expected: no error. If any other file referenced it, fix its import too.

- [ ] **Step 5: Run the frontend suite**

Run: `cd src/frontend && npm test -- --run`
Expected: PASS, same test count as before the move.

- [ ] **Step 6: Commit**

```bash
git add -A src/frontend/src
git commit -F - <<'EOF'
Move canonicalAddress into lib

Contacts needs the same address identity rule; it does not belong to the mail reader.
EOF
```

---

### Task 4: `captureModel.ts`

**Files:**
- Create: `src/frontend/src/modules/contacts/captureModel.ts`
- Test: `src/frontend/src/modules/contacts/captureModel.test.ts`

**Interfaces:**
- Consumes: `canonicalAddress` from `src/lib/canonicalAddress`; `Contact` from `./contactTypes`.
- Produces: `CaptureCandidate { firstName: string | null; lastName: string | null; address: string }`, `splitFullName(raw: string, address: string)`, `capturable(contacts: Contact[], recipients: string[], nameHints: Record<string, string>, mine: Set<string>): CaptureCandidate[]`.

- [ ] **Step 1: Write the failing tests**

Create `src/frontend/src/modules/contacts/captureModel.test.ts`:

```ts
import { describe, expect, it } from 'vitest'
import { capturable, splitFullName } from './captureModel'
import type { Contact } from './contactTypes'

function contact(id: string, addresses: string[]): Contact {
  return { id, firstName: null, lastName: null, nickname: null, isFavorite: false, addresses }
}

describe('splitFullName', () => {
  it('splits at the last space', () => {
    expect(splitFullName('Alice Dupont', 'a@x.be'))
      .toEqual({ firstName: 'Alice', lastName: 'Dupont' })
    expect(splitFullName('Jean Pierre Dupont', 'a@x.be'))
      .toEqual({ firstName: 'Jean Pierre', lastName: 'Dupont' })
  })

  it('reads a comma as Last, First', () => {
    expect(splitFullName('Dupont, Alice', 'a@x.be'))
      .toEqual({ firstName: 'Alice', lastName: 'Dupont' })
  })

  it('files a single word as the first name', () => {
    expect(splitFullName('Alice', 'a@x.be')).toEqual({ firstName: 'Alice', lastName: null })
  })

  it('yields nothing for a blank name', () => {
    expect(splitFullName('   ', 'a@x.be')).toEqual({ firstName: null, lastName: null })
  })

  // Many clients put the address in the display name; storing it as a first name would show the
  // address twice on the tile.
  it('yields nothing when the name is the address', () => {
    expect(splitFullName('Alice@X.be', 'alice@x.be')).toEqual({ firstName: null, lastName: null })
  })

  // Over 100 the backend refuses the whole contact, so the fix has to be a truncation, not a loss.
  it('truncates each half to 100 characters', () => {
    const long = 'a'.repeat(150)
    const split = splitFullName(`${long} ${long}`, 'a@x.be')

    expect(split.firstName).toHaveLength(100)
    expect(split.lastName).toHaveLength(100)
  })
})

describe('capturable', () => {
  it('captures an address the book does not hold', () => {
    const found = capturable([contact('1', ['bob@x.be'])], ['alice@x.be'], {}, new Set())

    expect(found).toEqual([{ firstName: null, lastName: null, address: 'alice@x.be' }])
  })

  it('names the candidate from the hint', () => {
    const found = capturable([], ['alice@x.be'], { 'alice@x.be': 'Alice Dupont' }, new Set())

    expect(found).toEqual([{ firstName: 'Alice', lastName: 'Dupont', address: 'alice@x.be' }])
  })

  it('skips an address already in the book, whatever its spelling', () => {
    expect(capturable([contact('1', ['alice@x.be'])], ['  Alice@X.BE '], {}, new Set())).toEqual([])
  })

  it('skips my own addresses, whatever their spelling', () => {
    expect(capturable([], ['me@x.be'], {}, new Set(['Me@X.be']))).toEqual([])
  })

  it('captures one candidate when a send names the same address twice', () => {
    const found = capturable([], ['alice@x.be', 'ALICE@x.be'], {}, new Set())

    expect(found).toHaveLength(1)
  })

  // The frontier between canonicalAddress and contactSearch's fold: fold strips diacritics so it
  // could answer a search, and would wrongly report this address as already known.
  it('treats two addresses differing by a diacritic as two candidates', () => {
    const found = capturable([contact('1', ['jose@x.be'])], ['josé@x.be'], {}, new Set())

    expect(found).toEqual([{ firstName: null, lastName: null, address: 'josé@x.be' }])
  })

  it('drops blank entries', () => {
    expect(capturable([], ['', '   '], {}, new Set())).toEqual([])
  })

  it('keeps the order the recipients came in', () => {
    const found = capturable([], ['b@x.be', 'a@x.be'], {}, new Set())

    expect(found.map(c => c.address)).toEqual(['b@x.be', 'a@x.be'])
  })
})
```

- [ ] **Step 2: Run them and watch them fail**

Run: `cd src/frontend && npm test -- --run src/modules/contacts/captureModel.test.ts`
Expected: FAIL — cannot resolve `./captureModel`.

- [ ] **Step 3: Write the module**

Create `src/frontend/src/modules/contacts/captureModel.ts`:

```ts
import { canonicalAddress } from '../../lib/canonicalAddress'
import type { Contact } from './contactTypes'

/** The column width the backend enforces; over it, the whole contact is refused. */
const MAX_NAME_LENGTH = 100

export interface CaptureCandidate {
  firstName: string | null
  lastName: string | null
  /** Canonical: the form the backend stores anyway. */
  address: string
}

const bounded = (value: string): string | null => {
  const trimmed = value.trim().slice(0, MAX_NAME_LENGTH)
  return trimmed === '' ? null : trimmed
}

/**
 * A header display name split into the two columns a contact has. A comma means the corporate
 * "Last, First"; otherwise the last space separates given names from the family name.
 */
export function splitFullName(
  raw: string, address: string,
): { firstName: string | null; lastName: string | null } {
  const name = raw.trim()
  if (name === '' || name.toLowerCase() === canonicalAddress(address)) {
    return { firstName: null, lastName: null }
  }

  const comma = name.indexOf(',')
  if (comma >= 0) {
    return { firstName: bounded(name.slice(comma + 1)), lastName: bounded(name.slice(0, comma)) }
  }

  const space = name.lastIndexOf(' ')
  if (space < 0) return { firstName: bounded(name), lastName: null }
  return { firstName: bounded(name.slice(0, space)), lastName: bounded(name.slice(space + 1)) }
}

/**
 * Which recipients of a sent message deserve a contact. Blank entries, the account's own
 * addresses, addresses the book already holds and repeats within one send are all dropped.
 *
 * Identity is `canonicalAddress`, never contactSearch's `fold`: the question here is whether the
 * row already exists, and only the rule the backend stores under can answer it.
 */
export function capturable(
  contacts: Contact[],
  recipients: string[],
  nameHints: Record<string, string>,
  mine: Set<string>,
): CaptureCandidate[] {
  const known = new Set<string>()
  for (const contact of contacts) {
    for (const address of contact.addresses) known.add(canonicalAddress(address))
  }
  const own = new Set([...mine].map(canonicalAddress))

  const seen = new Set<string>()
  const candidates: CaptureCandidate[] = []

  for (const recipient of recipients) {
    const address = canonicalAddress(recipient)
    if (address === '' || own.has(address) || known.has(address) || seen.has(address)) continue

    seen.add(address)
    candidates.push({ ...splitFullName(nameHints[address] ?? '', address), address })
  }

  return candidates
}
```

- [ ] **Step 4: Run the tests**

Run: `cd src/frontend && npm test -- --run src/modules/contacts/captureModel.test.ts`
Expected: PASS, 14 tests.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/src/modules/contacts
git commit -F - <<'EOF'
Add the capture decision model

Pure: which recipients deserve a contact, and how a header name splits into two columns.
EOF
```

---

### Task 5: `nameHints` on the compose seed

**Files:**
- Modify: `src/frontend/src/modules/mail/compose/composeSeed.ts`
- Test: `src/frontend/src/modules/mail/compose/composeSeed.test.ts`

**Interfaces:**
- Consumes: `canonicalAddress` from `src/lib/canonicalAddress`.
- Produces: `ComposeSeed.nameHints: Record<string, string>` — canonical address → the display name the original's headers carried.

- [ ] **Step 1: Write the failing tests**

The file already has module-level fixtures: `detail(overrides)`, `prepared`, `identities`, `aliases`. Use them — do not invent new ones.

Add this describe block at the top level of `composeSeed.test.ts`:

```ts
describe('nameHints', () => {
  it('carries the sender on a reply, keyed canonically', () => {
    const seed = buildComposeSeed(
      'reply', detail({ fromName: 'Alice Dupont', fromAddress: 'Alice@Ext.example' }),
      prepared, identities, aliases, 'me@weesky.be')

    expect(seed.nameHints['alice@ext.example']).toBe('Alice Dupont')
  })

  it('carries the other recipients on a reply-all', () => {
    const seed = buildComposeSeed(
      'replyAll', detail({ cc: [{ name: 'Bob Martin', address: 'Bob@Ext.example' }] }),
      prepared, identities, aliases, 'me@weesky.be')

    expect(seed.nameHints['bob@ext.example']).toBe('Bob Martin')
  })

  it('omits an address whose header carried no name', () => {
    const seed = buildComposeSeed(
      'reply', detail({ fromName: '', to: [{ name: '', address: 'me@weesky.be' }] }),
      prepared, identities, aliases, 'me@weesky.be')

    expect(seed.nameHints).toEqual({})
  })
})
```

and this one **inside** the existing `describe('buildDraftSeed', …)` block, which is where its `opened` and `ref` fixtures live:

```ts
  // A draft keeps no headers from whatever it was a reply to, so there is nothing to carry.
  it('carries no name hints', () => {
    expect(buildDraftSeed(opened, [], ref).nameHints).toEqual({})
  })
```

- [ ] **Step 2: Run them and watch them fail**

Run: `cd src/frontend && npm test -- --run src/modules/mail/compose/composeSeed.test.ts`
Expected: FAIL — `nameHints` is undefined on the seed.

- [ ] **Step 3: Add the field and the builder**

In `composeSeed.ts`, add the import:

```ts
import { canonicalAddress } from '../../../lib/canonicalAddress'
```

Add the field to the `ComposeSeed` interface, after `references`:

```ts
  /** Canonical address → the display name the original's headers carried. Feeds contact capture
      on send; nothing renders it. */
  nameHints: Record<string, string>
```

Add the pure builder above `buildComposeSeed`:

```ts
/** Every mailbox the original names, so a reply-all captures its Cc recipients by name too. */
function nameHintsFrom(detail: MailMessageDetail): Record<string, string> {
  const hints: Record<string, string> = {}
  const mailboxes = [
    { name: detail.fromName, address: detail.fromAddress },
    ...detail.replyTo, ...detail.to, ...detail.cc, ...detail.bcc,
  ]

  for (const mailbox of mailboxes) {
    const address = canonicalAddress(mailbox.address)
    const name = (mailbox.name ?? '').trim()
    if (address !== '' && name !== '' && !(address in hints)) hints[address] = name
  }

  return hints
}
```

In `buildComposeSeed`, compute it once beside `dateText`:

```ts
  const nameHints = nameHintsFrom(detail)
```

and add `nameHints,` to each of the three returned objects (`editAsNew`, `forward`, and the reply/replyAll one).

In `buildDraftSeed`, add to the returned object:

```ts
    nameHints: {},
```

- [ ] **Step 4: Run the tests**

Run: `cd src/frontend && npm test -- --run src/modules/mail/compose/composeSeed.test.ts`
Expected: PASS.

- [ ] **Step 5: Typecheck — every ComposeSeed literal in the tests now misses a field**

Run: `cd src/frontend && npx tsc --noEmit`
Expected: errors in test files that build a `ComposeSeed` by hand (`ComposeView.test.tsx` among them). Add `nameHints: {}` to each.

- [ ] **Step 6: Run the frontend suite**

Run: `cd src/frontend && npm test -- --run`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src/modules/mail/compose
git commit -F - <<'EOF'
Carry header display names on the compose seed

A reply knows the sender's full name; the send is where capture needs it.
EOF
```

---

### Task 6: A toast that carries an action

**Files:**
- Modify: `src/frontend/src/hooks/useToasts.js`
- Modify: `src/frontend/src/components/Toasts.jsx`
- Modify: `src/frontend/src/index.css`
- Test: `src/frontend/src/components/Toasts.test.jsx` (create)

**Interfaces:**
- Produces: `addToast(message, type = 'success', action)` where `action` is `{ label: string, onClick: () => void }` or undefined; a toast carrying one is dismissed after 8000 ms instead of 3000 ms.

- [ ] **Step 1: Write the failing tests**

Create `src/frontend/src/components/Toasts.test.jsx`:

```jsx
import { act, render, renderHook, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import Toasts from './Toasts.jsx'
import { useToasts } from '../hooks/useToasts.js'

describe('Toasts', () => {
  it('renders no button when the toast carries no action', () => {
    render(<Toasts toasts={[{ id: 1, message: 'Saved', type: 'success' }]} onRemove={() => {}} />)

    expect(screen.queryByRole('button')).toBeNull()
  })

  it('runs the action and dismisses the toast on click', async () => {
    const onClick = vi.fn()
    const onRemove = vi.fn()
    render(
      <Toasts
        toasts={[{ id: 7, message: '2 contacts added', type: 'success', action: { label: 'Undo', onClick } }]}
        onRemove={onRemove}
      />)

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }))

    expect(onClick).toHaveBeenCalledTimes(1)
    expect(onRemove).toHaveBeenCalledWith(7)
  })
})

describe('useToasts', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('dismisses a plain toast after 3 seconds', () => {
    const { result } = renderHook(() => useToasts())

    act(() => result.current.addToast('Saved'))
    act(() => vi.advanceTimersByTime(3000))

    expect(result.current.toasts).toHaveLength(0)
  })

  // Long enough to read what happened and decide to undo it; 3 seconds is not.
  it('keeps a toast carrying an action for 8 seconds', () => {
    const { result } = renderHook(() => useToasts())

    act(() => result.current.addToast('2 contacts added', 'success', { label: 'Undo', onClick: () => {} }))
    act(() => vi.advanceTimersByTime(3000))
    expect(result.current.toasts).toHaveLength(1)

    act(() => vi.advanceTimersByTime(5000))
    expect(result.current.toasts).toHaveLength(0)
  })
})
```

- [ ] **Step 2: Run them and watch them fail**

Run: `cd src/frontend && npm test -- --run src/components/Toasts.test.jsx`
Expected: FAIL — no button rendered, and the action toast disappears at 3000 ms.

- [ ] **Step 3: Accept an action in the hook**

Replace the body of `src/frontend/src/hooks/useToasts.js`:

```js
import { useState, useCallback } from 'react'

const DISMISS_MS = 3000
/** An actionable toast has to be read and acted on, not just noticed. */
const DISMISS_WITH_ACTION_MS = 8000

export function useToasts() {
  const [toasts, setToasts] = useState([])

  const removeToast = useCallback((id) => {
    setToasts(prev => prev.filter(t => t.id !== id))
  }, [])

  const addToast = useCallback((message, type = 'success', action) => {
    const id = Date.now()
    setToasts(prev => [...prev, { id, message, type, action }])
    if (type !== 'error') {
      const delay = action ? DISMISS_WITH_ACTION_MS : DISMISS_MS
      setTimeout(() => setToasts(prev => prev.filter(t => t.id !== id)), delay)
    }
  }, [])

  return { toasts, addToast, removeToast }
}
```

- [ ] **Step 4: Render the button**

In `src/frontend/src/components/Toasts.jsx`, inside the toast `<div>` and before the error close button:

```jsx
          {t.action && (
            <button
              type="button"
              className="toast-action"
              onClick={() => { t.action.onClick(); onRemove(t.id) }}
            >{t.action.label}</button>
          )}
```

- [ ] **Step 5: Style it without inventing a colour**

Append to `src/frontend/src/index.css`, beside the other `.toast-*` rules:

```css
/* Inherits the toast's own colour: the toast palette is the one thing that must not fork here. */
.toast-action {
  margin-left: 12px;
  padding: 0;
  border: none;
  background: none;
  font: inherit;
  color: inherit;
  text-decoration: underline;
  cursor: pointer;
}
```

- [ ] **Step 6: Run the tests**

Run: `cd src/frontend && npm test -- --run src/components/Toasts.test.jsx`
Expected: PASS, 4 tests.

- [ ] **Step 7: Run the frontend suite**

Run: `cd src/frontend && npm test -- --run`
Expected: PASS — no existing caller passes a third argument, so every current toast is unchanged.

- [ ] **Step 8: Commit**

```bash
git add src/frontend/src
git commit -F - <<'EOF'
Let a toast carry an action

An actionable toast holds for eight seconds; the button inherits the toast's own colour.
EOF
```

---

### Task 7: Client-side preferences, draft source, and a gated book

**Files:**
- Modify: `src/frontend/src/hooks/usePreferences.ts`
- Modify: `src/frontend/src/modules/contacts/contactTypes.ts`
- Modify: `src/frontend/src/modules/contacts/queries.ts`
- Test: `src/frontend/src/hooks/usePreferences.test.ts` (append; create if absent)

**Interfaces:**
- Produces: `PREFERENCE_KEYS.captureRecipients`, `PREFERENCE_KEYS.trustContacts`, `captureRecipientsOf(preferences): boolean`, `trustContactsOf(preferences): boolean`, `ContactDraft.source?: 'captured'`, `useContacts(enabled = true)`.

- [ ] **Step 1: Write the failing accessor tests**

Append to `src/frontend/src/hooks/usePreferences.test.ts` (follow the shape of the existing `showPreviewOf` tests):

```ts
describe('captureRecipientsOf', () => {
  // On unless explicitly off, like showPreviewOf: the default is true, so an account whose row
  // has never been written must capture.
  it('is on by default and off only for an explicit false', () => {
    expect(captureRecipientsOf({})).toBe(true)
    expect(captureRecipientsOf({ 'contacts.captureRecipients': 'true' })).toBe(true)
    expect(captureRecipientsOf({ 'contacts.captureRecipients': 'false' })).toBe(false)
  })
})

describe('trustContactsOf', () => {
  // Off unless explicitly on, like alwaysShowImagesOf: a key the backend has not sent yet must
  // not load remote images.
  it('is off by default and on only for an explicit true', () => {
    expect(trustContactsOf({})).toBe(false)
    expect(trustContactsOf({ 'mail.trustContacts': 'true' })).toBe(true)
    expect(trustContactsOf({ 'mail.trustContacts': 'garbage' })).toBe(false)
  })
})
```

- [ ] **Step 2: Run them and watch them fail**

Run: `cd src/frontend && npm test -- --run src/hooks/usePreferences.test.ts`
Expected: FAIL — the accessors do not exist.

- [ ] **Step 3: Add the keys and accessors**

In `usePreferences.ts`, add to `PREFERENCE_KEYS`:

```ts
  captureRecipients: 'contacts.captureRecipients',
  trustContacts: 'mail.trustContacts',
```

and beside the other accessors:

```ts
export function captureRecipientsOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.captureRecipients] !== 'false'
}

export function trustContactsOf(preferences: Preferences): boolean {
  return preferences[PREFERENCE_KEYS.trustContacts] === 'true'
}
```

- [ ] **Step 4: Add `source` to the draft**

In `contactTypes.ts`, inside `ContactDraft`, after `addresses`:

```ts
  /** Only the capture path sets this. The editor omits it and the API files the contact as
      "manual". */
  source?: 'captured'
```

- [ ] **Step 5: Let the book be gated**

In `queries.ts`, change the signature and pass it through:

```ts
export function useContacts(enabled = true) {
  const accountId = useAccountId()

  return useQuery({
    queryKey: contactKeys.all(accountId),
    queryFn: () => api.getContacts() as Promise<ContactListResponse>,
    staleTime: 5 * 60_000,
    select: (data): Contact[] => [...data.contacts].sort(compareContacts),
    enabled,
  })
}
```

Extend the doc comment's last line: `The reader passes false when its contact-trust setting is off, so an account that never opens Contacts pays nothing.`

- [ ] **Step 6: Run the tests and typecheck**

Run: `cd src/frontend && npm test -- --run && npx tsc --noEmit`
Expected: PASS, no type error — `useContacts()` with no argument is unchanged for its existing callers.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src
git commit -F - <<'EOF'
Add the client half of the two new preferences

Plus an optional source on a contact draft and an enabled flag on the book query.
EOF
```

---

### Task 8: Capture on send

**Files:**
- Create: `src/frontend/src/modules/contacts/useCaptureContacts.ts`
- Modify: `src/frontend/src/modules/mail/compose/ComposeView.tsx`
- Test: `src/frontend/src/modules/mail/compose/ComposeView.test.tsx`

**Interfaces:**
- Consumes: `capturable`, `CaptureCandidate` (Task 4); `ComposeSeed.nameHints` (Task 5); `addToast(message, type, action)` (Task 6); `captureRecipientsOf`, `ContactDraft.source` (Task 7); `contactKeys`, `displayNameOf`.
- Produces: `useCaptureContacts(): { create(candidates: CaptureCandidate[]): Promise<Contact[]>; remove(ids: string[]): Promise<boolean> }`.

- [ ] **Step 1: Write the capture hook**

Create `src/frontend/src/modules/contacts/useCaptureContacts.ts`:

```ts
import { useQueryClient } from '@tanstack/react-query'
import { api } from '../../api.js'
import { useAccountId } from '../../hooks/useAccountId'
import type { CaptureCandidate } from './captureModel'
import { contactKeys } from './queries'
import type { Contact } from './contactTypes'

/**
 * Creating and un-creating captured contacts. Deliberately not `useMutation`: both halves are
 * started by the composer and finish after it has navigated away — the create resolves during the
 * unmount, and the undo is clicked from a toast the composer no longer owns.
 */
export function useCaptureContacts() {
  const queryClient = useQueryClient()
  const accountId = useAccountId()

  const invalidate = () =>
    queryClient.invalidateQueries({ queryKey: contactKeys.all(accountId) })

  /** Every failure is swallowed: the message is already gone, and a refusal here is not the
      user's problem. */
  async function create(candidates: CaptureCandidate[]): Promise<Contact[]> {
    const results = await Promise.allSettled(candidates.map(candidate =>
      api.createContact({
        firstName: candidate.firstName,
        lastName: candidate.lastName,
        nickname: null,
        isFavorite: false,
        addresses: [candidate.address],
        source: 'captured',
      }) as Promise<Contact>))

    const created = results.flatMap(r => r.status === 'fulfilled' ? [r.value] : [])
    if (created.length > 0) await invalidate()
    return created
  }

  /** Answers whether every deletion landed — an undo was asked for, so its failure is spoken. */
  async function remove(ids: string[]): Promise<boolean> {
    const results = await Promise.allSettled(ids.map(id => api.deleteContact(id)))
    await invalidate()
    return results.every(r => r.status === 'fulfilled')
  }

  return { create, remove }
}
```

- [ ] **Step 2: Write the failing ComposeView tests**

**The harness this file already has**, which these tests use unchanged: `renderCompose(from?, seed?)` returning `{ onNotify, router }`; `addRecipient('To' | 'Cc' | 'Bcc', address)`; `sendButton()`. There is **no toast host** here — the composer receives `onNotify` as a prop, so every toast assertion goes through that spy, including its third argument.

Two additions to the file's own mocks are required first:

- add `getPreferences: vi.fn()`, `createContact: vi.fn()` and `deleteContact: vi.fn()` to the `vi.hoisted(() => ({ … }))` object at the top, and to the `api` object inside `vi.mock('../../../api.js', …)`;
- the `beforeEach` already sets `mocks.getContacts.mockResolvedValue({ contacts: [bruno] })`, where `bruno` holds `bruno@x.be`. Keep it: it gives the "already in the book" test its subject for free.

Append this block:

```tsx
describe('capturing new recipients', () => {
  const created = {
    id: 'c1', firstName: 'Alice', lastName: 'Dupont', nickname: null,
    isFavorite: false, addresses: ['alice@x.be'],
  }

  beforeEach(() => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    mocks.getPreferences.mockResolvedValue({ 'contacts.captureRecipients': 'true' })
    mocks.createContact.mockResolvedValue(created)
    mocks.deleteContact.mockResolvedValue(undefined)
  })

  it('creates a contact for a recipient the book does not hold', async () => {
    const { onNotify } = renderCompose()

    addRecipient('To', 'alice@x.be')
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.createContact).toHaveBeenCalledWith(
      expect.objectContaining({ addresses: ['alice@x.be'], source: 'captured' })))
    await waitFor(() => expect(onNotify).toHaveBeenCalledWith(
      'Alice Dupont added to contacts', 'success', expect.objectContaining({ label: 'Undo' })))
  })

  it('creates nothing for a recipient already in the book', async () => {
    renderCompose()

    addRecipient('To', 'bruno@x.be')
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalled())
    expect(mocks.createContact).not.toHaveBeenCalled()
  })

  it('creates nothing when the preference is off', async () => {
    mocks.getPreferences.mockResolvedValue({ 'contacts.captureRecipients': 'false' })
    renderCompose()

    addRecipient('To', 'alice@x.be')
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalled())
    expect(mocks.createContact).not.toHaveBeenCalled()
  })

  // Not knowing what the book holds means duplicating all of it. The send has already succeeded,
  // so a missed capture costs nothing and a wrong one is on screen forever.
  it('creates nothing while the book is still loading', async () => {
    mocks.getContacts.mockReturnValue(new Promise(() => {}))
    renderCompose()

    addRecipient('To', 'alice@x.be')
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalled())
    expect(mocks.createContact).not.toHaveBeenCalled()
  })

  it('names the contact from the seed name hints', async () => {
    renderCompose('INBOX', {
      ...draftSeed, to: ['alice@x.be'], nameHints: { 'alice@x.be': 'Alice Dupont' },
    })

    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.createContact).toHaveBeenCalledWith(
      expect.objectContaining({ firstName: 'Alice', lastName: 'Dupont' })))
  })

  it('offers an undo that deletes exactly what it created', async () => {
    const { onNotify } = renderCompose()

    addRecipient('To', 'alice@x.be')
    fireEvent.click(sendButton())

    await waitFor(() => expect(onNotify).toHaveBeenCalledWith(
      expect.any(String), 'success', expect.objectContaining({ label: 'Undo' })))
    const action = onNotify.mock.calls.at(-1)![2] as { onClick: () => void }
    action.onClick()

    await waitFor(() => expect(mocks.deleteContact).toHaveBeenCalledWith('c1'))
    expect(mocks.deleteContact).toHaveBeenCalledTimes(1)
  })

  // The message already left; a refused address book must not turn that into an error on screen.
  it('says nothing when a capture is refused', async () => {
    mocks.createContact.mockRejectedValue(new Error('at the ceiling'))
    const { onNotify } = renderCompose()

    addRecipient('To', 'alice@x.be')
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.createContact).toHaveBeenCalled())
    expect(onNotify).not.toHaveBeenCalledWith(
      expect.anything(), 'error', expect.anything())
    expect(onNotify).not.toHaveBeenCalledWith(
      expect.anything(), expect.anything(), expect.objectContaining({ label: 'Undo' }))
  })
})
```

`draftSeed` is the seed fixture the file already builds for its draft tests. If its shape does not accept a bare `to` override, build a `ComposeSeed` literal instead — every field is required, so copy the one `draftSeed` uses and change `to` and `nameHints`. Do not weaken an assertion to make a test pass.

- [ ] **Step 3: Run them and watch them fail**

Run: `cd src/frontend && npm test -- --run src/modules/mail/compose/ComposeView.test.tsx`
Expected: FAIL — `createContact` never called.

- [ ] **Step 4: Wire it into `ComposeView`**

Add imports:

```ts
import { capturable } from '../../contacts/captureModel'
import { useCaptureContacts } from '../../contacts/useCaptureContacts'
import { displayNameOf } from '../../contacts/contactName'
import { captureRecipientsOf, usePreferences } from '../../../hooks/usePreferences'
```

Beside the existing hooks:

```ts
  const { data: preferences } = usePreferences()
  const capture = useCaptureContacts()
```

Above `submit`, the account's own addresses and the capture itself:

```ts
  // A stale identity is still an address that was yours. A live alias carrying no identity is not
  // in this set and would be captured; the undo covers that rather than a second query.
  const mine = useMemo(() => new Set([
    ...(identity?.email ? [identity.email] : []),
    ...(identityList ?? []).map(i => i.address),
  ]), [identity?.email, identityList])

  function captureNewRecipients() {
    if (!preferences || !captureRecipientsOf(preferences) || !contacts) return

    const candidates = capturable(contacts, [...to, ...cc, ...bcc], seed?.nameHints ?? {}, mine)
    if (candidates.length === 0) return

    void capture.create(candidates).then(created => {
      if (created.length === 0) return
      const message = created.length === 1
        ? `${displayNameOf(created[0])} added to contacts`
        : `${created.length} contacts added`
      onNotify(message, 'success', {
        label: 'Undo',
        onClick: () => void capture.remove(created.map(c => c.id))
          .then(ok => { if (!ok) onNotify('Could not undo', 'error') }),
      })
    })
  }
```

In `submit`'s `onSuccess`, between the draft deletion and `leave()`:

```ts
        captureNewRecipients()
        leave()
```

- [ ] **Step 5: Run the tests**

Run: `cd src/frontend && npm test -- --run src/modules/mail/compose/ComposeView.test.tsx`
Expected: PASS.

- [ ] **Step 6: Run the frontend suite and typecheck**

Run: `cd src/frontend && npm test -- --run && npx tsc --noEmit && npm run lint`
Expected: PASS, no lint error — the deploy workflow lints on push, and an unused binding is an ESLint *error* here.

- [ ] **Step 7: Commit**

```bash
git add src/frontend/src
git commit -F - <<'EOF'
Capture new recipients when a message is sent

Silent creation with an undo toast; a refused capture never reaches the screen.
EOF
```

---

### Task 9: A contact's images load without asking

**Files:**
- Modify: `src/frontend/src/modules/mail/reader/MessageReader.tsx`
- Test: `src/frontend/src/modules/mail/reader/MessageReader.test.tsx`

**Interfaces:**
- Consumes: `trustContactsOf` (Task 7), `useContacts(enabled)` (Task 7), `canonicalAddress` from `src/lib/canonicalAddress` (Task 3).

- [ ] **Step 1: Write the failing tests**

Two additions to the file's own harness first:

- add `getContacts: vi.fn()` to the `vi.hoisted(() => ({ … }))` object and to the `api` object inside its `vi.mock`. Without it `useContacts` has nothing to call and every assertion below passes for the wrong reason.
- extend the existing `renderWithTrusted(addresses, preferences?, onNotify?)` helper with a fourth parameter that **seeds** the book, mirroring exactly what it already does for the trusted list and for the reason its own comment gives — an absent banner because a query is in flight is indistinguishable from an absent banner because the gate works:

```tsx
function renderWithTrusted(
  addresses: string[], preferences?: Record<string, string>,
  onNotify?: (message: string) => void, contacts?: Contact[],
) {
  const client = makeClient()
  client.setQueryData(['mail', 'primary', 'trustedSenders'], addresses)
  if (preferences) client.setQueryData(['preferences'], preferences)
  if (contacts) client.setQueryData(['contacts', 'primary'], { contacts })
  // …unchanged render…
}
```

The message the suite renders is uid 2; whatever `fromAddress` its fixture carries is the address these tests must put in the book. Read the fixture and use its real value rather than inventing `alice@x.be`. Below, `SENDER` stands for it.

```tsx
describe('images of a contact', () => {
  const inBook = [{
    id: 'c1', firstName: 'Alice', lastName: null, nickname: null,
    isFavorite: false, addresses: [SENDER.toUpperCase()],
  }]

  it('shows their images when the setting is on', async () => {
    renderWithTrusted([], { 'mail.trustContacts': 'true' }, undefined, inBook)

    await waitFor(() => expect(screen.queryByText(/blocked/)).toBeNull())
  })

  it('blocks the same sender when the setting is off', async () => {
    renderWithTrusted([], { 'mail.trustContacts': 'false' }, undefined, inBook)

    expect(await screen.findByText(/blocked/)).toBeInTheDocument()
  })

  it('blocks a sender the book does not hold', async () => {
    renderWithTrusted([], { 'mail.trustContacts': 'true' }, undefined, [])

    expect(await screen.findByText(/blocked/)).toBeInTheDocument()
  })

  // Revoking changes nothing on screen while the book is trusting, and the reader already
  // withholds this entry whenever something else is doing the trusting.
  it("offers no \"Block sender's images\" for a sender trusted only by the book", async () => {
    renderWithTrusted([], { 'mail.trustContacts': 'true' }, undefined, inBook)
    fireEvent.click(await screen.findByRole('button', { name: 'More actions' }))

    expect(screen.queryByText("Block sender's images")).toBeNull()
  })

  it('still offers it for an explicitly approved sender', async () => {
    renderWithTrusted([SENDER], { 'mail.trustContacts': 'false' }, undefined, [])
    fireEvent.click(await screen.findByRole('button', { name: 'More actions' }))

    expect(await screen.findByText("Block sender's images")).toBeInTheDocument()
  })

  it('does not fetch the book when the setting is off', async () => {
    renderWithTrusted([], { 'mail.trustContacts': 'false' })

    expect(await screen.findByText(/blocked/)).toBeInTheDocument()
    expect(mocks.getContacts).not.toHaveBeenCalled()
  })
})
```

The last test seeds no book on purpose: `enabled: false` stops the fetch, not a cache read, so seeding one would prove nothing. Use the kebab's real accessible name from the file rather than the `'More actions'` written here if they differ.

- [ ] **Step 2: Run them and watch them fail**

Run: `cd src/frontend && npm test -- --run src/modules/mail/reader/MessageReader.test.tsx`
Expected: FAIL — the banner shows for a contact.

- [ ] **Step 3: Split the two booleans and add the book**

Add imports:

```ts
import { useContacts } from '../../contacts/queries'
import { alwaysShowImagesOf, showSpamScoreOf, trustContactsOf, usePreferences } from '../../../hooks/usePreferences'
```

Beside the other queries:

```ts
  const trustContacts = !!preferences && trustContactsOf(preferences)
  const { data: contacts } = useContacts(trustContacts)
```

Replace the trust derivation (currently one `senderTrusted` line) with:

```ts
  // Two booleans, not one: senderApproved is the explicit list and is what the revoke entry acts
  // on, while contactTrusted is computed and has nothing to revoke.
  const senderApproved = senderAddress !== '' && trustedSenders?.has(senderAddress) === true
  const contactTrusted = trustContacts && senderAddress !== ''
    && (contacts ?? []).some(c => c.addresses.some(a => canonicalAddress(a) === senderAddress))
  const alwaysShow = !!preferences && alwaysShowImagesOf(preferences)
  const showImages = imagesShown || alwaysShow || senderApproved || contactTrusted
```

Extend the guard on the revoke entry — it currently reads `if (senderTrusted && !alwaysShow)`:

```ts
  // Only for an approved sender, and only while nothing else is already showing the images: with
  // the global setting or the book doing it, revoking changes nothing on screen, and an entry
  // whose effect is invisible misleads.
  if (senderApproved && !alwaysShow && !contactTrusted) {
```

- [ ] **Step 4: Run the tests**

Run: `cd src/frontend && npm test -- --run src/modules/mail/reader/MessageReader.test.tsx`
Expected: PASS.

- [ ] **Step 5: Run the frontend suite, typecheck and lint**

Run: `cd src/frontend && npm test -- --run && npx tsc --noEmit && npm run lint`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src
git commit -F - <<'EOF'
Load a contact's remote images without asking

Computed, not stored: no trusted_senders row, and the revoke entry stays out when the book is trusting.
EOF
```

---

### Task 10: The two rows in General

**Files:**
- Modify: `src/frontend/src/modules/settings/general/GeneralPage.tsx`
- Test: `src/frontend/src/modules/settings/general/GeneralPage.test.tsx`

**Interfaces:**
- Consumes: `PREFERENCE_KEYS.captureRecipients`, `PREFERENCE_KEYS.trustContacts`, `captureRecipientsOf`, `trustContactsOf` (Task 7).

- [ ] **Step 1: Write the failing tests**

The file's helper is `renderPage(preferences)`, which mocks `getPreferences`/`setPreference` and renders through the `wrapper`. The existing toggle tests use `fireEvent.click`, not `userEvent` — follow them.

```tsx
it('saves the capture preference', async () => {
  renderPage({ 'contacts.captureRecipients': 'true' })

  fireEvent.click(await screen.findByLabelText('Save new recipients to my contacts'))

  await waitFor(() => expect(mocks.setPreference)
    .toHaveBeenCalledWith('contacts.captureRecipients', 'false'))
})

it('saves the contact-images preference', async () => {
  renderPage({ 'mail.trustContacts': 'false' })

  fireEvent.click(await screen.findByLabelText('Always show images from my contacts'))

  await waitFor(() => expect(mocks.setPreference)
    .toHaveBeenCalledWith('mail.trustContacts', 'true'))
})

// It shipped disabled under "Available once Contacts ships". Contacts has shipped.
it('no longer says the contacts setting is unavailable', async () => {
  renderPage({})

  expect(await screen.findByLabelText('Always show images from my contacts')).toBeEnabled()
  expect(screen.queryByText('Available once Contacts ships.')).toBeNull()
})
```

- [ ] **Step 2: Run them and watch them fail**

Run: `cd src/frontend && npm test -- --run src/modules/settings/general/GeneralPage.test.tsx`
Expected: FAIL — no such labels; the old disabled row is still there.

- [ ] **Step 3: Replace the placeholder row and add the capture row**

In `GeneralPage.tsx`, add to the `usePreferences` import list: `captureRecipientsOf`, `trustContactsOf`.

Delete the whole placeholder block — the comment beginning `{/* Disabled until Contacts exists.`, the `<ToggleRow id="trust-contacts" …>` and the `<p className="settings-note">Available once Contacts ships.</p>` — and put in its place:

```tsx
          <ToggleRow
            id="trust-contacts"
            label="Always show images from my contacts"
            checked={trustContactsOf(preferences)}
            disabled={setPreference.isPending}
            onChange={on => save(PREFERENCE_KEYS.trustContacts, String(on),
              on ? "Images from your contacts will load" : "Images from your contacts stay blocked")}
          />

          <ToggleRow
            id="capture-recipients"
            label="Save new recipients to my contacts"
            checked={captureRecipientsOf(preferences)}
            disabled={setPreference.isPending}
            onChange={on => save(PREFERENCE_KEYS.captureRecipients, String(on),
              on ? 'New recipients will be saved' : 'New recipients will not be saved')}
          />
```

The label changed from "Trust my contacts": that phrasing never said what trusting produced, and it sits directly under "Always show remote images", which does.

- [ ] **Step 4: Run the tests**

Run: `cd src/frontend && npm test -- --run src/modules/settings/general/GeneralPage.test.tsx`
Expected: PASS.

- [ ] **Step 5: Run everything**

Run: `cd src/frontend && npm test -- --run && npx tsc --noEmit && npm run lint && npm run build`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/frontend/src
git commit -F - <<'EOF'
Add the two contacts settings to General

The contacts image toggle goes live and is renamed after its effect; capture gets its own row.
EOF
```

---

## Verification in a real browser

jsdom computes no layout and runs no MariaDB. Two defect classes in this plan are invisible to every test above, and both have bitten this module before: an undeclared EF relationship is not a compile error and the InMemory provider enforces no foreign keys, and a new toast button's geometry is not something a DOM assertion can see.

After deploying to `account-dev`, check by hand:

1. **Send to a brand-new address.** The contact appears in `/contacts`; the toast reads its name and carries `Undo`.
2. **Click `Undo`.** The contact disappears from `/contacts`.
3. **Reply to a message from someone unknown whose header carries a full name.** The created contact holds first and last name, not just the address.
4. **Send to three unknown addresses at once.** One toast, `3 contacts added`, one `Undo` removing all three.
5. **Turn the capture setting off, send again.** Nothing is created, nothing is said.
6. **Turn "Always show images from my contacts" on**, open a message from a contact carrying remote images: they load, no banner, and the kebab offers no "Block sender's images".
7. **Check the toast button's geometry in both themes** and in both light and dark palettes — it inherits the toast colour, so it must stay legible on the success and the error background alike.

Run the `ALTER TABLE` on `snoopy_webmail` and `snoopy_webmail_dev` **before** deploying the backend.
