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
  api: { getContacts: vi.fn(), getContactPhoto: vi.fn(), getAvatar: vi.fn() },
}))

const api = realApi as unknown as Record<'getContacts' | 'getContactPhoto' | 'getAvatar', ReturnType<typeof vi.fn>>
const photo = new Blob(['photo'])
const gravatar = new Blob(['gravatar'])
const newPhoto = new Blob(['new photo'])

function book(...contacts: Contact[]) {
  api.getContacts.mockResolvedValue({ contacts })
}

beforeEach(() => {
  vi.clearAllMocks()
  let made = 0
  URL.createObjectURL = vi.fn((blob: Blob) => `blob:${blob === gravatar ? 'gravatar' : 'photo'}-${++made}`)
  URL.revokeObjectURL = vi.fn()
  book(contactOf({ id: 'c1', addresses: ['ann@x.be'], hasPhoto: true }),
    contactOf({ id: 'c2', addresses: ['bob@x.be'] }))
  api.getContactPhoto.mockResolvedValue(photo)
  api.getAvatar.mockResolvedValue(gravatar)
})

function renderAvatar(address: string | undefined, enabled = true, client = createTestQueryClient()) {
  return renderHook(({ a, on }) => useSenderAvatarUrl(a, on),
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

  it('keys the contact photo under the book, so every contact write refreshes it', () => {
    expect(contactKeys.avatar('acc', 'c1').slice(0, 2)).toEqual(contactKeys.all('acc'))
  })
})
