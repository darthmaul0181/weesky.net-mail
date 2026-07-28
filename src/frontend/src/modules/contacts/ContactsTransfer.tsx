import { useRef, useState, type ChangeEvent } from 'react'
import { ApiError, api } from '../../api.js'
import Tooltip from '../../components/Tooltip'
import { downloadBlob } from '../../lib/downloadBlob'
import type { Contact, ContactImportReport } from './contactTypes'
import ImportReportModal from './ImportReportModal'
import { useImportContacts } from './queries'

interface Props {
  contacts: Contact[] | undefined
  onError: (message: string) => void
}

/**
 * The band's footer. The two file actions sit here rather than among the scopes because the band
 * is navigation and these are not — the same reason the mail column keeps its account block at the
 * foot.
 */
export default function ContactsTransfer({ contacts, onError }: Props) {
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
        ? 'That file is too large — the limit is 5 MB.'
        : (error as Error).message || 'Could not import the file')
    }
  }

  async function download() {
    setExporting(true)
    try {
      const { blob, fileName } = await api.exportContacts()
      downloadBlob(blob, fileName)
    } catch (error) {
      onError((error as Error).message || 'Could not export the contacts')
    } finally {
      setExporting(false)
    }
  }

  const empty = !contacts?.length

  return (
    <div className="contacts-transfer">
      <input ref={input} type="file" accept=".csv,text/csv" hidden onChange={pick}
        data-testid="contacts-import-input" />

      <Tooltip content="Merge a CSV file into this book">
        <button type="button" className="btn" disabled={importContacts.isPending}
          onClick={() => input.current?.click()}>
          {importContacts.isPending ? <span className="spinner" /> : 'Import…'}
        </button>
      </Tooltip>

      <Tooltip content={empty ? 'Nothing to export' : 'Download this book as CSV'}>
        <button type="button" className="btn" disabled={empty || exporting} onClick={download}>
          {exporting ? <span className="spinner" /> : 'Export'}
        </button>
      </Tooltip>

      {report && <ImportReportModal report={report} onClose={() => setReport(null)} />}
    </div>
  )
}
