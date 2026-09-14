import { useCallback, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import { ApiError } from '../../../api.js'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import LoadingBlock from '../../../components/LoadingBlock'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import ShieldAlertIcon from '../../../icons/ShieldAlertIcon'
import ShieldCheckIcon from '../../../icons/ShieldCheckIcon'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import { messageForCode } from '../../../lib/apiErrorMessage'
import { dateFormat } from '../../../lib/intl'
import { SECURITY_OPTIONS, securityLabel } from '../../../lib/mailEndpointValidation'
import { schedulingErrorMessage } from './schedulingAccountErrors'
import SchedulingAccountDialog from './SchedulingAccountDialog'
import {
  useDeleteSchedulingAccount, useSchedulingAccount, useTestSchedulingAccount,
} from './useSchedulingAccount'

interface Props {
  addToast: (message: string, kind?: string) => void
}

function securityText(security: string | undefined, t: TFunction<'admin'>): string {
  const option = SECURITY_OPTIONS.find(o => o.value === security)
  return option ? securityLabel(option, t) : (security ?? '')
}

/**
 * The SMTP service account `ServiceMailQueue` uses to send calendar invitations when a device
 * write (a phone, Thunderbird over CalDAV) has no user session of its own — configured here,
 * below the existing Application settings, and nowhere else (mockup "B", spec 5e2).
 */
export default function SchedulingAccountSection({ addToast }: Props) {
  const { t } = useTranslation('admin')
  const { data: account, isLoading, isError } = useSchedulingAccount()
  const deleteAccount = useDeleteSchedulingAccount()
  const testAccount = useTestSchedulingAccount()
  const [dialogOpen, setDialogOpen] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const headingRef = useRef<HTMLHeadingElement>(null)
  const cardNode = useRef<HTMLDivElement | null>(null)

  // A card replaced by another state (keyed below) would take the focus down with it. React
  // detaches the ref before removing the node, so focus can still be seen inside and handed on.
  const cardRef = useCallback((node: HTMLDivElement | null) => {
    if (!node && cardNode.current?.contains(document.activeElement)) headingRef.current?.focus()
    cardNode.current = node
  }, [])

  async function handleTest() {
    try {
      const result = await testAccount.mutateAsync(undefined)
      if (result.ok) addToast(t('scheduling.testSuccess'))
      else addToast(messageForCode(result.error, t('scheduling.testUnknownError')), 'error')
    } catch (err) {
      // A 404 here means the account was deleted from elsewhere mid-session; the mutation's own
      // onSettled already invalidated the query, so the card below reflects it on the next render.
      addToast(schedulingErrorMessage(err, t, t('scheduling.testUnknownError')), 'error')
    }
  }

  async function confirmDelete() {
    try {
      await deleteAccount.mutateAsync()
      addToast(t('scheduling.deleted'))
      setDeleting(false)
    } catch (err) {
      addToast(schedulingErrorMessage(err, t, t('scheduling.deleteFailed')), 'error')
      // A conflict reloads the account: the confirmation would name a login that may be gone.
      if (err instanceof ApiError && err.status === 409) setDeleting(false)
    }
  }

  function renderCard() {
    if (isLoading) return <LoadingBlock />
    if (isError || !account) return <p>{t('scheduling.loadFailed')}</p>

    if (!account.configured) {
      return (
        <div className="admin-list-item svc-account-card is-empty" key="empty" ref={cardRef}>
          <ShieldAlertIcon />
          <p className="svc-account-empty-text">{t('scheduling.emptyTitle')}</p>
          <button type="button" className="btn btn-primary btn-auto" onClick={() => setDialogOpen(true)}>
            {t('scheduling.configure')}
          </button>
        </div>
      )
    }

    if (!account.passwordReadable) {
      return (
        <div className="admin-list-item svc-account-card is-warn" key="unreadable" ref={cardRef}>
          <ShieldAlertIcon />
          <div className="svc-account-info">
            <div className="svc-account-login">{account.login}</div>
            <div className="svc-account-meta">{account.host}</div>
            <p className="svc-account-warn-text">{t('scheduling.unreadableTitle')}</p>
          </div>
          <div className="admin-list-item-actions">
            <button type="button" className="admin-icon-btn" title={t('actions.edit', { ns: 'common' })}
              onClick={() => setDialogOpen(true)}>
              <PencilIcon />
            </button>
            <button type="button" className="admin-icon-btn is-danger" title={t('actions.delete', { ns: 'common' })}
              onClick={() => setDeleting(true)}>
              <TrashIcon />
            </button>
          </div>
        </div>
      )
    }

    return (
      <div className="admin-list-item svc-account-card" key="configured" ref={cardRef}>
        <ShieldCheckIcon />
        <div className="svc-account-info">
          <div className="svc-account-login">{account.login}</div>
          <div className="svc-account-meta">
            {account.host} · {t('scheduling.portWord')} {account.port} · {securityText(account.security, t)}
          </div>
        </div>
        {account.lastTestOk === true && account.lastTestAt && (
          <span className="svc-account-pill is-ok">
            {t('scheduling.testedOn', {
              date: dateFormat({ day: 'numeric', month: 'short' }).format(new Date(account.lastTestAt)),
            })}
          </span>
        )}
        {account.lastTestOk === false && <span className="svc-account-pill is-fail">{t('scheduling.testFailed')}</span>}
        <div className="admin-list-item-actions">
          <button type="button" className="btn btn-ghost btn-auto" disabled={testAccount.isPending}
            aria-label={t('scheduling.test')} aria-busy={testAccount.isPending} onClick={handleTest}>
            {testAccount.isPending ? <span className="spinner" /> : t('scheduling.test')}
          </button>
          <button type="button" className="admin-icon-btn" title={t('actions.edit', { ns: 'common' })}
            onClick={() => setDialogOpen(true)}>
            <PencilIcon />
          </button>
          <button type="button" className="admin-icon-btn is-danger" title={t('actions.delete', { ns: 'common' })}
            onClick={() => setDeleting(true)}>
            <TrashIcon />
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className="svc-account-section">
      <h2 className="svc-account-section-title" ref={headingRef} tabIndex={-1}>{t('scheduling.title')}</h2>
      <p className="svc-account-section-intro">{t('scheduling.intro')}</p>
      <div className="svc-account-slot">{renderCard()}</div>

      {dialogOpen && account && (
        <SchedulingAccountDialog
          account={account}
          addToast={addToast}
          onSave={() => { setDialogOpen(false); addToast(t('scheduling.saved')) }}
          onClose={() => setDialogOpen(false)}
          returnFocusRef={headingRef}
        />
      )}
      {deleting && account && (
        <DeleteConfirmModal entityLabel={account.login} loading={deleteAccount.isPending}
          onConfirm={confirmDelete} onClose={() => setDeleting(false)} />
      )}
    </div>
  )
}
