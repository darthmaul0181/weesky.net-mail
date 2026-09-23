import { API_BASE, stagedAttachmentUrl } from '../../../api.js'

const RELATIVE = '/api/Mail/Attachments/'

const swap = (html: string, from: string, to: string) => html.split(from).join(to)

// The API is another origin, so quoted inline images are shown absolute. Only this quote's staged ids
// are rewritten: the quoted body is untrusted and may spell that path itself. The account rides in the
// query because an <img> cannot carry the X-Account-Id header.
export function absolutizeStagedUrls(html: string, ids: string[], accountId: string): string {
  return ids.reduce(
    (acc, id) => swap(acc, `${RELATIVE}${id}/content`, stagedAttachmentUrl(id, accountId)), html)
}

// MailSender maps only the relative form to a cid; an absolute URL would pass the outgoing sanitiser
// and ship a link to our API in the recipient's copy.
export function relativizeStagedUrls(html: string): string {
  // Read per call, not hoisted: a module-level read breaks every partial api.ts test mock on import.
  return swap(html, `${API_BASE}${RELATIVE}`, RELATIVE)
}
