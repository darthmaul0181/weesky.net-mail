import { skipToken, useQuery } from '@tanstack/react-query'
import { useEffect } from 'react'
import { api, nullOn404 } from '../../../../api.js'
import { useAccountId } from '../../../../hooks/useAccountId'
import { useKeyedState } from '../../../../hooks/useKeyedState'
import { canonicalAddress } from '../../../../lib/canonicalAddress'
import { contactKeys, useContactPhotoIds } from '../../../contacts/queries'

const GRAVATAR_STALE_MS = 24 * 60 * 60_000

/** The sender's picture as an object URL: the contact's own photo first, Gravatar otherwise, `null`
 * for the initials. Nothing is requested while `enabled` is false. */
export function useSenderAvatarUrl(address: string | undefined, enabled: boolean): string | null {
  const accountId = useAccountId()
  const key = canonicalAddress(address)
  const book = useContactPhotoIds(enabled)
  const contactId = book.data?.get(key) ?? null

  const contactPhoto = useQuery({
    queryKey: contactKeys.avatar(accountId, contactId ?? ''),
    queryFn: contactId === null ? skipToken : () => api.getContactPhoto(contactId).catch(nullOn404),
    enabled: enabled && contactId !== null,
    staleTime: 5 * 60_000,
  })
  const fellThrough = contactId === null || contactPhoto.data === null || contactPhoto.isError

  const gravatar = useQuery({
    queryKey: ['avatar', key],
    queryFn: () => api.getAvatar(key),
    enabled: enabled && key !== '' && !book.isPending && fellThrough,
    staleTime: GRAVATAR_STALE_MS,
  })

  const blob = !enabled ? null
    : !fellThrough ? contactPhoto.data ?? null
    : gravatar.data ?? null
  const source = blob === null ? 'none' : fellThrough ? `gravatar:${key}` : `contact:${contactId}`
  const [url, setUrl] = useKeyedState<string | null>(() => null, source)

  useEffect(() => {
    if (!blob) return
    const objectUrl = URL.createObjectURL(blob)
    setUrl(objectUrl)
    return () => URL.revokeObjectURL(objectUrl)
  }, [blob, setUrl])

  return url
}
