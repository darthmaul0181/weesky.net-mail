/** Shapes returned by the backend's /api/Mail endpoints. The API omits a null field rather than
    sending it, so every field the server may leave empty is `?:`, never `| null`. */

export type SpecialUse = 'inbox' | 'sent' | 'drafts' | 'trash' | 'junk' | 'archive'

/** What the sender declared. 'normal' is the absence of any priority header. */
export type MailPriority = 'normal' | 'high' | 'low'

export interface MailFolderNode {
  /** Full IMAP path. Opaque — never parsed or built client-side: the separator is the server's. */
  path: string
  name: string
  specialUse?: SpecialUse
  selectable: boolean
  subscribed: boolean
  /** Absent when the server gave no STATUS for the folder — a container, or a failed read. */
  total?: number
  unread?: number
  uidValidity: number
  /** Rises on every arrival — the poll's signal for new mail. Absent when not selectable. */
  uidNext?: number
  /** Rises on every flag change (RFC 7162). Absent without CONDSTORE or when not selectable. */
  highestModSeq?: number
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

/** One conversation of a grouped page: its messages, newest first. */
export interface MailThread {
  messages: MailMessageSummary[]
}

export interface MailFolderPage {
  folderPath: string
  /** When this changes, cached UIDs for the folder are stale and must be dropped. */
  uidValidity: number
  total: number
  page: number
  pageSize: number
  messages: MailMessageSummary[]
  /** Grouped mode only — absent on a flat page, which is how the client tells the modes apart. */
  threads?: MailThread[]
  /** Grouped mode only: what the pager pages. `total` keeps counting messages. */
  totalThreads?: number
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
  /** Bare Content-ID the body references as cid:; the reader's key to inline it. Absent when none. */
  contentId?: string
}

export interface MailAddressInfo {
  name: string
  address: string
}

/** SPF/DKIM/DMARC as the receiving server reported them, plus the raw header behind them. */
export interface MailAuthentication {
  spf?: string
  dkim?: string
  dmarc?: string
  raw: string
}

/** The spam filter's own verdict: score, the threshold it judges against, and the raw header. */
export interface MailSpamScore {
  score: number
  threshold: number
  raw: string
}

/** What the block is: the organiser asking for a date, the word that it is off, or a guest's answer. */
export type InvitationMethod = 'Request' | 'Cancel' | 'Reply'

/** Whether a guest's answer may enter the calendar. Only `Applicable` is ever written; `Stale`
    answers an earlier version, `Superseded` is older than the answer already recorded, and
    `UnsupportedAnswer` says something other than yes, maybe or no. */
export type ReplyStatus =
  | 'Applicable' | 'UnknownUid' | 'NotOwner' | 'UnknownAttendee' | 'OccurrenceOnly'
  | 'Stale' | 'Superseded' | 'UnsupportedAnswer'

/** The guest who answered, what they said, and whether the calendar already holds it. */
export interface InvitationReply {
  email: string
  name?: string
  partStat: string
  status: ReplyStatus
  applied: boolean
}

/** How the event the block names stands in the user's own calendar. `Outdated` is a copy the
    organiser has since revised, `Newer` one the calendar already holds a later version of. */
export type InvitationPresence = 'Absent' | 'Current' | 'Outdated' | 'Newer' | 'Cancelled'

/** What the reader can do about it. `AddOnly` files the event and mails nobody — a forwarded
    invitation has no answer to give — and `Remove` drops a cancelled one from the calendar. */
export type InvitationAnswer = 'Accepted' | 'Tentative' | 'Declined' | 'AddOnly' | 'Remove'

export interface InvitationPerson {
  email: string
  /** The display name the block carried; absent when it named an address alone. */
  name?: string
}

/** The invitation a message carries, read by the backend so the reader never parses iCalendar. */
export interface MailInvitation {
  method: InvitationMethod
  /** The event's own identifier, and the revision the organiser stamped this block with. */
  uid: string
  sequence: number
  summary?: string
  /** A timed event's two instants, in UTC. Absent on a whole-day one. */
  start?: string
  end?: string
  /** A whole-day event's first day, and the day after its last — iCalendar's half-open range. */
  startDate?: string
  endDateExclusive?: string
  isAllDay: boolean
  location?: string
  /** The event is one of a series: answering here answers the whole of it. */
  repeats: boolean
  organizer?: InvitationPerson
  attendees: InvitationPerson[]
  /** The account address the block lists as an attendee. Absent when the invitation reached the
      user by forwarding: there is then nobody to answer for, only an event to file. */
  addressedTo?: string
  /** The answer the file records for that address, and the one the calendar copy holds. */
  filePartStat?: string
  savedPartStat?: string
  inCalendar: InvitationPresence
  /** The calendar holding the copy, when the event is in one. */
  calendarId?: string
  /** The block carries a single date of a series: nothing here can be answered as a whole. */
  occurrenceOnly: boolean
  /** Set on a `Reply` block alone. */
  reply?: InvitationReply
  /** The MIME part the block was read from — what an answer names back. */
  part: string
  /** The block could not be read, `reason` says why: the file stays an ordinary attachment. */
  unreadable: boolean
  reason?: string
}

/** An answer's outcome: the block as it now stands, whether the reply reached the organiser,
    and whether the message itself left for the trash. */
export interface InvitationResponse {
  invitation: MailInvitation
  replySent: boolean
  /** `invitation_no_organizer` (no address to reply to) or `invitation_uid_unwritable` (a UID no reply can carry); the send failure otherwise. */
  replyError?: string
  trashed: boolean
}

export interface ApplyReplyArgs {
  folder: string
  uid: number
  part: string
}

/** A guest's answer carried into the calendar: `applied` is false, with its code, when the
    calendar did not take the write — the event changed in between, or it was busy. */
export interface ApplyReplyResponse {
  invitation: MailInvitation
  applied: boolean
  applyError?: string
}

/** `language` and `timeZone` word and place the reply mail the backend sends on the user's
    behalf — the organiser reads it, not the user, so neither can be inferred server-side. */
export interface RespondInvitationArgs {
  folder: string
  uid: number
  part: string
  answer: InvitationAnswer
  calendarId?: string
  language: string
  timeZone: string
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
  /** RFC 5322 message id, bare (no angle brackets). Absent when the original carries none. */
  messageId?: string
  /** References chain, oldest first, bare ids. Empty when absent. */
  references: string[]
  inReplyTo?: string
  replyTo: MailAddressInfo[]
  /** Kept on a Sent copy; empty on received mail. Feeds Edit-as-new. */
  bcc: MailAddressInfo[]
  authentication?: MailAuthentication
  spamScore?: MailSpamScore
  /** Expanded-header details — each absent when the message carries no such header. */
  mailingList?: string
  sentBy?: string
  signedBy?: string
  unsubscribeUrl?: string
  tlsReceived?: boolean
  priority: MailPriority
  /** Already sanitised by the backend. Still only ever rendered in a sandboxed iframe. */
  htmlBody: string
  textBody: string
  blockedImageCount: number
  /** The backend cut the body at one of its ceilings: what is shown is not the whole message. */
  truncated: boolean
  attachments: MailAttachmentInfo[]
  /** The calendar invitation the message carries, when it carries one the reader can act on. */
  invitation?: MailInvitation
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
  folderPath?: string
  provenance?: 'override' | 'specialUse' | 'name'
  /** The user's stored choice no longer matches a live folder — kept and signalled. */
  staleOverride?: FolderRoleStaleOverride
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

/** One row of `PUT /api/Identities`: what the user curates, the rest being the server's to derive. */
export type IdentityWrite = Pick<SendingIdentity, 'address' | 'displayName' | 'isDefault'>

export interface AliasInfo { name: string; domain: string }

/** One staged outgoing file, as the backend answers it. */
export interface StagedAttachmentInfo {
  id: string
  fileName: string
  size: number
  contentType: string
  /** Present marks an inline body resource (cid part) — hidden from the attachment tray. */
  contentId?: string
}

export type QuotePurpose = 'reply' | 'forward' | 'editAsNew'

/** The two flags a reader sets. */
export type MailFlagName = 'seen' | 'flagged'

export interface SendMessageArgs {
  to: string[]
  cc: string[]
  bcc: string[]
  subject: string
  htmlBody: string
  /** Present sends the message as text/plain alone; htmlBody rides along empty. */
  textBody?: string
  attachmentIds: string[]
  priority: MailPriority
  /** Omitted picks the account's own address server-side; the display label is always server-resolved. */
  fromAddress?: string
  /** Threading of a reply/forward: the original's id and its extended references chain. */
  inReplyTo?: string
  references?: string[]
}

export interface SendMessageResult { appendedToSent: boolean }

export type SaveDraftArgs = SendMessageArgs & { replaceUid?: number }

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
  fromAddress?: string
  htmlBody: string
  /** Present when the stored draft was written as text: the composer reopens in text mode. An
      HTML draft carries no `textBody` at all. */
  textBody?: string
  attachments: StagedAttachmentInfo[]
  inReplyTo?: string
  references: string[]
  priority: MailPriority
}

/** A message as it arrived. `source` is capped; `totalBytes` is what the whole message weighs. */
export interface MailMessageSource {
  subject: string
  messageId?: string
  date: string
  fromName: string
  fromAddress: string
  to: MailAddressInfo[]
  authentication?: MailAuthentication
  source: string
  totalBytes: number
  truncated: boolean
}
