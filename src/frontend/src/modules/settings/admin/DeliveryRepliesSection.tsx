import { Fragment, useCallback, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import LoadingBlock from '../../../components/LoadingBlock'
import RefreshIcon from '../../../icons/RefreshIcon'
import ShieldAlertIcon from '../../../icons/ShieldAlertIcon'
import ShieldCheckIcon from '../../../icons/ShieldCheckIcon'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import { dateFormat } from '../../../lib/intl'
import DeliveryKeyDialog from './DeliveryKeyDialog'
import { deliveryErrorMessage } from './deliveryReplyKeyErrors'
import {
  useDeleteDeliveryKey, useDeliveryReplyKey, useGenerateDeliveryKey, useSetDeliveryReplies,
} from './useDeliveryReplyKey'

interface Props {
  addToast: (message: string, kind?: string) => void
}

const day = () => dateFormat({ day: 'numeric', month: 'short' })
const stamp = () => dateFormat({ day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

/**
 * The key the mail server presents to apply guests' replies at delivery, and the switch (spec
 * 5e3). No "Test" button, unlike the sending account above: a test from here would prove the door
 * answers, not that Dovecot calls it — the last-call date is the only honest signal (décision 8).
 */
export default function DeliveryRepliesSection({ addToast }: Props) {
  const { t } = useTranslation('admin')
  const { data: key, isLoading, isError } = useDeliveryReplyKey()
  const generate = useGenerateDeliveryKey()
  const setEnabled = useSetDeliveryReplies()
  const remove = useDeleteDeliveryKey()
  const [shownKey, setShownKey] = useState<string | null>(null)
  const [confirming, setConfirming] = useState<'regenerate' | 'delete' | null>(null)
  const headingRef = useRef<HTMLHeadingElement>(null)
  const cardNode = useRef<HTMLDivElement | null>(null)

  // A card replaced while holding focus (its own icon buttons unmounting under a state change)
  // would take the focus down with it. React detaches the ref before removing the node, so focus
  // can still be seen inside and handed on — the same pattern as SchedulingAccountSection's.
  const cardRef = useCallback((node: HTMLDivElement | null) => {
    if (!node && cardNode.current?.contains(document.activeElement)) headingRef.current?.focus()
    cardNode.current = node
  }, [])

  async function runGenerate() {
    try {
      const { key: fresh } = await generate.mutateAsync()
      setShownKey(fresh)
      addToast(t('deliveryReplies.generated'))
    } catch (err) {
      addToast(deliveryErrorMessage(err, t, t('deliveryReplies.generateFailed')), 'error')
    } finally {
      setConfirming(null)
    }
  }

  async function toggle(enabled: boolean) {
    try {
      await setEnabled.mutateAsync(enabled)
      addToast(t(enabled ? 'deliveryReplies.enabled' : 'deliveryReplies.disabled'))
    } catch (err) {
      addToast(deliveryErrorMessage(err, t, t('deliveryReplies.toggleFailed')), 'error')
    }
  }

  async function confirmDelete() {
    try {
      await remove.mutateAsync()
      addToast(t('deliveryReplies.deleted'))
    } catch (err) {
      addToast(deliveryErrorMessage(err, t, t('deliveryReplies.deleteFailed')), 'error')
    } finally {
      setConfirming(null)
    }
  }

  function renderCard() {
    if (isLoading) return <LoadingBlock />
    if (isError || !key) return <p>{t('deliveryReplies.loadFailed')}</p>
    const configured = key.configured
    return (
      <div key={configured ? 'configured' : 'empty'}
        className={configured ? 'admin-list-item svc-account-card' : 'admin-list-item svc-account-card is-empty'}
        ref={cardRef}>
        {configured ? <ShieldCheckIcon /> : <ShieldAlertIcon />}
        <div className="svc-account-info">
          <div className={key.enabled ? 'dlv-toggle' : 'dlv-toggle is-off'}>
            <label htmlFor="dlv-enabled">{t('deliveryReplies.toggle')}</label>
            <label className={configured ? 'toggle-switch' : 'toggle-switch is-locked'}>
              <input id="dlv-enabled" type="checkbox" checked={key.enabled} disabled={!configured || setEnabled.isPending}
                onChange={e => void toggle(e.target.checked)} />
              <span className="toggle-track" />
            </label>
          </div>
          {configured ? (
            <div className="svc-account-meta">
              {t('deliveryReplies.createdOn', { date: day().format(new Date(key.createdAt!)) })}
            </div>
          ) : <p className="svc-account-empty-text">{t('deliveryReplies.noKeyHint')}</p>}
        </div>
        {configured && (key.lastCallAt
          ? <span className="svc-account-pill is-ok">{t('deliveryReplies.lastCallOn', { date: stamp().format(new Date(key.lastCallAt)) })}</span>
          : <span className="svc-account-pill">{t('deliveryReplies.noCall')}</span>)}
        <div className="admin-list-item-actions">
          {configured ? (
            // A distinct key from the "empty" branch below: without one, React's reconciler can
            // leave a stale DOM node's class/content behind rather than unmount+remount across
            // this Fragment/single-element ternary — the same reasoning SchedulingAccountSection's
            // own renderCard() keys its three branches for, one level up (its whole card, not just
            // this row). Caught here because it silently broke useLayer's "is the opener still
            // connected" check (§`hooks/useLayer.ts`): the reused node stayed connected under new content.
            <Fragment key="configured-actions">
              <button type="button" className="admin-icon-btn" title={t('deliveryReplies.regenerate')}
                aria-label={t('deliveryReplies.regenerate')} onClick={() => setConfirming('regenerate')}>
                <RefreshIcon />
              </button>
              <button type="button" className="admin-icon-btn is-danger" title={t('actions.delete', { ns: 'common' })}
                aria-label={t('actions.delete', { ns: 'common' })} onClick={() => setConfirming('delete')}>
                <TrashIcon />
              </button>
            </Fragment>
          ) : (
            <button key="empty-actions" type="button" className="btn btn-primary btn-auto"
              disabled={generate.isPending} onClick={() => void runGenerate()}>
              {t('deliveryReplies.generate')}
            </button>
          )}
        </div>
      </div>
    )
  }

  return (
    <div className="svc-account-section">
      <h2 className="svc-account-section-title" ref={headingRef} tabIndex={-1}>{t('deliveryReplies.title')}</h2>
      <p className="svc-account-section-intro">{t('deliveryReplies.intro')}</p>
      <div className="svc-account-slot">{renderCard()}</div>

      {shownKey !== null && (
        <DeliveryKeyDialog keyValue={shownKey} addToast={addToast} onClose={() => setShownKey(null)}
          returnFocusRef={headingRef} />
      )}
      {confirming === 'regenerate' && (
        <DeleteConfirmModal title={t('deliveryReplies.regenerateTitle')} confirmLabel={t('deliveryReplies.regenerate')}
          message={t('deliveryReplies.regenerateMessage')} loading={generate.isPending}
          onConfirm={() => void runGenerate()} onClose={() => setConfirming(null)} />
      )}
      {confirming === 'delete' && (
        <DeleteConfirmModal entityLabel={t('deliveryReplies.keyLabel')} message={t('deliveryReplies.deleteMessage')}
          loading={remove.isPending} onConfirm={() => void confirmDelete()} onClose={() => setConfirming(null)} />
      )}
    </div>
  )
}
