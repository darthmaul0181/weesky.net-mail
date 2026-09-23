import type { UseQueryResult } from '@tanstack/react-query'

export type ListQuery = Pick<UseQueryResult, 'isLoading' | 'isError' | 'data' | 'isFetchedAfterMount'>

/** What a surface draws while its lists load, judged per mount. A refetch of an empty list goes
 * back to pending, so loading means a first load only; a failure stays until data arrives. Pure:
 * `useAdminLists`' `useListLoad` adds the admin tabs' once-per-episode toast. */
export function useListLoadState(queries: ListQuery[]): { firstLoad: boolean; failed: boolean } {
  const firstLoad = queries.some(q => q.isLoading && !q.isFetchedAfterMount)
  const failed = queries.some(q => q.isError || (q.data === undefined && q.isFetchedAfterMount))
  return { firstLoad, failed }
}
