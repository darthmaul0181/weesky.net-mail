import type { RefObject } from 'react'
import { useTranslation } from 'react-i18next'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal'
import type { MailFolderNode } from '../api/mailTypes'
import MoveMessagesModal from '../MoveMessagesModal'
import AdvancedSearchModal from './AdvancedSearchModal'
import type { AdvancedForm } from './searchCriteria'
import type { BulkActions } from './useBulkActions'

interface Props {
  folderPath: string
  folderTitle: string
  folders: MailFolderNode[]
  regionRef?: RefObject<HTMLElement | null>
  expunging: { label: string; uids: number[] } | null
  onExpunge: () => void
  onCloseExpunge: () => void
  /** One delete mutation serves the row and the batch, so either keeps both dialogs busy. */
  deleting: boolean
  bulk: BulkActions
  advanced: { subject: string } | null
  onAdvancedSearch: (form: AdvancedForm) => void
  onCloseAdvanced: () => void
}

/** The list's dialogs: the row and bulk expunge confirms, the folder picker, the purge and search. */
export default function ListDialogs({
  folderPath, folderTitle, folders, regionRef, expunging, onExpunge, onCloseExpunge, deleting, bulk,
  advanced, onAdvancedSearch, onCloseAdvanced,
}: Props) {
  const { t } = useTranslation('mail')
  return (
    <>
      {/* Only inside the trash: everywhere else deleting is a move, and the trash is the undo. */}
      {expunging && (
        <DeleteConfirmModal
          entityLabel={expunging.label}
          onConfirm={onExpunge}
          onClose={onCloseExpunge}
          loading={deleting}
          returnFocusRef={regionRef}
        />
      )}

      {bulk.picker && (
        <MoveMessagesModal
          mode={bulk.picker.mode}
          folders={folders}
          currentFolderPath={folderPath}
          onPick={bulk.pickTarget}
          onClose={() => bulk.setPicker(null)}
        />
      )}

      {/* Bulk in-trash expunge: the same modal as a single row, named for the whole batch. */}
      {bulk.confirmingBulk && (
        <DeleteConfirmModal
          entityLabel={t('list.bulkLabel', { count: bulk.count })}
          onConfirm={bulk.expungeBulk}
          onClose={bulk.cancelBulk}
          loading={deleting}
          returnFocusRef={regionRef}
        />
      )}

      {/* Permanent purge, from trash or junk: a fuller warning worded for the folder, since this
          cannot be undone. Closing is the ✕ alone, like every delete confirm. */}
      {bulk.confirmingEmpty && (
        <DeleteConfirmModal
          entityLabel={folderTitle}
          message={
            <>
              {t('list.emptyConfirmLine1', { folder: folderTitle })}
              <br />
              {t('list.emptyConfirmLine2')}
            </>
          }
          onConfirm={bulk.confirmEmpty}
          onClose={bulk.cancelEmpty}
          loading={bulk.emptying}
          returnFocusRef={regionRef}
        />
      )}

      {advanced && (
        <AdvancedSearchModal
          folderTitle={folderTitle}
          initialSubject={advanced.subject}
          onSearch={onAdvancedSearch}
          onClose={onCloseAdvanced}
        />
      )}
    </>
  )
}
