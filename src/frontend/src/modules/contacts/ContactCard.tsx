import PencilIcon from '../../icons/PencilIcon.jsx'
import StarIcon from '../../icons/StarIcon'
import TrashIcon from '../../icons/TrashIcon.jsx'
import { displayNameOf } from './contactName'
import type { Contact } from './contactTypes'

interface Props {
  contact: Contact | null
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
export default function ContactCard({ contact, onEdit, onDelete, onToggleFavorite }: Props) {
  if (contact == null) {
    return <p className="contacts-empty contacts-card-invite">Select a contact to see its details</p>
  }

  return (
    <div className="contact-card">
      <div className="contact-card-head">
        <h2 className="contact-card-name">{displayNameOf(contact)}</h2>
      </div>

      <div className="contact-card-body">
        {contact.nickname && (
          <div className="contact-card-row">
            <span className="contact-card-label">Nickname</span>
            <span className="contact-card-value">{contact.nickname}</span>
          </div>
        )}

        {contact.addresses.length > 0 && (
          <div className="contact-card-row">
            <span className="contact-card-label">Addresses</span>
            <span className="contact-card-values">
              {contact.addresses.map((address, index) => (
                <span key={address} className="contact-card-value" data-testid="card-address">
                  <a href={`mailto:${address}`}>{address}</a>
                  {index === 0 && <span className="contact-card-primary">primary</span>}
                </span>
              ))}
            </span>
          </div>
        )}
      </div>

      {/* Bottom-right of whatever rows are actually present, like the reader's action cluster. */}
      <div className="contact-card-actions">
        <button type="button" className="btn contact-card-btn"
          aria-label={contact.isFavorite ? 'Remove from favourites' : 'Add to favourites'}
          aria-pressed={contact.isFavorite}
          onClick={() => onToggleFavorite(contact)}>
          <StarIcon size={15} filled={contact.isFavorite} />
          {contact.isFavorite ? 'Remove from favourites' : 'Add to favourites'}
        </button>
        <span className="actions-rule" />
        <button type="button" className="btn contact-card-btn" onClick={() => onEdit(contact.id)}>
          <PencilIcon size={15} /> Edit
        </button>
        <button type="button" className="btn contact-card-btn is-danger"
          onClick={() => onDelete(contact)}>
          <TrashIcon size={15} /> Delete
        </button>
      </div>
    </div>
  )
}
