import { useEffect, useState } from 'react'
import type { ComponentType } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import LoadingBlock from '../../../components/LoadingBlock'
import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import {
  ALL, PREFERENCE_KEYS, ROW_ACTIONS, alwaysShowImagesOf, captureRecipientsOf, composeFormatOf,
  groupConversationsOf, notifyDesktopOf, notifySoundOf, readingPaneOf, rowActionsOf,
  showFolderIconsOf, showPreviewOf, showSpamScoreOf, trustContactsOf, usePreferences,
  useSetPreference,
  type ComposeFormat, type ReadingPane, type RowAction,
} from '../../../hooks/usePreferences'
import {
  desktopPermission, playNewMailSound, requestDesktopPermission,
} from '../../mail/notify/channels'
import ArchiveIcon from '../../../icons/ArchiveIcon'
import JunkIcon from '../../../icons/JunkIcon'
import MailOpenIcon from '../../../icons/MailOpenIcon'
import SlidersIcon from '../../../icons/SlidersIcon'
import TrashIcon from '../../../icons/TrashIcon'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'

const PAGE_SIZES = ['10', '20', '30', '50', '100']

function pageSizeToast(value: string, t: TFunction<'settings'>): string {
  return value === ALL
    ? t('general.pageSize.toastAll')
    : t('general.pageSize.toast', { value })
}

/** Listed in ROW_ACTIONS order, which is the order the row draws them — the page has to show
    the arrangement it is configuring. */
const ROW_ACTION_CHOICES = [
  { value: 'seen', labelKey: 'general.rowActions.seen', Icon: MailOpenIcon },
  { value: 'archive', labelKey: 'general.rowActions.archive', Icon: ArchiveIcon },
  { value: 'junk', labelKey: 'general.rowActions.junk', Icon: JunkIcon },
  { value: 'delete', labelKey: 'general.rowActions.delete', Icon: TrashIcon },
] as const satisfies { value: RowAction; labelKey: string; Icon: ComponentType<{ size?: number }> }[]

const READING_PANES = [
  { value: 'right', labelKey: 'general.readingPane.right', toastKey: 'general.readingPane.rightToast' },
  { value: 'bottom', labelKey: 'general.readingPane.bottom', toastKey: 'general.readingPane.bottomToast' },
  { value: 'none', labelKey: 'general.readingPane.none', toastKey: 'general.readingPane.noneToast' },
] as const satisfies { value: ReadingPane; labelKey: string; toastKey: string }[]

/** No glyph beside these two: PaneGlyph draws three arrangements, a shape a miniature can carry.
    Two editors are not, and a decorative square would say nothing the label does not. */
const COMPOSE_FORMATS = [
  { value: 'html', labelKey: 'general.composeFormat.html', toastKey: 'general.composeFormat.htmlToast' },
  { value: 'text', labelKey: 'general.composeFormat.text', toastKey: 'general.composeFormat.textToast' },
] as const satisfies { value: ComposeFormat; labelKey: string; toastKey: string }[]

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
  /** A lock nothing on this page will lift. `disabled` alone also covers the save in flight, which
      must not look locked — every row carries it for the width of each mutation. */
  locked?: boolean
}

/** The label and the input are siblings under .field-h, so the htmlFor/id pair is the only
    thing naming the control — and the hint stays outside it, or it joins that name. */
