import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import DropdownMenu from '../../components/DropdownMenu'
import CheckIcon from '../../icons/CheckIcon'
import PencilIcon from '../../icons/PencilIcon.jsx'
import CalendarIcon from '../../icons/CalendarIcon'
import MailIcon from '../../icons/MailIcon'
import MapPinIcon from '../../icons/MapPinIcon'
import PersonPlusIcon from '../../icons/PersonPlusIcon.jsx'
import PhoneIcon from '../../icons/PhoneIcon'
import StarIcon from '../../icons/StarIcon'
import TrashIcon from '../../icons/TrashIcon.jsx'
import { birthdayToInput, inputToBirthday } from './contactBirthday'
import { PHONE_TYPES, POSTAL_TYPES, sanitizeTypeForSubmit, stripPref, typeLabel, typeOptions } from './contactLineTypes'
import { initialsOf } from './contactName'
import type {
  ContactDetail, ContactDraft, ContactDraftEmail, ContactDraftPhone, ContactDraftPostal,
} from './contactTypes'

interface Props {
  /** null in create mode. One component for both, because "Add" has no selected contact and a
      second surface for the same form would be a second dialect of it. The whole card, not the
      list row: the row carries neither the line positions nor the display name. */
  contact: ContactDetail | null
  /** The avatar's object URL, resolved by the layout: the form stays free of queries, which is
      what lets its tests mount it without an auth or a query provider. */
  photo?: string | null
  saving: boolean
  error: string | null
  onSave: (draft: ContactDraft) => void
  onCancel: () => void
}

/** The column widths the backend also enforces (VARCHAR(100) on the three names, VARCHAR(320) on
    the address): stopping the typing is what spares the user a round trip ending in a banner. */
const NAME_MAX = 100
const ADDRESS_MAX = 320

/** `ContactValidator.MaxAddressesPerContact` / `MaxPhonesPerContact` /
    `MaxPostalAddressesPerContact` (décision 8): the add button disappears at the cap rather than
    letting the save fail on a banner. */
const EMAIL_MAX = 50
const PHONE_MAX = 10
const POSTAL_MAX = 10

const POSTAL_PARTS = [
  'poBox', 'extended', 'street', 'locality', 'region', 'postalCode', 'country',
] as const

/** The ten a card may carry and most contacts do not. `group` says which side of the form the
    field belongs to once revealed — a name joins the hero beside the other names, everything else
    the aside column. A field the card fills is always rendered
    and never offered here: the menu hides emptiness, never content. `as const` narrows `label` to
    the literal keys below, so the typed `t()` checks each one with no second union to keep in
    sync — the idiom `roleLabel.ts`'s `KEYS` and `apiErrorMessage.ts`'s `CODES` already use. `id`
    is kebab-case, like every other id in this file, where `key` is the camelCase wire name.
    `displayName`'s 255 mirrors `contacts.display_name`'s column width, not a validator constant:
    unlike the other eight, `ContactValidator.Validate` never bounds it. */
const OPTIONAL = [
  { key: 'nickname', id: 'nickname', label: 'fields.nickname', maxLength: NAME_MAX, long: false, group: 'name' },
  { key: 'displayName', id: 'display-name', label: 'editor.displayName', maxLength: 255, long: false, group: 'name' },
  { key: 'middleName', id: 'middle-name', label: 'editor.middleName', maxLength: 100, long: false, group: 'name' },
  { key: 'namePrefix', id: 'name-prefix', label: 'editor.namePrefix', maxLength: 50, long: false, group: 'name' },
  { key: 'nameSuffix', id: 'name-suffix', label: 'editor.nameSuffix', maxLength: 50, long: false, group: 'name' },
  { key: 'organization', id: 'organization', label: 'fields.organization', maxLength: 255, long: false, group: 'other' },
  { key: 'department', id: 'department', label: 'fields.department', maxLength: 255, long: false, group: 'other' },
  { key: 'jobTitle', id: 'job-title', label: 'fields.jobTitle', maxLength: 255, long: false, group: 'other' },
  { key: 'website', id: 'website', label: 'fields.website', maxLength: 512, long: false, group: 'other' },
  { key: 'notes', id: 'notes', label: 'fields.notes', maxLength: 16000, long: true, group: 'other' },
] as const

type OptionalKey = (typeof OPTIONAL)[number]['key']

