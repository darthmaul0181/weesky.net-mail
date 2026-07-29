import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { mailAttachmentUrl, requestBlob } from '../../../api.js'
import type { MailAttachmentInfo } from '../api/mailTypes'
import { useAccountId } from '../queries'
import { referencedCids } from './inlineImages'
import { isImageType } from './mediaType'

const NONE: Record<string, string> = {}

function readAsDataUri(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(reader.error ?? new Error('Could not read the inline image'))
    reader.readAsDataURL(blob)
  })
}

/**
 * The parts the body displays itself: referenced by it, listed on the detail, and an image. A cid
 * pointing at anything else stays a broken image, the same rule the compose side applies.
 *
 * Exported because the attachment row withholds exactly these — a part shown in the body is not
 * one to offer again — and the two decisions must be one, or a file vanishes from both places.
 */
export function bodyInlineParts(
  attachments: MailAttachmentInfo[] | undefined, sanitizedHtml: string,
): { cid: string; part: string }[] {
  if (!attachments?.length) return []

  return referencedCids(sanitizedHtml).flatMap(cid => {
    const entry = attachments.find(attachment =>
      attachment.contentId === cid && isImageType(attachment.contentType))
    return entry ? [{ cid, part: entry.part }] : []
  })
}

/**
 * data: URIs for the inline images a message body references, keyed by bare cid.
 * Fetches nothing when the body references no cid or the detail lists no matching image part.
 *
 * The reader's iframe is sandboxed without allow-same-origin, so its own requests are
 * cookieless and no authenticated URL can load in there. The SPA fetches the bytes over the
 * session it already holds and hands them to the iframe inlined.
 */
export function useInlineImages(
  folder: string | null, uid: number | null,
  attachments: MailAttachmentInfo[] | undefined,
  sanitizedHtml: string,
): Record<string, string> {
  const accountId = useAccountId()
  const parts = useMemo(
    () => bodyInlineParts(attachments, sanitizedHtml), [attachments, sanitizedHtml])

  // Account-scoped like every other key here, so a second linked mailbox cannot serve its own
  // images for the same folder and uid. Message parts are immutable per folder+uid, so the pair
  // is the whole identity of the answer — and refetching them on every window focus, as the app
  // defaults to, is one IMAP part fetch per inline image per alt-tab.
  const { data } = useQuery({
    queryKey: ['mail', accountId, 'inline', folder ?? '', uid ?? 0],
    queryFn: async () => {
      const settled = await Promise.allSettled(parts.map(async ({ cid, part }) => {
        const { blob } = await requestBlob(mailAttachmentUrl(folder!, uid!, part, accountId))
        return [cid, await readAsDataUri(blob)] as const
      }))

      // A part that failed just stays a broken image: never a reason to fail the reader.
      return Object.fromEntries(
        settled.flatMap(result => result.status === 'fulfilled' ? [result.value] : []))
    },
    enabled: folder !== null && uid !== null && parts.length > 0,
    staleTime: Infinity,
  })

  return data ?? NONE
}
