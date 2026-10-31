import { describe, expect, it } from 'vitest'
import type { AuthContextValue, ActiveAccount } from '../contexts/AuthContext'
import { allowAdmin, allowAliases, allowPrimary, allowSieve } from './gates'

function auth(overrides: Partial<AuthContextValue>): AuthContextValue {
  return {
    isLoggedIn: true,
    isAdmin: false,
    account: null,
    accountLoaded: true,
    capabilities: null,
    identity: null,
    activeAccount: null,
    activeAccountId: 'primary',
    accounts: [],
    accountsLoading: false,
    switchAccount: () => {},
    syncFromSession: () => {},
    logout: async () => {},
    refreshAccount: async () => {},
    ...overrides,
  }
}

function account(overrides: Partial<ActiveAccount>): ActiveAccount {
  return {
    id: 'primary', email: 'a@x.com', displayName: 'A', isPrimary: true, domainName: null,
    credentialsValid: true, sieveSupported: true, authMode: 'Password',
    ...overrides,
  }
}

describe('allowAdmin', () => {
  it('waits while the account is still loading', () => {
    expect(allowAdmin(auth({ accountLoaded: false }))).toBe('wait')
  })

  it('lets an admin through once loaded, capabilities unanswered', () => {
    expect(allowAdmin(auth({ accountLoaded: true, isAdmin: true, capabilities: null }))).toBe(true)
  })

  it('refuses a non-admin', () => {
    expect(allowAdmin(auth({ accountLoaded: true, isAdmin: false }))).toBe(false)
  })

  it('refuses an admin the platform explicitly turned off', () => {
    expect(allowAdmin(auth({ accountLoaded: true, isAdmin: true, capabilities: { admin: false } }))).toBe(false)
  })
})

describe('allowAliases', () => {
  it('lets it through while capabilities are unanswered', () => {
    expect(allowAliases(auth({ capabilities: null }))).toBe(true)
  })

  it('refuses when the platform turned aliases off', () => {
    expect(allowAliases(auth({ capabilities: { aliases: false } }))).toBe(false)
  })
})

describe('allowPrimary', () => {
  it('lets the primary account through', () => {
    expect(allowPrimary(auth({ activeAccount: account({ isPrimary: true }) }))).toBe(true)
  })

  // The loading pin: activeAccount is null before the connected-accounts query resolves, and
  // that must read as "let it through", never as a redirect that then flips back.
  it('lets it through while the account list is still loading (activeAccount null)', () => {
    expect(allowPrimary(auth({ activeAccount: null }))).toBe(true)
  })

  it('refuses a connected (non-primary) account', () => {
    expect(allowPrimary(auth({ activeAccount: account({ isPrimary: false }) }))).toBe(false)
  })
})

describe('allowSieve', () => {
  it('lets it through while the account list is still loading (activeAccount null)', () => {
    expect(allowSieve(auth({ activeAccount: null, accountsLoading: true, capabilities: null }))).toBe(true)
  })

  it('refuses a connected account whose mailbox does not support Sieve', () => {
    expect(allowSieve(auth({
      activeAccount: account({ isPrimary: false, sieveSupported: false }), accountsLoading: false,
    }))).toBe(false)
  })

  it('waits on the primary capabilities check while the account list is still loading', () => {
    expect(allowSieve(auth({
      activeAccount: null, accountsLoading: true, capabilities: { rules: false },
    }))).toBe(true)
  })

  it('refuses the primary once loaded when capabilities.rules is off', () => {
    expect(allowSieve(auth({
      activeAccount: account({ isPrimary: true }), accountsLoading: false, capabilities: { rules: false },
    }))).toBe(false)
  })

  it('leaves a Sieve-capable connected account alone even when capabilities.rules is off', () => {
    expect(allowSieve(auth({
      activeAccount: account({ isPrimary: false, sieveSupported: true }), accountsLoading: false,
      capabilities: { rules: false },
    }))).toBe(true)
  })
})
