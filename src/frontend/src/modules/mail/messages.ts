import { skipToken, useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import i18next from 'i18next'
import { ApiError, api } from '../../api.js'
import { useAccountId, useComposeAccountId } from '../../hooks/useAccountId'
import { calendarKeys } from '../calendar/queries'
import type {
  ApplyReplyArgs, ApplyReplyResponse, InvitationResponse, MailFolderPage,
  MailMessageDetail, MailMessageSource, MailSearchPage, RespondInvitationArgs,
} from './api/mailTypes'
import {
  cancelLoaded, cancelListQueries, dropFolderCaches, patchTreeCounts, removeFromFolderCaches,
  restoreSnapshots, type Snapshot,
} from './cachePatches'
import type { FolderCountDeltas } from './list/listPatch'
import type { SearchCriteria } from './list/searchCriteria'
import { nextBlockIndex } from './list/messageStream'
import { mailKeys } from './mailKeys'

export function useMessages(
  folderPath: string | null, page: number, pageSize: number, enabled = true, grouped = false,
) {
  const accountId = useAccountId()

  return useQuery<MailFolderPage>({
    queryKey: mailKeys.messages(accountId, folderPath ?? '', page, pageSize, grouped),
    queryFn: folderPath === null ? skipToken : ({ signal }) =>
      api.getMailMessages(folderPath, page, pageSize, { signal, accountId, grouped }),
    enabled,
    // Holds the page only within one folder of one mailbox: held across either, it shows somebody
    // else's mail under this heading, a wrong state a reader cannot spot. The mode (key[6]) too: a flat
    // page under a grouped query would pace the pager on the message count.
    placeholderData: (previous, previousQuery) => {
      const key = previousQuery?.queryKey
      return key?.[1] === accountId && key?.[3] === (folderPath ?? '') && key?.[6] === grouped
        ? previous
        : undefined
    },
  })
}

export function useMessageStream(
  folderPath: string | null, requestSize: number, enabled: boolean, grouped = false,
) {
  const accountId = useAccountId()

  return useInfiniteQuery({
    queryKey: mailKeys.messageStream(accountId, folderPath ?? '', requestSize, grouped),
    queryFn: folderPath === null ? skipToken : ({ pageParam, signal }) =>
      api.getMailMessages(folderPath, pageParam, requestSize, { signal, accountId, grouped }),
    initialPageParam: 0,
    getNextPageParam: (lastPage, allPages) =>
      nextBlockIndex(lastPage, allPages.length, requestSize),
    enabled: enabled && requestSize > 0,
    // TanStack refetches *every* loaded block on focus. Forty blocks is forty IMAP
    // connections and forty full folder sorts, so this stays off.
    refetchOnWindowFocus: false,
  })
}

export function useMessage(folderPath: string | null, uid: number | null) {
  const accountId = useAccountId()

  return useQuery<MailMessageDetail>({
    queryKey: mailKeys.message(accountId, folderPath ?? '', uid ?? 0),
    queryFn: folderPath === null || uid === null
      ? skipToken
      : ({ signal }) => api.getMailMessage(folderPath, uid, { signal, accountId }),
    // Read/unread and flagged are patched into the list caches and read live from there
    // (useCachedSummaryFlags), never off this detail — so a stale reload here is the user's own
    // call, the same rule useMessageSource and useInlineImages follow.
    staleTime: Infinity,
  })
}

// The card redraws from the answer and the message query is refreshed. A decline filed in the trash is
// patched out as `useMoveMessages` does, or the row stays listed (opening it 404s) and the badges lie
// until the poll; the tree is invalidated too, since only the server knows what the trash holds.
export function useRespondInvitation() {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation<InvitationResponse, ApiError, RespondInvitationArgs>({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: args => api.respondInvitation(args, { accountId }),
    onSuccess: (answer, args) => {
      void queryClient.invalidateQueries({ queryKey: mailKeys.message(accountId, args.folder, args.uid) })
      // The calendar changed under the mail: its root key reaches the grid, the open event and
      // any search, which would otherwise keep the answer before this one.
      void queryClient.invalidateQueries({ queryKey: calendarKeys.all(accountId) })
      if (!answer.trashed) return
      const source = removeFromFolderCaches(queryClient, accountId, args.folder, [args.uid])
      patchTreeCounts(queryClient, accountId,
        [[args.folder, { total: -source.removed, unread: -source.removedUnread }]])
      void queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) })
    },
  })
}

// Asked for once by the card. The cached message takes the returned block instead of a refetch, so a
// reopen draws the answer as recorded.
export function useApplyInvitationReply() {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation<ApplyReplyResponse, ApiError, ApplyReplyArgs>({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: args => api.applyInvitationReply(args, { accountId }),
    onSuccess: (result, args) => {
      queryClient.setQueryData<MailMessageDetail>(mailKeys.message(accountId, args.folder, args.uid),
        detail => detail && { ...detail, invitation: result.invitation })
      void queryClient.invalidateQueries({ queryKey: calendarKeys.all(accountId) })
    },
  })
}

/** The message as it arrived. Its own key rather than a slice of `message`: what it caches is
    the RFC822 bytes, and a reader that has the detail has not paid for those. */
export function useMessageSource(folderPath: string | null, uid: number | null) {
  const accountId = useAccountId()

  return useQuery<MailMessageSource>({
    queryKey: mailKeys.messageSource(accountId, folderPath ?? '', uid ?? 0),
    queryFn: folderPath === null || uid === null
      ? skipToken
      : ({ signal }) => api.getMessageSource(folderPath, uid, { signal, accountId }),
    // The bytes of a given (folder, uid) never change; a stale reload is the user's own call.
    staleTime: Infinity,
  })
}

