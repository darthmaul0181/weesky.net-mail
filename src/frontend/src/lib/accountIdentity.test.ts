import { describe, it, expect } from 'vitest'
import { deriveIdentity } from './accountIdentity'

describe('deriveIdentity', () => {
  const account = {
    userName: 'mick',
    mailbox: 'WSY',
    fullName: 'Mick D.',
    domains: [{ id: 'WSY', name: 'weesky.be' }, { id: 'EXT', name: 'example.org' }],
  }

  it('builds email from userName and primary domain', () => {
    expect(deriveIdentity(account).email).toBe('mick@weesky.be')
  })

  it('displayName prefers fullName, falls back to email', () => {
    expect(deriveIdentity(account).displayName).toBe('Mick D.')
    expect(deriveIdentity({ ...account, fullName: '' }).displayName).toBe('mick@weesky.be')
  })

  // The mirror of LabelFor_LabelsAnUnstoredPrimaryTheWayTheFrontendPredicts: the server reads a
  // whitespace-only FullName as no name at all, so the optimistic row must not go blank where the
  // refetch will show the address.
  it('reads a whitespace-only fullName as no name, the way LabelFor does', () => {
    expect(deriveIdentity({ ...account, fullName: '   ' }).displayName).toBe('mick@weesky.be')
  })

  it('displayName keeps the stored casing, labelFallback canonicalises it', () => {
    // Mirrors LabelFor_LabelsAnUnstoredPrimaryTheWayTheFrontendPredicts's casing arm: a whitespace
    // fullName and an uppercase stored userName must not leave the two sides disagreeing.
    const uppercase = { ...account, userName: 'Mick', fullName: '   ' }
    expect(deriveIdentity(uppercase).displayName).toBe('Mick@weesky.be')
    expect(deriveIdentity(uppercase).labelFallback).toBe('mick@weesky.be')
  })

  it('labelFallback keeps a real name untouched, same as displayName', () => {
    expect(deriveIdentity(account).labelFallback).toBe('Mick D.')
  })

  it('initials are first letters of user and domain, uppercased', () => {
    expect(deriveIdentity(account).initials).toBe('MW')
  })

  it('subDomains excludes the primary domain', () => {
    expect(deriveIdentity(account).subDomains).toEqual([{ id: 'EXT', name: 'example.org' }])
  })

  it('when mailbox matches no domain, all domains are subDomains', () => {
    const a = { ...account, mailbox: 'ZZZ' }
    expect(deriveIdentity(a).subDomains).toHaveLength(2)
    expect(deriveIdentity(a).email).toBe('mick@weesky.be')
  })
})
