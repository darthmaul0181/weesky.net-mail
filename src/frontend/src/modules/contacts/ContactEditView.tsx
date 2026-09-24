import { useEffect, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import DropdownMenu from '../../components/DropdownMenu'
import PencilIcon from '../../icons/PencilIcon'
import CalendarIcon from '../../icons/CalendarIcon'
import PersonPlusIcon from '../../icons/PersonPlusIcon'
import StarIcon from '../../icons/StarIcon'
import { birthdayToInput, inputToBirthday } from './contactBirthday'
import { PHOTO_TOO_LARGE, PHOTO_UNREADABLE, reducePhoto, type PhotoErrorKey } from './contactPhoto'
import { EmailLines, PhoneLines, POSTAL_PARTS, PostalLines, useContactLines } from './ContactLineFields'
import { sanitizeTypeForSubmit } from './contactLineTypes'
import { initialsOf } from './contactName'
import type { ContactDetail, ContactDraft, ContactDraftEmail } from './contactTypes'

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

/** The column width the backend also enforces (VARCHAR(100) on the three names): stopping the
    typing is what spares the user a round trip ending in a banner. */
const NAME_MAX = 100

/** The ten optional fields; `group` says which side of the form a revealed one joins. A field the
 * card fills is rendered, never offered: the menu hides emptiness, not content. `displayName`'s
 * 255 mirrors its column width, since `ContactValidator.Validate` never bounds it. */
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

/** Here the server reads `null` as "not named, keep the card's own" and '' as a clear, the
 * opposite of the names. An untouched field sends `null`, which also spares a NOTE, ORG or URL the
 * projector truncated from being rewritten by an unrelated edit. */
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
    // Not `submitted`: `Apply` replaces the two names, null included, so an empty displayName goes
    // as null and the server computes the FN (an empty string would strip it). Echoing the seed
    // back froze the FN at the name the card was created with.
    NAMES.has(f.key) ? blank(scalars[f.key]) : submitted(scalars[f.key], seeded[f.key]),
  ])) as Record<OptionalKey, string | null>
}

/** What the user did to the photo, not what it is worth: the seeded picture arrives asynchronously
    (the layout resolves it from a query), so a value frozen at mount would read `null` and turn a
    removal into "unchanged". */
