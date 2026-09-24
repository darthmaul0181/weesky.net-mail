import { useTranslation } from 'react-i18next'
import CheckIcon from '../../icons/CheckIcon'
import MailIcon from '../../icons/MailIcon'
import MapPinIcon from '../../icons/MapPinIcon'
import PhoneIcon from '../../icons/PhoneIcon'
import { PHONE_TYPES, POSTAL_TYPES, stripPref } from './contactLineTypes'
import { LineList, LineRow, LineTypeSelect } from './LineList'
import { useLineList, type LineListState } from './useLineList'
import type { ContactDetail, ContactDraftEmail, ContactDraftPhone, ContactDraftPostal } from './contactTypes'

/** VARCHAR(320), the address column's width: stopping the typing spares a round trip ending in a
    banner. */
const ADDRESS_MAX = 320

/** `ContactValidator.MaxAddressesPerContact` / `MaxPhonesPerContact` /
    `MaxPostalAddressesPerContact`: the add button disappears at the cap rather than
    letting the save fail on a banner. */
const EMAIL_MAX = 50
const PHONE_MAX = 10
const POSTAL_MAX = 10

export const POSTAL_PARTS = [
  'poBox', 'extended', 'street', 'locality', 'region', 'postalCode', 'country',
] as const
type PostalPart = (typeof POSTAL_PARTS)[number]

const emptyRow = (): ContactDraftEmail => ({ position: null, address: '', type: '', pref: null })
const emptyPhone = (): ContactDraftPhone => ({ position: null, number: '', type: '' })
const emptyPostal = (): ContactDraftPostal => ({
  position: null, type: '', poBox: null, extended: null, street: null,
  locality: null, region: null, postalCode: null, country: null,
})

/** The editor's three repeatable families, seeded once from the card. */
export function useContactLines(contact: ContactDetail | null) {
  // The card's own rank per line, never the array index: a deleted address leaves a hole, and a
  // line arriving without its position is rebuilt by the composer, group and X- parameters lost.
  // Never zero rows: an address list with no box to type in offers no way back.
  const addresses = useLineList<ContactDraftEmail>(() =>
    contact && contact.addresses.length > 0
      ? contact.addresses.map(line => ({
          position: line.position, address: line.address, type: stripPref(line.type), pref: null,
        }))
      : [emptyRow()], { blank: emptyRow, keepOne: true, max: EMAIL_MAX })

  // No seeded empty row here, unlike addresses: neither family is required, so an empty list is a
  // valid, final answer rather than a state the user must first fill something into.
  const phones = useLineList<ContactDraftPhone>(() =>
    contact
      ? contact.phones.map(line => ({ position: line.position, number: line.number, type: stripPref(line.type) }))
      : [], { blank: emptyPhone, keepOne: false, max: PHONE_MAX })
  const postals = useLineList<ContactDraftPostal>(() =>
    contact
      ? contact.postalAddresses.map(line => ({
          position: line.position, type: stripPref(line.type), poBox: line.poBox, extended: line.extended,
          street: line.street, locality: line.locality, region: line.region,
          postalCode: line.postalCode, country: line.country,
        }))
      : [], { blank: emptyPostal, keepOne: false, max: POSTAL_MAX })
  return { addresses, phones, postals }
}

export function EmailLines({ addresses, primary }: {
  addresses: LineListState<ContactDraftEmail>; primary: ContactDraftEmail | undefined
}) {
  const { t } = useTranslation('contacts')
  // The preference is a property of the line, not its rank: moving the line would change nothing
  // now that the composer puts it back at its own position.
  function makePrimary(index: number) {
    addresses.replace(addresses.lines.map((line, i) => ({ ...line, pref: i === index ? 1 : 101 })))
  }

  return (
    <LineList icon={<MailIcon size={15} />} label={t('fields.addresses')} addLabel={t('editor.addAddress')}
      list={addresses}>
      {(line, index) => (
        <LineRow className="contact-address-row is-email" testId={`address-row-${index}`}
          removeLabel={t('editor.removeAddress', { index: index + 1 })} onRemove={() => addresses.remove(index)}>
          <label className="visually-hidden" htmlFor={`contact-address-${index}`}>
            {t('editor.addressLabel', { index: index + 1 })}
          </label>
          <input id={`contact-address-${index}`} type="email" value={line.address}
            placeholder={t('editor.addressPlaceholder')} maxLength={ADDRESS_MAX}
            onChange={event => addresses.update(index, { address: event.target.value })} />
          {line === primary
            ? <span className="contact-address-primary">{t('fields.primary')}</span>
            : (
              // Text content, not aria-label: an aria-label containing "address N" is also
              // picked up by getByLabelText(/address N/i), which collides with the field.
              // The name says which row it acts on, the tooltip does not have to.
              <button type="button" className="admin-icon-btn" title={t('editor.makePrimary')}
                onClick={() => makePrimary(index)}>
                <CheckIcon size={14} />
                <span className="visually-hidden">{t('editor.makePrimaryLine', { index: index + 1 })}</span>
              </button>
            )}
        </LineRow>
      )}
    </LineList>
  )
}

