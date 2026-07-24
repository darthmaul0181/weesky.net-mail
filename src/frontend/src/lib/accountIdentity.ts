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
  /** What `IdentityResolver.LabelFor` would send back for the primary with no stored row: the
      full name, else the *canonical* address — trimmed, lower-cased — never the stored casing
      `email` carries. Only a caller predicting that resolved label should read this instead of
      `displayName`; an account-display surface wants the address exactly as stored. */
  labelFallback: string
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
  // Whitespace is not a name, the way `IdentityResolver.LabelFor` reads it.
  const hasName = !!account.fullName?.trim()
  return {
    email,
    displayName: hasName ? account.fullName! : email,
    labelFallback: hasName ? account.fullName! : email.trim().toLowerCase(),
    initials,
    subDomains: primaryDomain ? list.filter(d => d.id !== account.mailbox) : list,
  }
}
