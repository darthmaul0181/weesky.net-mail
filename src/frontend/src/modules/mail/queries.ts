import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api.js'
import { useAuth } from '../../contexts/AuthContext'
import { notifyDesktopOf, notifySoundOf, usePreferences } from '../../hooks/usePreferences'
import type { MailFolderNode, MailFolderPage, MailMessageDetail, FolderRoleEntry } from './api/mailTypes'
import { nextBlockIndex } from './list/messageStream'


/**
 * Every key is scoped by the active account, so linking a second account later isolates its
 * cache instead of mixing two mailboxes together.
 */
export const mailKeys = {
  all: (accountId: string) => ['mail', accountId] as const,
  folders: (accountId: string) => ['mail', accountId, 'folders'] as const,
  // pageSize is part of the key: a cached page was computed under one size and means something
  // else under another.
  messages: (accountId: string, folder: string, page: number, pageSize: number) =>
    ['mail', accountId, 'messages', folder, page, pageSize] as const,
  message: (accountId: string, folder: string, uid: number) =>
    ['mail', accountId, 'message', folder, uid] as const,
  // Its own key: what it caches is not a page but a sequence of pages, and mixing the two
  // shapes under one key is a type error that only shows at runtime.
  messageStream: (accountId: string, folder: string, requestSize: number) =>
    ['mail', accountId, 'messageStream', folder, requestSize] as const,
  folderRoles: (accountId: string) => ['mail', accountId, 'folderRoles'] as const,
}

export function useAccountId(): string {
  return useAuth().activeAccount?.id ?? 'primary'
}

/** One cheap LIST+STATUS across all folders. Internal, like BLOCK_SIZE: not a setting. */
export const POLL_INTERVAL = 60_000

export function useFolders() {
  const accountId = useAccountId()
  const { data: preferences } = usePreferences()
  // Background polling is the cost of a notification, so only those who asked for one pay it:
  // an untouched tab keeps costing nothing.
  const notifies = preferences
    ? notifySoundOf(preferences) || notifyDesktopOf(preferences)
    : false

  return useQuery<MailFolderNode[]>({
    queryKey: mailKeys.folders(accountId),
    queryFn: ({ signal }) => api.getMailFolders({ signal }),
    refetchInterval: POLL_INTERVAL,
    refetchIntervalInBackground: notifies,
  })
}

export function useMessages(
  folderPath: string | null, page: number, pageSize: number, enabled = true,
) {
  const accountId = useAccountId()

  return useQuery<MailFolderPage>({
    queryKey: mailKeys.messages(accountId, folderPath ?? '', page, pageSize),
    queryFn: ({ signal }) => api.getMailMessages(folderPath, page, pageSize, { signal }),
    enabled: enabled && folderPath !== null,
    // Keeps the current page on screen while the next one loads, instead of flashing empty.
    placeholderData: (previous) => previous,
  })
}

export function useMessageStream(folderPath: string | null, requestSize: number, enabled: boolean) {
  const accountId = useAccountId()

  return useInfiniteQuery({
    queryKey: mailKeys.messageStream(accountId, folderPath ?? '', requestSize),
    queryFn: ({ pageParam, signal }) =>
      api.getMailMessages(folderPath, pageParam, requestSize, { signal }) as Promise<MailFolderPage>,
    initialPageParam: 0,
    getNextPageParam: (lastPage, allPages) =>
      nextBlockIndex(lastPage, allPages.length, requestSize),
    enabled: enabled && folderPath !== null && requestSize > 0,
    // TanStack refetches *every* loaded block on focus. Forty blocks is forty IMAP
    // connections and forty full folder sorts, so this stays off.
    refetchOnWindowFocus: false,
  })
}

export function useMessage(folderPath: string | null, uid: number | null) {
  const accountId = useAccountId()

  return useQuery<MailMessageDetail>({
    queryKey: mailKeys.message(accountId, folderPath ?? '', uid ?? 0),
    queryFn: ({ signal }) => api.getMailMessage(folderPath, uid, { signal }),
    enabled: folderPath !== null && uid !== null,
  })
}

export function useFolderRoles() {
  const accountId = useAccountId()

  return useQuery<FolderRoleEntry[]>({
    queryKey: mailKeys.folderRoles(accountId),
    queryFn: ({ signal }) => api.getFolderRoles({ signal }),
  })
}

/**
 * Role mutations invalidate the roles AND the folder tree: the tree's labels are the chain's
 * output, so changing a role changes what the tree displays.
 */
function useRoleMutation<TArgs>(mutationFn: (args: TArgs) => Promise<unknown>) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: mailKeys.folderRoles(accountId) })
      queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) })
    },
  })
}

export const useSetFolderRole = () =>
  useRoleMutation<{ role: string; folderPath: string }>(
    ({ role, folderPath }) => api.setFolderRole(role, folderPath))

export const useClearFolderRole = () =>
  useRoleMutation<{ role: string }>(({ role }) => api.clearFolderRole(role))

/**
 * Folder mutations all invalidate the tree: creating, renaming, deleting and subscribing each
 * change the hierarchy or the counts the tree displays.
 */
function useFolderMutation<TArgs>(mutationFn: (args: TArgs) => Promise<unknown>) {
  const accountId = useAccountId()
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) }),
  })
}

export const useCreateFolder = () =>
  useFolderMutation<{ parentPath: string; name: string }>(
    ({ parentPath, name }) => api.createMailFolder(parentPath, name))

export const useRenameFolder = () =>
  useFolderMutation<{ path: string; newParentPath: string; newName: string }>(
    ({ path, newParentPath, newName }) => api.renameMailFolder(path, newParentPath, newName))

export const useDeleteFolder = () =>
  useFolderMutation<{ path: string }>(({ path }) => api.deleteMailFolder(path))

export const useSetFolderSubscription = () =>
  useFolderMutation<{ path: string; subscribed: boolean }>(
    ({ path, subscribed }) => api.setMailFolderSubscription(path, subscribed))
