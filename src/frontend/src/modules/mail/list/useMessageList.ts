import { useMemo, useState } from 'react'
import {
  groupConversationsOf, isStreaming, requestSizeOf, usePreferences,
} from '../../../hooks/usePreferences'
import type { MailFolderPage, MailMessageSummary } from '../api/mailTypes'
import { useMessageStream, useMessages } from '../queries'
import { dedupeThreads, flatMessages, groupsOf, type ThreadGroup } from './threading'

export interface MessageListState {
  /** One entry per row: a conversation, or a single message wrapped as one. */
  groups: ThreadGroup[]
  /** The groups' members, flattened — what selection, the reader and the bulk actions read. */
  messages: MailMessageSummary[]
  total: number
  isLoading: boolean
  isError: boolean
  paging: { page: number; lastPage: number; onSelect: (page: number) => void } | null
  streaming: {
    hasMore: boolean
    isLoadingMore: boolean
    loadMoreFailed: boolean
    loadMore: () => void
  } | null
}

const WAITING: MessageListState = {
  groups: [], messages: [], total: 0, isLoading: true, isError: false,
  paging: null, streaming: null,
}

// A fresh [] on every render would change the memo's dependency every render.
const NO_BLOCKS: MailFolderPage[] = []
const NO_GROUPS: ThreadGroup[] = []

/**
 * One shape for both modes, so the list renders a pager or a sentinel without ever learning
 * what "All" means. Both queries are always called — hooks cannot be conditional — with the
 * inactive one disabled, which issues no request.
 */
export function useMessageList(folderPath: string | null): MessageListState {
  const [page, setPage] = useState(0)
  const [shownFolder, setShownFolder] = useState(folderPath)

  // Adjusted during render, not in an effect: an effect would let one query fire for the new
  // folder at the old page index before it corrected it.
  if (folderPath !== shownFolder) {
    setShownFolder(folderPath)
    setPage(0)
  }

  const { data: preferences } = usePreferences()
  const streams = preferences ? isStreaming(preferences) : false
  const requestSize = preferences ? requestSizeOf(preferences) : 0
  const grouped = preferences ? groupConversationsOf(preferences) : false

  const enabled = Boolean(preferences)
  const paged = useMessages(folderPath, page, requestSize, enabled && !streams, grouped)
  const stream = useMessageStream(folderPath, requestSize, enabled && streams, grouped)

  const blocks = stream.data?.pages ?? NO_BLOCKS
  // In flat mode dedupeThreads answers singletons, which is dedupeByUid one wrapper out.
  const streamedGroups = useMemo(() => dedupeThreads(blocks), [blocks])
  const streamedMessages = useMemo(() => flatMessages(streamedGroups), [streamedGroups])

  // Memoised beside the streaming pair rather than after the mode branch: a fresh array every
  // render would recompute every memo downstream of it.
  const pageGroups = useMemo(
    () => (paged.data ? groupsOf(paged.data) : NO_GROUPS), [paged.data])
  const pageMessages = useMemo(() => flatMessages(pageGroups), [pageGroups])

  if (!preferences) return WAITING

  if (streams) {
    return {
      groups: streamedGroups,
      messages: streamedMessages,
      total: blocks.length ? blocks[blocks.length - 1].total : 0,
      isLoading: stream.isLoading,
      isError: stream.isError && blocks.length === 0,
      paging: null,
      streaming: {
        hasMore: stream.hasNextPage,
        isLoadingMore: stream.isFetchingNextPage,
        // A block that failed after others succeeded: the list stays, Retry is offered.
        loadMoreFailed: stream.isError && blocks.length > 0,
        loadMore: () => {
          if (stream.hasNextPage && !stream.isFetchingNextPage) stream.fetchNextPage()
        },
      },
    }
  }

  const total = paged.data?.total ?? 0
  // What the pager pages: conversations on a grouped page, messages on a flat one. `total`
  // itself keeps counting messages, since that is what the heading reports.
  const pagedUnit = paged.data?.totalThreads ?? total

  return {
    groups: pageGroups,
    messages: pageMessages,
    total,
    isLoading: paged.isLoading,
    isError: paged.isError,
    paging: {
      page,
      lastPage: requestSize > 0 ? Math.max(0, Math.ceil(pagedUnit / requestSize) - 1) : 0,
      onSelect: setPage,
    },
    streaming: null,
  }
}
