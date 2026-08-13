import type { MailFolderPage, MailMessageSummary } from '../api/mailTypes'

/** How far before the last row the next block starts loading. Rows, not pixels: row height
    changes with the preview setting, so a pixel margin would mean a different number of
    messages depending on a setting that has nothing to do with it. */
export const PREFETCH_ROWS = 20

export function dedupeByUid(pages: MailFolderPage[]): MailMessageSummary[] {
  const seen = new Set<number>()
  const messages: MailMessageSummary[] = []

  for (const page of pages) {
    for (const message of page.messages) {
      if (seen.has(message.uid)) continue
      seen.add(message.uid)
      messages.push(message)
    }
  }

  return messages
}

/** Stops on a short block rather than on `total`: the total moves when mail arrives. A grouped
    block is measured in threads — its unit of request. */
export function nextBlockIndex(
  lastPage: MailFolderPage, loadedBlocks: number, requestSize: number,
): number | undefined {
  const count = lastPage.threads ? lastPage.threads.length : lastPage.messages.length
  return count < requestSize ? undefined : loadedBlocks
}

export function sentinelIndexOf(messageCount: number): number {
  return Math.max(0, messageCount - PREFETCH_ROWS)
}
