import { useMemo } from 'react'
import { useTranslation } from 'react-i18next'
import type { MailFolderNode, SpecialUse } from './api/mailTypes'
import { rolePathsOf } from './folders/folderNodes'

/** Where archive, junk and delete file a message from a folder of the given role, and why each
    is off when it is. Inside the trash, delete expunges instead of moving. */
export function useRoleActions(folders: MailFolderNode[] | undefined, folderRole: SpecialUse | null | undefined) {
  const { t } = useTranslation('mail')
  const roles = useMemo(() => rolePathsOf(folders ?? []), [folders])

  return useMemo(() => {
    const inTrash = folderRole === 'trash'
    return {
      roles,
      inTrash,
      archiveOff: !roles.archive || folderRole === 'archive',
      archiveReason: t(folderRole === 'archive' ? 'actions.alreadyArchived' : 'actions.noArchiveFolder'),
      junkOff: !roles.junk || folderRole === 'junk',
      junkReason: t(folderRole === 'junk' ? 'actions.alreadyJunk' : 'actions.noJunkFolder'),
      trashOff: !inTrash && !roles.trash,
      trashReason: t('actions.noTrashFolder'),
      deleteLabel: inTrash ? t('actions.deletePermanently') : t('actions.delete', { ns: 'common' }),
    }
  // A language switch hands back a new `t`, so the reasons follow it.
  }, [roles, folderRole, t])
}
