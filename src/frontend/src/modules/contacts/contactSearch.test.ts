import { describe, expect, it } from 'vitest'
import {
  compareContacts, filterContacts, fold, groupOptionsOf, matches, suggestionsFor,
} from './contactSearch'
import type { GroupOption } from './contactSearch'
import type { Contact } from './contactTypes'
import type { ContactGroup } from './contactGroupTypes'

function contact(fields: Partial<Contact> & { id: string }): Contact {
  return {
    firstName: null, lastName: null, nickname: null, isFavorite: false, addresses: [], ...fields,
  }
}

const bruno = contact({
  id: 'b', firstName: 'Bruno', lastName: 'Mertens', nickname: 'bru',
  addresses: ['bruno@example.com', 'b.mertens@wk.be'],
})
const chloe = contact({
  id: 'c', firstName: 'Chloé', lastName: 'Vermeulen', addresses: ['chloe@example.com'],
})
const alice = contact({
  id: 'a', firstName: 'Alice', lastName: 'Dupont', isFavorite: true,
  addresses: ['alice@example.com'],
})

describe('fold', () => {
  it('strips diacritics and lowercases', () => {
    expect(fold('Chloé VERMEULEN')).toBe('chloe vermeulen')
  })

  it('leaves plain text alone', () => {
    expect(fold('bruno')).toBe('bruno')
  })

  // \p{Diacritic} also covers ASCII '^' and '`', so stripping that class would delete a caret
  // from plain text — \p{M} (combining marks) is the narrower, correct class.
  it('does not strip ASCII characters that merely look like diacritics', () => {
    expect(fold('a^b`c')).toBe('a^b`c')
  })
})

describe('matches', () => {
  /* The only name on screen for a contact carrying one, so a search that skipped it would fail to
     find a contact by the very word the list shows. */
  it('matches on the display name', () => {
    expect(matches(contact({ id: 'r', firstName: 'Raphaël', lastName: 'Le Châtelier',
      displayName: 'Dr. Le Châtelier Jr.' }), 'jr')).toBe(true)
  })

  it('matches on the first name', () => {
    expect(matches(bruno, 'bru')).toBe(true)
  })

  it('matches on the last name', () => {
    expect(matches(bruno, 'mert')).toBe(true)
  })

  // A needle no other field carries: 'bru' prefixes the first name too, so it would match with
  // the nickname left out of the searched fields entirely.
  it('matches on the nickname', () => {
    expect(matches(contact({ id: 'n', firstName: 'Bruno', nickname: 'chef' }), 'chef')).toBe(true)
  })

  it('matches on any address, not only the primary', () => {
    expect(matches(bruno, 'wk.be')).toBe(true)
  })

  // Typing without accents has to find an accented contact: nobody reaches for the é key to
  // look somebody up. Neither fixture carries an address — chloe@example.com spells the name
  // unaccented, so it would answer both queries with the folding stripping nothing at all.
  it('ignores accents in both directions', () => {
    expect(matches(contact({ id: 'x', firstName: 'Chloé' }), 'chloe')).toBe(true)
    expect(matches(contact({ id: 'y', firstName: 'Chloe' }), 'chloé')).toBe(true)
  })

  it('ignores case', () => {
    expect(matches(bruno, 'BRUNO')).toBe(true)
  })

  it('matches anywhere in the field, not just at the start', () => {
    expect(matches(bruno, 'ertens')).toBe(true)
  })

  it('does not match unrelated text', () => {
    expect(matches(bruno, 'zzz')).toBe(false)
  })

  it('matches everything on an empty query', () => {
    expect(matches(bruno, '   ')).toBe(true)
  })

  // Regression: an over-wide diacritic class stripped '^' from the query, so 'a^b' folded down
  // to 'ab' and wrongly matched 'ab@x.com'.
  it('does not fold away a caret and match a caret-free address', () => {
    expect(matches(contact({ id: 'z3', firstName: 'Zoot', addresses: ['ab@x.com'] }), 'a^b')).toBe(false)
  })
})

describe('filterContacts', () => {
  it('keeps only the matching contacts', () => {
    expect(filterContacts([bruno, chloe, alice], 'chlo').map(c => c.id)).toEqual(['c'])
  })

  it('returns everything on an empty query', () => {
    expect(filterContacts([bruno, chloe, alice], '')).toHaveLength(3)
  })
})

