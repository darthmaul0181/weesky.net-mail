// Mirrors the identity derivation previously inlined in AliasesPage (getAccount effect).
export interface AccountDomain { id: string; name: string }

export interface Account {
  userName?: string
  mailbox?: string
  fullName?: string
  isAdmin?: boolean
  domains?: AccountDomain[]
}

export interface AccountIdentity {
  email: string
  displayName: string
  initials: string
  subDomains: AccountDomain[]
}

export function deriveIdentity(account: Account): AccountIdentity {
  const list = account.domains ?? []
  const primaryDomain = list.find(d => d.id === account.mailbox)
  const defaultDomain = primaryDomain ?? list[0]
  const domainName = defaultDomain?.name ?? ''
  const email = domainName ? `${account.userName}@${domainName}` : (account.userName ?? '')
  const initials =
    (account.userName?.[0] ?? '').toUpperCase() +
    (domainName?.[0] ?? account.mailbox?.[0] ?? '').toUpperCase()
  return {
    email,
    displayName: account.fullName || email,
    initials,
    subDomains: primaryDomain ? list.filter(d => d.id !== account.mailbox) : list,
  }
}
