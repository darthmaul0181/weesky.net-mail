import { useEffect, useMemo, useRef, useState, type DragEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import SelectionBand from '../../components/SelectionBand'
import { DeleteConfirmModal } from '../../components/DeleteConfirmModal.jsx'
import PencilIcon from '../../icons/PencilIcon.jsx'
import PersonMinusIcon from '../../icons/PersonMinusIcon.jsx'
import SearchIcon from '../../icons/SearchIcon'
import StarIcon from '../../icons/StarIcon'
import TrashIcon from '../../icons/TrashIcon.jsx'
import { buildDragPill, LIST_GLYPH } from '../mail/list/dragImage'
import { useSelection } from '../mail/list/useSelection'
import { displayNameOf, primaryAddressOf } from './contactName'
import { filterContacts } from './contactSearch'
import { CONTACT_DRAG_MIME, dragIds, serializeContactDrag } from './dragContacts'
import type { Contact } from './contactTypes'

interface Props {
  /** Already scoped by the layout; the text query is this component's own state. */
  contacts: Contact[]
  selectedId: string | null
  /** The current scope, so leaving it empties a selection made under the previous one. */
  scope: string
  /** The drawer hamburger below 1024px, where the scope column is no longer beside this heading. */
  leading?: ReactNode
  /** Actions the band carries that act on the book rather than on a selection — the transfer
      trigger, once the scope column that normally holds it is a drawer. */
  actions?: ReactNode
  onSelect: (id: string) => void
  onToggleFavorite: (contact: Contact) => void
  onEdit: (id: string) => void
  onDelete: (contact: Contact) => void
  onDeleteMany: (ids: string[]) => void
  /** Only under a group scope — the layout withholds it elsewhere, which is what keeps this band
      free of the action when there is no group to leave. Acts without a dialog: membership is what
      a drop restores, never a loss the way deleting the contact itself is. */
  onRemoveFromGroup?: (ids: string[]) => void
  /** What the parent drags. Reported in screen order, never in click order. */
  onSelectionChange?: (ids: string[]) => void
}

/**
 * The tiles, between a pinned heading band and nothing else — there is no pager: the whole book
 * is one cached list, so there is no page to go to.
 *
 * One tile skin, on two lines. The mail list carries two because three pane arrangements exist
 * there; here the list always sits beside the card, so a wide skin would be unreachable code.
 */
export default function ContactList({
  contacts, selectedId, scope, leading, actions,
  onSelect, onToggleFavorite, onEdit, onDelete, onDeleteMany, onRemoveFromGroup, onSelectionChange,
}: Props) {
  const { t } = useTranslation('contacts')
  const [query, setQuery] = useState('')
  const [confirming, setConfirming] = useState(false)
  const [draggingIds, setDraggingIds] = useState<string[] | null>(null)
  const searchBox = useRef<HTMLInputElement>(null)
  const wantsSearch = useRef(false)
  const shown = useMemo(() => filterContacts(contacts, query), [contacts, query])
  const filtering = query.trim() !== ''

  // The query is in the reset key as well as the scope: a search narrows what is on screen, and a
  // batch acting on rows the user can no longer see is the accident this forestalls.
  const selection = useSelection<string>(`${scope}::${query}`)
  const shownIds = shown.map(contact => contact.id)
  const selectedIds = shownIds.filter(id => selection.has(id))
  const count = selectedIds.length

  // The loupe asks for a field that is not mounted yet — clearing the selection is what renders it.
  // An effect on the count is when it exists; a timer or a frame callback would be a race.
  useEffect(() => {
    if (count > 0 || !wantsSearch.current) return
    wantsSearch.current = false
    searchBox.current?.focus()
  }, [count])

  // A drag carries the checked selection when the grabbed tile belongs to it, that tile alone
  // otherwise. The pill lives off-screen just long enough for the browser to snapshot it.
  function onTileDragStart(event: DragEvent<HTMLDivElement>, id: string) {
    const ids = dragIds(selectedIds, id)
    event.dataTransfer.setData(CONTACT_DRAG_MIME, serializeContactDrag({ ids }))
    event.dataTransfer.effectAllowed = 'copy'
    const pill = buildDragPill(ids.length, t('list.dragLabel'), LIST_GLYPH)
    pill.style.position = 'absolute'
    pill.style.top = '-9999px'
    document.body.appendChild(pill)
    event.dataTransfer.setDragImage(pill, 12, 12)
    setTimeout(() => pill.remove(), 0)
    setDraggingIds(ids)
  }

  // Joined rather than compared as an array: the identity changes on every render, so the effect
  // would fire on every one of them and the parent would re-render in a loop.
  const selectionKey = selectedIds.join(',')
  useEffect(() => {
    onSelectionChange?.(selectionKey === '' ? [] : selectionKey.split(','))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectionKey])

  return (
    <>
      <SelectionBand
        leading={leading}
        allSelected={count > 0 && count === shown.length}
        indeterminate={count > 0 && count < shown.length}
        onToggleAll={() =>
          (count === shown.length ? selection.clear() : selection.selectAll(shownIds))}
        selectAllLabel={t('list.selectAll')}
        count={count}
        countLabel={t('list.selected', { count })}
        center={<>
          <span className="contacts-search">
            <SearchIcon size={14} />
            <input ref={searchBox} type="search" className="search-input"
              aria-label={t('list.searchLabel')} placeholder={t('list.searchPlaceholder')}
              value={query} onChange={event => setQuery(event.target.value)} />
          </span>
          {/* Matching over total while filtering, the bare count otherwise: "2 / 2" reads as though
              something were hidden. */}
          <span className="contacts-count" data-testid="contact-count">
            {filtering ? `${shown.length} / ${contacts.length}` : contacts.length}
          </span>
        </>}
      >
        {onRemoveFromGroup && (
          <button type="button" className="selection-btn"
            aria-label={t('list.removeFromGroup')} title={t('list.removeFromGroup')}
            disabled={count === 0}
            onClick={() => { onRemoveFromGroup(selectedIds); selection.clear() }}>
            <PersonMinusIcon size={20} />
          </button>
        )}
        <button type="button" className="selection-btn is-danger"
          aria-label={t('list.deleteSelected')} title={t('list.deleteSelected')}
          disabled={count === 0} onClick={() => setConfirming(true)}>
          <TrashIcon size={20} />
        </button>
        {/* Only while the count holds the band: at rest the field is already there, and two doors
            onto the same thing read as a fault. Searching drops the selection because the field is
            what comes back — the mail's loupe stays lit through a selection for the same reason,
            that the search must never become unreachable. */}
        {count > 0 && (
          <button type="button" className="selection-btn"
            aria-label={t('list.searchLabel')} title={t('list.searchLabel')}
            onClick={() => { wantsSearch.current = true; selection.clear() }}>
            <SearchIcon size={20} />
          </button>
        )}
        {actions}
      </SelectionBand>

      <div className="contacts-list-scroll">
        {/* `contacts` already arrives scoped, so an empty group or an empty favourites list is not
            the whole book being empty — each gets its own line rather than borrowing `list.empty`. */}
        {contacts.length === 0 && (
          <p className="contacts-empty">
            {t(scope.startsWith('group:') ? 'list.emptyGroup'
              : scope === 'favorites' ? 'list.emptyFavourites' : 'list.empty')}
          </p>
        )}
        {contacts.length > 0 && shown.length === 0 && (
          <p className="contacts-empty">{t('list.noMatch')}</p>
        )}

        <div className={`contact-tiles${count > 0 ? ' has-selection' : ''}`}>
          {shown.map((contact, index) => {
            const name = displayNameOf(contact)
            const primary = primaryAddressOf(contact)
            const extra = contact.addresses.length - 1

            return (
              <div key={contact.id} data-testid={`contact-tile-${contact.id}`}
                className={`contact-tile${contact.id === selectedId ? ' is-selected' : ''}`
                  + (draggingIds?.includes(contact.id) ? ' is-dragging' : '')}
                role="button" tabIndex={0} onClick={() => onSelect(contact.id)}
                draggable
                onDragStart={event => onTileDragStart(event, contact.id)}
                onDragEnd={() => setDraggingIds(null)}
                onKeyDown={event => {
                  if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault()
                    onSelect(contact.id)
                  }
                }}>
                {/* In the gutter the tile reserves permanently, the message row's arrangement. */}
                <input type="checkbox" className="contact-tile-check"
                  aria-label={t('list.selectOne', { name })}
                  checked={selection.has(contact.id)}
                  onClick={event => event.stopPropagation()}
                  onChange={() => selection.toggle(contact.id, index)} />

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

      {confirming && (
        <DeleteConfirmModal
          message={t('list.deleteSelectedConfirm', { count })}
          onClose={() => setConfirming(false)}
          onConfirm={() => { onDeleteMany(selectedIds); selection.clear(); setConfirming(false) }} />
      )}
    </>
  )
}
