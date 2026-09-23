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
    const unreadable = () => reject(reader.error ?? new Error('Could not read the inline image'))
    reader.onload = () => { if (typeof reader.result === 'string') resolve(reader.result); else unreadable() }
    reader.onerror = unreadable
    reader.readAsDataURL(blob)
  })
}

// Referenced by the body, listed on the detail, and an image. Exported because the attachment row
// withholds exactly these: two separate decisions could make a file vanish from both places.
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

// data: URIs keyed by bare cid, fetched over the SPA's own session: the sandboxed iframe is
// cookieless, so no authenticated URL loads in there.
export function useInlineImages(
  folder: string | null, uid: number | null,
  attachments: MailAttachmentInfo[] | undefined,
  sanitizedHtml: string,
): Record<string, string> {
  const accountId = useAccountId()
  const parts = useMemo(
    () => bodyInlineParts(attachments, sanitizedHtml), [attachments, sanitizedHtml])

  // Account-scoped, so a second mailbox cannot serve its images for the same folder and uid. Parts are
  // immutable per folder+uid: a focus refetch would be one IMAP part fetch per image per alt-tab.
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
