import { renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api as realApi } from '../../../../api.js'
import { contactOf } from '../../../contacts/contactTestHarness'
import type { Contact } from '../../../contacts/contactTypes'
import { contactKeys } from '../../../contacts/queries'
import { createTestQueryClient, settle, waitFor, withQueryClient } from '../../../../test-utils'
import { useSenderAvatarUrl } from './useSenderAvatarUrl'

vi.mock('../../../../hooks/useAccountId', () => ({ useAccountId: () => 'acc' }))
vi.mock('../../../../api.js', async importOriginal => ({
  ...await importOriginal<typeof import('../../../../api.js')>(),
  api: { getContacts: vi.fn(), getContactPhoto: vi.fn(), getAvatar: vi.fn(), getBimiLogo: vi.fn() },
}))

const api = realApi as unknown as Record<'getContacts' | 'getContactPhoto' | 'getAvatar' | 'getBimiLogo', ReturnType<typeof vi.fn>>
const photo = new Blob(['photo'])
const gravatar = new Blob(['gravatar'])
const newPhoto = new Blob(['new photo'])
const bimi = new Blob(['bimi'])

function book(...contacts: Contact[]) {
  api.getContacts.mockResolvedValue({ contacts })
}

beforeEach(() => {
  vi.clearAllMocks()
  let made = 0
  URL.createObjectURL = vi.fn((blob: Blob) => `blob:${blob === gravatar ? 'gravatar' : blob === bimi ? 'bimi' : 'photo'}-${++made}`)
  URL.revokeObjectURL = vi.fn()
  book(contactOf({ id: 'c1', addresses: ['ann@x.be'], hasPhoto: true }),
    contactOf({ id: 'c2', addresses: ['bob@x.be'] }))
  api.getContactPhoto.mockResolvedValue(photo)
  api.getAvatar.mockResolvedValue(gravatar)
  api.getBimiLogo.mockResolvedValue(bimi)
})

type Bimi = { domain: string; selector?: string }

function renderAvatar(address: string | undefined, enabled = true, client = createTestQueryClient(), hint?: Bimi) {
  return renderHook(({ a, on }) => useSenderAvatarUrl(a, on, hint),
    { wrapper: withQueryClient(client), initialProps: { a: address, on: enabled } })
}

