import {
  useEffect, useMemo, useRef, useState,
  type DragEvent, type ReactNode, type RefObject,
} from 'react'
import { useTranslation } from 'react-i18next'
import SelectionBand from '../../components/SelectionBand'
import DeleteConfirmModal from '../../components/DeleteConfirmModal'
import PersonMinusIcon from '../../icons/PersonMinusIcon'
import SearchIcon from '../../icons/SearchIcon'
import TrashIcon from '../../icons/TrashIcon'
import { useForwarders } from '../../hooks/useForwarders'
import { useGridNav } from '../../hooks/useGridNav'
import { buildDragPill, LIST_GLYPH, setDragPill } from '../mail/list/dragImage'
import { useSelection } from '../mail/list/useSelection'
import ContactTile from './ContactTile'
import type { TileCallbacks } from './ContactTile'
import { filterContacts } from './contactSearch'
import { CONTACT_DRAG_MIME, dragIds, serializeContactDrag } from './dragContacts'
import type { Contact } from './contactTypes'

/** Its own component because `useGridNav` keys its effect on the ref alone, and this element
 * comes and goes with the rows (an empty book or an unmatched filter draws none). */
function ContactGrid({ label, selecting, children }: {
  label: string
  selecting: boolean
  children: ReactNode
}) {
  const grid = useRef<HTMLDivElement>(null)
  useGridNav({ ref: grid })
  return (
    <div
      className={`contact-tiles${selecting ? ' has-selection' : ''}`}
      role="grid"
      aria-label={label}
      ref={grid}
    >
      {children}
    </div>
  )
}

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
  /** The list column, where focus goes when a confirmed delete disables the band's own button. */
  regionRef?: RefObject<HTMLElement | null>
}

/** The tiles under a pinned heading band, with no pager: the whole book is one cached list. One
 * two-line skin, since the list always sits beside the card. */
export default function ContactList({
  contacts, selectedId, scope, leading, actions,
  onSelect, onToggleFavorite, onEdit, onDelete, onDeleteMany, onRemoveFromGroup,
  regionRef,
}: Props) {
  const { t } = useTranslation('contacts')
  const [query, setQuery] = useState('')
  const [confirming, setConfirming] = useState(false)
  // A Set, not the array: `includes` per tile is quadratic across the book, and a drag carrying the
  // whole selection is exactly when the book is largest.
  const [draggingIds, setDraggingIds] = useState<Set<string> | null>(null)
  const searchBox = useRef<HTMLInputElement>(null)
  const wantsSearch = useRef(false)
  const shown = useMemo(() => filterContacts(contacts, query), [contacts, query])
  const filtering = query.trim() !== ''

  // The query is in the reset key as well as the scope: a search narrows what is on screen, and a
  // batch acting on rows the user can no longer see is the accident this forestalls.
  const selection = useSelection<string>(`${scope}::${query}`)
  const selected = selection.selected
  const shownIds = useMemo(() => shown.map(contact => contact.id), [shown])
  const selectedIds = useMemo(() => shownIds.filter(id => selected.has(id)), [shownIds, selected])
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
    setDragPill(event.dataTransfer, buildDragPill(ids.length, t('list.dragLabel'), LIST_GLYPH))
    setDraggingIds(new Set(ids))
  }

  // What a tile calls, as one object built once and never rebuilt: a tile handed a callback this
  // render created would redraw whenever anything did.
  const tileOn: TileCallbacks = useForwarders({
    open: onSelect,
    check: (id: string) => selection.toggle(id, shownIds.indexOf(id)),
    toggleFavorite: onToggleFavorite,
    edit: onEdit,
    remove: onDelete,
    dragStart: onTileDragStart,
    dragEnd: () => setDraggingIds(null),
  })

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
        {/* Only while the count holds the band: at rest the field is already there. Searching drops
            the selection because the field comes back, so the search is never unreachable. */}
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

        {shown.length > 0 && (
          <ContactGrid label={t('list.gridLabel')} selecting={count > 0}>
            {shown.map(contact => (
              <ContactTile
                key={contact.id}
                contact={contact}
                checked={selected.has(contact.id)}
                open={contact.id === selectedId}
                dragging={draggingIds?.has(contact.id) ?? false}
                on={tileOn} />
            ))}
          </ContactGrid>
        )}
      </div>

      {confirming && (
        <DeleteConfirmModal
          message={t('list.deleteSelectedConfirm', { count })}
          onClose={() => setConfirming(false)}
          onConfirm={() => { onDeleteMany(selectedIds); selection.clear(); setConfirming(false) }}
          returnFocusRef={regionRef} />
      )}
    </>
  )
}
