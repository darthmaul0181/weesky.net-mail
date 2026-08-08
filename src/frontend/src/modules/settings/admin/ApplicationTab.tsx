import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import LoadingBlock from '../../../components/LoadingBlock'
import {
  APP_SETTING_KEYS, installableOf, useAppSettings, useSetAppSetting,
} from '../../../hooks/useAppSettings'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'

interface Props {
  addToast: (message: string, kind?: string) => void
}

/**
 * Whether the webmail advertises itself as an installable app, and under what name.
 *
 * An instance-wide setting, hence reserved to administration: the name is what every user will
 * see under the icon. Switching the toggle off does not uninstall anyone — an already-installed
 * app stays installed, it simply stops being offered to others.
 */
export default function ApplicationTab({ addToast }: Props) {
  const { t } = useTranslation('admin')
  const { data: settings, isLoading, isError } = useAppSettings()
  const setSetting = useSetAppSetting()
  const [name, setName] = useState('')
  const [shortName, setShortName] = useState('')

  // The fields are reseeded from the server's answer: after a save that is the value it kept,
  // after a refusal that is server state rather than an optimistic lie.
  useEffect(() => {
    if (!settings) return
    setName(settings[APP_SETTING_KEYS.name] ?? '')
    setShortName(settings[APP_SETTING_KEYS.shortName] ?? '')
  }, [settings])

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
      // Revert only the field(s) whose own save did not go through. The two calls are
      // sequential, so a refusal on the second one leaves the first already accepted by the
      // server — resetting it too would show a stale value for the moment before the next
      // refetch corrects it back. Invalidating alone is not enough for the field that does need
      // reverting: when the rejected value leaves the server's value unchanged, the refetch
      // returns data deep-equal to what is cached, so React Query's structural sharing keeps the
      // same object reference and the effect above never re-runs.
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
            onChange={event => save(
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
        onClick={saveNames}
      >
        {setSetting.isPending ? <span className="spinner" /> : t('actions.save', { ns: 'common' })}
      </button>
    </>
  )
}
