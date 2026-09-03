import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { contactKeys } from './queries'
import { useContactPhotoUrl } from './useContactPhotoUrl'

vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'acc' }))

beforeEach(() => {
  // jsdom n'implémente pas l'API des URL objet.
  URL.createObjectURL = vi.fn(() => 'blob:photo')
  URL.revokeObjectURL = vi.fn()
})

function withCache(seed: (client: QueryClient) => void) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, enabled: false } } })
  seed(client)
  function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>
  }
  return Wrapper
}

describe('useContactPhotoUrl', () => {
  it('serves the blob cached under the card it was read at', () => {
    const wrapper = withCache(c =>
      c.setQueryData(contactKeys.photo('acc', 'c1', 'h1'), new Blob(['x'])))

    const { result } = renderHook(() => useContactPhotoUrl('c1', true, 'h1'), { wrapper })

    expect(result.current).toBe('blob:photo')
  })

  // Le retrait et le remplacement sont le même fait : la carte a changé, donc la clé aussi, donc
  // il n'y a rien de périmé à servir (décision 10).
  it('does not serve it under the next card hash', () => {
    const wrapper = withCache(c =>
      c.setQueryData(contactKeys.photo('acc', 'c1', 'h1'), new Blob(['x'])))

    const { result } = renderHook(() => useContactPhotoUrl('c1', false, 'h2'), { wrapper })

    expect(result.current).toBeNull()
  })
})
