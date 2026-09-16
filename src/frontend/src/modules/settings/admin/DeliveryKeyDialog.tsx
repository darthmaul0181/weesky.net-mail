import { useEffect, useRef, type RefObject } from 'react'
import { useTranslation } from 'react-i18next'
import ModalOverlay from '../../../components/ModalOverlay'
import { useDialogFocusTrap } from '../../../hooks/useDialogFocusTrap'

interface Props {
  keyValue: string
  addToast: (message: string, kind?: string) => void
  onClose: () => void
  /** Takes the focus back on close when the button that opened the dialog is gone — the generate
      button unmounts once the key refetches configured, and the regenerate confirmation's own
      button unmounts with it. Mirrors `SchedulingAccountDialog`'s `returnFocusRef`. */
  returnFocusRef?: RefObject<HTMLElement | null>
}

/** The key, once. Read-only field so it can be selected by hand when the clipboard is refused. */
export default function DeliveryKeyDialog({ keyValue, addToast, onClose, returnFocusRef }: Props) {
  const { t } = useTranslation('admin')
  const dialogRef = useRef<HTMLDivElement>(null)
  const keyFieldRef = useRef<HTMLInputElement>(null)
  useDialogFocusTrap(dialogRef, { initialFocusRef: keyFieldRef, fallbackFocusRef: returnFocusRef })

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [onClose])

  async function copy() {
    try {
      await navigator.clipboard.writeText(keyValue)
      addToast(t('deliveryReplies.copied'))
    } catch {
      addToast(t('deliveryReplies.copyFailed'), 'error')
    }
  }

  return (
    <ModalOverlay onClose={onClose}>
      <div className="modal" tabIndex={-1} role="dialog" aria-modal="true" aria-labelledby="dlv-key-title" ref={dialogRef}
        onClick={e => e.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title" id="dlv-key-title">{t('deliveryReplies.dialogTitle')}</span>
          <button type="button" className="modal-close" onClick={onClose} aria-label={t('actions.close', { ns: 'common' })}>✕</button>
        </div>
        <p className="dlv-key-intro">{t('deliveryReplies.dialogIntro')}</p>
        <div className="dlv-key-row">
          <input className="dlv-key-value" type="text" readOnly value={keyValue} ref={keyFieldRef}
            onFocus={e => e.currentTarget.select()} />
          <button type="button" className="btn btn-primary btn-auto" onClick={() => void copy()}>{t('deliveryReplies.copy')}</button>
        </div>
      </div>
    </ModalOverlay>
  )
}
