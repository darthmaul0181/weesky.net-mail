import type { Dispatch, RefObject, SetStateAction } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { useAccountId } from '../../hooks/useAccountId'
import type { AddToast } from '../../hooks/useToasts'
import { apiErrorMessage } from '../../lib/apiErrorMessage'
import type { CalendarContextValue } from './calendarContext'
import type { PendingEvent } from './CalendarDialogs'
import type { EditScope, EventDetail, Occurrence } from './calendarTypes'
import {
  allowedScopes, isRecurring, ruleOf, updateBodyOf, writeOf, type EventFormState,
} from './eventForm'
import {
  calendarKeys, isConflict, type useCreateEvent, type useDeleteEvent, type useEvent,
  type useUpdateEvent,
} from './queries'
import { recurrenceSummary } from './recurrenceSummary'
import { eventErrorOf, rememberCalendar, type Seed } from './useEditorSeed'

interface EventWritesInput {
  detail: EventDetail | null
  occurrence: Occurrence | null
  seed: Seed | null
  eventQuery: ReturnType<typeof useEvent>
  inEditor: boolean
  previewOpen: boolean
  closePreview: () => void
  mainRef: RefObject<HTMLDivElement | null>
  askScope: CalendarContextValue['askScope']
  backToGrid: () => void
  setSaveError: Dispatch<SetStateAction<string | null>>
  setConflict: Dispatch<SetStateAction<boolean>>
  setReloads: Dispatch<SetStateAction<number>>
  setPendingEvent: Dispatch<SetStateAction<PendingEvent | null>>
  createEvent: ReturnType<typeof useCreateEvent>
  updateEvent: ReturnType<typeof useUpdateEvent>
  removeEvent: ReturnType<typeof useDeleteEvent>
  addToast: AddToast
  lang: string
  region: string
}