export function PhoneLines({ phones }: { phones: LineListState<ContactDraftPhone> }) {
  const { t } = useTranslation('contacts')
  return (
    <LineList icon={<PhoneIcon size={15} />} label={t('fields.phones')} addLabel={t('editor.addPhone')}
      list={phones}>
      {(line, index) => (
        <LineRow className="contact-address-row" testId={`phone-row-${index}`}
          removeLabel={t('editor.removePhone', { index: index + 1 })} onRemove={() => phones.remove(index)}>
          <label className="visually-hidden" htmlFor={`contact-phone-${index}`}>
            {t('editor.phoneLabel', { index: index + 1 })}
          </label>
          <input id={`contact-phone-${index}`} type="tel" value={line.number}
            placeholder={t('editor.phonePlaceholder')}
            onChange={event => phones.update(index, { number: event.target.value })} />
          <LineTypeSelect id={`contact-phone-type-${index}`} label={t('editor.phoneType', { index: index + 1 })}
            types={PHONE_TYPES} value={line.type} onChange={type => phones.update(index, { type })} />
        </LineRow>
      )}
    </LineList>
  )
}

export function PostalLines({ postals }: { postals: LineListState<ContactDraftPostal> }) {
  const { t } = useTranslation('contacts')
  return (
    <LineList icon={<MapPinIcon size={15} />} label={t('fields.postal')} addLabel={t('editor.addPostal')}
      list={postals}>
      {(line, index) => {
        const part = (key: PostalPart, label: string, className?: string) => (
          <>
            <label className="visually-hidden" htmlFor={`contact-postal-${key.toLowerCase()}-${index}`}>{label}</label>
            <input id={`contact-postal-${key.toLowerCase()}-${index}`} type="text" value={line[key] ?? ''}
              placeholder={label} className={className}
              onChange={event => postals.update(index, { [key]: event.target.value })} />
          </>
        )
        return (
          <div className="contact-postal-item" data-testid={`postal-row-${index}`}>
            {/* Street with its type, then the city line, then region and country. PO box
                and extended only when the card carries them: all seven stay editable, without
                two empty boxes on every address. */}
            {(line.poBox ?? '') !== '' || (line.extended ?? '') !== '' ? (
              <div className="contact-postal-row">
                {part('poBox', t('editor.postal.poBox'))}
                {part('extended', t('editor.postal.extended'))}
              </div>
            ) : null}
            <LineRow className="contact-postal-row" removeLabel={t('editor.removePostal', { index: index + 1 })}
              onRemove={() => postals.remove(index)}>
              {part('street', t('editor.postal.street'), 'contact-postal-full')}
              <LineTypeSelect id={`contact-postal-type-${index}`} label={t('editor.postalType', { index: index + 1 })}
                types={POSTAL_TYPES} value={line.type} className="contact-postal-type"
                onChange={type => postals.update(index, { type })} />
            </LineRow>
            <div className="contact-postal-row">
              {part('postalCode', t('editor.postal.postalCode'), 'contact-postal-short')}
              {part('locality', t('editor.postal.locality'))}
            </div>
            <div className="contact-postal-row">
              {part('region', t('editor.postal.region'))}
              {part('country', t('editor.postal.country'))}
            </div>
          </div>
        )
      }}
    </LineList>
  )
}
