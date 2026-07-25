import type { MailMessageDetail } from '../api/mailTypes'

export interface ThreadingHeaders { inReplyTo: string | null; references: string[] }

/**
 * RFC 5322 threading: the new message replies to the original (In-Reply-To) and extends its
 * References chain. Same computation for reply, reply-all and forward; edit-as-new sends none.
 */
export function threadingHeaders(
  detail: Pick<MailMessageDetail, 'messageId' | 'references'>,
): ThreadingHeaders {
  if (!detail.messageId) return { inReplyTo: null, references: [...detail.references] }
  const references = detail.references.includes(detail.messageId)
    ? [...detail.references]
    : [...detail.references, detail.messageId]
  return { inReplyTo: detail.messageId, references }
}
