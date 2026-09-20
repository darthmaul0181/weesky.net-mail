import { useRef, type RefObject } from 'react'
import { useTranslation } from 'react-i18next'
import Modal from '../../../components/Modal'

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
  const keyFieldRef = useRef<HTMLInputElement>(null)

  async function copy() {
    try {
      await navigator.clipboard.writeText(keyValue)
      addToast(t('deliveryReplies.copied'))
    } catch {
      addToast(t('deliveryReplies.copyFailed'), 'error')
    }
  }

  return (
    <Modal title={t('deliveryReplies.dialogTitle')} onClose={onClose}
      initialFocusRef={keyFieldRef} returnFocusRef={returnFocusRef}>
      <p className="dlv-key-intro">{t('deliveryReplies.dialogIntro')}</p>
      <div className="dlv-key-row">
        <input className="dlv-key-value" type="text" readOnly value={keyValue} ref={keyFieldRef}
          onFocus={e => e.currentTarget.select()} />
        <button type="button" className="btn btn-primary btn-auto" onClick={() => void copy()}>{t('deliveryReplies.copy')}</button>
      </div>
    </Modal>
  )
}
