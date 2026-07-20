import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import {
  PREFERENCE_KEYS, pageSizeOf, showPreviewOf, usePreferences, useSetPreference,
} from '../../../hooks/usePreferences'

const PAGE_SIZES = [10, 20, 30, 50, 100]

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
              value={String(pageSizeOf(preferences))}
              disabled={setPreference.isPending}
              onChange={event =>
                save(PREFERENCE_KEYS.pageSize, event.target.value,
                  `The message list now shows ${event.target.value} per page`)}
            >
              {PAGE_SIZES.map(size => <option key={size} value={size}>{size}</option>)}
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