/** `ContactValidator.MaxBirthdayLength`, enforced there exactly like the other length caps:
    stopping the typing is what spares the user a round trip ending in a banner. */
const BIRTHDAY_MAX = 64

/** Blank rows are dropped on submit; the trailing empty row exists so a create has something to
    type into without clicking "add" first. */
function blank(value: string | undefined): string | null {
  const trimmed = value?.trim() ?? ''
  return trimmed === '' ? null : trimmed
}

/** On these fields the server reads `null` as "the request does not name this field, the card
    keeps its own" and the empty string as the clear — the opposite of the names, where `null` is
    the user emptying the box. So an untouched field sends `null`, which also spares a NOTE, ORG
    or URL the projector truncated to its column from being rewritten by an unrelated edit. */
function submitted(value: string, seeded: string): string | null {
  const trimmed = value.trim()
  return trimmed === seeded.trim() ? null : trimmed
}

/** The two the server replaces rather than merges. */
const NAMES = new Set<OptionalKey>(['nickname', 'displayName'])

function scalarsToDraft(
  scalars: Record<OptionalKey, string>, seeded: Record<OptionalKey, string>,
): Record<OptionalKey, string | null> {
  return Object.fromEntries(OPTIONAL.map(f => [f.key,
    // The two names are excluded, because `Apply` replaces a name null included — there, null is
    // the user who emptied the box, where on the other eight it means the request did not name
    // the field at all. For displayName that also matters when nothing was typed: an empty string
    // strips the card's FN, which no valid vCard may lack, while null falls back to the one the
    // server computes. `submitted` would defeat it by echoing the seeded value back, which is
    // exactly how the FN used to freeze at the shape the name had on the day the card was made.
    NAMES.has(f.key) ? blank(scalars[f.key]) : submitted(scalars[f.key], seeded[f.key]),
  ])) as Record<OptionalKey, string | null>
}

const emptyRow = (): ContactDraftEmail => ({ position: null, address: '', type: '', pref: null })
const emptyPhone = (): ContactDraftPhone => ({ position: null, number: '', type: '' })
const emptyPostal = (): ContactDraftPostal => ({
  position: null, type: '', poBox: null, extended: null, street: null,
  locality: null, region: null, postalCode: null, country: null,
})

/** The primary is the line the user designated, and the first one until they do. */
function primaryIndexOf(lines: ContactDraftEmail[]): number {
  const chosen = lines.findIndex(line => line.pref === 1)
  return chosen >= 0 ? chosen : 0
}

