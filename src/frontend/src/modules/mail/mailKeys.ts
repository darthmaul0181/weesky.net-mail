import type { SearchCriteria } from './list/searchCriteria'

// The prefixes are the keys minus their trailing size arguments: what a mutation or a refresh
// matches on, since it patches a folder's every cached page whatever size it was fetched at.
const messagesIn = (accountId: string, folder: string) =>
  ['mail', accountId, 'messages', folder] as const
const messageStreamIn = (accountId: string, folder: string) =>
  ['mail', accountId, 'messageStream', folder] as const
const searchIn = (accountId: string) => ['mail', accountId, 'search'] as const

// Scoped by account, so two mailboxes never share a cache.
export const mailKeys = {
  all: (accountId: string) => ['mail', accountId] as const,
  folders: (accountId: string) => ['mail', accountId, 'folders'] as const,
  messagesIn,
  // pageSize is part of the key: a cached page was computed under one size and means something
  // else under another. So is `grouped`, which decides whether the page holds conversations or
  // messages — and it sits last, so the prefixes above still catch both modes.
  messages: (accountId: string, folder: string, page: number, pageSize: number, grouped = false) =>
    [...messagesIn(accountId, folder), page, pageSize, grouped] as const,
  message: (accountId: string, folder: string, uid: number) =>
    ['mail', accountId, 'message', folder, uid] as const,
  messageSource: (accountId: string, folder: string, uid: number) =>
    ['mail', accountId, 'source', folder, uid] as const,
  // Its own key: what it caches is not a page but a sequence of pages, and mixing the two
  // shapes under one key is a type error that only shows at runtime.
  messageStreamIn,
  messageStream: (accountId: string, folder: string, requestSize: number, grouped = false) =>
    [...messageStreamIn(accountId, folder), requestSize, grouped] as const,
  folderRoles: (accountId: string) => ['mail', accountId, 'folderRoles'] as const,
  identities: (accountId: string) => ['mail', accountId, 'identities'] as const,
  trustedSenders: (accountId: string) => ['mail', accountId, 'trustedSenders'] as const,
  aliases: (accountId: string) => ['mail', accountId, 'aliases'] as const,
  /** Prefix for every cached search — what the idle key falls back to when no search is active. */
  searchIn,
  // Criteria in the key: two different searches are two caches, never one overwriting the other.
  search: (accountId: string, criteria: SearchCriteria, page: number, pageSize: number) =>
    [...searchIn(accountId), criteria, page, pageSize] as const,
  /** Mutation key, not a query key: it is what lets the poll tell our own writes from a change.
      Carried by every write — flags, move, copy, delete — since each patches the same counters. */
  writes: (accountId: string) => ['mail', accountId, 'writes'] as const,
}
