/** The Administration tabs' wire shapes (`/api/Admin`). The API omits a null field, so every
    field the server may leave empty is `?:`, never `| null`. */

export interface LastLogin {
  /** The service the login came through: imap, pop3, … */
  service: string
  /** Absent for a service the account never logged into. */
  at?: string
}

export interface AdminUser {
  id: number
  userName: string
  domainId: string
  domainName: string
  fullName?: string
  quotaMb: number
  active: boolean
  admin: boolean
  lastLogins: LastLogin[]
}

/** The body of a user's creation and update. A null password on an update keeps the stored one. */
export interface AdminUserPayload {
  userName: string
  domainId: string
  password: string | null
  fullName: string
  quotaMb: number
  active: boolean
  admin: boolean
}

export interface AdminDomain {
  id: string
  name: string
  /** The aliases anchored on the domain, which its deletion takes with it. */
  aliasCount: number
}

export interface AdminDomainPayload {
  id: string
  name: string
}

export interface DomainOwner {
  ownerId: number
  ownerEmail: string
}

/** A virtual alias domain and the users allowed to create aliases on it. */
export interface VirtualDomain {
  domainId: string
  domainName: string
  owners: DomainOwner[]
}
