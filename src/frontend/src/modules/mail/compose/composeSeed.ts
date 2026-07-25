import type {
  AliasInfo, MailMessageDetail, OpenedDraft, PreparedQuote, SendingIdentity, StagedAttachmentInfo,
} from '../api/mailTypes'
import { formatReaderDate } from '../reader/formatReaderDate'
import {
  editAsNewFrom, myAddresses, preselectIdentity, replyAllRecipients, replyRecipients, subjectFor,
} from './replyModel'
import { threadingHeaders } from './threadingHeaders'
import { forwardQuote, replyQuote } from './quote'
import { absolutizeStagedUrls } from './stagedUrls'

export type ComposeAction = 'reply' | 'replyAll' | 'forward' | 'editAsNew' | 'draft'

/** Identifies the stored draft a save from the composer replaces. */
export interface DraftRef { folderPath: string; uid: number }

/** Everything a prefilled composer opens with — the shape a 2c3 draft also takes. */
export interface ComposeSeed {
  /** What opened the composer — the header names it. */
  action: ComposeAction
  to: string[]
  cc: string[]
  bcc: string[]
  subject: string
  html: string
  fromAddress: string | null
  attachments: StagedAttachmentInfo[]
  inReplyTo: string | null
  references: string[]
  /** Set when the composer is editing an existing draft — the version a save replaces. */
  draftRef: DraftRef | null
}

/** One place turns an original plus its prepared quote into a composer seed. Pure. */
export function buildComposeSeed(
  action: ComposeAction,
  detail: MailMessageDetail,
  prepared: PreparedQuote,
  identities: SendingIdentity[],
  aliases: AliasInfo[],
  primaryAddress: string | null,
): ComposeSeed {
  const dateText = formatReaderDate(detail.date)
  const quotable = absolutizeStagedUrls(prepared.quotableHtml, prepared.attachments.map(a => a.id))

  if (action === 'editAsNew') {
    return {
      action,
      to: detail.to.map(a => a.address),
      cc: detail.cc.map(a => a.address),
      bcc: detail.bcc.map(a => a.address),
      subject: detail.subject,
      html: quotable,
      fromAddress: editAsNewFrom(detail, identities),
      attachments: prepared.attachments,
      inReplyTo: null,
      references: [],
      draftRef: null,
    }
  }

  const threading = threadingHeaders(detail)
  const fromAddress = preselectIdentity(detail, identities)

  if (action === 'forward') {
    return {
      action,
      to: [], cc: [], bcc: [],
      subject: subjectFor('forward', detail.subject),
      html: forwardQuote(quotable, {
        fromName: detail.fromName, fromAddress: detail.fromAddress,
        dateText, subject: detail.subject, to: detail.to.map(a => a.address),
      }),
      fromAddress,
      attachments: prepared.attachments,
      inReplyTo: threading.inReplyTo,
      references: threading.references,
      draftRef: null,
    }
  }

  const mine = myAddresses(primaryAddress, aliases)
  const recipients = action === 'reply' ? replyRecipients(detail, mine) : replyAllRecipients(detail, mine)
  return {
    action,
    to: recipients.to, cc: recipients.cc, bcc: [],
    subject: subjectFor('reply', detail.subject),
    html: replyQuote(quotable, { dateText, name: detail.fromName, address: detail.fromAddress }),
    fromAddress,
    attachments: prepared.attachments,
    inReplyTo: threading.inReplyTo,
    references: threading.references,
    draftRef: null,
  }
}

/** Turns an opened draft into a composer seed. Pure. */
export function buildDraftSeed(
  opened: OpenedDraft, identities: SendingIdentity[], ref: DraftRef,
): ComposeSeed {
  const usable = identities.filter(i => !i.stale)
  const owned = opened.fromAddress
    ? usable.find(i => i.address.toLowerCase() === opened.fromAddress!.toLowerCase())
    : undefined
  return {
    action: 'draft',
    to: opened.to, cc: opened.cc, bcc: opened.bcc,
    subject: opened.subject,
    html: absolutizeStagedUrls(opened.htmlBody, opened.attachments.map(a => a.id)),
    fromAddress: owned?.address ?? null,
    attachments: opened.attachments,
    inReplyTo: opened.inReplyTo,
    references: opened.references,
    draftRef: ref,
  }
}