describe('compareContacts', () => {
  // The favourite is the one that sorts last by name: with Alice against Bruno the expected order
  // is also the alphabetical one, so a comparator ignoring the flag would pass.
  it('puts favourites first', () => {
    const zoe = contact({ id: 'z', firstName: 'Zoé', isFavorite: true })

    expect([bruno, zoe].sort(compareContacts).map(c => c.id)).toEqual(['z', 'b'])
  })

  it('sorts the rest by display name', () => {
    expect([chloe, bruno].sort(compareContacts).map(c => c.id)).toEqual(['b', 'c'])
  })

  // A codepoint sort files every accented name after Z, and a case-sensitive one exiles
  // 'e-commerce' past every capitalised name. localeCompare with base sensitivity is what the
  // folder list already uses.
  it('files an accented name where a reader expects it', () => {
    const eric = contact({ id: 'e', firstName: 'Éric' })
    const frank = contact({ id: 'f', firstName: 'Frank' })
    const dora = contact({ id: 'd', firstName: 'Dora' })

    expect([frank, eric, dora].sort(compareContacts).map(c => c.id)).toEqual(['d', 'e', 'f'])
  })
})

/** The rows of a call that hands in no group, narrowed to the address rows they can only be. */
function addressesFor(...args: Parameters<typeof suggestionsFor>) {
  return suggestionsFor(...args).map(row => {
    if (row.kind !== 'address') throw new Error(`unexpected group row: ${row.name}`)
    return row
  })
}

