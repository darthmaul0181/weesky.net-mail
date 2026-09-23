import type { TFunction } from 'i18next'
import { ApiError } from '../../../api.js'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { RequestTimeoutError } from '../../../lib/withTimeout'

/** A refusal is shown, never swallowed: a mapped code through `apiErrorMessage`, an unmapped 400
 * as the server's sentence after a translated lead, a 404 (account gone) plainly. */
export function schedulingErrorMessage(err: unknown, t: TFunction<'admin'>, fallback: string): string {
  const mapped = apiErrorMessage(err, '')
  if (mapped) return mapped
  if (err instanceof RequestTimeoutError) return t('scheduling.saveTimedOut', { ns: 'admin' })
  if (err instanceof ApiError) {
    // Explicit ns: this file has no useTranslation() of its own for the static key guard to
    // infer a namespace from, `t` arriving only as a parameter.
    if (err.status === 404) return t('scheduling.accountGone', { ns: 'admin' })
    if (err.status === 400 && err.message) {
      return t('scheduling.refusedByServer', { ns: 'admin', message: err.message })
    }
  }
  return fallback
}
