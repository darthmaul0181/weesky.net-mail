/** Shapes returned by the backend's /api/Mail endpoints. */

export type SpecialUse = 'inbox' | 'sent' | 'drafts' | 'trash' | 'junk' | 'archive'

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
  children: MailFolderNode[]
}

export interface MailMessageSummary {
  uid: number
  subject: string
  fromName: string
  fromAddress: string
  date: string
  seen: boolean
  flagged: boolean
  answered: boolean
  hasAttachments: boolean
  size: number
  preview: string
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

export interface MailAttachmentInfo {
  /** MIME part specifier — the download handle. */
  part: string
  fileName: string
  contentType: string
  size: number
  /** True for a part the body references by cid:; the UI hides these. */
  isInline: boolean
}

export interface MailMessageDetail {
  uid: number
  folderPath: string
  uidValidity: number
  subject: string
  fromName: string
  fromAddress: string
  to: string[]
  cc: string[]
  date: string
  /** Already sanitised by the backend. Still only ever rendered in a sandboxed iframe. */
  htmlBody: string
  textBody: string
  blockedImageCount: number
  attachments: MailAttachmentInfo[]
}

export interface FolderRoleStaleOverride {
  folderPath: string
}

/** One assignable role: what it resolves to today, and why. */
export interface FolderRoleEntry {
  role: string
  folderPath: string | null
  provenance: 'override' | 'specialUse' | 'name' | null
  /** The user's stored choice no longer matches a live folder — kept and signalled. */
  staleOverride: FolderRoleStaleOverride | null
}
