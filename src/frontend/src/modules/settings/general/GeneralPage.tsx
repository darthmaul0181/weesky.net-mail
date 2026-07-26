import { useEffect, useState } from 'react'
import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import {
  ALL, PREFERENCE_KEYS, alwaysShowImagesOf, notifyDesktopOf, notifySoundOf, readingPaneOf,
  showPreviewOf, showSpamScoreOf, usePreferences, useSetPreference, type ReadingPane,
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

const READING_PANES: { value: ReadingPane; label: string; toast: string }[] = [
  { value: 'right', label: 'Right', toast: 'The reader sits beside the message list' },
  { value: 'bottom', label: 'Bottom', toast: 'The reader sits below the message list' },
  { value: 'none', label: 'Hidden', toast: 'Messages will open in place of the list' },
]

/** A miniature of the arrangement — the glyph is the description, like Appearance's
    palette thumbnails. */
function PaneGlyph({ variant }: { variant: ReadingPane }) {
  return (
    <span className={`pane-glyph is-${variant}`} aria-hidden="true">
      <span className="pane-glyph-lines" />
      {variant !== 'none' && <span className="pane-glyph-pane" />}
    </span>
  )
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

          <div className="field-h is-setting">
            <span id="reading-pane-label">Reading pane</span>
            <div className="layout-cards" role="radiogroup" aria-labelledby="reading-pane-label">
              {READING_PANES.map(({ value, label, toast }) => (
                <label key={value} className="layout-card">
                  <PaneGlyph variant={value} />
                  <span className="layout-card-name">
                    <input
                      type="radio"
                      name="reading-pane"
                      value={value}
                      checked={readingPaneOf(preferences) === value}
                      disabled={setPreference.isPending}
                      onChange={() => save(PREFERENCE_KEYS.readingPane, value, toast)}
                    />
                    {label}
                  </span>
                </label>
              ))}
            </div>
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
            id="always-show-images"
            label="Always show remote images"
            checked={alwaysShowImagesOf(preferences)}
            disabled={setPreference.isPending}
            onChange={on => save(PREFERENCE_KEYS.alwaysShowImages, String(on),
              on ? 'Remote images will always load' : 'Remote images stay blocked until you ask')}
          />

          {alwaysShowImagesOf(preferences) && (
            <p className="settings-note">
              Loading them tells the sender you opened the message.
            </p>
          )}

          {/* Disabled until Contacts exists. No preference key is declared for it: nothing can
              write one while the row is disabled, and a registry entry nothing can reach is dead
              code with dead validation. When Contacts ships this becomes a real key. */}
          <ToggleRow
            id="trust-contacts"
            label="Trust my contacts"
            checked={false}
            disabled
            onChange={() => {}}
          />

          <p className="settings-note">Available once Contacts ships.</p>

          <ToggleRow
            id="show-spam-score"
            label="Show the spam score in the message reader"
            checked={showSpamScoreOf(preferences)}
            disabled={setPreference.isPending}
            onChange={on => save(PREFERENCE_KEYS.showSpamScore, String(on),
              on ? 'The spam score is shown' : 'The spam score is hidden')}
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
