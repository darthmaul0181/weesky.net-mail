import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import LoadingBlock from '../../../components/LoadingBlock'
import SlidersIcon from '../../../icons/SlidersIcon'
import { flatten, indent, sortFolders } from '../../mail/folders/folderNodes'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { roleLabel } from '../../mail/roleLabel'
import { useClearFolderRole, useFolderRoles, useFolders, useSetFolderRole } from '../../mail/queries'
import type { FolderRoleEntry, FolderRoleStaleOverride } from '../../mail/api/mailTypes'

const ROLES = ['sent', 'drafts', 'trash', 'junk', 'archive']

interface Props {
  onClose: () => void
  onNotify: (message: string, type?: 'success' | 'error') => void
}

/**
 * Assigns the five system roles. "Automatic" follows the server; a pick corrects it where a
 * messy history (both "Drafts" and "Brouillons") made detection guess wrong.
 */
export default function SystemFoldersModal({ onClose, onNotify }: Props) {
  const { t } = useTranslation('settings')
  const { t: tMail } = useTranslation('mail')
  const { data: folders, isLoading: foldersLoading, isError: foldersError } = useFolders()
  const { data: roles, isLoading: rolesLoading, isError: rolesError } = useFolderRoles()
  const setRole = useSetFolderRole()
  const clearRole = useClearFolderRole()
  const [pendingRole, setPendingRole] = useState<string | null>(null)

  const loading = foldersLoading || rolesLoading
  const failed = foldersError || rolesError || !folders || !roles

  const all = failed || loading ? [] : flatten(sortFolders(folders))
  const overrideByPath = new Map(
    (roles ?? [])
      .filter(entry => entry.provenance === 'override' && entry.folderPath)
      .map(entry => [entry.folderPath as string, entry.role]))

  const nameOf = (path: string | null) =>
    path ? all.find(item => item.node.path === path)?.node.name ?? path : null

  async function onChange(role: string, value: string) {
    setPendingRole(role)
    try {
      if (value === '') {
        await clearRole.mutateAsync({ role })
        onNotify(t('folders.roleAutomaticAgain', { role: roleLabel(role, tMail) }))
      } else {
        await setRole.mutateAsync({ role, folderPath: value })
        onNotify(t('folders.rolePointsAt', { role: roleLabel(role, tMail), name: nameOf(value) }))
      }
    } catch (error) {
      onNotify(apiErrorMessage(error, t('folders.roleSaveFailed')), 'error')
    } finally {
      setPendingRole(null)
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal modal-folders" onClick={event => event.stopPropagation()}>
        <div className="modal-header">
          <span className="modal-title"><SlidersIcon />{t('folders.systemFolders')}</span>
          <button className="modal-close" aria-label={t('actions.close', { ns: 'common' })} onClick={onClose}>✕</button>
        </div>

        <p className="modal-hint">{t('folders.systemHint')}</p>

        {loading && <LoadingBlock />}
        {!loading && failed && <p>{t('folders.configLoadFailed')}</p>}

        {!loading && !failed && (
          <div className="system-folders-roles">
            {ROLES.map(role => {
              const entry = roles!.find(item => item.role === role)
              const selected = entry?.provenance === 'override' ? entry.folderPath ?? '' : ''
              const options = all.filter(({ node }) =>
                node.selectable
                && node.specialUse !== 'inbox'
                && (!overrideByPath.has(node.path) || overrideByPath.get(node.path) === role))

              return (
                <div key={role}>
                  <div className="field-h">
                    <label htmlFor={`role-${role}`}>{roleLabel(role, tMail)}</label>
                    <select
                      id={`role-${role}`}
                      value={selected}
                      disabled={pendingRole === role}
                      onChange={event => onChange(role, event.target.value)}
                      aria-describedby={entry?.staleOverride ? `role-${role}-stale` : undefined}
                    >
                      <option value="">{automaticLabel(entry, nameOf, t)}</option>
                      {options.map(({ node, depth }) => (
                        <option key={node.path} value={node.path}>{indent(depth)}{node.name}</option>
                      ))}
                    </select>
                  </div>
                  {entry?.staleOverride && (
                    // Kept and signalled, never dropped (§ 5.3). aria-describedby so it is
                    // announced on focus, not just seen next to the select.
                    <p id={`role-${role}-stale`} className="system-folders-stale">
                      {staleMessage(entry.staleOverride, t)}
                    </p>
                  )}
                </div>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}

/**
 * Says what "automatic" resolves to and how. "The server declared it" versus "we guessed from
 * the name" is the distinction this dialog exists for: only the guess is likely to be wrong.
 */
function automaticLabel(
  entry: FolderRoleEntry | undefined,
  nameOf: (path: string | null) => string | null,
  t: TFunction<'settings'>,
): string {
  if (!entry || entry.provenance === 'override') return t('folders.automatic')
  if (!entry.folderPath) return t('folders.automaticNotSet')

  const name = nameOf(entry.folderPath)
  return t(entry.provenance === 'name'
    ? 'folders.automaticDetectedName'
    : 'folders.automaticNamed', { name })
}

/** Three causes, three messages: "deleted" about a folder still in the tree sends the user hunting. */
function staleMessage(stale: FolderRoleStaleOverride, t: TFunction<'settings'>): string {
  const path = stale.folderPath

  switch (stale.reason) {
    case 'notSelectable':
      return t('folders.staleNotSelectable', { path })
    case 'folderTaken':
      return t('folders.staleFolderTaken', { path })
    default:
      return t('folders.staleGone', { path })
  }
}
