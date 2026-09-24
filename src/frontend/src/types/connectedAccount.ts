/** How a row authenticates to its mail server. Frozen at creation on the backend. */
export type MailAuthMode = 'Password' | 'OAuth2'

export interface ConnectedAccount {
  id: string
  email: string
  displayName: string
  /** Absent for a local shared mailbox. */
  domainId?: string
  domainName?: string
  sieveSupported: boolean
  credentialsValid: boolean
  creationDate: string
  authMode: MailAuthMode
}