type PhotoChoice =
  | { kind: 'kept' }
  | { kind: 'removed' }
  | { kind: 'chosen'; base64: string; url: string }

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
  // Grows with the menu, never shrinks: a field emptied by the user stays on screen.
  const [revealed, setRevealed] = useState<Set<OptionalKey>>(() =>
    new Set(OPTIONAL.filter(f => blank(contact?.[f.key]) != null).map(f => f.key)))
  const { addresses, phones, postals } = useContactLines(contact)

  const [choice, setChoice] = useState<PhotoChoice>({ kind: 'kept' })
  const [photoError, setPhotoError] = useState<PhotoErrorKey | null>(null)
  const fileInput = useRef<HTMLInputElement>(null)

  // Revoked with the choice that created it: three successive choices would otherwise hold three
  // images for the life of the tab.
  useEffect(() => {
    if (choice.kind !== 'chosen') return
    return () => URL.revokeObjectURL(choice.url)
  }, [choice])

  const shownPhoto = choice.kind === 'chosen' ? choice.url : choice.kind === 'kept' ? photo : null
  const removable = choice.kind === 'chosen' || (choice.kind === 'kept' && photo != null)

  const initials = initialsOf(firstName, lastName, scalars.nickname)
  const revealedIn = (group: 'name' | 'other') =>
    OPTIONAL.filter(f => f.group === group && revealed.has(f.key))

  const kept = addresses.lines.filter(line => line.address.trim() !== '')
  // The same gate the backend enforces, so the user never spends a round trip to be told.
  const valid = blank(firstName) != null || blank(lastName) != null
    || blank(scalars.nickname) != null || kept.length > 0
  // Ranked on the rows the submit designates from, never on the blank ones it drops: designating
  // a row and then emptying its text would otherwise badge a line the save promotes past.
  const ranked = kept.length > 0 ? kept : addresses.lines
  const primary = ranked[primaryIndexOf(ranked)]

  function changeScalar(key: OptionalKey, value: string) {
    setScalars(previous => ({ ...previous, [key]: value }))
  }

  function reveal(key: OptionalKey) {
    setRevealed(previous => new Set(previous).add(key))
  }

  async function pickPhoto(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    // Cleared right away: re-picking the same file after an error would otherwise raise no change.
    event.target.value = ''
    if (!file) return

    setPhotoError(null)
    try {
      const { base64, blob } = await reducePhoto(file)
      setChoice({ kind: 'chosen', base64, url: URL.createObjectURL(blob) })
    } catch (error) {
      // Anything the module does not flag as too large reads as unreadable, which is also the
      // honest reading of an unexpected throw.
      setPhotoError((error as Error).message === PHOTO_TOO_LARGE ? PHOTO_TOO_LARGE : PHOTO_UNREADABLE)
    }
  }

  function removePhoto() {
    setPhotoError(null)
    setChoice(choice.kind === 'chosen' ? { kind: 'kept' } : { kind: 'removed' })
  }

  /** The design's fixed table: a token the table does not name is shown raw. */
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
      photo: choice.kind === 'chosen' ? choice.base64 : choice.kind === 'removed' ? '' : null,
      isFavorite,
      addresses: kept.map(line => ({
        position: line.position,
        address: line.address.trim(),
        type: sanitizeTypeForSubmit(line.type),
        // 101 is the erasure: without it, designating B primary would leave A claiming it too.
        pref: line === primary ? 1 : 101,
      })),
      phones: phones.lines
        .filter(line => line.number.trim() !== '')
        .map(line => ({
          position: line.position, number: line.number.trim(), type: sanitizeTypeForSubmit(line.type),
        })),
      // An address whose seven components are all blank says nothing, whatever its type: the
      // validator finds it meaningful on the type alone and would pose an empty ADR in the card.
      postalAddresses: postals.lines
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
            of reaching them. The avatar is the file picker — no separate button competing with it. */}
        <div className="contact-editor-hero">
          <div className="contact-editor-face">
            <button type="button" className="contact-editor-avatar-button"
              aria-label={t('editor.changePhoto')} onClick={() => fileInput.current?.click()}>
              {shownPhoto && (
                <img className="contact-editor-avatar" src={shownPhoto} alt="" data-testid="editor-photo" />
              )}
              {!shownPhoto && initials !== '' && (
                <span className="contact-editor-avatar is-initials" data-testid="editor-initials">{initials}</span>
              )}
              {!shownPhoto && initials === '' && (
                <span className="contact-editor-avatar is-blank" data-testid="editor-avatar-blank">
                  <PersonPlusIcon />
                </span>
              )}
            </button>
            <input ref={fileInput} type="file" hidden data-testid="editor-photo-input"
              accept="image/jpeg,image/png,image/gif,image/webp" onChange={event => void pickPhoto(event)} />
            {removable && (
              <button type="button" className="contact-editor-photo-remove" onClick={removePhoto}>
                {t('editor.removePhoto')}
              </button>
            )}
            {/* Under the avatar, not in the banner: the field failed, not the save. */}
            {photoError && (
              <p className="contact-editor-photo-error" data-testid="editor-photo-error">{t(photoError)}</p>
            )}
          </div>
          <div className="contact-editor-identity">
            <div className="field-v">
              <label htmlFor="contact-first-name">{t('editor.firstName')}</label>
              <input id="contact-first-name" type="text" value={firstName} maxLength={NAME_MAX}
                // eslint-disable-next-line jsx-a11y/no-autofocus -- the editor is a full route, not a dialog Modal: its first field autofocuses on open the way SearchBar's does
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

        <EmailLines addresses={addresses} primary={primary} />
        <PhoneLines phones={phones} />

        </div>
        <div className="contact-editor-col is-aside">

        <PostalLines postals={postals} />

        {/* Text, not a date picker: the vCard admits partial dates a picker cannot express. The
            field goes through contactBirthday (`19930621T115900Z` shows as a date, `27/10/1979`
            is stored in vCard form); text neither form recognises passes through untouched. */}
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
          /* `align`: `.dropdown-root` spans the column, so right anchoring put the menu 223px off
             the link. `direction`: the fold is right below, and the menu measured 75px past it. */
          <DropdownMenu ariaLabel={t('editor.addField')} className="contact-address-add"
            direction="auto" align="left" trigger={t('editor.addField')}
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
