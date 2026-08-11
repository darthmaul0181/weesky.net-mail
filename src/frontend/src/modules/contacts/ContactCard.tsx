import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import ArrowLeftIcon from '../../icons/ArrowLeftIcon'
import PencilIcon from '../../icons/PencilIcon.jsx'
import StarIcon from '../../icons/StarIcon'
import TrashIcon from '../../icons/TrashIcon.jsx'
import { displayNameOf } from './contactName'
import type { Contact } from './contactTypes'

interface Props {
  contact: Contact | null
  /** Set only where the card has replaced the list — a phone. It draws the ← and binds Escape. */
  onBack?: () => void
  onEdit: (id: string) => void
  onDelete: (contact: Contact) => void
  onToggleFavorite: (contact: Contact) => void
}

/**
 * The contact in reading mode — the column the mail module gives its reader. Editing happens on
 * its own route, in a full-width editor, so this stays a viewer.
 *
 * Every row renders only when its datum exists: an empty labelled row reads as data that went
 * missing rather than data that was never entered.
 */
export default function ContactCard({ contact, onBack, onEdit, onDelete, onToggleFavorite }: Props) {
  const { t } = useTranslation('contacts')

  // Escape mirrors the ← button, MessageReader's arrangement, and like it exists only where the
  // card has replaced the list. The layout withholds onBack while its delete confirm is open, so
  // Escape never backs out from under the dialog.
  useEffect(() => {
    if (!onBack) return
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onBack() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onBack])

  const back = onBack ? (
    <button type="button" className="contact-card-back" aria-label={t('card.back')}
      title={t('card.back')} onClick={onBack}>
      <ArrowLeftIcon size={16} />
    </button>
  ) : null

  if (contact == null) {
    // The ← has to survive the empty card: an ?id= naming a contact the book no longer holds
    // lands here, and on a phone this invite is the whole screen.
    if (!back) return <p className="contacts-empty contacts-card-invite">{t('card.invite')}</p>
    return (
      <div className="contact-card">
        <div className="contact-card-head">{back}</div>
        <p className="contacts-empty contacts-card-invite">{t('card.invite')}</p>
      </div>
    )
  }

  return (
    <div className="contact-card">
      <div className="contact-card-head">
        {back}
        <h2 className="contact-card-name">{displayNameOf(contact)}</h2>
      </div>

      <div className="contact-card-body">
        {contact.nickname && (
          <div className="contact-card-row">
            <span className="contact-card-label">{t('fields.nickname')}</span>
            <span className="contact-card-value">{contact.nickname}</span>
          </div>
        )}

        {contact.addresses.length > 0 && (
          <div className="contact-card-row">
            <span className="contact-card-label">{t('fields.addresses')}</span>
            <span className="contact-card-values">
              {contact.addresses.map((address, index) => (
                <span key={address} className="contact-card-value" data-testid="card-address">
                  <a href={`mailto:${address}`}>{address}</a>
                  {index === 0 && <span className="contact-card-primary">{t('fields.primary')}</span>}
                </span>
              ))}
            </span>
          </div>
        )}
      </div>

      {/* Bottom-right of whatever rows are actually present, like the reader's action cluster. */}
      <div className="contact-card-actions">
        <button type="button" className="btn contact-card-btn"
          aria-label={t(contact.isFavorite ? 'favourites.remove' : 'favourites.add')}
          aria-pressed={contact.isFavorite}
          onClick={() => onToggleFavorite(contact)}>
          <StarIcon size={15} filled={contact.isFavorite} />
          {t(contact.isFavorite ? 'favourites.remove' : 'favourites.add')}
        </button>
        <span className="actions-rule" />
        <button type="button" className="btn contact-card-btn" onClick={() => onEdit(contact.id)}>
          <PencilIcon size={15} /> {t('actions.edit', { ns: 'common' })}
        </button>
        <button type="button" className="btn contact-card-btn is-danger"
          onClick={() => onDelete(contact)}>
          <TrashIcon size={15} /> {t('actions.delete', { ns: 'common' })}
        </button>
      </div>
    </div>
  )
}
