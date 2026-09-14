import { useEffect, useRef, useState, type FormEvent, type RefObject } from 'react'
import { useTranslation } from 'react-i18next'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import ShieldAlertIcon from '../../../icons/ShieldAlertIcon'
import ShieldCheckIcon from '../../../icons/ShieldCheckIcon'
import { ApiError } from '../../../api.js'
import { useDialogFocusTrap } from '../../../hooks/useDialogFocusTrap'
import { apiErrorMessage, messageForCode } from '../../../lib/apiErrorMessage'
import { isValidHost, isValidPort, SECURITY_OPTIONS, securityLabel } from '../../../lib/mailEndpointValidation'
import { schedulingErrorMessage } from './schedulingAccountErrors'
import {
  useSaveSchedulingAccount, useTestSchedulingAccount,
  type SchedulingAccount, type SchedulingAccountPayload, type SchedulingSecurity,
} from './useSchedulingAccount'

const PASSWORD_MAX_BYTES = 512
const DIALOG_TITLE_ID = 'svc-account-dialog-title'

function utf8Bytes(value: string): number {
  return new TextEncoder().encode(value).length
}

interface Props {
  account: SchedulingAccount
  addToast: (message: string, kind?: string) => void
  onSave: () => void
  onClose: () => void
  /** Takes the focus on close when the button that opened the dialog has been replaced. */
  returnFocusRef?: RefObject<HTMLElement | null>
}

/**
 * The admin dialog for the calendar invitation service account. The password field is required
 * exactly when the server would also require one (`SchedulingAccountController`): nothing stored,
 * an unreadable stored password, or host/port no longer matching what was saved.
 */
