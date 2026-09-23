import type { TFunction } from 'i18next'
import type { ReactNode } from 'react'
import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import DropdownMenu from '../../components/DropdownMenu'
import ArrowLeftIcon from '../../icons/ArrowLeftIcon'
import CalendarIcon from '../../icons/CalendarIcon'
import GlobeIcon from '../../icons/GlobeIcon'
import KebabIcon from '../../icons/KebabIcon'
import MailIcon from '../../icons/MailIcon'
import MapPinIcon from '../../icons/MapPinIcon'
import PencilIcon from '../../icons/PencilIcon'
import PhoneIcon from '../../icons/PhoneIcon'
import PlainTextIcon from '../../icons/PlainTextIcon'
import StarIcon from '../../icons/StarIcon'
import TrashIcon from '../../icons/TrashIcon'
import UserIcon from '../../icons/UserIcon'
import { hasOpenLayer } from '../../lib/layerStack'
import { formatBirthday } from './contactBirthday'
import { typeLabel, visibleType } from './contactLineTypes'
import { displayNameOf, initialsOf } from './contactName'
import type { Contact, ContactDetailPostal } from './contactTypes'
import { websiteHref } from './contactWebsite'
import { useContact } from './queries'
import { useContactPhotoUrl } from './useContactPhotoUrl'

interface Props {
  contact: Contact | null
  /** Set only where the card has replaced the list — a phone. It draws the ← and binds Escape. */
  onBack?: () => void
  onEdit: (id: string) => void
  onDelete: (contact: Contact) => void
  onToggleFavorite: (contact: Contact) => void
  /** Opens the composer on this address. The layout owns it, like onEdit — the card knows the
      contact, the layout knows the router. */
  onWrite: (address: string) => void
  /** The groups holding this contact — the layout's own `groupsOf`, resolved off the group list
      rather than carried on the contact, since a group's membership lives on the group. */
  groups?: { id: string; name: string }[]
  /** Drops this contact from one group. Absent when there is nowhere to drop it from. */
  onRemoveFromGroup?: (groupId: string) => void
  /** The phone's shape: a band of named cells, and the Directions link (`geo:` is phone-only). Set
   * by the layout, the only thing that knows the tier; never a `useViewport` of the card's own. */
  bottomActions?: boolean
}

/** The contact in reading mode; editing has its own route. A banner, who this is, the actions,
 * then the data. A row renders only when its datum exists: an empty labelled row reads as data
 * that went missing. */
