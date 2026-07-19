import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api.js'
import { useAuth } from '../../contexts/AuthContext'
import type { MailFolderNode, MailFolderPage, MailMessageDetail } from './api/mailTypes'

export const PAGE_SIZE = 50

/**
 * Every key is scoped by the active account, so linking a second account later isolates its
 * cache instead of mixing two mailboxes together.
 */
export const mailKeys = {
  all: (accountId: string) => ['mail', accountId] as const,
  folders: (accountId: string) => ['mail', accountId, 'folders'] as const,
  messages: (accountId: string, folder: string, page: number) =>
    ['mail', accountId, 'messages', folder, page] as const,
  message: (accountId: string, folder: string, uid: number) =>
    ['mail', accountId, 'message', folder, uid] as const,
}

function useAccountId(): string {
  return useAuth().activeAccount?.id ?? 'primary'
}

export function useFolders() {
  const accountId = useAccountId()

  return useQuery<MailFolderNode[]>({
    queryKey: mailKeys.folders(accountId),
    queryFn: ({ signal }) => api.getMailFolders({ signal }),
  })
}

export function useMessages(folderPath: string | null, page: number) {
  const accountId = useAccountId()

  return useQuery<MailFolderPage>({
    queryKey: mailKeys.messages(accountId, folderPath ?? '', page),
    queryFn: ({ signal }) => api.getMailMessages(folderPath, page, PAGE_SIZE, { signal }),
    enabled: folderPath !== null,
    // Keeps the current page on screen while the next one loads, instead of flashing empty.
    placeholderData: (previous) => previous,
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
