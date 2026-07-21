import { useState } from 'react'
import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import {
  ALL, PREFERENCE_KEYS, notifyDesktopOf, notifySoundOf, showPreviewOf,
  usePreferences, useSetPreference,
} from '../../../hooks/usePreferences'
import {
  desktopPermission, playNewMailSound, requestDesktopPermission,
} from '../../mail/notify/channels'

const PAGE_SIZE_OPTIONS = [
  { value: '10', label: '10' },
  { value: '20', label: '20' },
  { value: '30', label: '30' },
  { value: '50', label: '50' },
  { value: '100', label: '100' },
  { value: ALL, label: 'All' },
]

function pageSizeToast(value: string): string {
  return value === ALL
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

  const [permission, setPermission] = useState(desktopPermission)
  const blocked = permission === 'denied'
  const unsupported = permission === 'unsupported'

  async function toggleSound(on: boolean) {
    await save(PREFERENCE_KEYS.notifySound, String(on),
      on ? 'New mail will play a sound' : 'New mail will be silent')
    // Played inside the click: it proves the sound works and earns the browser engagement
    // that lets a later, unattended notification play at all.
    if (on) playNewMailSound()
  }

  async function toggleDesktop(on: boolean) {
    if (!on) {
      await save(PREFERENCE_KEYS.notifyDesktop, 'false', 'Desktop notifications are off')
      return
    }

    // Asked inside the click gesture — Safari requires it, and a denied answer must not be
    // stored as an enabled setting that produces nothing.
    const answer = await requestDesktopPermission()
    setPermission(answer)
    if (answer === 'granted') {
      await save(PREFERENCE_KEYS.notifyDesktop, 'true', 'New mail will raise a notification')
    }
  }

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

          <div className="field-h is-setting">
            <label htmlFor="notify-sound">Sound on new mail</label>
            <label className="toggle-switch">
              <input
                id="notify-sound"
                type="checkbox"
                checked={notifySoundOf(preferences)}
                disabled={setPreference.isPending}
                onChange={event => toggleSound(event.target.checked)}
              />
              <span className="toggle-track" />
            </label>
          </div>

          <div className="field-h is-setting">
            <label htmlFor="notify-desktop">Desktop notification on new mail</label>
            <label className="toggle-switch">
              <input
                id="notify-desktop"
                type="checkbox"
                checked={notifyDesktopOf(preferences) && permission === 'granted'}
                disabled={setPreference.isPending || unsupported}
                onChange={event => toggleDesktop(event.target.checked)}
              />
              <span className="toggle-track" />
            </label>
          </div>

          {blocked && (
            <p className="settings-note">
              Notifications are blocked by your browser for this site. Allow them in its site
              settings, then switch this back on.
            </p>
          )}
          {unsupported && (
            <p className="settings-note">
              This browser does not support desktop notifications. On iPhone and iPad they work
              only once the site is added to the home screen.
            </p>
          )}
        </>
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
