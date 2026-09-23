import { memo } from 'react'
import type { DragEvent } from 'react'
import { useTranslation } from 'react-i18next'
import PencilIcon from '../../icons/PencilIcon'
import StarIcon from '../../icons/StarIcon'
import TrashIcon from '../../icons/TrashIcon'
import { displayNameOf, primaryAddressOf } from './contactName'
import type { Contact } from './contactTypes'

/** Everything a tile asks the list to do, as one object built once: a callback the list re-creates
 * would redraw every tile. Each takes the contact it acts on, so none closes over a tile. */
export interface TileCallbacks {
  open: (id: string) => void
  /** The index the selection anchors on is a position in the list, so the list resolves it: the
      book has no Shift+click range, and nothing here reads that anchor back. */
  check: (id: string) => void
  toggleFavorite: (contact: Contact) => void
  edit: (id: string) => void
  remove: (contact: Contact) => void
  dragStart: (event: DragEvent<HTMLDivElement>, id: string) => void
  dragEnd: () => void
}

export interface ContactTileProps {
  contact: Contact
  checked: boolean
  /** The contact the card is showing. `aria-current`, never `aria-selected`: the checkboxes are a
      real multi-selection, so "selected" already means something else to this list's user. */
  open: boolean
  dragging: boolean
  on: TileCallbacks
}

/** One tile on two lines. It reads its own catalogue: four labels interpolate the contact's name,
 * which the list would recompute every render, and the subscription redraws it on a language switch. */
function ContactTile({ contact, checked, open, dragging, on }: ContactTileProps) {
  const { t } = useTranslation('contacts')
  const name = displayNameOf(contact)
  const primary = primaryAddressOf(contact)
  const extra = contact.addresses.length - 1

  return (
    /* eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/interactive-supports-focus --
       the grid pattern: the tab stop and the keys that open the card are the content
       cell's, and the row's click is the pointer affordance over its padding. */
    <div data-testid={`contact-tile-${contact.id}`}
      className={`contact-tile${open ? ' is-selected' : ''}`
        + (dragging ? ' is-dragging' : '')}
      role="row" onClick={() => on.open(contact.id)}
      draggable
      onDragStart={event => on.dragStart(event, contact.id)}
      onDragEnd={() => on.dragEnd()}>
      {/* Four cells, always four: a row may own nothing but cells, so each focusable part of the
          tile answers for itself rather than being swallowed by the one button the tile used to
          be. In the gutter the tile reserves permanently, as before. */}
      <div className="contact-tile-select" role="gridcell">
        <input type="checkbox" className="contact-tile-check"
          aria-label={t('list.selectOne', { name })}
          checked={checked}
          onClick={event => event.stopPropagation()}
          onChange={() => on.check(contact.id)} />
      </div>

      {/* The cell that replaces the old role=button tile: it carries the name, the address and the
          keys that open the card, while the CLICK stays the row's, so the padding around this box
          opens the contact as it always did. */}
      <div className="contact-tile-content" role="gridcell" tabIndex={-1}
        // Where the card already is, so Tab into the list lands on the contact on screen
        // rather than on the first tile's checkbox.
        aria-current={open || undefined}
        onKeyDown={event => {
          if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault()
            on.open(contact.id)
          }
        }}>
        {/* The message row's layout, not the page tile's: the name takes the first line and the
            star closes it on the right, while the actions are the tile's last child — the cluster
            idiom, drawn over the bottom line rather than beside it. */}
        <div className="contact-tile-line">
          <span className="contact-tile-name">{name}</span>
        </div>

        {/* Always rendered, even empty, so a contact with no address is not a shorter tile
            than its neighbours. */}
        <div className="contact-tile-address">
          {primary ?? ''}{extra > 0 ? ` · +${extra}` : ''}
        </div>
      </div>

      {/* The star left the name's line for a cell of its own, drawn where that line's
          flow already put it: cell order is arrow order, so it stays between the name
          and the cluster, which is what the eye sees. */}
      <div className="contact-tile-flag" role="gridcell">
        <button type="button" className={`contact-star${contact.isFavorite ? ' is-on' : ''}`}
          title={t(contact.isFavorite ? 'favourites.remove' : 'favourites.add')}
          aria-label={t(
            contact.isFavorite ? 'favourites.removeNamed' : 'favourites.addNamed', { name })}
          onClick={event => { event.stopPropagation(); on.toggleFavorite(contact) }}>
          <StarIcon size={18} filled={contact.isFavorite} />
        </button>
      </div>

      <span className="contact-tile-actions" role="gridcell">
        <button type="button" className="admin-icon-btn" title={t('actions.edit', { ns: 'common' })}
          aria-label={t('list.edit', { name })}
          onClick={event => { event.stopPropagation(); on.edit(contact.id) }}>
          <PencilIcon size={18} />
        </button>
        <button type="button" className="admin-icon-btn is-danger" title={t('actions.delete', { ns: 'common' })}
          aria-label={t('list.delete', { name })}
          onClick={event => { event.stopPropagation(); on.remove(contact) }}>
          <TrashIcon size={18} />
        </button>
      </span>
    </div>
  )
}

// Every prop is a primitive, this tile's contact or the list's one callback object, so the shallow
// compare makes a ticked box or a typed letter redraw one tile, not the whole book.
export default memo(ContactTile)
