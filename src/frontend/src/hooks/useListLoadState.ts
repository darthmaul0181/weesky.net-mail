import type { UseQueryResult } from '@tanstack/react-query'

export type ListQuery = Pick<UseQueryResult, 'isLoading' | 'isError' | 'data' | 'isFetchedAfterMount'>

/**
 * What a surface draws while its lists load, judged per mount: a list that failed before this
 * surface mounted is a first load again. A refetch of a list holding no data puts it back to
 * pending, so `isLoading` alone would hide the surface and unmount its dialogs on every retry:
 * the loading state is a first load only. A failure stays failed until data arrives.
 *
 * Pure — no toast, no other side effect — so a caller that already announces failure its own way
 * (an inline live region, say) can use the same predicate without a second announcement riding
 * along. `useAdminLists.ts`'s `useListLoad` wraps this with the admin tabs' once-per-episode toast.
 */
export function useListLoadState(queries: ListQuery[]): { firstLoad: boolean; failed: boolean } {
  const firstLoad = queries.some(q => q.isLoading && !q.isFetchedAfterMount)
  const failed = queries.some(q => q.isError || (q.data === undefined && q.isFetchedAfterMount))
  return { firstLoad, failed }
}
