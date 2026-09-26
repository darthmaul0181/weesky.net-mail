import type { RefObject } from 'react'
import { useTranslation } from 'react-i18next'
import DeleteConfirmModal from '../../components/DeleteConfirmModal'
import { CALENDAR_COLORS } from './calendarColors'
import CalendarDialog from './CalendarDialog'
import CalendarImportReportModal from './CalendarImportReportModal'
import type { Calendar, EditScope } from './calendarTypes'
import ImportDialog from './ImportDialog'
import ScopeModal from './ScopeModal'
import type { CalendarWrites } from './useCalendarWrites'

/** The scope question, held open until its answer: one dialog, three callers — the editor's save,
    a deletion, and a drag-drop gesture. */
export interface ScopeAsk {
  title: string
  sentence: string
  allowed: EditScope[]
  resolve: (scope: EditScope | null) => void
}

/** A deletion with nothing to ask about: the shared confirm, then `scope: 'All'`. */
export interface PendingEvent { id: string; name: string }

interface Props {
  scopeAsk: ScopeAsk | null
  onScopeAskClosed: () => void
  pendingEvent: PendingEvent | null
  onPendingEventClosed: () => void
  deletingEvent: boolean
  /** Settles, never rejects: a refusal is told by a toast of its own. */
  onDeleteEvent: (id: string) => Promise<void>
  inEditor: boolean
  discarding: boolean
  onDiscard: () => void
  onDiscardClosed: () => void
  calendars: Calendar[]
  writes: CalendarWrites
  returnFocusRef: RefObject<HTMLElement | null>
}

export default function CalendarDialogs({
  scopeAsk, onScopeAskClosed, pendingEvent, onPendingEventClosed, deletingEvent, onDeleteEvent,
  inEditor, discarding, onDiscard, onDiscardClosed, calendars, writes, returnFocusRef,
}: Props) {
  const { t } = useTranslation('calendar')
  const {
    editing, setEditing, importing, setImporting, report, setReport, pendingDelete,
    setPendingDelete, saveCalendar, runImport, confirmDelete,
  } = writes

  return (
    <>
      {scopeAsk && (
        <ScopeModal title={scopeAsk.title} sentence={scopeAsk.sentence} allowed={scopeAsk.allowed}
          onPick={scope => { onScopeAskClosed(); scopeAsk.resolve(scope) }}
          onClose={() => { onScopeAskClosed(); scopeAsk.resolve(null) }} />
      )}

      {pendingEvent && (
        <DeleteConfirmModal
          message={t('dialogs.deleteEventMessage', { name: pendingEvent.name })}
          loading={deletingEvent}
          onConfirm={() => onDeleteEvent(pendingEvent.id)}
          onClose={onPendingEventClosed} returnFocusRef={returnFocusRef} />
      )}

      {/* `discarding` cannot be true here once `!inEditor` — the layout's render-phase reset
          already dropped it this same render — but `inEditor &&` costs nothing and outlives that
          fact. */}
      {inEditor && discarding && (
        <DeleteConfirmModal title={t('editor.discardTitle')} message={t('editor.discardBody')}
          confirmLabel={t('editor.discard')}
          onConfirm={() => { onDiscardClosed(); onDiscard() }}
          onClose={onDiscardClosed} returnFocusRef={returnFocusRef} />
      )}

      {editing && (
        <CalendarDialog
          title={t(editing.mode === 'create' ? 'dialogs.newCalendar' : 'dialogs.editCalendar')}
          initialName={editing.mode === 'create' ? '' : editing.calendar.displayName}
          // CALENDAR_COLORS is a fixed, non-empty literal list (see its own declaration).
          initialColor={editing.mode === 'create' ? CALENDAR_COLORS[0]! : editing.calendar.color}
          focus={editing.mode === 'colour' ? 'colour' : 'name'}
          saving={writes.savingCalendar}
          onSubmit={values => void saveCalendar(values)} onClose={() => setEditing(null)} />
      )}

      {importing && (
        <ImportDialog calendars={calendars} targetId={importing.id}
          saving={writes.importingFile}
          onImport={choice => void runImport(choice)} onClose={() => setImporting(null)} />
      )}

      {report && <CalendarImportReportModal report={report} onClose={() => setReport(null)} />}

      {pendingDelete && (
        <DeleteConfirmModal
          message={t('dialogs.deleteCalendarMessage', { name: pendingDelete.displayName })}
          loading={writes.deletingCalendar}
          onConfirm={() => void confirmDelete()} onClose={() => setPendingDelete(null)}
          returnFocusRef={returnFocusRef} />
      )}
    </>
  )
}
