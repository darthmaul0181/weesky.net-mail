import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../../api.js'
import { useAccountId } from '../../hooks/useAccountId'
import type {
  MailFolderNode, QuotePurpose, SaveDraftArgs, SendMessageArgs, SendMessageResult,
} from './api/mailTypes'
import { flatten } from './folders/folderNodes'
import { mailKeys } from './mailKeys'

export type { SaveDraftArgs, SendMessageArgs, SendMessageResult }

// On success invalidates the tree (the Sent copy changes its counts) and the Sent folder's messages,
// found in the cached tree by specialUse.
export function useSendMessage(pinnedAccountId?: string) {
  const activeAccountId = useAccountId()
  const accountId = pinnedAccountId ?? activeAccountId
  const queryClient = useQueryClient()

  return useMutation({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: (args: SendMessageArgs) =>
      api.sendMessage(args, { accountId }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) })
      const folders = queryClient.getQueryData<MailFolderNode[]>(mailKeys.folders(accountId))
      const sent = folders ? flatten(folders).find(entry => entry.node.specialUse === 'sent') : undefined
      if (sent) {
        void queryClient.invalidateQueries({ queryKey: mailKeys.messagesIn(accountId, sent.node.path) })
        void queryClient.invalidateQueries({ queryKey: mailKeys.messageStreamIn(accountId, sent.node.path) })
      }
    },
  })
}

// A mutation, not a query: every call stages files with a TTL, so it must never be cached or replayed.
export function usePrepareQuote() {
  const accountId = useAccountId()

  return useMutation({
    mutationFn: (args: { folder: string; uid: number; purpose: QuotePurpose }) =>
      api.prepareQuote(args.folder, args.uid, args.purpose, { accountId }),
  })
}

/** Files the draft under the drafts role; each success replaces the version before it. */
export function useSaveDraft(pinnedAccountId?: string) {
  const activeAccountId = useAccountId()
  const accountId = pinnedAccountId ?? activeAccountId
  const queryClient = useQueryClient()

  return useMutation({
    mutationKey: mailKeys.writes(accountId),
    mutationFn: (args: SaveDraftArgs) => api.saveDraft(args, { accountId }),
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: mailKeys.folders(accountId) })
      void queryClient.invalidateQueries({ queryKey: mailKeys.messagesIn(accountId, saved.folderPath) })
      void queryClient.invalidateQueries({ queryKey: mailKeys.messageStreamIn(accountId, saved.folderPath) })
    },
  })
}

// A mutation, not a query: every call re-stages the draft's parts with a TTL, so it is never replayed.
export function useOpenDraft() {
  const accountId = useAccountId()

  return useMutation({
    mutationFn: (args: { folder: string; uid: number }) =>
      api.openDraft(args.folder, args.uid, { accountId }),
  })
}
