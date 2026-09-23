import { useId, useRef, type FormEvent, type KeyboardEvent, type ReactNode, type RefObject } from 'react'
import { useTranslation } from 'react-i18next'
import { useLayer } from '../hooks/useLayer'
import ModalOverlay from './ModalOverlay'

export interface ModalProps {
  /** Rendered as the `.modal-title`; required unless `header` is false. */
  title?: ReactNode
  /** Icon continuity with the trigger. Modal owns the spacing — callers pass no space. */
  icon?: ReactNode
  /** Omitted: no ✕ and no backdrop close. */
  onClose?: () => void
  /** Defaults to `onClose`; a dialog whose answer to Escape is not "close" passes its own. */
  onEscape?: () => void
  closeLabel?: string
  /** A write in flight: the ✕ is disabled, the backdrop and Escape do nothing. */
  busy?: boolean
  role?: 'dialog' | 'alertdialog'
  /** Where focus lands on open. Without it the ✕ does, being the first focusable. */
  initialFocusRef?: RefObject<HTMLElement | null>
  /** Where focus goes on close when the element that opened the dialog is gone. */
  returnFocusRef?: RefObject<HTMLElement | null>
  /** Read at close: true and `returnFocusRef` wins over an opener still on screen — what a
      confirmed destructive action asks for, its opener leaving on a later round trip. */
  preferReturnRef?: RefObject<boolean>
  className?: string
  overlayClassName?: string
  /** Between the title and the ✕: a viewer's count, a help button. */
  headerExtra?: ReactNode
  /** False: the children draw their own header and `labelledBy` names the dialog. */
  header?: boolean
  labelledBy?: string
  /** Given, the `.modal` root is the `<form>`, so Enter submits. */
  onSubmit?: (event: FormEvent<HTMLFormElement>) => void
  /** Keys the dialog answers itself. Bound to the root, so it hears them only while the trap
      holds the focus — a dialog opened over this one takes them with it. */
  onKeyDown?: (event: KeyboardEvent<HTMLElement>) => void
  children: ReactNode
}

/** The one dialog shell: a named `aria-modal` box on the layer stack, closed by the ✕, Escape or
 * a press that starts and ends on the backdrop. Sizing stays in `styles/modal.css`. */
export default function Modal({
  title, icon, onClose, onEscape, closeLabel, busy = false, role = 'dialog',
  initialFocusRef, returnFocusRef, preferReturnRef, className, overlayClassName, headerExtra,
  header = true,
  labelledBy, onSubmit, onKeyDown, children,
}: ModalProps) {
  const { t } = useTranslation('common')
  const generatedId = useId()
  // The root is a <div> or a <form>; both refs are read as the plain element the layer traps.
  const modalRef = useRef<HTMLDivElement & HTMLFormElement>(null)
  // The name is the caller's element when it points at one, and the header's own title otherwise.
  // Neither: no attribute at all, rather than one pointing at an id nothing carries.
  const titleId = title && header && !labelledBy ? generatedId : undefined
  const nameId = labelledBy ?? titleId

  useLayer({
    active: true,
    ref: modalRef,
    onEscape: busy ? undefined : (onEscape ?? onClose),
    initialFocusRef,
    returnFocusRef,
    preferReturnRef,
  })

  const rootProps = {
    role,
    'aria-modal': true,
    'aria-labelledby': nameId,
    tabIndex: -1,
    className: className ? `modal ${className}` : 'modal',
    onKeyDown,
  }

  const body = (
    <>
      {header && (
        <div className="modal-header">
          <span className="modal-title" id={titleId}>{icon}{title}</span>
          {headerExtra}
          {onClose && (
            <button
              type="button"
              className="modal-close"
              aria-label={closeLabel ?? t('actions.close')}
              disabled={busy}
              onClick={onClose}
            >
              ✕
            </button>
          )}
        </div>
      )}
      {children}
    </>
  )

  return (
    <ModalOverlay className={overlayClassName} onClose={() => { if (!busy) onClose?.() }}>
      {onSubmit
        ? <form ref={modalRef} {...rootProps} onSubmit={onSubmit}>{body}</form>
        : <div ref={modalRef} {...rootProps}>{body}</div>}
    </ModalOverlay>
  )
}
