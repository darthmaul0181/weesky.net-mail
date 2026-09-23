import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api } from '../../api.js'
import type { AddToast } from '../../hooks/useToasts'
import { apiErrorMessage } from '../../lib/apiErrorMessage'
import { downloadBlob } from '../../lib/downloadBlob'
import type { CalendarValues } from './CalendarDialog'
import type { Calendar, CalendarImportReport } from './calendarTypes'
import type { ImportChoice } from './ImportDialog'
import {
  useCreateCalendar, useDeleteCalendar, useImportCalendar, useImportCalendarAsNew,
  useSetCalendarVisible, useUpdateCalendar,
} from './queries'

type Editing =
  | { mode: 'create' }
  | { mode: 'rename' | 'colour'; calendar: Calendar }

/** The calendars' own writes — create, rename, recolour, show, delete, import, export — and the
    dialog each of them opens. */
export function useCalendarWrites(tz: string, addToast: AddToast) {
  const { t } = useTranslation('calendar')
  const setVisible = useSetCalendarVisible()
  const createCalendar = useCreateCalendar()
  const updateCalendar = useUpdateCalendar()
  const deleteCalendar = useDeleteCalendar()
  const importInto = useImportCalendar()
  const importAsNew = useImportCalendarAsNew()

  const [editing, setEditing] = useState<Editing | null>(null)
  const [importing, setImporting] = useState<Calendar | null>(null)
  const [report, setReport] = useState<CalendarImportReport | null>(null)
  const [pendingDelete, setPendingDelete] = useState<Calendar | null>(null)

  async function saveCalendar({ displayName, color }: CalendarValues) {
    try {
      if (editing?.mode === 'create') await createCalendar.mutateAsync({
        calendar: { displayName, color }, tz,
      })
      else if (editing) await updateCalendar.mutateAsync({
        id: editing.calendar.id, calendar: { displayName, color },
      })
      setEditing(null)
    } catch (error) {
      // The dialog stays open carrying what was typed: a refusal that closed it would make the
      // user retype the name to find out whether it was the name that was refused.
      addToast(apiErrorMessage(error, t('errors.calendarSave')), 'error')
    }
  }

  async function confirmDelete() {
    if (!pendingDelete) return
    try {
      await deleteCalendar.mutateAsync(pendingDelete.id)
    } catch (error) {
      addToast(apiErrorMessage(error, t('errors.calendarDelete')), 'error')
    } finally {
      setPendingDelete(null)
    }
  }

  function toggleVisible(calendar: Calendar, visible: boolean) {
    setVisible.mutate({ id: calendar.id, visible }, {
      onError: error => addToast(apiErrorMessage(error, t('errors.calendarSave')), 'error'),
    })
  }

  async function exportOne(calendar: Calendar) {
    try {
      const { blob, fileName } = await api.exportCalendar(calendar.id)
      downloadBlob(blob, fileName)
    } catch (error) {
      addToast(apiErrorMessage(error, t('errors.export')), 'error')
    }
  }

  async function runImport(choice: ImportChoice) {
    try {
      setReport(choice.mode === 'existing'
        ? await importInto.mutateAsync({ id: choice.id, file: choice.file })
        : (await importAsNew.mutateAsync({
            file: choice.file, displayName: choice.displayName, color: choice.color, tz,
          })).report)
      setImporting(null)
    } catch (error) {
      addToast(apiErrorMessage(error, t('errors.import')), 'error')
    }
  }

  return {
    editing, setEditing, importing, setImporting, report, setReport, pendingDelete,
    setPendingDelete, saveCalendar, confirmDelete, toggleVisible, exportOne, runImport,
    savingCalendar: createCalendar.isPending || updateCalendar.isPending,
    importingFile: importInto.isPending || importAsNew.isPending,
    deletingCalendar: deleteCalendar.isPending,
  }
}

export type CalendarWrites = ReturnType<typeof useCalendarWrites>
