/** Hands a blob to the browser's downloader. Shared so the reader and the contacts export cannot
    drift into two spellings of the same six lines. */
export function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  URL.revokeObjectURL(url)
}
