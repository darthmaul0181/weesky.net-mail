import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { useEmptyFolder, mailKeys } from './queries'
import { settle } from '../../test-utils'

const mocks = vi.hoisted(() => ({ emptyFolder: vi.fn() }))
vi.mock('../../api.js', () => ({ api: mocks }))
vi.mock('../../contexts/AuthContext', () => ({ useAuth: () => ({ activeAccount: { id: 'primary' } }) }))
vi.mock('../../hooks/usePreferences', () => ({
  usePreferences: () => ({ data: {} }), notifiesOf: () => false,
}))

const ACC = 'primary'
function seededClient() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  client.setQueryData(mailKeys.messages(ACC, 'Trash', 0, 50), {
    messages: [{ uid: 5, seen: false }, { uid: 6, seen: true }], total: 2, page: 0, pageSize: 50,
  })
  client.setQueryData(mailKeys.folders(ACC), [
    { path: 'Trash', name: 'Trash', specialUse: 'trash', total: 2, unread: 1, children: [] },
    { path: 'Projects', name: 'Projects', specialUse: null, total: 3, unread: 2, children: [] },
  ])
  return client
}
function wrapperFor(client: QueryClient) {
  function wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>
  }
  return wrapper
}

beforeEach(() => { vi.clearAllMocks(); mocks.emptyFolder.mockResolvedValue(undefined) })

describe('useEmptyFolder', () => {
  it('purges: drops the source caches and zeroes its counts', async () => {
    const client = seededClient()
    const { result } = renderHook(() => useEmptyFolder(), { wrapper: wrapperFor(client) })

    await act(async () => { result.current.mutate({ folderPath: 'Trash' }); await settle() })

    expect(mocks.emptyFolder).toHaveBeenCalledWith('Trash', null)
    expect(client.getQueryData(mailKeys.messages(ACC, 'Trash', 0, 50))).toBeUndefined()
    const tree = client.getQueryData(mailKeys.folders(ACC)) as { path: string; total: number; unread: number }[]
    const trash = tree.find(n => n.path === 'Trash')!
    expect(trash.total).toBe(0)
    expect(trash.unread).toBe(0)
  })

  it('moves: zeroes the source and adds its counts to the target', async () => {
    const client = seededClient()
    client.setQueryData(mailKeys.messages(ACC, 'Projects', 0, 50), {
      messages: [{ uid: 1, seen: false }], total: 3, page: 0, pageSize: 50,
    })
    const { result } = renderHook(() => useEmptyFolder(), { wrapper: wrapperFor(client) })

    await act(async () => { result.current.mutate({ folderPath: 'Projects', targetFolderPath: 'Trash' }); await settle() })

    expect(mocks.emptyFolder).toHaveBeenCalledWith('Projects', 'Trash')
    const tree = client.getQueryData(mailKeys.folders(ACC)) as { path: string; total: number; unread: number }[]
    expect(tree.find(n => n.path === 'Projects')!.total).toBe(0)
    expect(tree.find(n => n.path === 'Trash')!.total).toBe(5) // 2 + 3
    expect(tree.find(n => n.path === 'Trash')!.unread).toBe(3) // 1 + 2
  })

  it('rolls the caches back when the request fails', async () => {
    const client = seededClient()
    mocks.emptyFolder.mockRejectedValue(new Error('nope'))
    const { result } = renderHook(() => useEmptyFolder(), { wrapper: wrapperFor(client) })

    await act(async () => { result.current.mutate({ folderPath: 'Trash' }); await settle() })

    expect(client.getQueryData(mailKeys.messages(ACC, 'Trash', 0, 50))).toBeDefined()
    const tree = client.getQueryData(mailKeys.folders(ACC)) as { path: string; total: number }[]
    expect(tree.find(n => n.path === 'Trash')!.total).toBe(2)
  })
})
