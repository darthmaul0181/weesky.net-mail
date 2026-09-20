import { useTranslation } from 'react-i18next'

export function Toasts({ toasts, onRemove, onPause, onResume }) {
  const { t } = useTranslation('common')
  if (!toasts.length) return null
  return (
    <div className="toast-container" role="status" aria-live="polite" aria-atomic="false">
      {toasts.map(toast => (
        <div key={toast.id} className={`toast toast-${toast.type}`} role={toast.type === 'error' ? 'alert' : undefined}
          onMouseEnter={() => onPause?.(toast.id)} onMouseLeave={() => onResume?.(toast.id)}
          onFocus={() => onPause?.(toast.id)} onBlur={() => onResume?.(toast.id)}>
          <span>{toast.message}</span>
          {toast.action && (
            <button
              type="button"
              className="toast-action"
              onClick={() => { toast.action.onClick(); onRemove(toast.id) }}
            >{toast.action.label}</button>
          )}
          {toast.type === 'error' && (
            <button className="toast-close" aria-label={t('actions.close')} onClick={() => onRemove(toast.id)}>✕</button>
          )}
        </div>
      ))}
    </div>
  )
}

export default Toasts
