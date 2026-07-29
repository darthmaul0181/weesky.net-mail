import { API_BASE, stagedAttachmentUrl } from '../../../api.js'

const RELATIVE = '/api/Mail/Attachments/'

const swap = (html: string, from: string, to: string) => html.split(from).join(to)

/**
 * The backend quotes inline images as request-relative srcs. The SPA and the API are separate
 * origins, so the composer has to show them absolute. Only the ids staged with this quote are
 * rewritten — the quoted body is untrusted text and may spell that path itself.
 * The account rides in the query: an <img> subresource cannot carry the X-Account-Id header,
 * and staged files are namespaced by account.
 */
export function absolutizeStagedUrls(html: string, ids: string[], accountId: string): string {
  return ids.reduce(
    (acc, id) => swap(acc, `${RELATIVE}${id}/content`, stagedAttachmentUrl(id, accountId)), html)
}

/**
 * The inverse, on the way out: MailSender turns a staged URL into a cid by matching the relative
 * form only, and an absolute https URL would pass the outgoing sanitiser and ship a link to our
 * API in the recipient's copy.
 */
export function relativizeStagedUrls(html: string): string {
  // Read per call, not hoisted: a module-level read breaks every partial api.js test mock on import.
  return swap(html, `${API_BASE}${RELATIVE}`, RELATIVE)
}
