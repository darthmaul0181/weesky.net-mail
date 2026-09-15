import type { TFunction } from 'i18next'
import { ApiError } from '../../../api.js'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'

/**
 * A 404 (the key deleted from elsewhere) says so before anything else: `DeleteDeliveryReplyKey`
 * answers a 404 carrying `delivery_key_missing`, which is also a mapped `CODES` entry (for the
 * 409 a locked toggle can hit) — tested first, that mapped text would read "Generate a key…" on
 * a delete refused because the key is already gone, leaving `keyGone` unreachable. Every other
 * mapped code (409s) still goes through `apiErrorMessage`.
 */
export function deliveryErrorMessage(err: unknown, t: TFunction<'admin'>, fallback: string): string {
  if (err instanceof ApiError && err.status === 404) return t('deliveryReplies.keyGone', { ns: 'admin' })
  const mapped = apiErrorMessage(err, '')
  if (mapped) return mapped
  return fallback
}
