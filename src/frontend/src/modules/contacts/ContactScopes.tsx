import { useState, type CSSProperties, type DragEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import ContactsIcon from '../../icons/ContactsIcon'
import StarIcon from '../../icons/StarIcon'
import { CONTACT_DRAG_MIME, canDropIntoScope, parseContactDrag } from './dragContacts'
import type { ContactDragPayload } from './dragContacts'

export type ContactScope = 'all' | 'favorites'

interface Props {
  scope: ContactScope
  total: number
  favorites: number
  onScope: (scope: ContactScope) => void
  /** Absent while nothing can be dropped: the rows are then plain navigation. */
  onDropContacts?: (scope: ContactScope, payload: ContactDragPayload) => void
}

interface RowProps {
  scope: ContactScope
  active: boolean
  icon: ReactNode
  label: string
  count: number
  dropLabel: string
  onScope: (scope: ContactScope) => void
  onDropContacts?: (scope: ContactScope, payload: ContactDragPayload) => void
}

/** One row, holding its own drop state: a single flag on the band would light every row at once. */
function ScopeRow({
  scope, active, icon, label, count, dropLabel, onScope, onDropContacts,
}: RowProps) {
  const [dropReady, setDropReady] = useState(false)
  const droppable = Boolean(onDropContacts) && canDropIntoScope(scope)

  function onDragOver(event: DragEvent<HTMLButtonElement>) {
    if (!droppable || !event.dataTransfer.types.includes(CONTACT_DRAG_MIME)) return
    // The default is "no drop"; preventing it is what opens the scope up.
    event.preventDefault()
    event.dataTransfer.dropEffect = 'copy'
    setDropReady(true)
  }

  function onDrop(event: DragEvent<HTMLButtonElement>) {
    setDropReady(false)
    if (!droppable) return
    event.preventDefault()
    const payload = parseContactDrag(event.dataTransfer.getData(CONTACT_DRAG_MIME))
    if (payload) onDropContacts!(scope, payload)
  }

  return (
    <button type="button"
      className={`contact-scope${active ? ' is-active' : ''}${dropReady ? ' drop-ready' : ''}`}
      aria-current={active ? 'true' : undefined}
      // The label is a translated string, so it travels as a custom property rather than living in
      // the stylesheet's `content` — which is where the mail folder tree kept its English one.
      style={{ '--drop-label': `"${dropLabel}"` } as CSSProperties}
      onClick={() => onScope(scope)}
      onDragOver={onDragOver}
      onDragLeave={() => setDropReady(false)}
      onDrop={onDrop}>
      {icon}
      <span className="contact-scope-label">{label}</span>
      <span className="contact-scope-count">{count}</span>
    </button>
  )
}

/**
 * The module's navigation band, on the same surface as the mail folder tree and the settings
 * context pane. It marks its active row with a fill and heavier weight and **no accent bar**: the
 * bar belongs to content lists, and keeping the two languages apart is how a reader tells a
 * navigation pane from a list of rows at a glance.
 *
 * Two scopes today, with CardDAV address books the next thing to land here — the reason the module
 * has a band at all rather than starting flush against the rail. The column ends on these rows:
 * import and export are one trigger up beside Add contact, not a footer under them.
 *
 * A row is also a drop target, under `canDropIntoScope`: All contacts is the complete view rather
 * than a group, so it never lights up — the refusal the mail's source folder makes.
 */
export default function ContactScopes({ scope, total, favorites, onScope, onDropContacts }: Props) {
  const { t } = useTranslation('contacts')

  return (
    <nav className="contact-scopes">
      <ScopeRow scope="all" active={scope === 'all'} icon={<ContactsIcon size={15} />}
        label={t('scopes.all')} count={total} dropLabel={t('scopes.dropHere')}
        onScope={onScope} onDropContacts={onDropContacts} />
      <ScopeRow scope="favorites" active={scope === 'favorites'} icon={<StarIcon size={15} />}
        label={t('scopes.favourites')} count={favorites} dropLabel={t('scopes.dropHere')}
        onScope={onScope} onDropContacts={onDropContacts} />
    </nav>
  )
}