describe('suggestionsFor', () => {
  it('answers one row per address, not per contact', () => {
    const rows = addressesFor([bruno], 'bru')

    expect(rows.map(r => r.address)).toEqual(['bruno@example.com', 'b.mertens@wk.be'])
  })

  it('names each row with its contact', () => {
    expect(addressesFor([chloe], 'chlo')[0].names).toEqual(['Chloé Vermeulen'])
  })

  // The decision to allow a shared address lands here: one row, every owner named. Two rows would
  // produce the identical recipient, and picking one name would be an arbitrary arbitration.
  it('collapses an address shared by two contacts into one row naming both', () => {
    const shared = 'info@example.com'
    const first = contact({ id: '1', firstName: 'Alice', lastName: 'Dupont', addresses: [shared] })
    const second = contact({ id: '2', firstName: 'Compta', lastName: 'Weesky', addresses: [shared] })

    const rows = addressesFor([first, second], 'info')

    expect(rows).toHaveLength(1)
    expect(rows[0].address).toBe(shared)
    expect(rows[0].names).toEqual(['Alice Dupont', 'Compta Weesky'])
  })

  // A nameless card names nobody: printing its address as its own name put the same string twice
  // on one row, and beside a real contact's row it read as a second person.
  it('leaves a row unnamed when the contact carries no name of its own', () => {
    const shadow = contact({ id: 's', nickname: 'ghost@example.com', addresses: ['ghost@example.com'] })

    expect(addressesFor([shadow], 'ghost')[0].names).toEqual([])
  })

  it('names a shared address after the contact that has a name', () => {
    const shared = 'info@example.com'
    const shadow = contact({ id: 's', nickname: shared, addresses: [shared] })
    const named = contact({ id: 'n', firstName: 'Compta', lastName: 'Weesky', addresses: [shared] })

    expect(addressesFor([shadow, named], 'info')[0].names).toEqual(['Compta Weesky'])
  })

  // Same mailbox, different case: two rows would be the identical bug the shared-address test
  // above rules out, just reached through case rather than through two separate contacts.
  it('collapses an address shared with a different case into one row naming both', () => {
    const first = contact({ id: '1', firstName: 'Alice', lastName: 'Dupont', addresses: ['Info@Example.com'] })
    const second = contact({ id: '2', firstName: 'Compta', lastName: 'Weesky', addresses: ['info@example.com'] })

    const rows = addressesFor([first, second], 'info')

    expect(rows).toHaveLength(1)
    expect(rows[0].address).toBe('Info@Example.com')
    expect(rows[0].names).toEqual(['Alice Dupont', 'Compta Weesky'])
  })

  // Task 14 builds `exclude` from what the user typed into the field, where case is free — an
  // already-present token in another case must still keep its address out of the dropdown.
  it('excludes an address regardless of the case it is spelled in', () => {
    const emma = contact({ id: 'e2', firstName: 'Emma', addresses: ['emma@example.com'] })

    const rows = addressesFor([emma], 'emma', { exclude: new Set(['EMMA@EXAMPLE.COM']) })

    expect(rows).toEqual([])
  })

  // suggestionsFor's own matches() filter carries no coverage without a contact in the input that
  // must not come back out: the 26 baseline tests all pass even with that filter deleted.
  it('leaves out a contact unrelated to the query', () => {
    const unrelated = contact({ id: 'u', firstName: 'Zach', lastName: 'Stranger', addresses: ['zach@example.com'] })

    const rows = addressesFor([bruno, unrelated], 'bru')

    expect(rows.map(r => r.address)).toEqual(['bruno@example.com', 'b.mertens@wk.be'])
  })

  // Both rows tie on favourite (neither) and on primary (a lone address is always its own
  // contact's primary), so only the address key can decide — Zack's name sorts first, its address
  // does not, so a comparator that drops the address key would leave insertion order untouched
  // and answer zzz before aaa.
  it('breaks a tie on the first two sort keys alphabetically by address', () => {
    const zack = contact({ id: 'z2', firstName: 'Zack', addresses: ['aaa@example.com'] })
    const amy = contact({ id: 'am', firstName: 'Amy', addresses: ['zzz@example.com'] })

    const rows = addressesFor([zack, amy], 'example')

    expect(rows.map(r => r.address)).toEqual(['aaa@example.com', 'zzz@example.com'])
  })

  // The favourite rule outranks the primary rule: a favourite's secondary address (favourite,
  // not primary) must still beat a non-favourite's primary address (primary, not favourite) — the
  // opposite order would come out if the two keys were compared in the other order.
  it('puts a favourite’s secondary address before a non-favourite’s primary address', () => {
    const favorite = contact({
      id: 'fav', firstName: 'Nora', isFavorite: true,
      addresses: ['nora@example.com', 'nora.secondary@example.com'],
    })
    const plain = contact({ id: 'plain', firstName: 'Oscar', addresses: ['oscar@example.com'] })

    const rows = addressesFor([favorite, plain], 'example')
    const secondary = rows.findIndex(r => r.address === 'nora.secondary@example.com')
    const primary = rows.findIndex(r => r.address === 'oscar@example.com')

    expect(secondary).toBeLessThan(primary)
  })

  // The favourite's address sorts last alphabetically and is nobody's primary but its own, so it
  // reaches the top on the favourite rule alone — alice@example.com would have won the address
  // tiebreak without it.
  it('puts a favourite contact’s address first', () => {
    const zoe = contact({
      id: 'z', firstName: 'Zoé', isFavorite: true, addresses: ['zoe@example.com'],
    })

    const rows = addressesFor([bruno, zoe], 'e')

    expect(rows[0].address).toBe('zoe@example.com')
  })

  it('puts a primary address before a secondary one', () => {
    const rows = addressesFor([bruno], 'e')
    const primary = rows.findIndex(r => r.address === 'bruno@example.com')
    const secondary = rows.findIndex(r => r.address === 'b.mertens@wk.be')

    expect(primary).toBeLessThan(secondary)
  })

  it('finds a contact by name and offers its addresses', () => {
    expect(addressesFor([chloe], 'vermeulen').map(r => r.address)).toEqual(['chloe@example.com'])
  })

  // An excluded address must not eat a slot, or a field with nine tokens would show one option.
  it('drops excluded addresses without spending the cap', () => {
    const many = Array.from({ length: 12 }, (_, i) =>
      contact({ id: `c${i}`, firstName: `C${i}`, addresses: [`c${i}@example.com`] }))

    const rows = addressesFor(many, 'example', { exclude: new Set(['c0@example.com']), limit: 10 })

    expect(rows).toHaveLength(10)
    expect(rows.map(r => r.address)).not.toContain('c0@example.com')
  })

  it('caps the list at ten rows by default', () => {
    const many = Array.from({ length: 30 }, (_, i) =>
      contact({ id: `c${i}`, firstName: `C${i}`, addresses: [`c${i}@example.com`] }))

    expect(addressesFor(many, 'example')).toHaveLength(10)
  })

  it('answers nothing on an empty query', () => {
    expect(addressesFor([bruno], '   ')).toEqual([])
  })

  it('ignores a contact carrying no address', () => {
    expect(addressesFor([contact({ id: 'n', firstName: 'Nobody' })], 'nobody')).toEqual([])
  })
})

