import { useTranslation } from 'react-i18next'
import { useSetPreference } from '../../../hooks/usePreferences'
import type { AddToast } from '../../../hooks/useToasts'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'

/** One preference write, confirmed or refused by a toast — the same on every settings page. */
export function useSavePreference(addToast: AddToast) {
  const { t } = useTranslation('settings')
  const setPreference = useSetPreference()

  async function save(key: string, value: string, message: string) {
    try {
      await setPreference.mutateAsync({ key, value })
      addToast(message)
    } catch (error) {
      addToast(apiErrorMessage(error, t('general.saveFailed')), 'error')
    }
  }

  return { save, saving: setPreference.isPending }
}
