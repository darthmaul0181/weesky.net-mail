import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import DropdownMenu from '../../components/DropdownMenu'
import ArrowLeftIcon from '../../icons/ArrowLeftIcon'
import KebabIcon from '../../icons/KebabIcon'
import PencilIcon from '../../icons/PencilIcon.jsx'
import StarIcon from '../../icons/StarIcon'
import TrashIcon from '../../icons/TrashIcon.jsx'
import { formatBirthday } from './contactBirthday'
import { displayNameOf } from './contactName'
import type { Contact, ContactDetailPostal } from './contactTypes'
import { useContact, useContactPhoto } from './queries'

interface Props {
  contact: Contact | null
  /** Set only where the card has replaced the list — a phone. It draws the ← and binds Escape. */
  onBack?: () => void
  onEdit: (id: string) => void
  onDelete: (contact: Contact) => void
  onToggleFavorite: (contact: Contact) => void
  /** The phone's foot-of-screen shape: named cells in a band instead of a header cluster. Set by
      the layout, which is the only thing that knows the tier — never a `useViewport` of the card's
      own, for the reason `MessageReader.bottomActions` is a prop too. */
  bottomActions?: boolean
}

/**
 * The contact in reading mode — the column the mail module gives its reader. Editing happens on
 * its own route, in a full-width editor, so this stays a viewer.
 *
 * Every row renders only when its datum exists: an empty labelled row reads as data that went
 * missing rather than data that was never entered.
 */
