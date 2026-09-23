import { useState } from 'react'
import { useDeleteMessages, useEmptyFolder, useMoveMessages, useSetFlags } from '../queries'
import type { RowExit } from './useRowExit'

interface Options {
  folderPath: string | null
  inTrash: boolean
  purges: boolean
  trashPath: string | null
  selectedUids: number[]
  selectedUid: number | null
  selection: { clear: () => void }
  rowExit: RowExit
  onDeparted?: (uid: number, batch?: number[]) => void
  onNotify?: (message: string) => void
  /** Shared with the single-row actions, so a dialog's `loading` reads either kind of write. */
  moveMessages: ReturnType<typeof useMoveMessages>
  deleteMessages: ReturnType<typeof useDeleteMessages>
  setFlags: ReturnType<typeof useSetFlags>
}

/** What the toolbar does to the selection, and to the whole folder when it empties it. */
export function useBulkActions({
  folderPath, inTrash, purges, trashPath, selectedUids, selectedUid, selection, rowExit, onDeparted,
  onNotify, moveMessages, deleteMessages, setFlags,
}: Options) {
  const emptyFolder = useEmptyFolder(onNotify)
  const [confirmingBulk, setConfirmingBulk] = useState(false)
  const [confirmingEmpty, setConfirmingEmpty] = useState(false)
  const [picker, setPicker] = useState<{ mode: 'move' | 'copy' } | null>(null)
  const count = selectedUids.length

  // Fires the batch, advances the reader when the open row is in it, then drops the selection.
  // The whole batch is handed on so the reader can skip every departing row, not just the open one.
  // The rows leave together — a stagger over a batch of fifty is two seconds of waiting.
  function runBulk(uids: number[], fire: () => void) {
    rowExit.depart(uids, fire)
    if (selectedUid !== null && uids.includes(selectedUid)) onDeparted?.(selectedUid, uids)
    selection.clear()
  }

  function bulkMove(target: string | null, copy: boolean) {
    if (!folderPath || !target || !count) return
    const path = folderPath
    const uids = selectedUids
    if (copy) {  // A copy departs nothing; the rows stay, so only the selection is dropped.
      moveMessages.mutate({ folderPath: path, uids, targetFolderPath: target, copy: true })
      selection.clear()
    } else {
      runBulk(uids, () => moveMessages.mutate({ folderPath: path, uids, targetFolderPath: target, copy: false }))
    }
  }

  function bulkDelete() {
    if (!folderPath || !count) return
    if (inTrash) { setConfirmingBulk(true); return }
    bulkMove(trashPath, false)
  }

  function expungeBulk() {
    if (!folderPath || !count) return
    const path = folderPath
    const uids = selectedUids
    runBulk(uids, () => deleteMessages.mutate({ folderPath: path, uids }))
    setConfirmingBulk(false)
  }

  function bulkMark(value: boolean) {
    if (!folderPath || !count) return  // Marking read keeps the rows, so the reader never advances.
    setFlags.mutate({ folderPath, uids: selectedUids, flag: 'seen', value })
    selection.clear()
  }

  function pickTarget(target: string) {
    if (!picker) return
    bulkMove(target, picker.mode === 'copy')
    setPicker(null)
  }

  // Trash/junk purge permanently, so they confirm first; elsewhere it's a move to trash, undoable.
  function requestEmpty() {
    if (!folderPath) return
    if (purges) setConfirmingEmpty(true)
    else emptyFolder.mutate({ folderPath, targetFolderPath: trashPath })
  }

  function confirmEmpty() {
    if (!folderPath) return
    emptyFolder.mutate({ folderPath })
    setConfirmingEmpty(false)
  }

  return {
    count, bulkMove, bulkDelete, expungeBulk, bulkMark,
    picker, setPicker, pickTarget,
    confirmingBulk, cancelBulk: () => setConfirmingBulk(false),
    confirmingEmpty, cancelEmpty: () => setConfirmingEmpty(false), requestEmpty, confirmEmpty,
    emptying: emptyFolder.isPending,
  }
}

export type BulkActions = ReturnType<typeof useBulkActions>
