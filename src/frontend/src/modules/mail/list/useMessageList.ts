import { useEffect, useMemo, useState } from 'react'
import { isStreaming, requestSizeOf, usePreferences } from '../../../hooks/usePreferences'
import type { MailFolderPage, MailMessageSummary } from '../api/mailTypes'
import { useMessageStream, useMessages } from '../queries'
import { dedupeByUid } from './messageStream'

export interface MessageListState {
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
  messages: [], total: 0, isLoading: true, isError: false, paging: null, streaming: null,
}

// A fresh [] on every render would change the memo's dependency every render.
const NO_BLOCKS: MailFolderPage[] = []

/**
 * One shape for both modes, so the list renders a pager or a sentinel without ever learning
 * what "All" means. Both queries are always called — hooks cannot be conditional — with the
 * inactive one disabled, which issues no request.
 */
export function useMessageList(folderPath: string | null): MessageListState {
  const [page, setPage] = useState(0)
  useEffect(() => { setPage(0) }, [folderPath])

  const { data: preferences } = usePreferences()
  const streams = preferences ? isStreaming(preferences) : false
  const requestSize = preferences ? requestSizeOf(preferences) : 0

  const paged = useMessages(folderPath, page, requestSize, Boolean(preferences) && !streams)
  const stream = useMessageStream(folderPath, requestSize, Boolean(preferences) && streams)

  const blocks = stream.data?.pages ?? NO_BLOCKS
  const streamed = useMemo(() => dedupeByUid(blocks), [blocks])

  if (!preferences) return WAITING

  if (streams) {
    return {
      messages: streamed,
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

  return {
    messages: paged.data?.messages ?? [],
    total,
    isLoading: paged.isLoading,
    isError: paged.isError,
    paging: {
      page,
      lastPage: requestSize > 0 ? Math.max(0, Math.ceil(total / requestSize) - 1) : 0,
      onSelect: setPage,
    },
    streaming: null,
  }
}
