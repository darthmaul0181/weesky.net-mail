/** Shapes returned by the backend's /api/Mail endpoints. */

export type SpecialUse = 'inbox' | 'sent' | 'drafts' | 'trash' | 'junk' | 'archive'

/** What the sender declared. 'normal' is the absence of any priority header. */
export type MailPriority = 'normal' | 'high' | 'low'

export interface MailFolderNode {
  /** Full IMAP path. Opaque — never parsed or built client-side: the separator is the server's. */
  path: string
  name: string
  specialUse: SpecialUse | null
  selectable: boolean
  subscribed: boolean
  total: number | null
  unread: number | null
  uidValidity: number
  /** Rises on every arrival — the poll's signal for new mail. Null when not selectable. */
  uidNext: number | null
  /** Rises on every flag change (RFC 7162). Null without CONDSTORE or when not selectable. */
  highestModSeq: number | null
  children: MailFolderNode[]
}

export interface MailMessageSummary {
  uid: number
  subject: string
  fromName: string
  fromAddress: string
  /** Envelope recipients — the drafts folder lists "To:" instead of the sender. */
  to: MailAddressInfo[]
  date: string
  seen: boolean
  flagged: boolean
  answered: boolean
  hasAttachments: boolean
  size: number
  preview: string
  priority: MailPriority
}

export interface MailFolderPage {
  folderPath: string
  /** When this changes, cached UIDs for the folder are stale and must be dropped. */
  uidValidity: number
  total: number
  page: number
  pageSize: number
  messages: MailMessageSummary[]
}

/** One search hit: a summary plus where it lives — in all-folders scope each row names its folder. */
export interface MailSearchResult extends MailMessageSummary {
  folderPath: string
  /** That folder's UID validity at search time — the result is a snapshot. */
  uidValidity: number
}

export interface MailSearchPage {
  total: number
  page: number
  pageSize: number
  results: MailSearchResult[]
}

export interface MailAttachmentInfo {
  /** MIME part specifier — the download handle. */
  part: string
  fileName: string
  contentType: string
  size: number
  /** True for a part the body references by cid:; the UI hides these. */
  isInline: boolean
  /** Bare Content-ID the body references as cid:; the reader's key to inline it. Null when none. */
  contentId: string | null
}

export interface MailAddressInfo {
  name: string
  address: string
}

/** SPF/DKIM/DMARC as the receiving server reported them, plus the raw header behind them. */
export interface MailAuthentication {
  spf: string | null
  dkim: string | null
  dmarc: string | null
  raw: string
}

/** The spam filter's own verdict: score, the threshold it judges against, and the raw header. */
export interface MailSpamScore {
  score: number
  threshold: number
  raw: string
}

export interface MailMessageDetail {
  uid: number
  folderPath: string
  uidValidity: number
  subject: string
  fromName: string
  fromAddress: string
  to: MailAddressInfo[]
  cc: MailAddressInfo[]
  date: string
  /** RFC 5322 message id, bare (no angle brackets). Null when the original carries none. */
  messageId: string | null
  /** References chain, oldest first, bare ids. Empty when absent. */
  references: string[]
  inReplyTo: string | null
  replyTo: MailAddressInfo[]
  /** Kept on a Sent copy; empty on received mail. Feeds Edit-as-new. */
  bcc: MailAddressInfo[]
  authentication: MailAuthentication | null
  spamScore: MailSpamScore | null
  /** Expanded-header details — each null when the message carries no such header. */
  mailingList: string | null
  sentBy: string | null
  signedBy: string | null
  unsubscribeUrl: string | null
  tlsReceived: boolean | null
  priority: MailPriority
  /** Already sanitised by the backend. Still only ever rendered in a sandboxed iframe. */
  htmlBody: string
  textBody: string
  blockedImageCount: number
  attachments: MailAttachmentInfo[]
}

/**
 * Why the stored choice no longer holds. The page words its notice from this: one
 * undifferentiated flag made it assert the folder had been renamed or deleted even when the
 * folder is plainly still there.
 */
export type StaleOverrideReason = 'missing' | 'notSelectable' | 'folderTaken'

export interface FolderRoleStaleOverride {
  folderPath: string
  reason: StaleOverrideReason
}

/** One assignable role: what it resolves to today, and why. */
export interface FolderRoleEntry {
  role: string
  folderPath: string | null
  provenance: 'override' | 'specialUse' | 'name' | null
  /** The user's stored choice no longer matches a live folder — kept and signalled. */
  staleOverride: FolderRoleStaleOverride | null
}

/** One entry of the curated From list: a stored row merged with what the account actually owns. */
export interface SendingIdentity {
  address: string
  displayName: string
  isDefault: boolean
  isPrimary: boolean
  /** The alias behind it is gone — kept and flagged, never silently deleted, never offered in From. */
  stale: boolean
  labelIsCustom: boolean
}

export interface IdentityListResponse { identities: SendingIdentity[] }

export interface AliasInfo { name: string; domain: string }

/** One staged outgoing file, as the backend answers it. */
export interface StagedAttachmentInfo {
  id: string
  fileName: string
  size: number
  contentType: string
  /** Non-null marks an inline body resource (cid part) — hidden from the attachment tray. */
  contentId: string | null
}

export type QuotePurpose = 'reply' | 'forward' | 'editAsNew'

/** What PrepareQuote answers: the outbound-sanitised original, cid images rewritten to staged URLs. */
export interface PreparedQuote {
  quotableHtml: string
  attachments: StagedAttachmentInfo[]
}

export interface SavedDraft { uid: number; folderPath: string }

/** Everything the composer needs to resume a draft — the seed's raw material. */
export interface OpenedDraft {
  to: string[]
  cc: string[]
  bcc: string[]
  subject: string
  fromAddress: string | null
  htmlBody: string
  attachments: StagedAttachmentInfo[]
  inReplyTo: string | null
  references: string[]
  priority: MailPriority
}

/** A message as it arrived. `source` is capped; `totalBytes` is what the whole message weighs. */
export interface MailMessageSource {
  subject: string
  messageId: string | null
  date: string
  fromName: string
  fromAddress: string
  to: MailAddressInfo[]
  authentication: MailAuthentication | null
  source: string
  totalBytes: number
  truncated: boolean
}
