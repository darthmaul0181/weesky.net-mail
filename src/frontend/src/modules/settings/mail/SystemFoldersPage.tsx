import { useState } from 'react'
import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import { flatten } from '../../mail/folders/FolderDialogs'
import { roleLabel } from '../../mail/roleLabel'
import { useClearFolderRole, useFolderRoles, useFolders, useSetFolderRole } from '../../mail/queries'
import type { FolderRoleEntry, FolderRoleStaleOverride } from '../../mail/api/mailTypes'

const ROLES = ['sent', 'drafts', 'trash', 'junk', 'archive']

/**
 * Assigns the five system roles to folders. "Automatic" follows what the server declares —
 * the right default for a freshly provisioned mailbox — and a pick corrects it where a messy
 * history (say, both "Drafts" and "Brouillons") made the detection guess wrong.
 */
export default function SystemFoldersPage() {
  const { data: folders, isLoading: foldersLoading, isError: foldersError } = useFolders()
  const { data: roles, isLoading: rolesLoading, isError: rolesError } = useFolderRoles()
  const setRole = useSetFolderRole()
  const clearRole = useClearFolderRole()
  const { toasts, addToast, removeToast } = useToasts()
  const [pendingRole, setPendingRole] = useState<string | null>(null)

  if (foldersLoading || rolesLoading) return <p>Loading…</p>
  if (foldersError || rolesError || !folders || !roles) {
    return <p>Could not load the folder configuration.</p>
  }

  const all = flatten(folders)
  const overrideByPath = new Map(
    roles
      .filter(entry => entry.provenance === 'override' && entry.folderPath)
      .map(entry => [entry.folderPath as string, entry.role]))

  const nameOf = (path: string | null) =>
    path ? all.find(item => item.node.path === path)?.node.name ?? path : null

  async function onChange(role: string, value: string) {
    setPendingRole(role)
    try {
      if (value === '') {
        await clearRole.mutateAsync({ role })
        addToast(`${roleLabel(role)} is back to automatic detection`)
      } else {
        await setRole.mutateAsync({ role, folderPath: value })
        addToast(`${roleLabel(role)} now points at "${nameOf(value)}"`)
      }
    } catch (error) {
      addToast(error instanceof Error ? error.message : 'Could not save the folder role', 'error')
    } finally {
      setPendingRole(null)
    }
  }

  return (
    <div className="settings-page">
      <h1>System folders</h1>
      <p style={{ color: 'var(--text-muted)', fontSize: 13, margin: '6px 0 20px' }}>
        Which folders act as Sent, Drafts, Trash, Junk and Archive. Automatic follows what the
        server declares; pick a folder only where the detection gets it wrong.
      </p>

      {ROLES.map(role => {
        const entry = roles.find(item => item.role === role)
        const selected = entry?.provenance === 'override' ? entry.folderPath ?? '' : ''
        const options = all.filter(({ node }) =>
          node.selectable
          && node.specialUse !== 'inbox'
          && (!overrideByPath.has(node.path) || overrideByPath.get(node.path) === role))

        return (
          <div key={role}>
            <div className="field-h">
              <label htmlFor={`role-${role}`}>{roleLabel(role)}</label>
              <select
                id={`role-${role}`}
                value={selected}
                disabled={pendingRole === role}
                onChange={event => onChange(role, event.target.value)}
                aria-describedby={entry?.staleOverride ? `role-${role}-stale` : undefined}
              >
                <option value="">{automaticLabel(entry, nameOf)}</option>
                {options.map(({ node, depth }) => (
                  <option key={node.path} value={node.path}>{' '.repeat(depth * 3)}{node.name}</option>
                ))}
              </select>
            </div>
            {entry?.staleOverride && (
              // Kept and signalled, never silently dropped: the user's choice was invalidated
              // outside this app, and this is the place where they can act on it. Linked to the
              // select via aria-describedby so a screen-reader user hears it on focus, not just
              // sighted users relying on DOM adjacency.
              <p
                id={`role-${role}-stale`}
                style={{ color: 'var(--text-muted)', fontSize: 12.5, margin: '-6px 0 12px 126px' }}
              >
                {staleMessage(entry.staleOverride)}
              </p>
            )}
          </div>
        )
      })}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}

/**
 * The empty option says what "automatic" currently resolves to, so choosing it is informed —
 * and *how* it got there. "The server declared this folder is the archive" and "no server said
 * anything, so we guessed from the folder's name" are the two things this page exists to tell
 * apart: only the second is worth a second look, and only the second is likely to be wrong.
 */
function automaticLabel(
  entry: FolderRoleEntry | undefined,
  nameOf: (path: string | null) => string | null,
): string {
  if (!entry || entry.provenance === 'override') return 'Automatic'
  if (!entry.folderPath) return 'Automatic — not set'

  const name = nameOf(entry.folderPath)
  return entry.provenance === 'name'
    ? `Automatic — ${name} (detected from the name)`
    : `Automatic — ${name}`
}

/**
 * A stale override has three distinct causes and the notice must state the right one — telling
 * a user their folder was deleted when it is sitting in the tree, merely unable to hold
 * messages, sends them looking for a problem that isn't there.
 */
function staleMessage(stale: FolderRoleStaleOverride): string {
  const choice = `Your previous choice “${stale.folderPath}”`

  switch (stale.reason) {
    case 'notSelectable':
      return `${choice} can no longer hold messages.`
    case 'folderTaken':
      return `${choice} is already used for another role.`
    default:
      return `${choice} was renamed or deleted outside this app.`
  }
}
