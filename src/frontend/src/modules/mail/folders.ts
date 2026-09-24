import { useIsFetching, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import i18next from 'i18next'
import { useCallback } from 'react'
import { api } from '../../api.js'
import { useAccountId } from '../../hooks/useAccountId'
import { notifiesOf, usePreferences } from '../../hooks/usePreferences'
import type { FolderRoleEntry, MailFolderNode } from './api/mailTypes'
import {
  blankFolderCaches, cancelListQueries, dropFolderCaches, patchTreeCounts, restoreSnapshots,
  type Snapshot,
} from './cachePatches'
import { folderByPath } from './folders/folderNodes'
import type { FolderCountDeltas } from './list/listPatch'
import { mailKeys } from './mailKeys'

// Every call below carries `{ accountId }`, read at render rather than at fire time: a write
// started under one mailbox lands there even if the user switches while it is in flight.

/** One cheap LIST+STATUS across all folders. Internal, like BLOCK_SIZE: not a setting. */
export const POLL_INTERVAL = 60_000

/** `enabled` is what keeps a user who asked for no notification off the poll: the shell passes
    `sound || desktop`, /mail passes nothing, and one enabled observer runs the query. */
export function useFolders(enabled = true) {
  const accountId = useAccountId()
  const { data: preferences } = usePreferences()
  // Background polling is the cost of a notification, so only those who asked for one pay it:
  // an untouched tab keeps costing nothing.
  const notifies = preferences ? notifiesOf(preferences) : false

  return useQuery<MailFolderNode[]>({
    queryKey: mailKeys.folders(accountId),
    queryFn: ({ signal }) => api.getMailFolders({ signal, accountId }),
    enabled,
    refetchInterval: POLL_INTERVAL,
    refetchIntervalInBackground: notifies,
  })
}

// Refetches the folders and lets useListRefresh cascade onto the list by its own rules, so the
// "never invalidate the stream" rule holds by construction. `fetching` covers the poll tick too.
export function useMailRefresh() {
  const accountId = useAccountId()
  const queryClient = useQueryClient()
  const fetching = useIsFetching({ queryKey: mailKeys.folders(accountId) }) > 0
  const refresh = useCallback(() => {
    void queryClient.refetchQueries({ queryKey: mailKeys.folders(accountId), type: 'active' })
  }, [queryClient, accountId])
  return { refresh, fetching }
}

export function useFolderRoles() {
  const accountId = useAccountId()

  return useQuery<FolderRoleEntry[]>({
    queryKey: mailKeys.folderRoles(accountId),
    queryFn: ({ signal }) => api.getFolderRoles({ signal, accountId }),
  })
}

// Roles AND the tree are invalidated: the tree's labels are the role chain's output.
function useRoleMutation<TArgs>(
  mutationFn: (args: TArgs, options: { accountId: string }) => Promise<unknown>,
) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (args: TArgs) => mutationFn(args, { accountId }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: mailKeys.folderRoles(accountId) })
      void queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) })
    },
  })
}

export const useSetFolderRole = () =>
  useRoleMutation<{ role: string; folderPath: string }>(
    ({ role, folderPath }, options) => api.setFolderRole(role, folderPath, options))

export const useClearFolderRole = () =>
  useRoleMutation<{ role: string }>(({ role }, options) => api.clearFolderRole(role, options))

// Every folder mutation changes the hierarchy or the counts the tree displays.
function useFolderMutation<TArgs>(
  mutationFn: (args: TArgs, options: { accountId: string }) => Promise<unknown>,
) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (args: TArgs) => mutationFn(args, { accountId }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) }),
  })
}

export const useCreateFolder = () =>
  useFolderMutation<{ parentPath: string; name: string }>(
    ({ parentPath, name }, options) => api.createMailFolder(parentPath, name, options))

export const useRenameFolder = () =>
  useFolderMutation<{ path: string; newParentPath: string; newName: string }>(
    ({ path, newParentPath, newName }, options) =>
      api.renameMailFolder(path, newParentPath, newName, options))

export const useDeleteFolder = () =>
  useFolderMutation<{ path: string }>(({ path }, options) => api.deleteMailFolder(path, options))

export const useSetFolderSubscription = () =>
  useFolderMutation<{ path: string; subscribed: boolean }>(
    ({ path, subscribed }, options) =>
      api.setMailFolderSubscription(path, subscribed, options))

export interface EmptyFolderArgs {
  folderPath: string
  /** Blank/absent = purge; set = move every message into this folder. */
  targetFolderPath?: string | null
}

// The source's caches are emptied in place and its counts zeroed; a move adds its total/unread (from
// the tree node) to the target. Snapshot rollback, never an invalidate: the 60s poll reconciles.
export function useEmptyFolder(onError?: (message: string) => void) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: ({ folderPath, targetFolderPath }: EmptyFolderArgs) =>
      api.emptyFolder(folderPath, targetFolderPath ?? null, { accountId }),

    onMutate: async ({ folderPath, targetFolderPath }: EmptyFolderArgs) => {
      await cancelListQueries(queryClient, accountId, folderPath)

      // Empty in place, not removeQueries: the source is the folder on screen, and removing its
      // query refetches the rows the server has not expunged yet, racing them back until the poll.
      const snapshots: Snapshot[] = blankFolderCaches(queryClient, accountId, folderPath)

      // The source folder's own counts drive both the zeroing and, on a move, the target's gain.
      const tree = queryClient.getQueryData<MailFolderNode[]>(mailKeys.folders(accountId))
      const node = folderByPath(tree, folderPath)
      const source = { total: node?.total ?? 0, unread: node?.unread ?? 0 }

      const patches: [string, FolderCountDeltas][] = [
        [folderPath, { total: -source.total, unread: -source.unread }],
      ]

      const move = !!targetFolderPath
      if (move) {
        await cancelListQueries(queryClient, accountId, targetFolderPath)
        snapshots.push(...dropFolderCaches(queryClient, accountId, targetFolderPath))
        patches.push([targetFolderPath, { total: source.total, unread: source.unread }])
      }

      snapshots.push(...patchTreeCounts(queryClient, accountId, patches))
      return { snapshots }
    },

    onError: (_error, _args, context) => {
      restoreSnapshots(queryClient, context)
      onError?.(i18next.t('mail:mutations.emptyFailed'))
    },
  })
}