export default function SchedulingAccountDialog({ account, addToast, onSave, onClose, returnFocusRef }: Props) {
  const { t } = useTranslation('admin')
  const saveAccount = useSaveSchedulingAccount()
  const testAccount = useTestSchedulingAccount()
  const dialogRef = useRef<HTMLDivElement>(null)
  const hostFieldRef = useRef<HTMLInputElement>(null)
  useDialogFocusTrap(dialogRef, { initialFocusRef: hostFieldRef, fallbackFocusRef: returnFocusRef })

  const [host, setHost] = useState(account.host ?? '')
  const [port, setPort] = useState(String(account.port ?? 587))
  const [security, setSecurity] = useState<SchedulingSecurity>(account.security ?? 'StartTls')
  const [login, setLogin] = useState(account.login ?? '')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)

  const pending = saveAccount.isPending
  const busy = pending || testAccount.isPending

  // A stored None stays listed where the server now refuses it, so the admin can see it and move away.
  const securityOptions = SECURITY_OPTIONS.filter(o =>
    o.value !== 'None' || account.allowCleartext || account.security === 'None')

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape' && !pending) onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose, pending])

  // Disabling the focused Save or Tester drops focus to <body>; once the work settles, hand it back.
  const actionRef = useRef<HTMLElement | null>(null)
  useEffect(() => {
    const dialog = dialogRef.current
    if (busy || !dialog || dialog.contains(document.activeElement)) return
    const action = actionRef.current
    ;(action && dialog.contains(action) && !action.matches(':disabled') ? action : dialog).focus()
  }, [busy])

  const hostValid = isValidHost(host)
  const portValid = isValidPort(port)
  const loginValid = login.trim() !== '' && login.trim().length <= 320
  const passwordFilled = password !== ''
  const passwordValid = !passwordFilled || utf8Bytes(password) <= PASSWORD_MAX_BYTES

  // The server keeps a stored password only when it can still decrypt it AND host/port did not
  // move — every other case (nothing stored, an unreadable cipher, a changed endpoint) forces a
  // fresh one, exactly the 400 `SchedulingAccountController` would otherwise answer.
  const hostOrPortChanged = account.configured
    && (host.trim().toLowerCase() !== (account.host ?? '').toLowerCase() || Number(port) !== account.port)
  const passwordRequired = !account.configured || !account.passwordReadable || hostOrPortChanged
  const passwordStoredNoteShown = account.configured && account.passwordReadable && !hostOrPortChanged

  const formValid = hostValid && portValid && loginValid && passwordValid && (passwordFilled || !passwordRequired)

  const payload: SchedulingAccountPayload = {
    host: host.trim(),
    port: Number(port),
    security,
    login: login.trim(),
    ...(passwordFilled ? { password } : {}),
  }

  // Derived, never stored: a verdict shows only while the fields still hold the values it was tried on.
  function testVerdict(): { ok: boolean; message: string } | null {
    if (testAccount.isPending || JSON.stringify(testAccount.variables) !== JSON.stringify(payload)) return null
    const unknown = t('scheduling.testUnknownError')
    if (testAccount.isError) return { ok: false, message: schedulingErrorMessage(testAccount.error, t, unknown) }
    if (!testAccount.data) return null
    return testAccount.data.ok
      ? { ok: true, message: t('scheduling.testResultOk') }
      : { ok: false, message: messageForCode(testAccount.data.error, unknown) }
  }
  const testResult = testVerdict()

  function requestClose() {
    if (!pending) onClose()
  }

  function handleTest() {
    if (!formValid) return
    actionRef.current = document.activeElement as HTMLElement
    testAccount.mutate(payload)
  }

  // mutate's own callbacks, not an awaited mutateAsync: TanStack drops them once this dialog unmounts.
  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (!formValid || pending) return
    setError(null)
    actionRef.current = document.activeElement as HTMLElement
    saveAccount.mutate(payload, {
      onSuccess: onSave,
      onError: err => {
        // A conflict means the row changed under this form and was reloaded: say so and step out.
        if (err instanceof ApiError && err.code === 'scheduling_account_changed_concurrently') {
          addToast(apiErrorMessage(err, t('scheduling.saveFailed')), 'error')
          onClose()
        } else {
          setError(schedulingErrorMessage(err, t, t('scheduling.saveFailed')))
        }
      },
    })
  }

  return (
    <div className="modal-overlay" onClick={requestClose}>
      <div className="modal" ref={dialogRef} tabIndex={-1} onClick={e => e.stopPropagation()}
        role="dialog" aria-modal="true" aria-labelledby={DIALOG_TITLE_ID}>
        <div className="modal-header">
          <span className="modal-title" id={DIALOG_TITLE_ID}><PencilIcon /> {t('scheduling.dialogTitle')}</span>
          <button type="button" className="modal-close" aria-label={t('actions.close', { ns: 'common' })}
            disabled={pending} onClick={requestClose}>✕</button>
        </div>
        <form onSubmit={handleSubmit}>
          {error && <div className="alert alert-error" role="alert">{error}</div>}

          <div className="field-h">
            <label htmlFor="svc-account-host">{t('scheduling.host')}</label>
            <input id="svc-account-host" type="text" value={host} ref={hostFieldRef}
              className={host && !hostValid ? 'is-error' : undefined}
              onChange={e => setHost(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor="svc-account-port">{t('scheduling.port')}</label>
            <input id="svc-account-port" type="number" min={1} max={65535} value={port}
              className={!portValid ? 'is-error' : undefined}
              onChange={e => setPort(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor="svc-account-security">{t('scheduling.security')}</label>
            <select id="svc-account-security" value={security}
              onChange={e => setSecurity(e.target.value as SchedulingSecurity)}>
              {securityOptions.map(o => <option key={o.value} value={o.value}>{securityLabel(o, t)}</option>)}
            </select>
          </div>
          <div className="field-h">
            <label htmlFor="svc-account-login">{t('scheduling.login')}</label>
            <input id="svc-account-login" type="text" value={login}
              className={login && !loginValid ? 'is-error' : undefined}
              onChange={e => setLogin(e.target.value)} />
          </div>
          <div className="field-h">
            <label htmlFor="svc-account-password">{t('scheduling.password')}</label>
            <input id="svc-account-password" type="password" autoComplete="new-password" value={password}
              className={passwordFilled && !passwordValid ? 'is-error' : undefined}
              placeholder={passwordStoredNoteShown ? t('scheduling.passwordUnchanged') : undefined}
              onChange={e => setPassword(e.target.value)} />
          </div>
          {passwordStoredNoteShown && (
            <p className="settings-note svc-account-field-indent">{t('scheduling.passwordStoredNote')}</p>
          )}
          {passwordRequired && !passwordFilled && (
            <p className="settings-note is-warn svc-account-field-indent">{t('scheduling.passwordRequiredHint')}</p>
          )}

          {testResult && (
            <div className={`svc-account-test-result svc-account-field-indent${testResult.ok ? ' is-ok' : ' is-fail'}`}
              role="status">
              {testResult.ok ? <ShieldCheckIcon size={16} /> : <ShieldAlertIcon size={16} />}
              <span>{testResult.message}</span>
            </div>
          )}

          <div className="folder-pick-submit">
            <button type="button" className="btn btn-ghost btn-auto svc-account-test-btn"
              disabled={!formValid || testAccount.isPending}
              aria-label={t('scheduling.testConnection')} aria-busy={testAccount.isPending} onClick={handleTest}>
              {testAccount.isPending ? <span className="spinner" /> : t('scheduling.testConnection')}
            </button>
            <button type="button" className="btn btn-ghost btn-auto" disabled={pending} onClick={requestClose}>
              {t('actions.cancel', { ns: 'common' })}
            </button>
            <button type="submit" className="btn btn-primary btn-auto" disabled={pending || !formValid}
              aria-label={t('actions.save', { ns: 'common' })} aria-busy={pending}>
              {pending ? <span className="spinner" /> : t('actions.save', { ns: 'common' })}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
