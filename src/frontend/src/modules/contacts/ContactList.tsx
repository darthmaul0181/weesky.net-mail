import { useMemo, useState } from 'react'
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
  contacts, selectedId, onSelect, onToggleFavorite, onEdit, onDelete,
}: Props) {
  const [query, setQuery] = useState('')
  const shown = useMemo(() => filterContacts(contacts, query), [contacts, query])
  const filtering = query.trim() !== ''

  return (
    <>
      <div className="contacts-list-heading">
        <span className="contacts-search">
          <SearchIcon size={14} />
          <input type="search" className="search-input" aria-label="Search contacts"
            placeholder="Search contacts…" value={query}
            onChange={event => setQuery(event.target.value)} />
        </span>
        {/* Matching over total while filtering, the bare count otherwise: "2 / 2" reads as though
            something were hidden. */}
        <span className="contacts-count" data-testid="contact-count">
          {filtering ? `${shown.length} / ${contacts.length}` : contacts.length}
        </span>
      </div>

      <div className="contacts-list-scroll">
        {contacts.length === 0 && <p className="contacts-empty">No contacts yet</p>}
        {contacts.length > 0 && shown.length === 0 && (
          <p className="contacts-empty">No matching contacts</p>
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
                <div className="contact-tile-line">
                  {/* The star leads on the far left and the actions close on the far right — the
                      tile anatomy, identical to the admin and identities lists. */}
                  <button type="button" className={`contact-star${contact.isFavorite ? ' is-on' : ''}`}
                    title={contact.isFavorite ? 'Remove from favourites' : 'Add to favourites'}
                    aria-label={contact.isFavorite
                      ? `Remove ${name} from favourites` : `Add ${name} to favourites`}
                    onClick={event => { event.stopPropagation(); onToggleFavorite(contact) }}>
                    <StarIcon size={14} filled={contact.isFavorite} />
                  </button>

                  <span className="contact-tile-name">{name}</span>

                  <span className="contact-tile-actions">
                    <button type="button" className="admin-icon-btn" title="Edit"
                      aria-label={`Edit ${name}`}
                      onClick={event => { event.stopPropagation(); onEdit(contact.id) }}>
                      <PencilIcon size={14} />
                    </button>
                    <button type="button" className="admin-icon-btn is-danger" title="Delete"
                      aria-label={`Delete ${name}`}
                      onClick={event => { event.stopPropagation(); onDelete(contact) }}>
                      <TrashIcon size={14} />
                    </button>
                  </span>
                </div>

                {/* Always rendered, even empty, so a contact with no address is not a shorter tile
                    than its neighbours. */}
                <div className="contact-tile-address">
                  {primary ?? ''}{extra > 0 ? ` · +${extra}` : ''}
                </div>
              </div>
            )
          })}
        </div>
      </div>
    </>
  )
}