describe('useSenderAvatarUrl', () => {
  it('requests nothing while disabled', async () => {
    const client = createTestQueryClient()
    client.setQueryData(contactKeys.all('acc'), { contacts: [contactOf({ id: 'c1', addresses: ['ann@x.be'], hasPhoto: true })] })
    const { result } = renderAvatar('ann@x.be', false, client)
    await settle()

    expect(api.getContactPhoto).not.toHaveBeenCalled()
    expect(api.getAvatar).not.toHaveBeenCalled()
    expect(result.current).toBeNull()
  })

  it('shows the photo of the contact holding the address, without asking Gravatar', async () => {
    const { result } = renderAvatar('  Ann@X.be ')

    await waitFor(() => expect(result.current).toMatch(/^blob:photo/))
    expect(api.getContactPhoto).toHaveBeenCalledWith('c1')
    expect(api.getAvatar).not.toHaveBeenCalled()
  })

  it.each(['bob@x.be', 'nobody@x.be'])('asks Gravatar once for %s', async address => {
    const { result } = renderAvatar(` ${address} `)

    await waitFor(() => expect(result.current).toMatch(/^blob:gravatar/))
    expect(api.getAvatar).toHaveBeenCalledOnce()
    expect(api.getAvatar).toHaveBeenCalledWith(address)
    expect(api.getContactPhoto).not.toHaveBeenCalled()
  })

  it('answers null when Gravatar has nothing, and does not ask again on a rerender', async () => {
    api.getAvatar.mockResolvedValue(null)
    const { result, rerender } = renderAvatar('nobody@x.be')
    await waitFor(() => expect(api.getAvatar).toHaveBeenCalledOnce())

    rerender({ a: 'nobody@x.be', on: true })
    await settle()

    expect(result.current).toBeNull()
    expect(api.getAvatar).toHaveBeenCalledOnce()
  })

  it('makes one request for ten rows of the same sender', async () => {
    const client = createTestQueryClient()
    const rows = Array.from({ length: 10 }, () => renderAvatar('nobody@x.be', true, client))

    await waitFor(() => rows.forEach(r => expect(r.result.current).toMatch(/^blob:gravatar/)))
    expect(api.getAvatar).toHaveBeenCalledOnce()
    expect(api.getContacts).toHaveBeenCalledOnce()
  })

  it('falls through to Gravatar when the contact photo is missing', async () => {
    api.getContactPhoto.mockRejectedValue(new ApiError('Not found', 404, null))
    const { result } = renderAvatar('ann@x.be')

    await waitFor(() => expect(result.current).toMatch(/^blob:gravatar/))
    expect(api.getAvatar).toHaveBeenCalledWith('ann@x.be')
  })

  it('revokes the object URL on unmount', async () => {
    const { result, unmount } = renderAvatar('ann@x.be')
    await waitFor(() => expect(result.current).not.toBeNull())
    const url = result.current

    unmount()

    expect(URL.revokeObjectURL).toHaveBeenCalledWith(url)
  })

  it('drops a removed contact photo for Gravatar once the book is invalidated', async () => {
    const client = createTestQueryClient()
    const { result } = renderAvatar('ann@x.be', true, client)
    await waitFor(() => expect(result.current).toMatch(/^blob:photo/))

    book(contactOf({ id: 'c1', addresses: ['ann@x.be'], hasPhoto: false }))
    api.getContactPhoto.mockRejectedValue(new ApiError('Not found', 404, null))
    await client.invalidateQueries({ queryKey: contactKeys.all('acc') })

    await waitFor(() => expect(result.current).toMatch(/^blob:gravatar/))
    expect(api.getAvatar).toHaveBeenCalledWith('ann@x.be')
    const photoCalls = api.getContactPhoto.mock.calls.length
    const next = renderAvatar('ann@x.be', true, client)
    await waitFor(() => expect(next.result.current).toMatch(/^blob:gravatar/))
    expect(api.getContactPhoto).toHaveBeenCalledTimes(photoCalls)
  })

  it('shows a replaced contact photo and revokes the old one', async () => {
    const client = createTestQueryClient()
    const { result } = renderAvatar('ann@x.be', true, client)
    await waitFor(() => expect(result.current).toMatch(/^blob:photo/))
    const old = result.current

    api.getContactPhoto.mockResolvedValue(newPhoto)
    await client.invalidateQueries({ queryKey: contactKeys.all('acc') })

    await waitFor(() => expect(result.current).not.toBe(old))
    expect(result.current).toMatch(/^blob:photo/)
    expect(URL.createObjectURL).toHaveBeenLastCalledWith(newPhoto)
    expect(URL.revokeObjectURL).toHaveBeenCalledWith(old)
    expect(api.getAvatar).not.toHaveBeenCalled()
  })

  describe('the BIMI step', () => {
    const hint = { domain: 'brand.com', selector: 'sel' }

    it('puts the BIMI logo ahead of Gravatar', async () => {
      const { result } = renderAvatar('nobody@brand.com', true, createTestQueryClient(), hint)

      await waitFor(() => expect(result.current).toMatch(/^blob:bimi/))
      expect(api.getBimiLogo).toHaveBeenCalledWith('brand.com', 'sel')
      expect(api.getAvatar).not.toHaveBeenCalled()
    })

    it('keeps the contact photo ahead of the BIMI logo, and does not ask for it', async () => {
      const { result } = renderAvatar('ann@x.be', true, createTestQueryClient(), hint)

      await waitFor(() => expect(result.current).toMatch(/^blob:photo/))
      expect(api.getBimiLogo).not.toHaveBeenCalled()
    })

    it('falls through to Gravatar on a 404', async () => {
      api.getBimiLogo.mockResolvedValue(null)
      const { result } = renderAvatar('nobody@brand.com', true, createTestQueryClient(), hint)

      await waitFor(() => expect(result.current).toMatch(/^blob:gravatar/))
    })

    it.each([429, 500])('falls through to Gravatar on a %i, then to the initials', async status => {
      api.getBimiLogo.mockRejectedValue(new ApiError('Boom', status, null))
      const first = renderAvatar('nobody@brand.com', true, createTestQueryClient(), hint)
      await waitFor(() => expect(first.result.current).toMatch(/^blob:gravatar/))

      api.getAvatar.mockResolvedValue(null)
      const second = renderAvatar('other@brand.com', true, createTestQueryClient(), hint)
      await waitFor(() => expect(api.getAvatar).toHaveBeenCalledWith('other@brand.com'))
      await settle()
      expect(second.result.current).toBeNull()
    })

    it('makes no BIMI call for a row without a hint', async () => {
      const { result } = renderAvatar('nobody@x.be')

      await waitFor(() => expect(result.current).toMatch(/^blob:gravatar/))
      expect(api.getBimiLogo).not.toHaveBeenCalled()
    })

    it('makes one request for two rows of the same domain and selector', async () => {
      const client = createTestQueryClient()
      const rows = [renderAvatar('a@brand.com', true, client, hint), renderAvatar('b@brand.com', true, client, hint)]

      await waitFor(() => rows.forEach(r => expect(r.result.current).toMatch(/^blob:bimi/)))
      expect(api.getBimiLogo).toHaveBeenCalledOnce()
    })

    it('does not ask Gravatar while the BIMI query is pending', async () => {
      let answer: (blob: Blob) => void = () => {}
      api.getBimiLogo.mockReturnValue(new Promise<Blob>(resolve => { answer = resolve }))
      const { result } = renderAvatar('nobody@brand.com', true, createTestQueryClient(), hint)
      await settle()

      expect(api.getAvatar).not.toHaveBeenCalled()
      expect(result.current).toBeNull()
      answer(bimi)
      await waitFor(() => expect(result.current).toMatch(/^blob:bimi/))
      expect(api.getAvatar).not.toHaveBeenCalled()
    })

    it('never shows a cached Gravatar while the BIMI query is pending', async () => {
      const client = createTestQueryClient()
      client.setQueryData(['avatar', 'nobody@brand.com'], gravatar)
      let answer: (blob: Blob | null) => void = () => {}
      api.getBimiLogo.mockReturnValue(new Promise<Blob | null>(resolve => { answer = resolve }))
      const { result } = renderAvatar('nobody@brand.com', true, client, hint)
      await settle()
      expect(result.current).toBeNull()
      expect(vi.mocked(URL.createObjectURL).mock.calls.some(([blob]) => blob === gravatar)).toBe(false)

      answer(bimi)
      await waitFor(() => expect(result.current).toMatch(/^blob:bimi/))
      expect(vi.mocked(URL.createObjectURL).mock.calls.some(([blob]) => blob === gravatar)).toBe(false)
    })

    it('falls back to the cached Gravatar when the pending BIMI answers 404', async () => {
      const client = createTestQueryClient()
      client.setQueryData(['avatar', 'nobody@brand.com'], gravatar)
      let answer: (blob: Blob | null) => void = () => {}
      api.getBimiLogo.mockReturnValue(new Promise<Blob | null>(resolve => { answer = resolve }))
      const { result } = renderAvatar('nobody@brand.com', true, client, hint)
      await settle()
      expect(result.current).toBeNull()

      answer(null)
      await waitFor(() => expect(result.current).toMatch(/^blob:gravatar/))
    })

    it('asks BIMI again once the avatar cache is invalidated, as saving the switch does', async () => {
      const client = createTestQueryClient()
      api.getBimiLogo.mockResolvedValueOnce(null)
      const { result } = renderAvatar('nobody@brand.com', true, client, hint)
      await waitFor(() => expect(result.current).toMatch(/^blob:gravatar/))

      await client.invalidateQueries({ queryKey: ['avatar'] })

      await waitFor(() => expect(result.current).toMatch(/^blob:bimi/))
    })

    it('asks a failing BIMI endpoint once across a remount of the row', async () => {
      api.getBimiLogo.mockRejectedValue(new ApiError('Boom', 429, null))
      const client = createTestQueryClient()
      const first = renderAvatar('nobody@brand.com', true, client, hint)
      await waitFor(() => expect(first.result.current).toMatch(/^blob:gravatar/))
      first.unmount()

      const second = renderAvatar('nobody@brand.com', true, client, hint)
      await waitFor(() => expect(second.result.current).toMatch(/^blob:gravatar/))
      await settle()
      expect(api.getBimiLogo).toHaveBeenCalledOnce()
    })

    it('shares one query between an omitted and a default selector', async () => {
      const client = createTestQueryClient()
      const rows = [renderAvatar('a@brand.com', true, client, { domain: 'brand.com' }),
        renderAvatar('b@brand.com', true, client, { domain: 'brand.com', selector: 'default' })]
      await waitFor(() => rows.forEach(r => expect(r.result.current).toMatch(/^blob:bimi/)))
      expect(api.getBimiLogo).toHaveBeenCalledOnce()
    })

    it('revokes the BIMI object URL on unmount', async () => {
      const { result, unmount } = renderAvatar('nobody@brand.com', true, createTestQueryClient(), hint)
      await waitFor(() => expect(result.current).toMatch(/^blob:bimi/))
      const url = result.current

      unmount()

      expect(URL.revokeObjectURL).toHaveBeenCalledWith(url)
    })
  })

  it('keys the contact photo under the book, so every contact write refreshes it', () => {
    expect(contactKeys.avatar('acc', 'c1').slice(0, 2)).toEqual(contactKeys.all('acc'))
  })
})
