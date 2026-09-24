import type { QueryClient, QueryKey } from '@tanstack/react-query'

// onSettled, not onSuccess: a refused write must leave the screen on server state rather than
// on an optimistic lie.
export function invalidateOnSettled(client: QueryClient, queryKey: QueryKey) {
  return () => { void client.invalidateQueries({ queryKey }) }
}
