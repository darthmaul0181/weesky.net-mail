import { useCallback, useRef } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { useAccountId } from '../../hooks/useAccountId'
import type { AddToast } from '../../hooks/useToasts'
import { apiErrorMessage } from '../../lib/apiErrorMessage'
import type { CalendarContextValue } from './calendarContext'
import type { EditScope, Occurrence } from './calendarTypes'
import { movedBody, movedOccurrence } from './eventForm'
import { MINUTES_PER_DAY } from './plainDate'
import { eventQueryOptions, type useMoveOccurrence } from './queries'
import { recurrenceSummary } from './recurrenceSummary'

interface GestureWritesInput {
  moveEventAsync: ReturnType<typeof useMoveOccurrence>['mutateAsync']
  askScope: CalendarContextValue['askScope']
  addToast: AddToast
  lang: string
  region: string
}

export function useGestureWrites({
  moveEventAsync, askScope, addToast, lang, region,
}: GestureWritesInput) {
  const { t } = useTranslation('calendar')
  const queryClient = useQueryClient()
  const accountId = useAccountId()
  /** One lane per event: the promise a gesture in flight resolves when it has settled. */
  const pending = useRef(new Map<string, Promise<void>>())

  /** The version a write has to prove it read: the cache while it is fresh, a request otherwise —
      and always a request when another write on this event has just landed. */
  const loadDetail = useCallback(async (id: string, refresh: boolean) => {
    try {
      const options = eventQueryOptions(accountId, id)
      return await queryClient.fetchQuery(refresh ? { ...options, staleTime: 0 } : options)
    } catch (error) {
      addToast(apiErrorMessage(error, t('errors.load')), 'error')
      return null
    }
  }, [queryClient, accountId, addToast, t])

  /** A block dropped or a handle released: the detail comes from the editor's cache or one fetch,
   * a series is asked its scope, and the window is patched. One event's gestures are queued, or
   * two quick drops share an `ifHash` and the second is refused 409 (docs/architecture-calendar.md). */
  const applyGesture = useCallback(async (
    one: Occurrence, deltaMinutes: number, deltaDays: number, newDuration: number | null,
  ) => {
    const delta = deltaMinutes + deltaDays * MINUTES_PER_DAY
    if (delta === 0 && newDuration === null) return

    // Claimed before the first await: two drops a frame apart would otherwise both find the lane
    // empty and set off together.
    const queued = pending.current.get(one.eventId)
    let release = () => {}
    const mine = new Promise<void>(resolve => { release = resolve })
    pending.current.set(one.eventId, mine)

    try {
      // Behind another write, the cached copy is the version that write has just replaced.
      if (queued) await queued
      const detail = await loadDetail(one.eventId, queued !== undefined)
      if (detail === null) return

      // An occurrence of a series carries its RECURRENCE-ID; a lone event's is empty. A drop never
      // changes calendar, so it has no scope to withhold.
      let scope: EditScope = 'All'
      if (one.instanceId) {
        // `detail` just landed above, from the cache or a fresh fetch — never the raw stored rule:
        // the same exact-or-generic split as everywhere else in the module.
        const rule = detail.repeatIsExact ? detail.fields.repeat : undefined
        const chosen = await askScope('save', one.summary || t('views.noTitle'),
          rule ? recurrenceSummary(rule, t, lang, region) : t('preview.repeatsGeneric'))
        if (chosen === null) return
        scope = chosen
      }

      await moveEventAsync({
        id: one.eventId,
        body: movedBody(detail, one, delta, newDuration, scope),
        moved: movedOccurrence(detail, one, delta, newDuration),
      }).catch(error => addToast(apiErrorMessage(error, t('errors.save')), 'error'))
    } finally {
      release()
      if (pending.current.get(one.eventId) === mine) pending.current.delete(one.eventId)
    }
  }, [loadDetail, addToast, t, lang, region, askScope, moveEventAsync])

  const moveOccurrence = useCallback(
    (one: Occurrence, deltaMinutes: number, deltaDays: number) => {
      void applyGesture(one, deltaMinutes, deltaDays, null)
    }, [applyGesture])
  const resizeOccurrence = useCallback((one: Occurrence, duration: number) => {
    void applyGesture(one, 0, 0, duration)
  }, [applyGesture])

  return { moveOccurrence, resizeOccurrence }
}