function ToggleRow(
  { id, label, hint, checked, disabled, onChange, nested, covered, locked }: ToggleRowProps,
) {
  return (
    <div className={`field-h is-setting${nested ? ' is-child' : ''}${covered ? ' is-covered' : ''}`}>
      <span className="setting-label">
        <label htmlFor={id}>{label}</label>
        <span className="setting-hint">{hint}</span>
      </span>
      <label className={`toggle-switch${locked ? ' is-locked' : ''}`}>
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
  const { t } = useTranslation('settings')
  const { data: preferences, isLoading, isError } = usePreferences()
  const setPreference = useSetPreference()
  const { toasts, addToast, removeToast } = useToasts()

  const chosenActions = preferences ? rowActionsOf(preferences) : []

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
      t(on ? 'general.notifySound.on' : 'general.notifySound.off'))
  }

  async function toggleDesktop(on: boolean) {
    if (!on) {
      await save(PREFERENCE_KEYS.notifyDesktop, 'false', t('general.notifyDesktop.off'))
      return
    }

    // Asked inside the click gesture — Safari requires it, and a denied answer must not be
    // stored as an enabled setting that produces nothing.
    const answer = await requestDesktopPermission()
    setPermission(answer)
    if (answer === 'granted') {
      await save(PREFERENCE_KEYS.notifyDesktop, 'true', t('general.notifyDesktop.on'))
    }
  }

  async function save(key: string, value: string, message: string) {
    try {
      await setPreference.mutateAsync({ key, value })
      addToast(message)
    } catch (error) {
      addToast(apiErrorMessage(error, t('general.saveFailed')), 'error')
    }
  }

  return (
    <div className="settings-page">
      <div className="settings-page-header">
        <h1 className="settings-page-title"><SlidersIcon size={17} />{t('nav.general')}</h1>
      </div>

      {isLoading && <LoadingBlock />}
      {!isLoading && (isError || !preferences) && <p>{t('general.loadFailed')}</p>}

      {!isLoading && !isError && preferences && (
        <>
          <section className="account-section">
            <h2>{t('general.layout')}</h2>

            <div className="field-h is-setting is-stacked">
              <span className="setting-label">
                <span id="reading-pane-label">{t('general.readingPane.label')}</span>
                <span className="setting-hint">{t('general.readingPane.hint')}</span>
              </span>
              <div className="layout-cards" role="radiogroup" aria-labelledby="reading-pane-label">
                {READING_PANES.map(({ value, labelKey, toastKey }) => (
                  <label key={value} className="layout-card">
                    <PaneGlyph variant={value} />
                    <span className="layout-card-name">
                      <input
                        type="radio"
                        name="reading-pane"
                        value={value}
                        checked={readingPaneOf(preferences) === value}
                        disabled={setPreference.isPending}
                        onChange={() => save(PREFERENCE_KEYS.readingPane, value, t(toastKey))}
                      />
                      {t(labelKey)}
                    </span>
                  </label>
                ))}
              </div>
            </div>

            <div className="field-h is-setting">
              <span className="setting-label">
                <label htmlFor="page-size">{t('general.pageSize.label')}</label>
                <span className="setting-hint">{t('general.pageSize.hint')}</span>
              </span>
              <select
                id="page-size"
                value={preferences[PREFERENCE_KEYS.pageSize]}
                disabled={setPreference.isPending}
                onChange={event =>
                  save(PREFERENCE_KEYS.pageSize, event.target.value, pageSizeToast(event.target.value, t))}
              >
                {PAGE_SIZES.map(size => <option key={size} value={size}>{size}</option>)}
                <option value={ALL}>{t('general.pageSize.all')}</option>
              </select>
            </div>

            <ToggleRow
              id="show-preview"
              label={t('general.preview.label')}
              hint={t('general.preview.hint')}
              checked={showPreviewOf(preferences)}
              disabled={setPreference.isPending}
              onChange={on => save(PREFERENCE_KEYS.showPreview, String(on),
                t(on ? 'general.preview.on' : 'general.preview.off'))}
            />

            <ToggleRow
              id="group-conversations"
              label={t('general.groupConversations.label')}
              hint={t('general.groupConversations.hint')}
              checked={groupConversationsOf(preferences)}
              disabled={setPreference.isPending}
              onChange={on => save(PREFERENCE_KEYS.groupConversations, String(on),
                t(on ? 'general.groupConversations.on' : 'general.groupConversations.off'))}
            />

            <ToggleRow
              id="show-folder-icons"
              label={t('general.folderIcons.label')}
              hint={t('general.folderIcons.hint')}
              checked={showFolderIconsOf(preferences)}
              disabled={setPreference.isPending}
              onChange={on => save(PREFERENCE_KEYS.showFolderIcons, String(on),
                t(on ? 'general.folderIcons.on' : 'general.folderIcons.off'))}
            />

            <div className="field-h is-setting is-stacked">
              <span className="setting-label">
                <span id="row-actions-label">{t('general.rowActions.label')}</span>
                <span className="setting-hint">{t('general.rowActions.hint')}</span>
              </span>
              {/* The fill is the state, so there is no box to tick: aria-pressed is what carries
                  that to anything not looking at the colour. */}
              <div className="action-chips" role="group" aria-labelledby="row-actions-label">
                {ROW_ACTION_CHOICES.map(({ value, labelKey, Icon }) => {
                  const on = chosenActions.includes(value)
                  const label = t(labelKey)
                  return (
                    <button
                      key={value}
                      type="button"
                      className={`action-chip${on ? ' is-on' : ''}`}
                      aria-pressed={on}
                      disabled={setPreference.isPending}
                      onClick={() => save(
                        PREFERENCE_KEYS.rowActions,
                        // Rebuilt from the canonical order, never from click order, so the stored
                        // string is the one the list already renders.
                        ROW_ACTIONS.filter(a => a === value ? !on : chosenActions.includes(a)).join(','),
                        t(on ? 'general.rowActions.off' : 'general.rowActions.on', { action: label }))}
                    >
                      <Icon size={16} />
                      {label}
                    </button>
                  )
                })}
              </div>
            </div>
          </section>

          <section className="account-section">
            <h2>{t('general.privacy')}</h2>

            <ToggleRow
              id="always-show-images"
              label={t('general.remoteImages.label')}
              hint={t('general.remoteImages.hint')}
              checked={alwaysShowImagesOf(preferences)}
              disabled={setPreference.isPending}
              onChange={on => save(PREFERENCE_KEYS.alwaysShowImages, String(on),
                t(on ? 'general.remoteImages.on' : 'general.remoteImages.off'))}
            />

            <ToggleRow
              id="trust-contacts"
              label={t('general.trustContacts.label')}
              hint={t(alwaysShowImagesOf(preferences)
                ? 'general.trustContacts.hintCovered'
                : 'general.trustContacts.hint')}
              nested
              covered={alwaysShowImagesOf(preferences)}
              checked={trustContactsOf(preferences)}
              disabled={setPreference.isPending || alwaysShowImagesOf(preferences)}
              onChange={on => save(PREFERENCE_KEYS.trustContacts, String(on),
                t(on ? 'general.trustContacts.on' : 'general.trustContacts.off'))}
            />

            <ToggleRow
              id="show-spam-score"
              label={t('general.spamScore.label')}
              hint={t('general.spamScore.hint')}
              checked={showSpamScoreOf(preferences)}
              disabled={setPreference.isPending}
              onChange={on => save(PREFERENCE_KEYS.showSpamScore, String(on),
                t(on ? 'general.spamScore.on' : 'general.spamScore.off'))}
            />
          </section>

          <section className="account-section">
            <h2>{t('general.composing')}</h2>

            <div className="field-h is-setting is-stacked">
              <span className="setting-label">
                <span id="compose-format-label">{t('general.composeFormat.label')}</span>
                <span className="setting-hint">{t('general.composeFormat.hint')}</span>
              </span>
              <div className="layout-cards" role="radiogroup" aria-labelledby="compose-format-label">
                {COMPOSE_FORMATS.map(({ value, labelKey, toastKey }) => (
                  <label key={value} className="layout-card">
                    <span className="layout-card-name">
                      <input
                        type="radio"
                        name="compose-format"
                        value={value}
                        checked={composeFormatOf(preferences) === value}
                        disabled={setPreference.isPending}
                        onChange={() => save(PREFERENCE_KEYS.composeFormat, value, t(toastKey))}
                      />
                      {t(labelKey)}
                    </span>
                  </label>
                ))}
              </div>
            </div>

            <ToggleRow
              id="capture-recipients"
              label={t('general.captureRecipients.label')}
              hint={t('general.captureRecipients.hint')}
              checked={captureRecipientsOf(preferences)}
              disabled={setPreference.isPending}
              onChange={on => save(PREFERENCE_KEYS.captureRecipients, String(on),
                t(on ? 'general.captureRecipients.on' : 'general.captureRecipients.off'))}
            />
          </section>

          <section className="account-section">
            <h2>{t('general.notifications.heading')}</h2>

            <ToggleRow
              id="notify-sound"
              label={t('general.notifySound.label')}
              hint={t('general.notifySound.hint')}
              checked={notifySoundOf(preferences)}
              disabled={setPreference.isPending}
              onChange={toggleSound}
            />

            {/* Blocked disables it: a denied origin is never re-prompted, so a click would be a
                no-op. The visibility refresh above is what makes it reachable again. */}
            <ToggleRow
              id="notify-desktop"
              label={t('general.notifyDesktop.label')}
              hint={t('general.notifyDesktop.hint')}
              checked={notifyDesktopOf(preferences) && permission === 'granted'}
              disabled={setPreference.isPending || blocked || unsupported || insecure}
              locked={blocked || unsupported || insecure}
              onChange={toggleDesktop}
            />

            {blocked && (
              <p className="settings-note">{t('general.notifications.blocked')}</p>
            )}
            {insecure && (
              <p className="settings-note">{t('general.notifications.insecure')}</p>
            )}
            {unsupported && (
              <p className="settings-note">{t('general.notifications.unsupported')}</p>
            )}
          </section>
        </>
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
