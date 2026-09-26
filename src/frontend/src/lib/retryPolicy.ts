import { RequestTimeoutError } from './withTimeout'

/** Never retried: a 401 (api.ts already cleared the session; a retry delays /login) and a 409
 * `connected_credentials_invalid` (every request fails until the password is re-entered), nor a
 * timed-out read (the server already went silent for its whole deadline). */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  if (error instanceof RequestTimeoutError) return false
  const status = (error as { status?: number } | null)?.status
  if (status === 401 || status === 409) return false
  return failureCount < 2
}
