import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api } from '../../../api.js'

/**
 * The admin-curated external mail providers users may attach a connected account from — the
 * only source of external hosts in the product, which is what `ExternalDomainDialog`'s
 * client-side validation exists to guard.
 */
export interface ExternalDomain {
  id: string
  name: string
  imapHost: string
  imapPort: number
  imapSecurity: string
  smtpHost: string
  smtpPort: number
  smtpSecurity: string
  sieveHost: string | null
  sievePort: number | null
  authMode: 'Password' | 'OAuth2'
  oauthAuthorizationUrl: string | null
  oauthTokenUrl: string | null
  oauthScopes: string | null
  oauthClientId: string | null
  /** The secret itself never leaves the backend: this flag is all a reader learns. */
  oauthClientSecretSet: boolean
}

export type ExternalDomainPayload = Omit<ExternalDomain, 'id' | 'oauthClientSecretSet'> & {
  /** Write-only: null on an edit keeps the stored secret. */
  oauthClientSecret: string | null
}

const EXTERNAL_DOMAINS_KEY = ['adminExternalDomains'] as const

export function useExternalDomains() {
  return useQuery<ExternalDomain[]>({
    queryKey: EXTERNAL_DOMAINS_KEY,
    queryFn: async () => (await api.adminGetExternalDomains()) ?? [],
  })
}

// onSettled, not onSuccess: a refused write must leave the screen on server state rather than
// on an optimistic lie.
function refreshList(client: QueryClient) {
  return () => { client.invalidateQueries({ queryKey: EXTERNAL_DOMAINS_KEY }) }
}

export function useCreateExternalDomain() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (domain: ExternalDomainPayload) => api.adminCreateExternalDomain(domain),
    onSettled: refreshList(client),
  })
}

export function useUpdateExternalDomain() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, domain }: { id: string; domain: ExternalDomainPayload }) =>
      api.adminUpdateExternalDomain(id, domain),
    onSettled: refreshList(client),
  })
}

export function useDeleteExternalDomain() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: string) => api.adminDeleteExternalDomain(id),
    onSettled: refreshList(client),
  })
}
