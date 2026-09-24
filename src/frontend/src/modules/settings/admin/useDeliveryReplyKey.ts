import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../../api.js'
import { invalidateOnSettled } from '../invalidateOnSettled'

/** The key the mail server presents to apply guests' replies at delivery, and the switch that
    opens that door — Administration > Application, `api/DeliveryReplyKey`. The key itself is
    returned once, by the generation, and never by the query. */
export interface DeliveryReplyKey {
  configured: boolean
  enabled: boolean
  createdAt?: string
  lastCallAt?: string
}

export interface DeliveryReplyKeyGenerated {
  key: string
}

const DELIVERY_KEY = ['adminDeliveryReplyKey'] as const

// The generated key lives in the mutation's variables/data: gone from the cache the moment the
// dialog stops observing it.
const FORGET_KEY = { gcTime: 0 } as const

export function useDeliveryReplyKey() {
  return useQuery<DeliveryReplyKey>({
    queryKey: DELIVERY_KEY,
    queryFn: () => api.adminGetDeliveryReplyKey(),
  })
}

export function useGenerateDeliveryKey() {
  const client = useQueryClient()
  return useMutation({
    ...FORGET_KEY,
    mutationFn: () => api.adminGenerateDeliveryReplyKey(),
    onSettled: invalidateOnSettled(client, DELIVERY_KEY),
  })
}

export function useSetDeliveryReplies() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (enabled: boolean) => api.adminSetDeliveryReplies({ enabled }),
    onSettled: invalidateOnSettled(client, DELIVERY_KEY),
  })
}

export function useDeleteDeliveryKey() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => api.adminDeleteDeliveryReplyKey(),
    onSettled: invalidateOnSettled(client, DELIVERY_KEY),
  })
}
