import { useState, type CSSProperties, type DragEvent, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import DropdownMenu from '../../components/DropdownMenu'
import ContactsIcon from '../../icons/ContactsIcon'
import KebabIcon from '../../icons/KebabIcon'
import PeopleIcon from '../../icons/PeopleIcon'
import StarIcon from '../../icons/StarIcon'
import type { ContactGroup } from './contactGroupTypes'
import { CONTACT_DRAG_MIME, canDropIntoScope, parseContactDrag } from './dragContacts'
import type { ContactDragPayload } from './dragContacts'

export type ContactScope = 'all' | 'favorites' | `group:${string}`

/** The GUID a group scope carries, or null for the two scopes that name no group. */
export function groupIdOf(scope: ContactScope): string | null {
  return scope.startsWith('group:') ? scope.slice('group:'.length) : null
}

interface Props {
  scope: ContactScope
  total: number
  favorites: number
  groups: ContactGroup[]
  onScope: (scope: ContactScope) => void
  onCreateGroup: () => void
  onRenameGroup: (group: ContactGroup) => void
  onDeleteGroup: (group: ContactGroup) => void
  onWriteToGroup: (group: ContactGroup) => void
  /** Whether writing to the group would reach anybody — the parent resolves the members. */
  groupHasAddresses: (group: ContactGroup) => boolean
  /** A refused list is said out loud: an empty section would claim the account holds no group. */
  groupsError?: boolean
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
 * Two fixed scopes, then the groups, with CardDAV address books the next thing to land here — the
 * reason the module has a band at all rather than starting flush against the rail. The column ends
 * on these rows: import and export are one trigger up beside Add contact, not a footer under them.
 *
 * A row is also a drop target, under `canDropIntoScope`: All contacts is the complete view rather
 * than a group, so it never lights up — the refusal the mail's source folder makes.
 *
 * The « + » sits on the Groups heading rather than in `.column-actions` (decision 13): that row is
 * measured to the pixel for a French label, and a third 40px square there would re-open it.
 */
export default function ContactScopes({
  scope, total, favorites, groups, onScope, onCreateGroup, onRenameGroup, onDeleteGroup,
  onWriteToGroup, groupHasAddresses, groupsError, onDropContacts,
}: Props) {
  const { t } = useTranslation('contacts')

  return (
    <nav className="contact-scopes">
      <ScopeRow scope="all" active={scope === 'all'} icon={<ContactsIcon size={15} />}
        label={t('scopes.all')} count={total} dropLabel={t('scopes.dropHere')}
        onScope={onScope} onDropContacts={onDropContacts} />
      <ScopeRow scope="favorites" active={scope === 'favorites'} icon={<StarIcon size={15} />}
        label={t('scopes.favourites')} count={favorites} dropLabel={t('scopes.dropHere')}
        onScope={onScope} onDropContacts={onDropContacts} />

      {/* Drawn even with no group at all: the first one is created from here. */}
      <div className="contact-scopes-groups-header">
        <h2>{t('groups.title')}</h2>
        <button type="button" className="contact-scopes-add" aria-label={t('groups.add')}
          title={t('groups.add')} onClick={onCreateGroup}>+</button>
      </div>
      {groupsError && <p className="contact-scopes-error">{t('groups.loadFailed')}</p>}
      {groups.map(group => {
        const groupScope: ContactScope = `group:${group.id}`
        const writable = groupHasAddresses(group)
        return (
          <div key={group.id} className="contact-scope-row">
            <ScopeRow scope={groupScope} active={scope === groupScope}
              icon={<PeopleIcon size={15} />} label={group.name} count={group.memberIds.length}
              dropLabel={t('scopes.dropHere')} onScope={onScope} onDropContacts={onDropContacts} />
            {/* A menu rather than an in-place field: the row is already a drop target, and a text
                box taking a dragover is a conflict nothing needs. */}
            {/* `auto`: the band scrolls, so the last row's menu would open under the fold. */}
            <DropdownMenu ariaLabel={t('groups.menu', { name: group.name })} className="admin-icon-btn"
              direction="auto" trigger={<KebabIcon size={14} />}
              items={[
                { label: t('groups.rename'), onSelect: () => onRenameGroup(group) },
                {
                  label: t('groups.write'), onSelect: () => onWriteToGroup(group),
                  disabled: !writable, title: writable ? undefined : t('groups.writeEmpty'),
                },
                'separator',
                { label: t('groups.delete'), onSelect: () => onDeleteGroup(group) },
              ]} />
          </div>
        )
      })}
    </nav>
  )
}
