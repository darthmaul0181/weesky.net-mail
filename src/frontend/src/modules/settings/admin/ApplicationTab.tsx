import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import LoadingBlock from '../../../components/LoadingBlock'
import {
  APP_SETTING_KEYS, installableOf, useAppSettings, useSetAppSetting,
} from '../../../hooks/useAppSettings'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import DeliveryRepliesSection from './DeliveryRepliesSection'
import SchedulingAccountSection from './SchedulingAccountSection'
import type { AddToast } from '../../../hooks/useToasts'

interface Props {
  addToast: AddToast
}

/** Whether the webmail advertises itself as an installable app, and under what name: instance-wide,
 * so admin only. Switching it off uninstalls nobody; it stops offering the app to others. */
export default function ApplicationTab({ addToast }: Props) {
  const { t } = useTranslation('admin')
  const { data: settings, isLoading, isError } = useAppSettings()
  const setSetting = useSetAppSetting()
  const [name, setName] = useState('')
  const [shortName, setShortName] = useState('')

  // Seeded during render from each new answer object, never in an effect, which mounted the inputs
  // empty for one frame (CI read it). A deep-equal refetch keeps the same object, reseeding nothing,
  // so the catch below reverts a refused field itself (docs/architecture-settings.md).
  const [seeded, setSeeded] = useState<typeof settings>(undefined)
  if (settings && settings !== seeded) {
    setSeeded(settings)
    setName(settings[APP_SETTING_KEYS.name] ?? '')
    setShortName(settings[APP_SETTING_KEYS.shortName] ?? '')
  }

  if (isLoading) return <LoadingBlock />
  if (isError || !settings) return <p>{t('application.loadFailed')}</p>

  // Narrowed once, here: a nested function below closes over this rather than over `settings`
  // itself, since TypeScript's narrowing from the guard above does not reach into a closure.
  const s = settings
  const enabled = installableOf(s)

  async function save(key: string, value: string, message: string) {
    try {
      await setSetting.mutateAsync({ key, value })
      addToast(message)
    } catch (error) {
      addToast(apiErrorMessage(error, t('application.saveFailed')), 'error')
    }
  }

  async function saveNames() {
    let nameSaved = false
    let shortNameSaved = false
    try {
      await setSetting.mutateAsync({ key: APP_SETTING_KEYS.name, value: name })
      nameSaved = true
      await setSetting.mutateAsync({ key: APP_SETTING_KEYS.shortName, value: shortName })
      shortNameSaved = true
      addToast(t('application.nameSaved'))
    } catch (error) {
      // Revert only the field whose own save failed: the calls are sequential, so a second refusal
      // leaves the first accepted, and a deep-equal refetch keeps the same object, reseeding nothing.
      if (!nameSaved) setName(s[APP_SETTING_KEYS.name] ?? '')
      if (!shortNameSaved) setShortName(s[APP_SETTING_KEYS.shortName] ?? '')
      addToast(apiErrorMessage(error, t('application.nameSaveFailed')), 'error')
    }
  }

  return (
    <>
      {/* .field-h puts the label beside its control: without the htmlFor/id pair the control
          has no accessible name. */}
      <div className="field-h is-setting">
        <label htmlFor="app-installable">{t('application.installable')}</label>
        <label className="toggle-switch">
          <input
            id="app-installable"
            type="checkbox"
            checked={enabled}
            disabled={setSetting.isPending}
            aria-label={t('application.installable')}
            onChange={event => void save(
              APP_SETTING_KEYS.installable, String(event.target.checked),
              t(event.target.checked
                ? 'application.installableOn'
                : 'application.installableOff'))}
          />
          <span className="toggle-track" />
        </label>
      </div>

      <div className="field-h is-setting">
        <label htmlFor="app-name">{t('application.name')}</label>
        <input
          id="app-name"
          type="text"
          maxLength={60}
          value={name}
          disabled={!enabled || setSetting.isPending}
          onChange={event => setName(event.target.value)}
        />
      </div>

      <div className="field-h is-setting">
        <label htmlFor="app-short-name">{t('application.shortName')}</label>
        <input
          id="app-short-name"
          type="text"
          maxLength={12}
          value={shortName}
          disabled={!enabled || setSetting.isPending}
          onChange={event => setShortName(event.target.value)}
        />
      </div>

      {/* .btn-auto: a primary button is full-width by default, which is a dialog's shape. On a
          settings page it would stretch the whole panel. */}
      <button
        type="button"
        className="btn btn-primary btn-auto"
        disabled={!enabled || setSetting.isPending}
        onClick={() => void saveNames()}
      >
        {setSetting.isPending ? <span className="spinner" /> : t('actions.save', { ns: 'common' })}
      </button>

      <SchedulingAccountSection addToast={addToast} />
      <DeliveryRepliesSection addToast={addToast} />
    </>
  )
}
