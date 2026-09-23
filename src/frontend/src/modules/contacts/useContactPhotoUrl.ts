import { useEffect } from 'react'
import { useKeyedState } from '../../hooks/useKeyedState'
import { useContactPhoto } from './queries'

/**
 * The avatar's object URL, revoked with the blob that produced it: without the revocation every
 * contact opened would leave its picture in memory for the life of the tab. No blob, no URL: the
 * picture leaves with its blob rather than one frame later. Shared by the card and the editor,
 * which draw the same face and must not each keep their own copy of the revocation.
 */
export function useContactPhotoUrl(
  contactId: string | null, hasPhoto: boolean, cardHash: string | null,
): string | null {
  const { data: blob } = useContactPhoto(contactId, hasPhoto, cardHash)
  // Keyed on which photo, not on which blob: a refetch of the same card keeps the face on screen.
  const [url, setUrl] = useKeyedState<string | null>(
    () => null, blob ? `${contactId}:${cardHash}` : 'none')

  useEffect(() => {
    if (!blob) return
    const objectUrl = URL.createObjectURL(blob)
    // Set from the effect on purpose: the URL is made here so the cleanup that pairs with it revokes it.
    setUrl(objectUrl)
    return () => URL.revokeObjectURL(objectUrl)
  }, [blob, setUrl])

  return url
}