/** The editor's save and reload, and every deletion of an event, whichever door it came through. */
export function useEventWrites({
  detail, occurrence, seed, eventQuery, inEditor, previewOpen, closePreview, mainRef, askScope,
  backToGrid, setSaveError, setConflict, setReloads, setPendingEvent, createEvent, updateEvent,
  removeEvent, addToast, lang, region,
}: EventWritesInput) {
  const { t } = useTranslation('calendar')
  const queryClient = useQueryClient()
  const accountId = useAccountId()

  const summaryOf = (form: EventFormState) => {
    const rule = ruleOf(form.repeat)
    return rule ? recurrenceSummary(rule, t, lang, region) : null
  }

  async function saveEvent(form: EventFormState, scope: EditScope | null) {
    setSaveError(null)
    // Cleared with the message it belongs to: a Reload button left beside an unrelated refusal
    // offers a way out of a conflict that is no longer there.
    setConflict(false)
    try {
      let chosen = scope
      if (detail) {
        // The occurrence's own RECURRENCE-ID, never the repeat the picker holds. A repeat just
        // added has no other occurrence, so there is nothing to ask about.
        if (chosen === null && isRecurring(occurrence)) {
          chosen = await askScope('save', form.title || t('views.noTitle'), summaryOf(form),
            allowedScopes(form, detail, occurrence))
          if (chosen === null) return
        }
        // The editor always hands `scope` in as null, so the question above is the only door to a
        // narrow scope and it is asked only on an occurrence that has one. This covers a caller
        // that named a scope itself: sent with no instance, the server takes the whole series.
        if (chosen !== null && chosen !== 'All' && !occurrence?.instanceId) {
          setSaveError(t('errors.occurrenceMissing'))
          return
        }
      }
      const result = detail
        ? await updateEvent.mutateAsync({
          id: detail.id,
          body: updateBodyOf(form, { ...detail, icsHash: seed?.hash ?? detail.icsHash },
            occurrence, chosen ?? 'All'),
        })
        : await createEvent.mutateAsync(writeOf(form))
      const sent = result?.scheduling?.sent ?? 0
      rememberCalendar(form.calendarId)
      addToast(sent > 0 ? t('editor.savedSent', { count: sent }) : t('editor.saved'), 'success')
      backToGrid()
    } catch (error) {
      // The form stays as typed. A stale write gets a band with Reload, and keeps the seeded hash
      // so a bare retry is refused again rather than overwriting the other client's write.
      if (isConflict(error)) {
        setConflict(true)
        setSaveError(t('errors.conflict'))
        return
      }
      setSaveError(apiErrorMessage(error, t('errors.save')))
    }
  }

  /** The user's own choice, never a consequence of the refusal: the form stands untouched behind
      the band until this runs. A refetch that failed has nothing to seed from, so nothing moves
      but the band, which says why and keeps the Reload. */
  async function reloadEvent() {
    const { isError: failed, error } = await eventQuery.refetch()
    if (failed) setSaveError(eventErrorOf(error, t))
    else setReloads(previous => previous + 1)
  }

  async function runDelete(id: string, scope: EditScope, instanceId?: string) {
    if (scope !== 'All' && !instanceId) {
      addToast(t('errors.occurrenceMissing'), 'error')
      return
    }
    try {
      // The instance names an occurrence, so it travels only with a scope that reaches one.
      await removeEvent.mutateAsync({
        id, scope, instanceId: scope === 'All' ? undefined : instanceId,
      })
      addToast(t('editor.deleted'), 'success')
      // The chip leaves with the event, but only when the window refetch lands: focus handed back
      // to it would be sitting on a node about to go, so the column takes it while the bubble is
      // still the surface being closed.
      if (previewOpen) mainRef.current?.focus()
      closePreview()
      if (inEditor) backToGrid()
    } catch (error) {
      addToast(apiErrorMessage(error, t('errors.delete')), 'error')
    }
  }

  /** One deletion, whichever door it came through: nothing to ask on a lone event, the scope
      question on a series. `repeatText` is `null` for an event that does not repeat, and the empty
      string for one whose rule nothing has worded — which is still a series. */
  async function askDelete(
    id: string, name: string, repeatText: string | null, instanceId?: string,
  ) {
    if (repeatText === null) return setPendingEvent({ id, name })
    const scope = await askScope('delete', name, repeatText)
    if (scope) await runDelete(id, scope, instanceId)
  }

  function deleteEdited() {
    if (!detail) return
    const rule = detail.fields.repeat
    // The raw RRULE must never reach the screen: a rule the picker cannot draw exactly
    // (`repeatIsExact` false) gets the same generic label as `EventPreview`, never the stored text.
    const repeatText = !rule ? null
      : detail.repeatIsExact ? recurrenceSummary(rule, t, lang, region) : t('preview.repeatsGeneric')
    void askDelete(detail.id, detail.fields.summary || t('views.noTitle'), repeatText,
      occurrence?.instanceId)
  }

  /** The worded rule for a dialog raised from an `Occurrence` in a handler, with no fetch mid-gesture:
   * it reads the cached `EventDetail` and falls back to the generic label as `EventPreview` does,
   * never to the raw stored rule. */
  function repeatLabelOf(eventId: string): string {
    const cached = queryClient.getQueryData<EventDetail>(calendarKeys.event(accountId, eventId))
    const rule = cached?.repeatIsExact ? cached.fields.repeat : undefined
    return rule ? recurrenceSummary(rule, t, lang, region) : t('preview.repeatsGeneric')
  }

  function deletePreviewed(one: Occurrence) {
    // An occurrence of a series carries its RECURRENCE-ID; a lone event's is empty.
    void askDelete(one.eventId, one.summary || t('views.noTitle'),
      one.instanceId ? repeatLabelOf(one.eventId) : null, one.instanceId)
  }

  return { saveEvent, reloadEvent, runDelete, deleteEdited, deletePreviewed }
}
