import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import type { QueryClient } from '@tanstack/react-query'
import { useSetFolderSubscription, mailKeys } from './queries'
import { createTestQueryClient, settle, withQueryClient } from '../../test-utils'
import type { MailFolderNode } from './api/mailTypes'

const mocks = vi.hoisted(() => ({ setMailFolderSubscription: vi.fn() }))
vi.mock('../../api.js', () => ({ api: mocks }))
vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => ({ activeAccount: { id: 'primary' }, activeAccountId: 'primary' }),
}))

const ACC = 'primary'

function node(partial: Partial<MailFolderNode>): MailFolderNode {
  return {
    path: 'X', name: 'X', selectable: true, subscribed: true,
    total: 0, unread: 0, uidValidity: 1, children: [], ...partial,
  }
}

function seededClient() {
  const client = createTestQueryClient()
  client.setQueryData(mailKeys.folders(ACC), [
    node({ path: 'Projects', name: 'Projects', subscribed: true }),
    node({
      path: 'Parent', name: 'Parent',
      children: [node({ path: 'Parent/Child', name: 'Child', subscribed: true })],
    }),
  ])
  return client
}

function wrapperFor(client: QueryClient) {
  return withQueryClient(client)
}

beforeEach(() => vi.clearAllMocks())

// M13: bound straight to the tree's own `subscribed`, the switch flipped back during the request
// with no patch, and a second click before it landed sent the same value again.
describe('useSetFolderSubscription', () => {
  it('patches the tree optimistically, before the request resolves', async () => {
    const client = seededClient()
    let resolve!: () => void
    mocks.setMailFolderSubscription.mockReturnValue(new Promise<void>(res => { resolve = res }))
    const { result } = renderHook(() => useSetFolderSubscription(), { wrapper: wrapperFor(client) })

    act(() => { result.current.mutate({ path: 'Projects', subscribed: false }) })

    const tree = client.getQueryData<MailFolderNode[]>(mailKeys.folders(ACC))!
    expect(tree.find(n => n.path === 'Projects')!.subscribed).toBe(false)

    await act(async () => { resolve(); await settle() })
  })

  it('finds and patches a nested folder', async () => {
    const client = seededClient()
    mocks.setMailFolderSubscription.mockResolvedValue(undefined)
    const { result } = renderHook(() => useSetFolderSubscription(), { wrapper: wrapperFor(client) })

    await act(async () => {
      result.current.mutate({ path: 'Parent/Child', subscribed: false })
      await settle()
    })

    const tree = client.getQueryData<MailFolderNode[]>(mailKeys.folders(ACC))!
    expect(tree.find(n => n.path === 'Parent')!.children[0]!.subscribed).toBe(false)
  })

  it('rolls back the patch when the request fails', async () => {
    const client = seededClient()
    mocks.setMailFolderSubscription.mockRejectedValue(new Error('nope'))
    const { result } = renderHook(() => useSetFolderSubscription(), { wrapper: wrapperFor(client) })

    await act(async () => {
      result.current.mutate({ path: 'Projects', subscribed: false })
      await settle()
    })

    const tree = client.getQueryData<MailFolderNode[]>(mailKeys.folders(ACC))!
    expect(tree.find(n => n.path === 'Projects')!.subscribed).toBe(true)
  })

  it('carries the active account and the chosen value on the wire', async () => {
    const client = seededClient()
    mocks.setMailFolderSubscription.mockResolvedValue(undefined)
    const { result } = renderHook(() => useSetFolderSubscription(), { wrapper: wrapperFor(client) })

    await act(async () => {
      result.current.mutate({ path: 'Projects', subscribed: false })
      await settle()
    })

    expect(mocks.setMailFolderSubscription).toHaveBeenCalledWith(
      'Projects', false, { accountId: 'primary' })
  })

  it('reconciles with the server once the request lands', async () => {
    const client = seededClient()
    mocks.setMailFolderSubscription.mockResolvedValue(undefined)
    const invalidate = vi.spyOn(client, 'invalidateQueries')
    const { result } = renderHook(() => useSetFolderSubscription(), { wrapper: wrapperFor(client) })

    await act(async () => {
      result.current.mutate({ path: 'Projects', subscribed: false })
      await settle()
    })

    expect(invalidate).toHaveBeenCalledWith({ queryKey: mailKeys.folders(ACC) })
  })
})