export default function ContactCard({
  contact, onBack, onEdit, onDelete, onToggleFavorite, bottomActions = false,
}: Props) {
  const { t } = useTranslation('contacts')

  // The list paints the card at once and the detail enriches it when it lands: a selection that
  // began with a blank would flicker on every click, for a request that costs one round trip.
  const { data: detail } = useContact(contact?.id ?? null)
  const photo = usePhotoUrl(contact?.id ?? null, detail?.hasPhoto ?? false)

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

  // The card's own once it is here — the server ranked them by pref then position — the list's
  // until then.
  const addresses = detail?.addresses.map(line => line.address) ?? contact.addresses

  const favouriteLabel = t(contact.isFavorite ? 'favourites.remove' : 'favourites.add')
  const editLabel = t('actions.edit', { ns: 'common' })
  const deleteLabel = t('actions.delete', { ns: 'common' })

  /* The tile's own vocabulary, moved up to the card: the star outside the cluster because it is a
     flag rather than an action, then `.admin-icon-btn` for what acts on the contact. Delete is one
     click deeper than the two reversible actions beside it. */
  const cluster = (
    <div className="contact-card-actions">
      <button type="button" className={`contact-star${contact.isFavorite ? ' is-on' : ''}`}
        aria-label={favouriteLabel} title={favouriteLabel} aria-pressed={contact.isFavorite}
        onClick={() => onToggleFavorite(contact)}>
        <StarIcon size={18} filled={contact.isFavorite} />
      </button>
      <button type="button" className="admin-icon-btn" aria-label={editLabel} title={editLabel}
        onClick={() => onEdit(contact.id)}>
        <PencilIcon size={16} />
      </button>
      <DropdownMenu ariaLabel={t('card.actions')} className="admin-icon-btn"
        trigger={<KebabIcon size={16} />}
        items={[{
          label: deleteLabel, icon: <TrashIcon size={18} />, onSelect: () => onDelete(contact),
        }]} />
    </div>
  )

  /* Three cells, no kebab: the card holds three actions and a bar whose last cell only ever opens
     a one-entry menu spends a third of the screen saying nothing. The visible word stays short
     where the accessible name does not — a cell has no room to spell out which way the star goes. */
  const bar = (
    <div className="actionbar">
      <button type="button" className="actionbar-item" aria-label={favouriteLabel}
        aria-pressed={contact.isFavorite} onClick={() => onToggleFavorite(contact)}>
        <StarIcon size={21} filled={contact.isFavorite} />
        <span className="actionbar-label">{t('card.bar.favourite')}</span>
      </button>
      <button type="button" className="actionbar-item" aria-label={editLabel}
        onClick={() => onEdit(contact.id)}>
        <PencilIcon size={21} />
        <span className="actionbar-label">{editLabel}</span>
      </button>
      <button type="button" className="actionbar-item is-danger" aria-label={deleteLabel}
        onClick={() => onDelete(contact)}>
        <TrashIcon size={21} />
        <span className="actionbar-label">{deleteLabel}</span>
      </button>
    </div>
  )

  return (
    <div className="contact-card">
      <div className="contact-card-head">
        {back}
        {photo && (
          <img className="contact-card-photo" src={photo} alt="" data-testid="card-photo" />
        )}
        <h2 className="contact-card-name">{displayNameOf(contact)}</h2>
        {!bottomActions && cluster}
      </div>

      <div className="contact-card-body">
        <Row label={t('fields.nickname')} value={contact.nickname} />

        <Row label={t('fields.organization')} value={detail?.organization} />
        <Row label={t('fields.department')} value={detail?.department} />
        <Row label={t('fields.jobTitle')} value={detail?.jobTitle} />

        {addresses.length > 0 && (
          <div className="contact-card-row">
            <span className="contact-card-label">{t('fields.addresses')}</span>
            <span className="contact-card-values">
              {addresses.map((address, index) => (
                <span key={address} className="contact-card-value" data-testid="card-address">
                  <a href={`mailto:${address}`}>{address}</a>
                  {index === 0 && <span className="contact-card-primary">{t('fields.primary')}</span>}
                </span>
              ))}
            </span>
          </div>
        )}

        {detail && detail.phones.length > 0 && (
          <div className="contact-card-row">
            <span className="contact-card-label">{t('fields.phones')}</span>
            <span className="contact-card-values">
              {detail.phones.map(phone => (
                <span key={`${phone.position}-${phone.number}`} className="contact-card-value"
                  data-testid="card-phone">
                  <a href={`tel:${phone.number.replace(/\s/g, '')}`}>{phone.number}</a>
                </span>
              ))}
            </span>
          </div>
        )}

        {detail && detail.postalAddresses.length > 0 && (
          <div className="contact-card-row">
            <span className="contact-card-label">{t('fields.postal')}</span>
            <span className="contact-card-values">
              {detail.postalAddresses.map(postal => (
                <span key={postal.position} className="contact-card-value" data-testid="card-postal">
                  {postalLines(postal).map(line => <span key={line}>{line}</span>)}
                </span>
              ))}
            </span>
          </div>
        )}

        <Row label={t('fields.birthday')} value={formatBirthday(detail?.birthday)} />
        {detail?.website && (
          <div className="contact-card-row">
            <span className="contact-card-label">{t('fields.website')}</span>
            <span className="contact-card-value">
              <a href={detail.website} target="_blank" rel="noreferrer noopener">{detail.website}</a>
            </span>
          </div>
        )}
        <Row label={t('fields.notes')} value={detail?.notes} />
      </div>

      {/* Last band of the column, so it sits on the screen's own edge. */}
      {bottomActions && bar}
    </div>
  )
}

/** A labelled row, or nothing at all: a label with no value reads as data that went missing. */
function Row({ label, value }: { label: string; value: string | null | undefined }) {
  if (!value) return null
  return (
    <div className="contact-card-row">
      <span className="contact-card-label">{label}</span>
      <span className="contact-card-value">{value}</span>
    </div>
  )
}

/** The postal address as it is written on an envelope, empty components skipped. */
function postalLines(postal: ContactDetailPostal): string[] {
  const city = [postal.postalCode, postal.locality].filter(Boolean).join(' ')
  return [postal.poBox, postal.extended, postal.street, city, postal.region, postal.country]
    .map(part => part?.trim())
    .filter((part): part is string => !!part)
}

/**
 * The avatar's object URL, revoked with the blob that produced it: without the revocation every
 * contact opened would leave its picture in memory for the life of the tab.
 */
function usePhotoUrl(contactId: string | null, hasPhoto: boolean): string | null {
  const { data: blob } = useContactPhoto(contactId, hasPhoto)
  const [url, setUrl] = useState<string | null>(null)

  useEffect(() => {
    if (!blob) {
      setUrl(null)
      return
    }
    const objectUrl = URL.createObjectURL(blob)
    setUrl(objectUrl)
    return () => URL.revokeObjectURL(objectUrl)
  }, [blob])

  return url
}