// A snapshot: no focus replay (an all-folders sweep is N IMAP SEARCHes), no poll, no writes key.
export function useSearchMessages(criteria: SearchCriteria | null, page: number, pageSize: number) {
  const accountId = useAccountId()

  return useQuery<MailSearchPage>({
    queryKey: criteria
      ? mailKeys.search(accountId, criteria, page, pageSize)
      : [...mailKeys.searchIn(accountId), 'idle'],
    queryFn: criteria === null
      ? skipToken
      : ({ signal }) => api.searchMessages(criteria, page, pageSize, { signal, accountId }),
    enabled: pageSize > 0,
    refetchOnWindowFocus: false,
    // Only the account is checked: another account's hits under this heading are the wrong state
    // useMessages guards against, while keeping a page as another loads is the placeholder's point.
    placeholderData: (previous, previousQuery) => {
      const key = previousQuery?.queryKey
      return key?.[1] === accountId ? previous : undefined
    },
  })
}

export interface MoveMessagesArgs {
  folderPath: string
  uids: number[]
  targetFolderPath: string
  copy: boolean
}

// The source loses its rows, the target's caches are dropped rather than invalidated, both counters
// move. Snapshot rollback; the lists are never invalidated, the 60s poll being their truth.
export function useMoveMessages(onError?: (message: string) => void) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: ({ folderPath, uids, targetFolderPath, copy }: MoveMessagesArgs) =>
      copy
        ? api.copyMessages(folderPath, uids, targetFolderPath, { accountId })
        : api.moveMessages(folderPath, uids, targetFolderPath, { accountId }),

    onMutate: async ({ folderPath, uids, targetFolderPath, copy }: MoveMessagesArgs) => {
      await cancelListQueries(queryClient, accountId, folderPath)
      // removeFromFolderCaches writes the search cache too: cancel an in-flight *refetch* of a
      // loaded search, so a late resolve can't repopulate the removed row on a success path that
      // never rolls back. A first load is left to land (cancelLoaded); onSettled reconciles it.
      await cancelLoaded(queryClient, mailKeys.searchIn(accountId))

      const snapshots: Snapshot[] = []
      const patches: [string, FolderCountDeltas][] = []
      // A copy removes nothing, so how many of the batch were unread is unknowable here without
      // scanning for it alone; the target badge waits for the poll instead.
      let added = { total: uids.length, unread: 0 }

      if (!copy) {
        const source = removeFromFolderCaches(queryClient, accountId, folderPath, uids)
        snapshots.push(...source.snapshots)
        patches.push([folderPath, { total: -source.removed, unread: -source.removedUnread }])
        // Target mirrors what actually left the source, not uids.length, so a cold source
        // cache can't inflate it past the source's own drop. Consequence: an uncached source
        // moves neither folder's counters until the next poll.
        added = { total: source.removed, unread: source.removedUnread }
      }

      // Mirror the source: cancel an in-flight refetch of a loaded target cache before dropping
      // it, so a late resolve can't repopulate what we just removed and race the rollback. A
      // first load survives cancelLoaded; removeQueries below destroys it outright anyway.
      await cancelListQueries(queryClient, accountId, targetFolderPath)
      snapshots.push(...dropFolderCaches(queryClient, accountId, targetFolderPath))
      patches.push([targetFolderPath, added])
      snapshots.push(...patchTreeCounts(queryClient, accountId, patches))

      return { snapshots, copy }
    },

    onError: (_error, _args, context) => {
      restoreSnapshots(queryClient, context)
      onError?.(i18next.t(context?.copy ? 'mail:mutations.copyFailed' : 'mail:mutations.moveFailed'))
    },

    // A removal shrank the active search page and left sibling pages a stale total, and no poll
    // touches search — so reconcile the mounted search against the server, which re-windows it.
    // A copy leaves the source intact: nothing to reconcile.
    onSettled: (_data, _error, { copy }) => {
      if (!copy) void queryClient.invalidateQueries({ queryKey: mailKeys.searchIn(accountId) })
    },
  })
}

export interface DeleteMessagesArgs {
  folderPath: string
  uids: number[]
}

// A move with no receiving folder. `pinnedAccountId` is the composer's: a draft belongs to the mailbox
// it was written in, so a switch under an open composer must not send its staged ids elsewhere.
export function useDeleteMessages(onError?: (message: string) => void, pinnedAccountId?: string) {
  const accountId = useComposeAccountId(pinnedAccountId)
  const queryClient = useQueryClient()

  return useMutation({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: ({ folderPath, uids }: DeleteMessagesArgs) =>
      api.deleteMessages(folderPath, uids, { accountId }),

    onMutate: async ({ folderPath, uids }: DeleteMessagesArgs) => {
      await cancelListQueries(queryClient, accountId, folderPath)
      // removeFromFolderCaches writes the search cache too: cancel an in-flight *refetch* of a
      // loaded search, so a late resolve can't repopulate the removed row on a success path that
      // never rolls back. A first load is left to land (cancelLoaded); onSettled reconciles it.
      await cancelLoaded(queryClient, mailKeys.searchIn(accountId))

      const source = removeFromFolderCaches(queryClient, accountId, folderPath, uids)
      const tree = patchTreeCounts(queryClient, accountId,
        [[folderPath, { total: -source.removed, unread: -source.removedUnread }]])

      return { snapshots: [...source.snapshots, ...tree] }
    },

    onError: (_error, _args, context) => {
      restoreSnapshots(queryClient, context)
      onError?.(i18next.t('mail:mutations.deleteFailed'))
    },

    // The removal re-windows the active search page; reconcile it against the server (see move).
    onSettled: () =>
      queryClient.invalidateQueries({ queryKey: mailKeys.searchIn(accountId) }),
  })
}
