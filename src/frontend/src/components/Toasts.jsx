import { useRef } from 'react'
import { useTranslation } from 'react-i18next'

/** One row. A ref, not state: the reason set is interaction bookkeeping, never painted itself. */
function Toast({ toast, onRemove, onPause, onResume }) {
  const { t } = useTranslation('common')
  // Hover and focus are counted separately, not as one flag: a pause the keyboard armed must
  // survive the mouse merely passing over and leaving (WCAG 2.2.1), so resume only fires once
  // neither reason is still held.
  const reasons = useRef(new Set())

  function pauseFor(reason) {
    if (reasons.current.size === 0) onPause?.(toast.id)
    reasons.current.add(reason)
  }

  function resumeFor(reason) {
    reasons.current.delete(reason)
    if (reasons.current.size === 0) onResume?.(toast.id)
  }

  // A tap emulates pointerenter with no matching pointerleave until the next interaction
  // elsewhere: a touch user has no hover intent, so only a real mouse arms the hover reason.
  function onPointerEnter(event) {
    if (event.pointerType !== 'mouse') return
    pauseFor('hover')
  }

  function onPointerLeave(event) {
    if (event.pointerType !== 'mouse') return
    resumeFor('hover')
  }

  return (
    <div className={`toast toast-${toast.type}`} role={toast.type === 'error' ? 'alert' : undefined}
      onPointerEnter={onPointerEnter} onPointerLeave={onPointerLeave}
      onFocus={() => pauseFor('focus')} onBlur={() => resumeFor('focus')}>
      <span>{toast.message}</span>
      {toast.action && (
        <button
          type="button"
          className="toast-action"
          onClick={() => { toast.action.onClick(); onRemove(toast.id) }}
        >{toast.action.label}</button>
      )}
      {toast.type === 'error' && (
        <button type="button" className="toast-close" aria-label={t('actions.close')}
          onClick={() => onRemove(toast.id)}>✕</button>
      )}
    </div>
  )
}

export function Toasts({ toasts, onRemove, onPause, onResume }) {
  // Always rendered, never withheld while empty: a live region created together with its first
  // child is routinely not announced, so the container has to already exist when a toast lands.
  // Empty, it costs nothing on screen — position: fixed, no padding or background, 0x0.
  return (
    <div className="toast-container" role="status" aria-live="polite" aria-atomic="false">
      {toasts.map(toast => (
        <Toast key={toast.id} toast={toast} onRemove={onRemove} onPause={onPause} onResume={onResume} />
      ))}
    </div>
  )
}

export default Toasts
