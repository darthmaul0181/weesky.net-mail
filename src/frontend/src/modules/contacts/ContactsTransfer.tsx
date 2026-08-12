import { useRef, useState, type ChangeEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ApiError, api } from '../../api.js'
import DropdownMenu from '../../components/DropdownMenu'
import DownloadIcon from '../../icons/DownloadIcon'
import KebabIcon from '../../icons/KebabIcon'
import UploadIcon from '../../icons/UploadIcon'
import { apiErrorMessage } from '../../lib/apiErrorMessage'
import { downloadBlob } from '../../lib/downloadBlob'
import type { Contact, ContactImportReport } from './contactTypes'
import ImportReportModal from './ImportReportModal'
import { useImportContacts } from './queries'

interface Props {
  contacts: Contact[] | undefined
  onError: (message: string) => void
  /**
   * The trigger's skin, which is the caller's because the placement is: a 40px `.btn` beside Add
   * contact on a desktop, a `.selection-btn` in the list band once the scope column is a drawer.
   */
  triggerClassName: string
}

/**
 * Import and export, behind one trigger. They are the rarest actions in the module and used to be
 * two filled buttons across the foot of the scope column — the heaviest thing in it after the
 * primary action, and a shape no other column here wears. The mail folder column's own top row is
 * the precedent: a primary action and one square beside it.
 */
export default function ContactsTransfer({ contacts, onError, triggerClassName }: Props) {
  const { t } = useTranslation('contacts')
  const input = useRef<HTMLInputElement>(null)
  const [report, setReport] = useState<ContactImportReport | null>(null)
  const [exporting, setExporting] = useState(false)
  const importContacts = useImportContacts()

  async function pick(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    // Cleared before anything is awaited: an input keeping its value fires no change event when the
    // same file is chosen a second time.
    event.target.value = ''
    if (!file) return

    try {
      setReport(await importContacts.mutateAsync(file))
    } catch (error) {
      // A framework-generated 413 carries no envelope, so its message is a bare "Payload Too Large".
      onError(error instanceof ApiError && error.status === 413
        ? t('transfer.tooLarge')
        : apiErrorMessage(error, t('transfer.importFailed')))
    }
  }

  async function download() {
    setExporting(true)
    try {
      const { blob, fileName } = await api.exportContacts()
      downloadBlob(blob, fileName)
    } catch (error) {
      onError(apiErrorMessage(error, t('transfer.exportFailed')))
    } finally {
      setExporting(false)
    }
  }

  const empty = !contacts?.length

  return (
    <>
      <input ref={input} type="file" accept=".csv,text/csv" hidden onChange={pick}
        data-testid="contacts-import-input" />

      <DropdownMenu
        ariaLabel={t('transfer.actions')}
        className={triggerClassName}
        trigger={importContacts.isPending ? <span className="spinner" /> : <KebabIcon size={16} />}
        items={[
          {
            label: t('transfer.import'), icon: <UploadIcon size={15} />,
            title: t('transfer.importHint'), disabled: importContacts.isPending,
            onSelect: () => input.current?.click(),
          },
          {
            label: t('transfer.export'), icon: <DownloadIcon size={15} />,
            // The reason a shut door is shut, where the two buttons carried it in a tooltip.
            title: t(empty ? 'transfer.nothingToExport' : 'transfer.exportHint'),
            disabled: empty || exporting, onSelect: download,
          },
        ]}
      />

      {report && <ImportReportModal report={report} onClose={() => setReport(null)} />}
    </>
  )
}
