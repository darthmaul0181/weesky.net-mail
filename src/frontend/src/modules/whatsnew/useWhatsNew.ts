import { useCallback, useEffect, useMemo } from 'react'
import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { api } from '../../api.js'
import { onSessionEnd } from '../../contexts/sessionEvents'
import { useAppSettings } from '../../hooks/useAppSettings'
import { WEB_VERSION } from '../../lib/appVersion'
import { WHATS_NEW } from './catalog'
import { noveltiesOf, stamp } from './model'
import type { WhatsNewMark, WhatsNewState } from './types'

export const whatsNewKey = ['whatsNew'] as const
/** The most ids one `PUT items` takes. */
const BATCH = 50

function batches(ids: string[]) {
  return Array.from({ length: Math.ceil(ids.length / BATCH) }, (_, i) => ids.slice(i * BATCH, (i + 1) * BATCH))
}

/** Only the last of overlapping writes refetches: an earlier answer would undo a later optimistic stamp. */
function settle(client: QueryClient) {
  return client.isMutating({ mutationKey: whatsNewKey }) === 1
    ? client.invalidateQueries({ queryKey: whatsNewKey })
    : undefined
}

// Once per session and mark: a refused write would otherwise come back unstamped and be sent again, endlessly.
let sentBy = new WeakMap<QueryClient, Set<string>>()
onSessionEnd(() => { sentBy = new WeakMap() })

function unsent(client: QueryClient, ids: string[], mark: WhatsNewMark) {
  const sent = sentBy.get(client) ?? new Set<string>()
  sentBy.set(client, sent)
  const fresh = ids.filter(id => !sent.has(`${mark}:${id}`))
  fresh.forEach(id => sent.add(`${mark}:${id}`))
  return fresh
}

function useNovelties() {
  const { data: state, dataUpdatedAt } = useQuery({
    queryKey: whatsNewKey,
    queryFn: ({ signal }) => api.getWhatsNew({ signal }),
    staleTime: 5 * 60 * 1000,
  })
  const { data: appSettings } = useAppSettings()
  // The answer's own time rather than Date.now(): render stays pure, and expiry moves on with each refetch.
  const novelties = useMemo(
    () => state && appSettings ? noveltiesOf(WHATS_NEW, state, { appSettings }, dataUpdatedAt) : [],
    [state, dataUpdatedAt, appSettings],
  )
  return { state, novelties }
}

export function useMarkWhatsNew() {
  const client = useQueryClient()
  const { mutate } = useMutation({
    mutationKey: whatsNewKey,
    mutationFn: ({ ids, mark }: { ids: string[]; mark: WhatsNewMark }) =>
      Promise.all(batches(ids).map(batch => api.markWhatsNew(batch, mark))).then(() => null),
    // Optimistic, so a dot goes out the moment its novelty is reached rather than a round trip later.
    onMutate: async ({ ids, mark }) => {
      await client.cancelQueries({ queryKey: whatsNewKey })
      client.setQueryData<WhatsNewState>(whatsNewKey, old => old && stamp(old, ids, mark, new Date().toISOString()))
    },
    onSettled: () => settle(client),
  })
  return useCallback((ids: string[], mark: WhatsNewMark) => {
    const fresh = unsent(client, ids, mark)
    if (fresh.length > 0) mutate({ ids: fresh, mark })
  }, [client, mutate])
}

export function useWhatsNew() {
  const { novelties } = useNovelties()
  const mark = useMarkWhatsNew()
  return useMemo(() => {
    const active = novelties.filter(n => n.active)
    const unlisted = active.filter(n => n.unlisted)
    return {
      novelties,
      fresh: novelties.filter(n => n.fresh),
      active,
      unlistedCount: unlisted.length,
      lights: (anchor: string) => active.some(n => n.entry.path.includes(anchor)),
      targetOf: (anchor: string, activeOnly = true) => (activeOnly ? active : novelties).find(n => n.entry.target === anchor),
      /** `ids`: only these, so a list stamps what it shows and nothing past it. */
      markListed: (ids?: string[]) => {
        mark(unlisted.map(n => n.entry.id).filter(id => !ids || ids.includes(id)), 'listed')
      },
    }
  }, [novelties, mark])
}

/** Mounted once, in AppShell: writes a new account's baseline and starts each dot's 30 days. */
export function useWhatsNewSync() {
  const client = useQueryClient()
  const { state, novelties } = useNovelties()
  const mark = useMarkWhatsNew()
  const { mutate: setBaseline } = useMutation({
    mutationKey: whatsNewKey,
    mutationFn: (version: string) => api.setWhatsNewBaseline(version),
    onSettled: () => settle(client),
  })

  const baselineMissing = state !== undefined && state.baseline === undefined
  useEffect(() => { if (baselineMissing) setBaseline(WEB_VERSION) }, [baselineMissing, setBaseline])

  const unshown = novelties.filter(n => n.active && !n.state?.firstShownAt).map(n => n.entry.id).join(',')
  useEffect(() => { mark(unshown.split(',').filter(Boolean), 'shown') }, [unshown, mark])
}
