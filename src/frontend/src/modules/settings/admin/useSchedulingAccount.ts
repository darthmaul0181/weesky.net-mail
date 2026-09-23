import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api } from '../../../api.js'
import { withTimeout } from '../../../lib/withTimeout'

export type SchedulingSecurity = 'None' | 'StartTls' | 'SslOnConnect'

/** The invitation service account (`api/SchedulingAccount`, Administration > Application). The
 * password is write-only: the API never returns it. */
export interface SchedulingAccount {
  configured: boolean
  host?: string
  port?: number
  security?: SchedulingSecurity
  login?: string
  passwordStored: boolean
  passwordReadable: boolean
  /** Mail:AllowCleartext on the server: whether "None" can be saved at all. */
  allowCleartext: boolean
  lastTestAt?: string
  lastTestOk?: boolean
}

export interface SchedulingAccountPayload {
  host: string
  port: number
  security: SchedulingSecurity
  login: string
  /** Empty or omitted keeps the stored password — allowed only when host and port are unchanged. */
  password?: string
}

export interface SchedulingAccountTestResult {
  ok: boolean
  error?: string
}

const SCHEDULING_ACCOUNT_KEY = ['adminSchedulingAccount'] as const

// api.ts sets no timeout of its own, and the dialog blocks every way out while a save is pending.
const SAVE_TIMEOUT_MS = 30_000

// Their variables carry the typed password: gone from the cache as soon as no dialog observes them.
const FORGET_PASSWORD = { gcTime: 0 } as const

export function useSchedulingAccount() {
  return useQuery<SchedulingAccount>({
    queryKey: SCHEDULING_ACCOUNT_KEY,
    queryFn: () => api.adminGetSchedulingAccount(),
  })
}

// onSettled, not onSuccess: a refused write must leave the screen on server state rather than
// on an optimistic lie.
function refreshAccount(client: QueryClient) {
  return () => { void client.invalidateQueries({ queryKey: SCHEDULING_ACCOUNT_KEY }) }
}

export function useSaveSchedulingAccount() {
  const client = useQueryClient()
  return useMutation({
    ...FORGET_PASSWORD,
    mutationFn: (account: SchedulingAccountPayload) =>
      withTimeout(signal => api.adminSaveSchedulingAccount(account, { signal }), SAVE_TIMEOUT_MS),
    onSettled: refreshAccount(client),
  })
}

export function useDeleteSchedulingAccount() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => api.adminDeleteSchedulingAccount(),
    onSettled: refreshAccount(client),
  })
}

// Test-without-body persists lastTestAt/lastTestOk on the stored account and is what the card's
// own "Tester" button calls; test-with-body (the dialog, trying values before Save) persists
// nothing, so only the no-argument call needs to refresh the query.
export function useTestSchedulingAccount() {
  const client = useQueryClient()
  return useMutation({
    ...FORGET_PASSWORD,
    mutationFn: (account?: SchedulingAccountPayload) =>
      api.adminTestSchedulingAccount(account),
    onSettled: (_data, _error, account) => { if (!account) refreshAccount(client)() },
  })
}
