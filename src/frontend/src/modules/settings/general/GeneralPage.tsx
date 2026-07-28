import { useEffect, useState } from 'react'
import LoadingBlock from '../../../components/LoadingBlock'
import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import {
  ALL, PREFERENCE_KEYS, alwaysShowImagesOf, captureRecipientsOf, notifyDesktopOf, notifySoundOf,
  readingPaneOf, showPreviewOf, showSpamScoreOf, trustContactsOf, usePreferences, useSetPreference,
  type ReadingPane,
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
  hint: string
  checked: boolean
  disabled: boolean
  onChange: (on: boolean) => void
  /** Indented under the row it depends on, and greyed while that row makes it moot. */
  nested?: boolean
  covered?: boolean
}

/** The label and the input are siblings under .field-h, so the htmlFor/id pair is the only
    thing naming the control — and the hint stays outside it, or it joins that name. */
function ToggleRow(
  { id, label, hint, checked, disabled, onChange, nested, covered }: ToggleRowProps,
) {
  return (
    <div className={`field-h is-setting${nested ? ' is-child' : ''}${covered ? ' is-covered' : ''}`}>
      <span className="setting-label">
        <label htmlFor={id}>{label}</label>
        <span className="setting-hint">{hint}</span>
      </span>
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

      {isLoading && <LoadingBlock />}
      {!isLoading && (isError || !preferences) && <p>Could not load the settings.</p>}

      {!isLoading && !isError && preferences && (
        <>
          <section className="account-section">
            <h2>Layout</h2>

            <div className="field-h is-setting is-stacked">
              <span className="setting-label">
                <span id="reading-pane-label">Reading pane</span>
                <span className="setting-hint">
                  Where a message opens — beside the list, under it, or full width.
                </span>
              </span>
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

            <div className="field-h is-setting">
              <span className="setting-label">
                <label htmlFor="page-size">Messages per page</label>
                <span className="setting-hint">How many rows the list loads at a time.</span>
              </span>
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
              hint="Show the first line of each message under its subject."
              checked={showPreviewOf(preferences)}
              disabled={setPreference.isPending}
              onChange={on => save(PREFERENCE_KEYS.showPreview, String(on),
                on ? 'Previews are shown' : 'Previews are hidden')}
            />
          </section>

          <section className="account-section">
            <h2>Privacy &amp; security</h2>

            <ToggleRow
              id="always-show-images"
              label="Always show remote images"
              hint="Loading them tells the sender you opened the message."
              checked={alwaysShowImagesOf(preferences)}
              disabled={setPreference.isPending}
              onChange={on => save(PREFERENCE_KEYS.alwaysShowImages, String(on),
                on ? 'Remote images will always load' : 'Remote images stay blocked until you ask')}
            />

            <ToggleRow
              id="trust-contacts"
              label="Always show images from my contacts"
              hint={alwaysShowImagesOf(preferences)
                ? 'Already covered by the setting above.'
                : 'Images load without asking when the sender is in your address book.'}
              nested
              covered={alwaysShowImagesOf(preferences)}
              checked={trustContactsOf(preferences)}
              disabled={setPreference.isPending || alwaysShowImagesOf(preferences)}
              onChange={on => save(PREFERENCE_KEYS.trustContacts, String(on),
                on ? 'Images from your contacts will load' : 'Images from your contacts stay blocked')}
            />

            <ToggleRow
              id="show-spam-score"
              label="Show the spam score in the message reader"
              hint="Adds a score bar under the recipients when the server sends one."
              checked={showSpamScoreOf(preferences)}
              disabled={setPreference.isPending}
              onChange={on => save(PREFERENCE_KEYS.showSpamScore, String(on),
                on ? 'The spam score is shown' : 'The spam score is hidden')}
            />
          </section>

          <section className="account-section">
            <h2>Composing</h2>

            <ToggleRow
              id="capture-recipients"
              label="Save new recipients to my contacts"
              hint="Anyone you write to for the first time joins your address book."
              checked={captureRecipientsOf(preferences)}
              disabled={setPreference.isPending}
              onChange={on => save(PREFERENCE_KEYS.captureRecipients, String(on),
                on ? 'New recipients will be saved' : 'New recipients will not be saved')}
            />
          </section>

          <section className="account-section">
            <h2>Notifications</h2>

            <ToggleRow
              id="notify-sound"
              label="Sound on new mail"
              hint="Plays a short chime when mail reaches the inbox."
              checked={notifySoundOf(preferences)}
              disabled={setPreference.isPending}
              onChange={toggleSound}
            />

            {/* Blocked disables it: a denied origin is never re-prompted, so a click would be a
                no-op. The visibility refresh above is what makes it reachable again. */}
            <ToggleRow
              id="notify-desktop"
              label="Desktop notification on new mail"
              hint="Your browser will ask for permission the first time."
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
          </section>
        </>
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
