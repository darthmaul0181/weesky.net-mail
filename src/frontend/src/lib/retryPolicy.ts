/**
 * The app-wide query retry rule. Two statuses a retry cannot rescue: 401, where api.js has
 * already cleared the session and retrying only delays the redirect to /login, and 409
 * `connected_credentials_invalid`, where the account's stored password no longer decrypts and
 * every request under it fails until the user re-enters it.
 */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  const status = (error as { status?: number } | null)?.status
  if (status === 401 || status === 409) return false
  return failureCount < 2
}
