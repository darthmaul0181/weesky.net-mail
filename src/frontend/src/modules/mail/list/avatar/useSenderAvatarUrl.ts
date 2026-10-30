import { skipToken, useQuery } from '@tanstack/react-query'
import { useEffect } from 'react'
import { api, nullOn404 } from '../../../../api.js'
import { useAccountId } from '../../../../hooks/useAccountId'
import { useKeyedState } from '../../../../hooks/useKeyedState'
import { canonicalAddress } from '../../../../lib/canonicalAddress'
import { contactKeys, useContactPhotoIds } from '../../../contacts/queries'

const STALE_MS = 24 * 60 * 60_000

export interface BimiHint { domain: string; selector?: string }

function useBimiLogo(bimi: BimiHint | undefined, enabled: boolean) {
  return useQuery({
    queryKey: ['avatar', 'bimi', bimi?.domain, bimi?.selector ?? 'default'],
    queryFn: bimi ? () => api.getBimiLogo(bimi.domain, bimi.selector) : skipToken,
    enabled,
    staleTime: STALE_MS,
    retry: false,
    retryOnMount: false,
  })
}

function useGravatar(key: string, enabled: boolean) {
  return useQuery({
    queryKey: ['avatar', key],
    queryFn: () => api.getAvatar(key),
    enabled: enabled && key !== '',
    staleTime: STALE_MS,
  })
}

/** The sender's picture as an object URL: the contact's own photo, then the BIMI logo, then Gravatar,
 * `null` for the initials. Nothing is requested while `enabled` is false. */
export function useSenderAvatarUrl(address: string | undefined, enabled: boolean, bimi?: BimiHint): string | null {
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

  const logo = useBimiLogo(bimi, enabled && !book.isPending && fellThrough && bimi !== undefined)
  const logoPending = bimi !== undefined && fellThrough && !logo.isError && logo.data === undefined
  const logoShown = logo.data != null
  const gravatar = useGravatar(key, enabled && !book.isPending && fellThrough && !logoPending && !logoShown)

  const blob = !enabled ? null
    : !fellThrough ? contactPhoto.data ?? null
    : logoShown ? logo.data ?? null
    : logoPending ? null
    : gravatar.data ?? null
  const source = blob === null ? 'none'
    : !fellThrough ? `contact:${contactId}`
    : logoShown ? `bimi:${bimi?.domain}:${bimi?.selector ?? ''}`
    : `gravatar:${key}`
  const [url, setUrl] = useKeyedState<string | null>(() => null, source)

  useEffect(() => {
    if (!blob) return
    const objectUrl = URL.createObjectURL(blob)
    setUrl(objectUrl)
    return () => URL.revokeObjectURL(objectUrl)
  }, [blob, setUrl])

  return url
}
