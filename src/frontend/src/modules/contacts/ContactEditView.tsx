import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import PencilIcon from '../../icons/PencilIcon.jsx'
import PersonPlusIcon from '../../icons/PersonPlusIcon.jsx'
import StarIcon from '../../icons/StarIcon'
import TrashIcon from '../../icons/TrashIcon.jsx'
import type { Contact, ContactDraft } from './contactTypes'

interface Props {
  /** null in create mode. One component for both, because "Add" has no selected contact and a
      second surface for the same form would be a second dialect of it. */
  contact: Contact | null
  saving: boolean
  error: string | null
  onSave: (draft: ContactDraft) => void
  onCancel: () => void
}

/** The column widths the backend also enforces (VARCHAR(100) on the three names, VARCHAR(320) on
    the address): stopping the typing is what spares the user a round trip ending in a banner. */
const NAME_MAX = 100
const ADDRESS_MAX = 320

/** Blank rows are dropped on submit; the trailing empty row exists so a create has something to
    type into without clicking "add" first. */
function blank(value: string): string | null {
  const trimmed = value.trim()
  return trimmed === '' ? null : trimmed
}

export default function ContactEditView({ contact, saving, error, onSave, onCancel }: Props) {
  const { t } = useTranslation('contacts')
  const [firstName, setFirstName] = useState(contact?.firstName ?? '')
  const [lastName, setLastName] = useState(contact?.lastName ?? '')
  const [nickname, setNickname] = useState(contact?.nickname ?? '')
  const [isFavorite, setIsFavorite] = useState(contact?.isFavorite ?? false)
  const [addresses, setAddresses] = useState<string[]>(
    contact && contact.addresses.length > 0 ? [...contact.addresses] : [''])

  const filled = addresses.map(blank).filter((a): a is string => a != null)
  // The same gate the backend enforces, so the user never spends a round trip to be told.
  const valid = blank(firstName) != null || blank(lastName) != null
    || blank(nickname) != null || filled.length > 0

  function change(index: number, value: string) {
    setAddresses(previous => previous.map((address, i) => (i === index ? value : address)))
  }

  function remove(index: number) {
    setAddresses(previous => {
      const next = previous.filter((_, i) => i !== index)
      // Never zero rows: an address list with no box to type in offers no way back.
      return next.length > 0 ? next : ['']
    })
  }

  // Reordering is how the primary changes — position 0 is the primary by definition, so there is
  // no flag that could fall out of step with the order.
  function moveUp(index: number) {
    setAddresses(previous => {
      if (index === 0) return previous
      const next = [...previous]
      ;[next[index - 1], next[index]] = [next[index], next[index - 1]]
      return next
    })
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    if (!valid || saving) return

    onSave({
      firstName: blank(firstName),
      lastName: blank(lastName),
      nickname: blank(nickname),
      isFavorite,
      addresses: filled,
    })
  }

  return (
    <form className="contact-editor-form" onSubmit={submit}>
      <div className="contact-editor-head">
        <h2 className="contact-editor-title">
          {/* PersonPlusIcon takes no size prop today (fixed 15px); PencilIcon does. */}
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

        {/* Full width is what lets these be .field-h rows at all: at the card's 380px, and worse
            at its 240px floor, a 110px label column leaves nothing for the control. */}
        <div className="field-h">
          <label htmlFor="contact-first-name">{t('editor.firstName')}</label>
          <input id="contact-first-name" type="text" value={firstName} maxLength={NAME_MAX}
            onChange={event => setFirstName(event.target.value)} autoFocus />
        </div>
        <div className="field-h">
          <label htmlFor="contact-last-name">{t('editor.lastName')}</label>
          <input id="contact-last-name" type="text" value={lastName} maxLength={NAME_MAX}
            onChange={event => setLastName(event.target.value)} />
        </div>
        <div className="field-h">
          <label htmlFor="contact-nickname">{t('fields.nickname')}</label>
          <input id="contact-nickname" type="text" value={nickname} maxLength={NAME_MAX}
            onChange={event => setNickname(event.target.value)} />
        </div>

        <div className="field-h contact-editor-addresses">
          <span className="field-h-label">{t('fields.addresses')}</span>
          <div className="contact-address-list">
            {addresses.map((address, index) => (
              <div key={index} className="contact-address-row" data-testid={`address-row-${index}`}>
                <label className="visually-hidden" htmlFor={`contact-address-${index}`}>
                  {t('editor.addressLabel', { index: index + 1 })}
                </label>
                <input id={`contact-address-${index}`} type="email" value={address}
                  placeholder={t('editor.addressPlaceholder')} maxLength={ADDRESS_MAX}
                  onChange={event => change(index, event.target.value)} />
                {index === 0
                  ? <span className="contact-address-primary">{t('fields.primary')}</span>
                  : (
                    // Text content, not aria-label: an aria-label containing "address N" is also
                    // picked up by getByLabelText(/address N/i), which collides with the field.
                    <button type="button" className="admin-icon-btn" title={t('editor.makePrimary')}
                      onClick={() => moveUp(index)}>
                      <span aria-hidden="true">↑</span>
                      <span className="visually-hidden">{t('editor.moveUp', { index: index + 1 })}</span>
                    </button>
                  )}
                <button type="button" className="admin-icon-btn is-danger" title={t('actions.remove', { ns: 'common' })}
                  onClick={() => remove(index)}>
                  <TrashIcon size={14} />
                  <span className="visually-hidden">{t('editor.removeAddress', { index: index + 1 })}</span>
                </button>
              </div>
            ))}
            <button type="button" className="contact-address-add"
              onClick={() => setAddresses(previous => [...previous, ''])}>
              {t('editor.addAddress')}
            </button>
          </div>
        </div>

        <div className="field-h">
          <label htmlFor="contact-favorite">{t('editor.favourite')}</label>
          <button type="button" id="contact-favorite"
            className={`contact-star${isFavorite ? ' is-on' : ''}`}
            aria-pressed={isFavorite}
            onClick={() => setIsFavorite(previous => !previous)}>
            <StarIcon size={16} filled={isFavorite} />
          </button>
        </div>
      </div>
    </form>
  )
}
