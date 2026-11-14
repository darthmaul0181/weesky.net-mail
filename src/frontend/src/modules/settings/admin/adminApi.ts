import { type RequestOptions, request } from '../../../lib/http'
import type { Quota } from '../../../types/account'
import type {
  AdminDomain, AdminDomainPayload, AdminUser, AdminUserPayload, VirtualDomain,
} from './adminTypes'
import type { ExternalDomain, ExternalDomainPayload } from './useExternalDomains'
import type {
  SchedulingAccount, SchedulingAccountPayload, SchedulingAccountTestResult,
} from './useSchedulingAccount'
import type { DeliveryReplyKey, DeliveryReplyKeyGenerated } from './useDeliveryReplyKey'

/** Administration: users, domains, external domains, the scheduling account, delivery replies. */
export const adminApi = {
  adminGetUsers: () =>
    request<AdminUser[]>('GET', '/api/Admin/users'),

  adminCreateUser: (payload: AdminUserPayload) =>
    request<AdminUser>('POST', '/api/Admin/users', payload),

  adminUpdateUser: (id: number, payload: AdminUserPayload) =>
    request<AdminUser>('PUT', `/api/Admin/users/${id}`, payload),

  adminDeleteUser: (id: number) =>
    request<null>('DELETE', `/api/Admin/users/${id}`),

  adminGetDomains: () =>
    request<AdminDomain[]>('GET', '/api/Admin/domains'),

  adminCreateDomain: (payload: AdminDomainPayload) =>
    request<AdminDomain>('POST', '/api/Admin/domains', payload),

  adminUpdateDomain: (id: string, payload: AdminDomainPayload) =>
    request<AdminDomain>('PUT', `/api/Admin/domains/${id}`, payload),

  // deleteAliases acknowledges the cascade: the aliases anchored on the domain go with it. Sent
  // only once the user has confirmed, so an unacknowledged call is refused server-side too.
  adminDeleteDomain: (id: string, deleteAliases = false) =>
    request<null>('DELETE', `/api/Admin/domains/${id}${deleteAliases ? '?deleteAliases=true' : ''}`),

  adminGetUserQuota: (id: number) =>
    request<Quota>('GET', `/api/Admin/users/${id}/quota`),

  adminGetVirtualDomains: () =>
    request<VirtualDomain[]>('GET', '/api/Admin/domains/virtuals'),

  adminAddVirtualDomainOwner: (domainId: string, userId: number) =>
    request<VirtualDomain>('PUT', `/api/Admin/domains/virtuals/${domainId}`, { userId }),

  adminRemoveVirtualDomainOwner: (domainId: string, userId: number) =>
    request<null>('DELETE', `/api/Admin/domains/virtuals/${domainId}/${userId}`),

  adminGetExternalDomains: () =>
    request<ExternalDomain[]>('GET', '/api/Admin/domains/external'),

  adminCreateExternalDomain: (domain: ExternalDomainPayload) =>
    request<ExternalDomain>('POST', '/api/Admin/domains/external', domain),

  adminUpdateExternalDomain: (id: string, domain: ExternalDomainPayload) =>
    request<null>('PUT', `/api/Admin/domains/external/${id}`, domain),

  adminDeleteExternalDomain: (id: string) =>
    request<null>('DELETE', `/api/Admin/domains/external/${id}`),

  adminGetSchedulingAccount: () =>
    request<SchedulingAccount>('GET', '/api/SchedulingAccount'),

  adminSaveSchedulingAccount: (account: SchedulingAccountPayload, options?: RequestOptions) =>
    request<null>('PUT', '/api/SchedulingAccount', account, options),

  adminDeleteSchedulingAccount: () =>
    request<null>('DELETE', '/api/SchedulingAccount'),

  // `account` is optional: no body tests and records the stored one, a body tests those values
  // without persisting anything — the same request shape either way.
  adminTestSchedulingAccount: (account?: SchedulingAccountPayload) =>
    request<SchedulingAccountTestResult>('POST', '/api/SchedulingAccount/Test', account),

  adminGetDeliveryReplyKey: () =>
    request<DeliveryReplyKey>('GET', '/api/DeliveryReplyKey'),

  adminGenerateDeliveryReplyKey: () =>
    request<DeliveryReplyKeyGenerated>('POST', '/api/DeliveryReplyKey'),

  adminSetDeliveryReplies: (body: { enabled: boolean }) =>
    request<null>('PUT', '/api/DeliveryReplyKey', body),

  adminDeleteDeliveryReplyKey: () =>
    request<null>('DELETE', '/api/DeliveryReplyKey'),
}