export default function ContactEditView({
  contact, photo = null, saving, error, onSave, onCancel,
}: Props) {
  const { t } = useTranslation('contacts')
  const [firstName, setFirstName] = useState(contact?.firstName ?? '')
  const [lastName, setLastName] = useState(contact?.lastName ?? '')
  const [isFavorite, setIsFavorite] = useState(contact?.isFavorite ?? false)
  const [birthday, setBirthday] = useState(birthdayToInput(contact?.birthday))
  const [scalars, setScalars] = useState<Record<OptionalKey, string>>(() =>
    Object.fromEntries(OPTIONAL.map(f => [f.key, contact?.[f.key] ?? ''])) as Record<OptionalKey, string>)
  // What the card seeded, held for the whole life of the form: `submitted` reads it to tell a
  // field the user never touched from one they emptied on purpose.
  const [seededScalars] = useState(scalars)
  const [seededBirthday] = useState(birthday)
  // Grows with the menu, never shrinks: a field emptied by the user stays on screen (décision 1).
  const [revealed, setRevealed] = useState<Set<OptionalKey>>(() =>
    new Set(OPTIONAL.filter(f => blank(contact?.[f.key]) != null).map(f => f.key)))
  // The card's own rank per line, never the array index: a deleted address leaves a hole, and a
  // line arriving without its position is rebuilt by the composer, group and X- parameters lost.
  const [addresses, setAddresses] = useState<ContactDraftEmail[]>(() =>
    contact && contact.addresses.length > 0
      ? contact.addresses.map(line => ({
          position: line.position, address: line.address, type: stripPref(line.type), pref: null,
        }))
      : [emptyRow()])

  // No seeded empty row here, unlike addresses: neither family is required, so an empty list is a
  // valid, final answer rather than a state the user must first fill something into.
  const [phones, setPhones] = useState<ContactDraftPhone[]>(() =>
    contact
      ? contact.phones.map(line => ({ position: line.position, number: line.number, type: stripPref(line.type) }))
      : [])
  const [postalAddresses, setPostalAddresses] = useState<ContactDraftPostal[]>(() =>
    contact
      ? contact.postalAddresses.map(line => ({
          position: line.position, type: stripPref(line.type), poBox: line.poBox, extended: line.extended,
          street: line.street, locality: line.locality, region: line.region,
          postalCode: line.postalCode, country: line.country,
        }))
      : [])

  const initials = initialsOf(firstName, lastName, scalars.nickname)
  const revealedIn = (group: 'name' | 'other') =>
    OPTIONAL.filter(f => f.group === group && revealed.has(f.key))

  const kept = addresses.filter(line => line.address.trim() !== '')
  // The same gate the backend enforces, so the user never spends a round trip to be told.
  const valid = blank(firstName) != null || blank(lastName) != null
    || blank(scalars.nickname) != null || kept.length > 0
  // Ranked on the rows the submit designates from, never on the blank ones it drops: designating
  // a row and then emptying its text would otherwise badge a line the save promotes past.
  const ranked = kept.length > 0 ? kept : addresses
  const primary = ranked[primaryIndexOf(ranked)]

  function change(index: number, value: string) {
    setAddresses(previous =>
      previous.map((line, i) => (i === index ? { ...line, address: value } : line)))
  }

  function remove(index: number) {
    setAddresses(previous => {
      const next = previous.filter((_, i) => i !== index)
      // Never zero rows: an address list with no box to type in offers no way back.
      return next.length > 0 ? next : [emptyRow()]
    })
  }

  // The preference is a property of the line, not its rank: moving the line would change nothing
  // now that the composer puts it back at its own position (decision 5).
  function makePrimary(index: number) {
    setAddresses(previous => previous.map((line, i) => ({ ...line, pref: i === index ? 1 : 101 })))
  }

  function changePhone(index: number, value: string) {
    setPhones(previous => previous.map((line, i) => (i === index ? { ...line, number: value } : line)))
  }

  function changePhoneType(index: number, value: string) {
    setPhones(previous => previous.map((line, i) => (i === index ? { ...line, type: value } : line)))
  }

  function removePhone(index: number) {
    setPhones(previous => previous.filter((_, i) => i !== index))
  }

  function changePostalType(index: number, value: string) {
    setPostalAddresses(previous => previous.map((line, i) => (i === index ? { ...line, type: value } : line)))
  }

  function changePostalPart(index: number, part: (typeof POSTAL_PARTS)[number], value: string) {
    setPostalAddresses(previous =>
      previous.map((line, i) => (i === index ? { ...line, [part]: value } : line)))
  }

  function removePostal(index: number) {
    setPostalAddresses(previous => previous.filter((_, i) => i !== index))
  }

  function changeScalar(key: OptionalKey, value: string) {
    setScalars(previous => ({ ...previous, [key]: value }))
  }

  function reveal(key: OptionalKey) {
    setRevealed(previous => new Set(previous).add(key))
  }

  /** The design's fixed table (décision 4): a token the table does not name is shown raw. */
  function submit(event: FormEvent) {
    event.preventDefault()
    if (!valid || saving) return

    onSave({
      ...scalarsToDraft(scalars, seededScalars),
      // Compared as typed and stored as vCard: an untouched field is null and the card keeps the
      // spelling it arrived with, time component and all.
      birthday: submitted(birthday, seededBirthday) === null ? null : inputToBirthday(birthday),
      firstName: blank(firstName),
      lastName: blank(lastName),
      isFavorite,
      addresses: kept.map(line => ({
        position: line.position,
        address: line.address.trim(),
        type: sanitizeTypeForSubmit(line.type),
        // 101 is the erasure: without it, designating B primary would leave A claiming it too.
        pref: line === primary ? 1 : 101,
      })),
      phones: phones
        .filter(line => line.number.trim() !== '')
        .map(line => ({
          position: line.position, number: line.number.trim(), type: sanitizeTypeForSubmit(line.type),
        })),
      // An address whose seven components are all blank says nothing, whatever its type: the
      // validator finds it meaningful on the type alone and would pose an empty ADR in the card.
      postalAddresses: postalAddresses
        .filter(line => POSTAL_PARTS.some(part => (line[part] ?? '').trim() !== ''))
        .map(line => ({
          ...line,
          type: sanitizeTypeForSubmit(line.type),
          ...Object.fromEntries(POSTAL_PARTS.map(part => [part, blank(line[part] ?? '')])),
        })),
    })
  }

  return (
    <form className="contact-editor-form" onSubmit={submit}>
      <div className="contact-editor-head">
        <h2 className="contact-editor-title">
          {contact ? <PencilIcon size={16} /> : <PersonPlusIcon />}
          {t(contact ? 'editor.editTitle' : 'editor.newTitle')}
        </h2>
        <button type="submit" className="btn btn-primary contact-save-btn" disabled={!valid || saving}>
          {saving && <span className="spinner" data-testid="editor-spinner" />}
          {t('editor.save')}
        </button>
        {/* The ✕ is the only dismissal, as in every dialog of this app — no Cancel beside Save. */}
        <button type="button" className="modal-close" aria-label={t('editor.close')} onClick={onCancel}>✕</button>
      </div>

      <div className="contact-editor-body">
        {error && <div className="alert alert-error" role="alert">{error}</div>}

        {/* The face first, then the names beside it: what identifies the contact, before the ways
            of reaching them. The photo is shown, never replaced — no PHOTO write door exists. */}
        <div className="contact-editor-hero">
          {photo && <img className="contact-editor-avatar" src={photo} alt="" data-testid="editor-photo" />}
          {!photo && initials !== '' && (
            <span className="contact-editor-avatar is-initials" data-testid="editor-initials">{initials}</span>
          )}
          {!photo && initials === '' && (
            <span className="contact-editor-avatar is-blank" data-testid="editor-avatar-blank">
              <PersonPlusIcon />
            </span>
          )}
          <div className="contact-editor-identity">
            <div className="field-v">
              <label htmlFor="contact-first-name">{t('editor.firstName')}</label>
              <input id="contact-first-name" type="text" value={firstName} maxLength={NAME_MAX}
                onChange={event => setFirstName(event.target.value)} autoFocus />
            </div>
            <div className="field-v">
              <label htmlFor="contact-last-name">{t('editor.lastName')}</label>
              <input id="contact-last-name" type="text" value={lastName} maxLength={NAME_MAX}
                onChange={event => setLastName(event.target.value)} />
            </div>
            {revealedIn('name').map(f => (
              <div key={f.key} className="field-v">
                <label htmlFor={`contact-${f.id}`}>{t(f.label)}</label>
                <input id={`contact-${f.id}`} type="text" value={scalars[f.key]} maxLength={f.maxLength}
                  onChange={event => changeScalar(f.key, event.target.value)} />
              </div>
            ))}
          </div>
          {/* The star describes the contact, not the form, so it rides the hero rather than
              sitting as a labelled row among the fields. */}
          <label className="visually-hidden" htmlFor="contact-favorite">{t('editor.favourite')}</label>
          <button type="button" id="contact-favorite"
            className={`contact-star${isFavorite ? ' is-on' : ''}`}
            aria-pressed={isFavorite}
            onClick={() => setIsFavorite(previous => !previous)}>
            <StarIcon size={20} filled={isFavorite} />
          </button>
        </div>

        <div className="contact-editor-cols">
        <div className="contact-editor-col">

        <div className="field-v contact-editor-addresses">
          <span className="field-v-label"><MailIcon size={15} />{t('fields.addresses')}</span>
          <div className="contact-address-list">
            {addresses.map((line, index) => (
              <div key={index} className="contact-address-row" data-testid={`address-row-${index}`}>
                <label className="visually-hidden" htmlFor={`contact-address-${index}`}>
                  {t('editor.addressLabel', { index: index + 1 })}
                </label>
                <input id={`contact-address-${index}`} type="email" value={line.address}
                  placeholder={t('editor.addressPlaceholder')} maxLength={ADDRESS_MAX}
                  onChange={event => change(index, event.target.value)} />
                {line === primary
                  ? <span className="contact-address-primary">{t('fields.primary')}</span>
                  : (
                    // Text content, not aria-label: an aria-label containing "address N" is also
                    // picked up by getByLabelText(/address N/i), which collides with the field.
                    // The name says which row it acts on, the tooltip does not have to.
                    <button type="button" className="admin-icon-btn" title={t('editor.makePrimary')}
                      onClick={() => makePrimary(index)}>
                      <CheckIcon size={14} />
                      <span className="visually-hidden">
                        {t('editor.makePrimaryLine', { index: index + 1 })}
                      </span>
                    </button>
                  )}
                <button type="button" className="admin-icon-btn is-danger" title={t('actions.remove', { ns: 'common' })}
                  onClick={() => remove(index)}>
                  <TrashIcon size={14} />
                  <span className="visually-hidden">{t('editor.removeAddress', { index: index + 1 })}</span>
                </button>
              </div>
            ))}
            {addresses.length < EMAIL_MAX && (
              <button type="button" className="contact-address-add"
                onClick={() => setAddresses(previous => [...previous, emptyRow()])}>
                {t('editor.addAddress')}
              </button>
            )}
          </div>
        </div>

        <div className="field-v contact-editor-addresses">
          <span className="field-v-label"><PhoneIcon size={15} />{t('fields.phones')}</span>
          <div className="contact-address-list">
            {phones.map((line, index) => (
              <div key={index} className="contact-address-row" data-testid={`phone-row-${index}`}>
                <label className="visually-hidden" htmlFor={`contact-phone-${index}`}>
                  {t('editor.phoneLabel', { index: index + 1 })}
                </label>
                <input id={`contact-phone-${index}`} type="tel" value={line.number}
                  placeholder={t('editor.phonePlaceholder')}
                  onChange={event => changePhone(index, event.target.value)} />
                <label className="visually-hidden" htmlFor={`contact-phone-type-${index}`}>
                  {t('editor.phoneType', { index: index + 1 })}
                </label>
                <select id={`contact-phone-type-${index}`} value={line.type}
                  onChange={event => changePhoneType(index, event.target.value)}>
                  {typeOptions(PHONE_TYPES, line.type).map(option => (
                    <option key={option} value={option}>{typeLabel(option, t)}</option>
                  ))}
                </select>
                <button type="button" className="admin-icon-btn is-danger" title={t('actions.remove', { ns: 'common' })}
                  onClick={() => removePhone(index)}>
                  <TrashIcon size={14} />
                  <span className="visually-hidden">{t('editor.removePhone', { index: index + 1 })}</span>
                </button>
              </div>
            ))}
            {phones.length < PHONE_MAX && (
              <button type="button" className="contact-address-add"
                onClick={() => setPhones(previous => [...previous, emptyPhone()])}>
                {t('editor.addPhone')}
              </button>
            )}
          </div>
        </div>

        </div>
        <div className="contact-editor-col is-aside">

        <div className="field-v contact-editor-addresses">
          <span className="field-v-label"><MapPinIcon size={15} />{t('fields.postal')}</span>
          <div className="contact-address-list">
            {postalAddresses.map((line, index) => (
              <div key={index} className="contact-postal-item" data-testid={`postal-row-${index}`}>
                {/* Three rows, the shape the mockup settled on: street with its type, then the
                    city line, then region and country. PO box and extended only when the card
                    carries them — décision 9 keeps all seven editable, it does not demand two
                    empty boxes on every address. */}
                {(line.poBox ?? '') !== '' || (line.extended ?? '') !== '' ? (
                  <div className="contact-postal-row">
                    <label className="visually-hidden" htmlFor={`contact-postal-pobox-${index}`}>
                      {t('editor.postal.poBox')}
                    </label>
                    <input id={`contact-postal-pobox-${index}`} type="text" value={line.poBox ?? ''}
                      placeholder={t('editor.postal.poBox')}
                      onChange={event => changePostalPart(index, 'poBox', event.target.value)} />
                    <label className="visually-hidden" htmlFor={`contact-postal-extended-${index}`}>
                      {t('editor.postal.extended')}
                    </label>
                    <input id={`contact-postal-extended-${index}`} type="text" value={line.extended ?? ''}
                      placeholder={t('editor.postal.extended')}
                      onChange={event => changePostalPart(index, 'extended', event.target.value)} />
                  </div>
                ) : null}
                <div className="contact-postal-row">
                  <label className="visually-hidden" htmlFor={`contact-postal-street-${index}`}>
                    {t('editor.postal.street')}
                  </label>
                  <input id={`contact-postal-street-${index}`} type="text" value={line.street ?? ''}
                    placeholder={t('editor.postal.street')} className="contact-postal-full"
                    onChange={event => changePostalPart(index, 'street', event.target.value)} />
                  <label className="visually-hidden" htmlFor={`contact-postal-type-${index}`}>
                    {t('editor.postalType', { index: index + 1 })}
                  </label>
                  <select id={`contact-postal-type-${index}`} value={line.type}
                    className="contact-postal-type"
                    onChange={event => changePostalType(index, event.target.value)}>
                    {typeOptions(POSTAL_TYPES, line.type).map(option => (
                      <option key={option} value={option}>{typeLabel(option, t)}</option>
                    ))}
                  </select>
                  <button type="button" className="admin-icon-btn is-danger" title={t('actions.remove', { ns: 'common' })}
                    onClick={() => removePostal(index)}>
                    <TrashIcon size={14} />
                    <span className="visually-hidden">{t('editor.removePostal', { index: index + 1 })}</span>
                  </button>
                </div>
                <div className="contact-postal-row">
                  <label className="visually-hidden" htmlFor={`contact-postal-postalcode-${index}`}>
                    {t('editor.postal.postalCode')}
                  </label>
                  <input id={`contact-postal-postalcode-${index}`} type="text" value={line.postalCode ?? ''}
                    placeholder={t('editor.postal.postalCode')} className="contact-postal-short"
                    onChange={event => changePostalPart(index, 'postalCode', event.target.value)} />
                  <label className="visually-hidden" htmlFor={`contact-postal-locality-${index}`}>
                    {t('editor.postal.locality')}
                  </label>
                  <input id={`contact-postal-locality-${index}`} type="text" value={line.locality ?? ''}
                    placeholder={t('editor.postal.locality')}
                    onChange={event => changePostalPart(index, 'locality', event.target.value)} />
                </div>
                <div className="contact-postal-row">
                  <label className="visually-hidden" htmlFor={`contact-postal-region-${index}`}>
                    {t('editor.postal.region')}
                  </label>
                  <input id={`contact-postal-region-${index}`} type="text" value={line.region ?? ''}
                    placeholder={t('editor.postal.region')}
                    onChange={event => changePostalPart(index, 'region', event.target.value)} />
                  <label className="visually-hidden" htmlFor={`contact-postal-country-${index}`}>
                    {t('editor.postal.country')}
                  </label>
                  <input id={`contact-postal-country-${index}`} type="text" value={line.country ?? ''}
                    placeholder={t('editor.postal.country')}
                    onChange={event => changePostalPart(index, 'country', event.target.value)} />
                </div>
              </div>
            ))}
            {postalAddresses.length < POSTAL_MAX && (
              <button type="button" className="contact-address-add"
                onClick={() => setPostalAddresses(previous => [...previous, emptyPostal()])}>
                {t('editor.addPostal')}
              </button>
            )}
          </div>
        </div>

        {/* A native date picker can only express a full date; the vCard admits three others
            (décision 7), so this stays text. What travels is no longer what is typed: the field
            reads and writes through contactBirthday, so a card exported by a phone shows as a date
            instead of as `19930621T115900Z`, and the placeholder's own `27/10/1979` is stored as
            the vCard spelling instead of verbatim. Text neither form recognises still passes
            through untouched — that is the escape hatch décision 7 asked for. */}
        <span className="field-v-label"><CalendarIcon size={15} />{t('editor.misc')}</span>

        <div className="field-v">
          <label htmlFor="contact-birthday">{t('fields.birthday')}</label>
          <input id="contact-birthday" type="text" value={birthday} maxLength={BIRTHDAY_MAX}
            placeholder={t('editor.birthdayPlaceholder')}
            onChange={event => setBirthday(event.target.value)} />
        </div>

        {revealedIn('other').map(f => (
          <div key={f.key} className="field-v">
            <label htmlFor={`contact-${f.id}`}>{t(f.label)}</label>
            {f.long ? (
              <textarea id={`contact-${f.id}`} value={scalars[f.key]} maxLength={f.maxLength}
                onChange={event => changeScalar(f.key, event.target.value)} />
            ) : (
              <input id={`contact-${f.id}`} type="text" value={scalars[f.key]} maxLength={f.maxLength}
                onChange={event => changeScalar(f.key, event.target.value)} />
            )}
          </div>
        ))}
        {OPTIONAL.some(f => !revealed.has(f.key)) && (
          <DropdownMenu ariaLabel={t('editor.addField')} className="contact-address-add"
            trigger={t('editor.addField')}
            items={OPTIONAL.filter(f => !revealed.has(f.key)).map(f => (
              { label: t(f.label), onSelect: () => reveal(f.key) }
            ))} />
        )}

        </div>
        </div>
      </div>
    </form>
  )
}
