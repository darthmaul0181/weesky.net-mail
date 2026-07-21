import { useEffect, useState } from 'react'
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

type ToggleRowProps = {
  id: string
  label: string
  checked: boolean
  disabled: boolean
  onChange: (on: boolean) => void
}

/** The label and the input are siblings under .field-h, so the htmlFor/id pair is the only
    thing naming the control. */
function ToggleRow({ id, label, checked, disabled, onChange }: ToggleRowProps) {
  return (
    <div className="field-h is-setting">
      <label htmlFor={id}>{label}</label>
      <label className="toggle-switch">
        <input
          id={id}
          type="checkbox"
          checked={checked}
          disabled={disabled}
          onChange={event => onChange(event.target.checked)}
        />
        <span className="toggle-track" />
      </label>
    </div>
  )
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
  // Notifications need a secure context, so plain HTTP hides the API entirely. Same symptom as
  // an old browser, different cure: say which one it is.
  const insecure = permission === 'unsupported' && window.isSecureContext === false
  const unsupported = permission === 'unsupported' && !insecure

  // The permission changes in browser settings while this page sits open — exactly the trip
  // the blocked note sends the user on. Re-read it on the way back.
  useEffect(() => {
    const refresh = () => setPermission(desktopPermission())
    document.addEventListener('visibilitychange', refresh)
    return () => document.removeEventListener('visibilitychange', refresh)
  }, [])

  async function toggleSound(on: boolean) {
    // Played inside the click, before any await: WebKit's transient activation is gone by the
    // time the save resolves, so the gesture wins over the confirmation — a failed save gets
    // both the chime and an error toast.
    if (on) playNewMailSound()
    await save(PREFERENCE_KEYS.notifySound, String(on),
      on ? 'New mail will play a sound' : 'New mail will be silent')
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

          <ToggleRow
            id="show-preview"
            label="Preview in the message list"
            checked={showPreviewOf(preferences)}
            disabled={setPreference.isPending}
            onChange={on => save(PREFERENCE_KEYS.showPreview, String(on),
              on ? 'Previews are shown' : 'Previews are hidden')}
          />

          <ToggleRow
            id="notify-sound"
            label="Sound on new mail"
            checked={notifySoundOf(preferences)}
            disabled={setPreference.isPending}
            onChange={toggleSound}
          />

          {/* Blocked disables it: a denied origin is never re-prompted, so a click would be a
              no-op. The visibility refresh above is what makes it reachable again. */}
          <ToggleRow
            id="notify-desktop"
            label="Desktop notification on new mail"
            checked={notifyDesktopOf(preferences) && permission === 'granted'}
            disabled={setPreference.isPending || blocked || unsupported || insecure}
            onChange={toggleDesktop}
          />

          {blocked && (
            <p className="settings-note">
              Notifications are blocked by your browser for this site. Allow them in its site
              settings, then switch this back on.
            </p>
          )}
          {insecure && (
            <p className="settings-note">
              Desktop notifications need a secure connection. Open this site over HTTPS to switch
              them on.
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
