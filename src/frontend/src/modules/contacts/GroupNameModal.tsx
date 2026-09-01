import { useState } from 'react'
import { useTranslation } from 'react-i18next'

interface Props {
  title: string
  /** Empty for a create; the current name for a rename, which is also what makes Save inert. */
  initialName: string
  saving: boolean
  onSubmit: (name: string) => void
  onClose: () => void
}

/**
 * One dialog for the two gestures (decision 13): same field, same validation, two titles. The
 * ✕ is the only way out, as in the admin dialogs, and the form is what makes Enter submit.
 */
export default function GroupNameModal({ title, initialName, saving, onSubmit, onClose }: Props) {
  const { t } = useTranslation('contacts')
  const [name, setName] = useState(initialName)

  const trimmed = name.trim()
  const submittable = trimmed !== '' && trimmed !== initialName.trim() && !saving

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={event => event.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title">{title}</span>
          <button className="modal-close" aria-label={t('actions.close', { ns: 'common' })}
            onClick={onClose}>✕</button>
        </div>
        {/* The guard is replayed here: a disabled submit does not stop Enter in every browser,
            and an empty or unchanged name must not reach the API. */}
        <form onSubmit={event => {
          event.preventDefault()
          if (!submittable) return
          onSubmit(trimmed)
        }}>
          <div className="field-h">
            <label htmlFor="contact-group-name">{t('groups.nameLabel')}</label>
            {/* 255 is the column's own bound: refusing it at the keyboard beats refusing it after
                a round trip. */}
            <input id="contact-group-name" type="text" maxLength={255} value={name} autoFocus
              onChange={event => setName(event.target.value)} />
          </div>
          <div className="modal-actions">
            <button type="submit" className="btn btn-primary" disabled={!submittable}>
              {saving ? <span className="spinner" /> : t('actions.save', { ns: 'common' })}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
