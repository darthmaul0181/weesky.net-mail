export function Toasts({ toasts, onRemove }) {
  if (!toasts.length) return null
  return (
    <div className="toast-container">
      {toasts.map(t => (
        <div key={t.id} className={`toast toast-${t.type}`}>
          <span>{t.message}</span>
          {t.type === 'error' && (
            <button className="toast-close" onClick={() => onRemove(t.id)}>✕</button>
          )}
        </div>
      ))}
    </div>
  )
}

export default Toasts
