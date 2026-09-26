import { useQuery } from '@tanstack/react-query'
import { api } from '../../../api.js'
import type { Quota } from '../../../types/account'

const QUOTA_KEY = ['accountQuota'] as const

// A plain effect swallowed every failure and never retried, so a blip left the block empty for
// the rest of the session. TanStack's default retry and cache are what this page was missing.
export function useQuota() {
  return useQuery<Quota | null>({
    queryKey: QUOTA_KEY,
    queryFn: () => api.getQuota(),
  })
}
