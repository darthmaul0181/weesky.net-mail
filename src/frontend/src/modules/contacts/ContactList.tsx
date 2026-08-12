import { useMemo, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import PencilIcon from '../../icons/PencilIcon.jsx'
import SearchIcon from '../../icons/SearchIcon'
import StarIcon from '../../icons/StarIcon'
import TrashIcon from '../../icons/TrashIcon.jsx'
import { displayNameOf, primaryAddressOf } from './contactName'
import { filterContacts } from './contactSearch'
import type { Contact } from './contactTypes'

interface Props {
  /** Already scoped by the layout; the text query is this component's own state. */
  contacts: Contact[]
  selectedId: string | null
  /** The drawer hamburger below 1024px, where the scope column is no longer beside this heading. */
  leading?: ReactNode
  onSelect: (id: string) => void
  onToggleFavorite: (contact: Contact) => void
  onEdit: (id: string) => void
  onDelete: (contact: Contact) => void
}

/**
 * The tiles, between a pinned heading band and nothing else — there is no pager: the whole book
 * is one cached list, so there is no page to go to.
 *
 * One tile skin, on two lines. The mail list carries two because three pane arrangements exist
 * there; here the list always sits beside the card, so a wide skin would be unreachable code.
 */
export default function ContactList({
  contacts, selectedId, leading, onSelect, onToggleFavorite, onEdit, onDelete,
}: Props) {
  const { t } = useTranslation('contacts')
  const [query, setQuery] = useState('')
  const shown = useMemo(() => filterContacts(contacts, query), [contacts, query])
  const filtering = query.trim() !== ''

  return (
    <>
      <div className="contacts-list-heading">
        {leading}
        <span className="contacts-search">
          <SearchIcon size={14} />
          <input type="search" className="search-input" aria-label={t('list.searchLabel')}
            placeholder={t('list.searchPlaceholder')} value={query}
            onChange={event => setQuery(event.target.value)} />
        </span>
        {/* Matching over total while filtering, the bare count otherwise: "2 / 2" reads as though
            something were hidden. */}
        <span className="contacts-count" data-testid="contact-count">
          {filtering ? `${shown.length} / ${contacts.length}` : contacts.length}
        </span>
      </div>

      <div className="contacts-list-scroll">
        {contacts.length === 0 && <p className="contacts-empty">{t('list.empty')}</p>}
        {contacts.length > 0 && shown.length === 0 && (
          <p className="contacts-empty">{t('list.noMatch')}</p>
        )}

        <div className="contact-tiles">
          {shown.map(contact => {
            const name = displayNameOf(contact)
            const primary = primaryAddressOf(contact)
            const extra = contact.addresses.length - 1

            return (
              <div key={contact.id} data-testid={`contact-tile-${contact.id}`}
                className={`contact-tile${contact.id === selectedId ? ' is-selected' : ''}`}
                role="button" tabIndex={0} onClick={() => onSelect(contact.id)}
                onKeyDown={event => {
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault()
                    onSelect(contact.id)
                  }
                }}>
                {/* The message row's layout, not the page tile's: the name takes the first line and
                    the star closes it on the right, while the actions are the tile's last child —
                    the cluster idiom, drawn over the bottom line rather than beside the name. */}
                <div className="contact-tile-line">
                  <span className="contact-tile-name">{name}</span>

                  <button type="button" className={`contact-star${contact.isFavorite ? ' is-on' : ''}`}
                    title={t(contact.isFavorite ? 'favourites.remove' : 'favourites.add')}
                    aria-label={t(
                      contact.isFavorite ? 'favourites.removeNamed' : 'favourites.addNamed', { name })}
                    onClick={event => { event.stopPropagation(); onToggleFavorite(contact) }}>
                    <StarIcon size={18} filled={contact.isFavorite} />
                  </button>
                </div>

                {/* Always rendered, even empty, so a contact with no address is not a shorter tile
                    than its neighbours. */}
                <div className="contact-tile-address">
                  {primary ?? ''}{extra > 0 ? ` · +${extra}` : ''}
                </div>

                <span className="contact-tile-actions">
                  <button type="button" className="admin-icon-btn" title={t('actions.edit', { ns: 'common' })}
                    aria-label={t('list.edit', { name })}
                    onClick={event => { event.stopPropagation(); onEdit(contact.id) }}>
                    <PencilIcon size={18} />
                  </button>
                  <button type="button" className="admin-icon-btn is-danger" title={t('actions.delete', { ns: 'common' })}
                    aria-label={t('list.delete', { name })}
                    onClick={event => { event.stopPropagation(); onDelete(contact) }}>
                    <TrashIcon size={18} />
                  </button>
                </span>
              </div>
            )
          })}
        </div>
      </div>
    </>
  )
}