describe('groupOptionsOf', () => {
  const group = (fields: Partial<ContactGroup> & { id: string }): ContactGroup =>
    ({ name: 'Team', memberIds: [], ...fields })

  it('resolves every member to its primary address, in member order', () => {
    const rows = groupOptionsOf([group({ id: 'g', memberIds: ['b', 'a'] })], [bruno, alice])

    expect(rows).toEqual([{
      id: 'g', name: 'Team', memberCount: 2,
      addresses: ['bruno@example.com', 'alice@example.com'],
    }])
  })

  // Two members sharing a mailbox spelled two ways would otherwise put the identical recipient in
  // the field twice — the rule suggestionsFor already applies to its own rows.
  it('deduplicates two members carrying one address spelled differently', () => {
    const first = contact({ id: '1', firstName: 'Alice', addresses: ['Info@Example.com'] })
    const second = contact({ id: '2', firstName: 'Compta', addresses: ['info@example.com'] })

    const [option] = groupOptionsOf([group({ id: 'g', memberIds: ['1', '2'] })], [first, second])

    expect(option.addresses).toEqual(['Info@Example.com'])
  })

  // The count is the membership, never what writing would reach: a group of three whose members
  // carry no address is still a group of three, and the row says so.
  it('counts every member while offering only the addresses it resolves', () => {
    const nameless = contact({ id: 'n', firstName: 'Nobody' })

    const [option] = groupOptionsOf(
      [group({ id: 'g', memberIds: ['n', 'gone', 'a'] })], [nameless, alice])

    expect(option.memberCount).toBe(3)
    expect(option.addresses).toEqual(['alice@example.com'])
  })

  it('answers a row per group, empty addresses included', () => {
    const rows = groupOptionsOf([group({ id: 'g', name: 'Empty' })], [alice])

    expect(rows).toEqual([{ id: 'g', name: 'Empty', memberCount: 0, addresses: [] }])
  })

  // Address identity is canonicalAddress (trim + lowercase), not fold: 'josé@x.com' and
  // 'jose@x.com' are two distinct SMTPUTF8 mailboxes, and fold's diacritic-stripping would
  // otherwise collapse them into one — a member who never receives the mail.
  it('keeps two mailboxes that differ only by a diacritic', () => {
    const jose = contact({ id: 'j1', firstName: 'José', addresses: ['josé@x.com'] })
    const joseAscii = contact({ id: 'j2', firstName: 'Jose', addresses: ['jose@x.com'] })

    const [option] = groupOptionsOf(
      [group({ id: 'g', memberIds: ['j1', 'j2'] })], [jose, joseAscii])

    expect(option.addresses).toEqual(['josé@x.com', 'jose@x.com'])
  })
})

describe('suggestionsFor — group rows', () => {
  const team: GroupOption = {
    id: 'g1', name: 'Team', memberCount: 2,
    addresses: ['alice@example.com', 'bruno@example.com'],
  }

  // 'te' matches Mertens as well, so the group is ranged against real address rows rather than
  // being the only thing in the list.
  it('ranges a matching group ahead of the addresses, carrying its member count', () => {
    const rows = suggestionsFor([bruno], 'te', { groups: [team] })

    expect(rows[0]).toEqual({ kind: 'group', ...team })
    expect(rows.slice(1).map(row => row.kind)).toEqual(['address', 'address'])
  })

  it('matches the group name the way it matches a contact — folded', () => {
    const accented: GroupOption = { ...team, name: 'Équipe' }

    expect(suggestionsFor([], 'equipe', { groups: [accented] })).toHaveLength(1)
  })

  it('leaves out a group the query does not name', () => {
    expect(suggestionsFor([], 'zzz', { groups: [team] })).toEqual([])
  })

  // The three group slots and the ten address slots are two budgets (decision 15): a matched
  // group must never cost the field one of the addresses it would otherwise have offered.
  it('caps the groups at three without spending an address slot', () => {
    const groups: GroupOption[] = Array.from({ length: 4 }, (_, i) => ({
      id: `g${i}`, name: `Example ${i}`, memberCount: 1, addresses: [`g${i}@example.com`],
    }))
    const many = Array.from({ length: 12 }, (_, i) =>
      contact({ id: `c${i}`, firstName: `C${i}`, addresses: [`c${i}@example.com`] }))

    const rows = suggestionsFor(many, 'example', { groups })

    expect(rows.filter(row => row.kind === 'group')).toHaveLength(3)
    expect(rows.filter(row => row.kind === 'address')).toHaveLength(10)
  })

  // Picking it could only add nothing, and the field answers "nothing to add" with a toast — so
  // the row would be an offer whose every outcome is an error message.
  it('drops a group whose every address is already a token', () => {
    const rows = suggestionsFor([], 'team', {
      groups: [team], exclude: new Set(['ALICE@EXAMPLE.COM', 'bruno@example.com']),
    })

    expect(rows).toEqual([])
  })

  it('keeps a group one of whose addresses is still free', () => {
    const rows = suggestionsFor([], 'team', {
      groups: [team], exclude: new Set(['alice@example.com']),
    })

    expect(rows).toEqual([{ kind: 'group', ...team }])
  })

  // The one group with nothing to offer that still appears: a group nobody in the book resolves
  // is a state the user has to be told about, and the field's toast is where that is said.
  it('offers a group carrying no address at all', () => {
    const empty: GroupOption = { id: 'g2', name: 'Team', memberCount: 0, addresses: [] }

    expect(suggestionsFor([], 'team', { groups: [empty] })).toEqual([{ kind: 'group', ...empty }])
  })

  it('answers nothing on an empty query, groups included', () => {
    expect(suggestionsFor([bruno], '   ', { groups: [team] })).toEqual([])
  })
})
