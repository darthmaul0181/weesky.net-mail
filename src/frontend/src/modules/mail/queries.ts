// Composition entry for the mail module's TanStack Query layer. The hooks and their cache
// helpers live in files of their own — mailKeys, cachePatches, folders, messages, compose,
// identities — and are re-exported from here so every existing importer keeps working unchanged.

// Moved to src/hooks: contacts scope their keys on it too. Re-exported so the mail module's own
// importers keep working from here.
export { useAccountId } from '../../hooks/useAccountId'

export { mailKeys } from './mailKeys'

// Every hook across this module's files carries `{ accountId }`, read at render rather than at
// fire time: a write started under one mailbox lands there even if the user switches while it is
// in flight.

export {
  listKeysOf, useSetFlags,
  type SetFlagsArgs,
} from './cachePatches'

export {
  POLL_INTERVAL, useClearFolderRole, useCreateFolder, useDeleteFolder, useEmptyFolder,
  useFolderRoles, useFolders, useMailRefresh, useRenameFolder, useSetFolderRole,
  useSetFolderSubscription,
  type EmptyFolderArgs,
} from './folders'

export {
  useApplyInvitationReply, useDeleteMessages, useMessage, useMessages, useMessageSource,
  useMessageStream, useMoveMessages, useRespondInvitation, useSearchMessages,
  type DeleteMessagesArgs, type MoveMessagesArgs,
} from './messages'

export {
  useOpenDraft, usePrepareQuote, useSaveDraft, useSendMessage,
  type SaveDraftArgs, type SendMessageArgs, type SendMessageResult,
} from './compose'

export {
  identitiesQueryOptions, useAliases, useIdentities, useReplaceIdentities, useTrustSender,
  useTrustedSenders,
} from './identities'