export default function ContactCard({
  contact, onBack, onEdit, onDelete, onToggleFavorite, onWrite, groups, onRemoveFromGroup,
  bottomActions = false,
}: Props) {
  const { t } = useTranslation('contacts')

  // The list paints the card at once and the detail enriches it when it lands: a selection that
  // began with a blank would flicker on every click, for a request that costs one round trip.
  const { data: detail } = useContact(contact?.id ?? null)
  const photo = useContactPhotoUrl(
    contact?.id ?? null, detail?.hasPhoto ?? false, detail?.cardHash ?? null)

  // Escape mirrors the ← button, MessageReader's arrangement, and like it exists only where the
  // card has replaced the list. Any open layer owns the key, so the card never backs out from
  // under a dialog — its own delete confirm or anything else on the screen.
  useEffect(() => {
    if (!onBack) return
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !event.defaultPrevented && !hasOpenLayer()) onBack()
    }
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
        <div className="contact-card-banner">{back}</div>
        <p className="contacts-empty contacts-card-invite">{t('card.invite')}</p>
      </div>
    )
  }

  // The card's own once it is here — the server ranked them by pref then position — the list's
  // until then. The list carries no type, so a chip appears with the detail rather than before it.
  const addresses = detail?.addresses
    ?? contact.addresses.map(address => ({ address, type: '' }))
  const phones = detail?.phones ?? []
  const postals = detail?.postalAddresses ?? []

  const initials = initialsOf(contact.firstName ?? '', contact.lastName ?? '', contact.nickname ?? '')
  // What situates somebody belongs to their name, not to a row among the phone numbers.
  const role = detail?.jobTitle
  const org = [detail?.organization, detail?.department].filter(Boolean).join(' · ')
  const websiteUrl = detail?.website ? websiteHref(detail.website) : null

  const favouriteLabel = t(contact.isFavorite ? 'favourites.remove' : 'favourites.add')
  const editLabel = t('actions.edit', { ns: 'common' })
  const deleteLabel = t('actions.delete', { ns: 'common' })

  /* The tile's own vocabulary, moved up to the banner: the star outside the cluster because it is
     a flag rather than an action, then `.admin-icon-btn` for what acts on the contact. Delete is
     one click deeper than the two reversible actions beside it. */
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

  // Write opens this webmail's composer, not the OS's mailto: handler (none, or the wrong app); Call
  // stays `tel:`. Directions hands `geo:` to the device's maps app, so nothing reaches a third party,
  // and is phone-only: no desktop browser handles it. Each is drawn only when it has a target.
  const primaryAddress = addresses[0]?.address
  const firstPhone = phones[0]?.number
  const geoHref = bottomActions ? geoLink(postals[0]) : null
  const quick = (primaryAddress || firstPhone || geoHref) ? (
    <div className="contact-card-quick">
      {primaryAddress && (
        <button type="button" className="contact-quick-btn"
          onClick={() => onWrite(primaryAddress)}>
          <MailIcon size={15} />{t('card.write')}
        </button>
      )}
      {firstPhone && (
        <a className="contact-quick-btn is-ghost" href={`tel:${firstPhone.replace(/\s/g, '')}`}>
          <PhoneIcon size={15} />{t('card.call')}
        </a>
      )}
      {geoHref && (
        <a className="contact-quick-btn is-ghost" href={geoHref}>
          <MapPinIcon size={15} />{t('card.directions')}
        </a>
      )}
    </div>
  ) : null

  return (
    <div className="contact-card">
      <div className="contact-card-banner">
        {back}
        {!bottomActions && cluster}
      </div>

      <div className="contact-card-identity">
        {photo
          ? <img className="contact-card-avatar" src={photo} alt="" data-testid="card-photo" />
          : (
            <span className={`contact-card-avatar${initials ? ' is-initials' : ' is-blank'}`}
              aria-hidden="true" data-testid="card-initials">
              {initials || <UserIcon size={30} />}
            </span>
          )}
        <h2 className="contact-card-name">{displayNameOf(contact)}</h2>
        {role && <p className="contact-card-role">{role}</p>}
        {org && <p className="contact-card-org">{org}</p>}
      </div>

      {quick}

      {groups && groups.length > 0 && (
        <div className="contact-card-groups">
          {groups.map(g => (
            <span key={g.id} className="contact-group-chip">
              {g.name}
              <button type="button" aria-label={t('card.removeFromGroup', { name: g.name })}
                onClick={() => onRemoveFromGroup?.(g.id)}>✕</button>
            </span>
          ))}
        </div>
      )}

      <div className="contact-card-body">
        <div className="contact-card-rows">
          {addresses.length > 0 && (
            <CardRow icon={<MailIcon size={15} />} label={t('fields.addresses')}>
              {addresses.map((line, index) => (
                <span key={index} className="contact-card-value" data-testid="card-address">
                  <button type="button" className="contact-card-link" onClick={() => onWrite(line.address)}>
                    {line.address}
                  </button>
                  <TypeChip type={line.type} t={t} />
                  {index === 0 && <span className="contact-card-primary">{t('fields.primary')}</span>}
                </span>
              ))}
            </CardRow>
          )}

          {phones.length > 0 && (
            <CardRow icon={<PhoneIcon size={15} />} label={t('fields.phones')}>
              {phones.map(phone => (
                <span key={`${phone.position}-${phone.number}`} className="contact-card-value"
                  data-testid="card-phone">
                  <a href={`tel:${phone.number.replace(/\s/g, '')}`}>{phone.number}</a>
                  <TypeChip type={phone.type} t={t} />
                </span>
              ))}
            </CardRow>
          )}

          {postals.length > 0 && (
            <CardRow icon={<MapPinIcon size={15} />} label={t('fields.postal')}>
              {postals.map(postal => (
                <span key={postal.position} className="contact-card-value is-postal"
                  data-testid="card-postal">
                  <TypeChip type={postal.type} t={t} />
                  {postalLines(postal).map((line, index) => <span key={index}>{line}</span>)}
                </span>
              ))}
            </CardRow>
          )}

          <Row icon={<UserIcon size={15} />} label={t('fields.nickname')} value={contact.nickname} />
          <Row icon={<CalendarIcon size={15} />} label={t('fields.birthday')}
            value={formatBirthday(detail?.birthday)} />
          {detail?.website && (
            <CardRow icon={<GlobeIcon />} label={t('fields.website')}>
              <span className="contact-card-value">
                {websiteUrl
                  ? <a href={websiteUrl} target="_blank" rel="noreferrer noopener">{detail.website}</a>
                  : detail.website}
              </span>
            </CardRow>
          )}
          <Row icon={<PlainTextIcon size={15} />} label={t('fields.notes')} value={detail?.notes} />
        </div>
      </div>

      {/* Last band of the column, so it sits on the screen's own edge. */}
      {bottomActions && bar}
    </div>
  )
}

/** The kind of line this is — Mobile, Domicile, Bureau — or nothing when the stored token names
    no kind a reader would recognise. */
function TypeChip({ type, t }: { type: string; t: TFunction<'contacts'> }) {
  const shown = visibleType(type)
  if (shown === '') return null
  return <span className="contact-card-type">{typeLabel(shown, t)}</span>
}

/** A labelled row. The icon is the editor's own for that family, so the two screens name one
    family one way — the reason `displayNameOf` is shared, applied to the glyph. */
function CardRow({ icon, label, children }: { icon: ReactNode; label: string; children: ReactNode }) {
  return (
    <div className="contact-card-row">
      <span className="contact-card-label">{icon}{label}</span>
      <span className="contact-card-values">{children}</span>
    </div>
  )
}

/** A single-value row, or nothing at all: a label with no value reads as data that went missing. */
function Row({ icon, label, value }: { icon: ReactNode; label: string; value: string | null | undefined }) {
  if (!value) return null
  return (
    <CardRow icon={icon} label={label}>
      <span className="contact-card-value">{value}</span>
    </CardRow>
  )
}

/** RFC 5870 `geo:` with the de-facto `?q=` search, or null without a street or a locality (a
 * country alone opens a whole nation). `0,0` is the required placeholder; the query resolves. */
function geoLink(postal: ContactDetailPostal | undefined): string | null {
  if (!postal) return null
  if (!postal.street?.trim() && !postal.locality?.trim()) return null
  return `geo:0,0?q=${encodeURIComponent(postalLines(postal).join(', '))}`
}

/** The postal address as it is written on an envelope, empty components skipped. */
function postalLines(postal: ContactDetailPostal): string[] {
  const city = [postal.postalCode, postal.locality].filter(Boolean).join(' ')
  return [postal.poBox, postal.extended, postal.street, city, postal.region, postal.country]
    .map(part => part?.trim())
    .filter((part): part is string => !!part)
}
