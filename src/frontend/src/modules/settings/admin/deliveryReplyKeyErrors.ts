import type { TFunction } from 'i18next'
import { ApiError } from '../../../api.js'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'

/** A 404 is tested first: it carries `delivery_key_missing`, also a mapped code (for a locked
 * toggle's 409), whose "Generate a key…" would hide `keyGone` on a delete of a key already gone.
 * Other codes go through `apiErrorMessage`. */
export function deliveryErrorMessage(err: unknown, t: TFunction<'admin'>, fallback: string): string {
  if (err instanceof ApiError && err.status === 404) return t('deliveryReplies.keyGone', { ns: 'admin' })
  const mapped = apiErrorMessage(err, '')
  if (mapped) return mapped
  return fallback
}
