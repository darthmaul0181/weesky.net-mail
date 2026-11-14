import { type RequestOptions, largeRead, request, requestBlob } from '../../../lib/http'
import type {
  ApplyReplyArgs, ApplyReplyResponse, FolderRoleEntry, InvitationResponse, MailFlagName,
  MailFolderNode, MailFolderPage, MailExportFormat, MailMessageDetail, MailMessageSource,
  MailSearchPage, OpenedDraft, PreparedQuote, QuotePurpose, RespondInvitationArgs, SaveDraftArgs,
  SavedDraft, SendMessageArgs, SendMessageResult,
} from './mailTypes'
import type { SearchCriteria } from '../list/searchCriteria'

/** Mail: folders, messages, the composer and its attachments. */
export const mailApi = {
  // ── Mail ──────────────────────────────────────────────────────────────────
  // Folder paths are encoded: they may contain '/', '&' or '#'.

  getMailFolders: (options?: RequestOptions) =>
    request<MailFolderNode[]>('GET', '/api/Mail/Folders', undefined, options),

  /** Answers the new folder's full path. */
  createMailFolder: (parentPath: string, name: string, options?: RequestOptions) =>
    request<string>('POST', '/api/Mail/Folders', { parentPath, name }, options),

  renameMailFolder: (path: string, newParentPath: string, newName: string, options?: RequestOptions) =>
    request<string>('PUT', '/api/Mail/Folders', { path, newParentPath, newName }, options),

  deleteMailFolder: (path: string, options?: RequestOptions) =>
    request<null>('DELETE', '/api/Mail/Folders', { path }, options),

  setMailFolderSubscription: (path: string, subscribed: boolean, options?: RequestOptions) =>
    request<null>('PUT', '/api/Mail/Folders/Subscription', { path, subscribed }, options),

  getMailMessages: (
    folder: string, page: number, pageSize: number, options?: RequestOptions & { grouped?: boolean },
  ) =>
    request<MailFolderPage>('GET', `/api/Mail/Messages?folder=${encodeURIComponent(folder)}&page=${page}&pageSize=${pageSize}`
      + (options?.grouped ? '&grouped=true' : ''), undefined, options),

  getMailMessage: (folder: string, uid: number, options?: RequestOptions) =>
    request<MailMessageDetail>('GET', `/api/Mail/Messages/Detail?folder=${encodeURIComponent(folder)}&uid=${uid}`, undefined, options),

  // A calendar route reached from the reader: it answers the organiser and files the event, and
  // it names the message the block was read from, so it lives beside the message calls.
  respondInvitation: (body: RespondInvitationArgs, options?: RequestOptions) =>
    request<InvitationResponse>('POST', '/api/Calendar/Invitations/Respond', body, options),
  applyInvitationReply: (body: ApplyReplyArgs, options?: RequestOptions) =>
    request<ApplyReplyResponse>('POST', '/api/Calendar/Invitations/ApplyReply', body, options),

  exportMessage: (folder: string, uid: number, format: MailExportFormat, options?: RequestOptions) =>
    requestBlob(`/api/Mail/Messages/Export?folder=${encodeURIComponent(folder)}&uid=${uid}&format=${format}`, options),

  getMessageSource: (folder: string, uid: number, options?: RequestOptions) =>
    request<MailMessageSource>('GET', `/api/Mail/Messages/Source?folder=${encodeURIComponent(folder)}&uid=${uid}`, undefined,
      largeRead(options)),

  setMessageFlags: (folder: string, uids: number[], flag: MailFlagName, value: boolean, options?: RequestOptions) =>
    request<null>('PUT', '/api/Mail/Messages/Flags', { folderPath: folder, uids, flag, value }, options),

  moveMessages: (folder: string, uids: number[], targetFolder: string, options?: RequestOptions) =>
    request<null>('POST', '/api/Mail/Messages/Move', { folderPath: folder, uids, targetFolderPath: targetFolder }, options),

  copyMessages: (folder: string, uids: number[], targetFolder: string, options?: RequestOptions) =>
    request<null>('POST', '/api/Mail/Messages/Copy', { folderPath: folder, uids, targetFolderPath: targetFolder }, options),

  deleteMessages: (folder: string, uids: number[], options?: RequestOptions) =>
    request<null>('DELETE', '/api/Mail/Messages', { folderPath: folder, uids }, options),

  emptyFolder: (folder: string, targetFolder?: string | null, options?: RequestOptions) =>
    request<null>('POST', '/api/Mail/Folders/Empty', { folderPath: folder, targetFolderPath: targetFolder ?? null }, options),

  searchMessages: (criteria: SearchCriteria, page: number, pageSize: number, options?: RequestOptions) =>
    request<MailSearchPage>('POST', '/api/Mail/Messages/Search', { ...criteria, page, pageSize }, options),

  getFolderRoles: (options?: RequestOptions) =>
    request<FolderRoleEntry[]>('GET', '/api/Mail/FolderRoles', undefined, options),

  setFolderRole: (role: string, folderPath: string, options?: RequestOptions) =>
    request<null>('PUT', '/api/Mail/FolderRoles', { role, folderPath }, options),

  clearFolderRole: (role: string, options?: RequestOptions) =>
    request<null>('DELETE', `/api/Mail/FolderRoles?role=${encodeURIComponent(role)}`, undefined, options),

  sendMessage: (payload: SendMessageArgs, options?: RequestOptions) =>
    request<SendMessageResult>('POST', '/api/Mail/Send', payload, options),

  deleteAttachment: (id: string, options?: RequestOptions) =>
    request<null>('DELETE', `/api/Mail/Attachments/${id}`, undefined, options),

  prepareQuote: (folder: string, uid: number, purpose: QuotePurpose, options?: RequestOptions) =>
    request<PreparedQuote>('POST', '/api/Mail/Messages/PrepareQuote', { folder, uid, purpose }, options),

  saveDraft: (payload: SaveDraftArgs, options?: RequestOptions) =>
    request<SavedDraft>('POST', '/api/Mail/Drafts', payload, options),

  openDraft: (folder: string, uid: number, options?: RequestOptions) =>
    request<OpenedDraft>('POST', '/api/Mail/Drafts/Open', { folder, uid }, options),
}
