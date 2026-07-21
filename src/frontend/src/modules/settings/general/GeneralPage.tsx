import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import {
  PREFERENCE_KEYS, showPreviewOf, usePreferences, useSetPreference,
} from '../../../hooks/usePreferences'

const PAGE_SIZE_OPTIONS = [
  { value: '10', label: '10' },
  { value: '20', label: '20' },
  { value: '30', label: '30' },
  { value: '50', label: '50' },
  { value: '100', label: '100' },
  { value: 'all', label: 'All' },
]

function pageSizeToast(value: string): string {
  return value === 'all'
    ? 'The message list now shows every message'
    : `The message list now shows ${value} per page`
}

/**
 * Settings that shape the app rather than the account. The values come from the backend with
 * its defaults already filled in, so this page never has to know what a default is.
 */
export default function GeneralPage() {
  const { data: preferences, isLoading, isError } = usePreferences()
  const setPreference = useSetPreference()
  const { toasts, addToast, removeToast } = useToasts()

  async function save(key: string, value: string, message: string) {
    try {
      await setPreference.mutateAsync({ key, value })
      addToast(message)
    } catch (error) {
      addToast(error instanceof Error ? error.message : 'Could not save the setting', 'error')
    }
  }

  return (
    <div className="settings-page">
      <h1>General</h1>

      {isLoading && <p>Loading…</p>}
      {!isLoading && (isError || !preferences) && <p>Could not load the settings.</p>}

      {!isLoading && !isError && preferences && (
        <>
          <div className="field-h is-setting">
            <label htmlFor="page-size">Messages per page</label>
            <select
              id="page-size"
              value={preferences[PREFERENCE_KEYS.pageSize]}
              disabled={setPreference.isPending}
              onChange={event =>
                save(PREFERENCE_KEYS.pageSize, event.target.value, pageSizeToast(event.target.value))}
            >
              {PAGE_SIZE_OPTIONS.map(option =>
                <option key={option.value} value={option.value}>{option.label}</option>)}
            </select>
          </div>

          <div className="field-h is-setting">
            <label htmlFor="show-preview">Preview in the message list</label>
            <label className="toggle-switch">
              <input
                id="show-preview"
                type="checkbox"
                checked={showPreviewOf(preferences)}
                disabled={setPreference.isPending}
                onChange={event =>
                  save(PREFERENCE_KEYS.showPreview, String(event.target.checked),
                    event.target.checked ? 'Previews are shown' : 'Previews are hidden')}
              />
              <span className="toggle-track" />
            </label>
          </div>
        </>
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
