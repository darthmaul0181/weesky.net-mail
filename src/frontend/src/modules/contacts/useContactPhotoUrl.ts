import { useEffect, useState } from 'react'
import { useContactPhoto } from './queries'

/**
 * The avatar's object URL, revoked with the blob that produced it: without the revocation every
 * contact opened would leave its picture in memory for the life of the tab. Shared by the card and
 * the editor, which draw the same face and must not each keep their own copy of the revocation.
 */
export function useContactPhotoUrl(
  contactId: string | null, hasPhoto: boolean, cardHash: string | null,
): string | null {
  const { data: blob } = useContactPhoto(contactId, hasPhoto, cardHash)
  const [url, setUrl] = useState<string | null>(null)

  useEffect(() => {
    if (!blob) {
      setUrl(null)
      return
    }
    const objectUrl = URL.createObjectURL(blob)
    setUrl(objectUrl)
    return () => URL.revokeObjectURL(objectUrl)
  }, [blob])

  return url
}
