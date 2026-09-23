import { useRef, useState } from 'react'
import { isImage } from './useInlineUploads'

function carriesFiles(event: React.DragEvent) {
  return Array.from(event.dataTransfer.types).includes('Files')
}
// A drag withholds its bytes until the drop, but not the types — enough to word the overlay on
// the predicate routeFiles will sort by, so it never promises an insertion the drop won't make.
function carriesImage(event: React.DragEvent) {
  return Array.from(event.dataTransfer.items).some(
    item => item.kind === 'file' && isImage(item.type))
}

// A file dropped on the surface goes to the tray; one dropped or pasted on the body goes to routeFiles.
export function useComposeDropZone(
  plainText: boolean, addFiles: (files: File[]) => void, routeFiles: (files: File[]) => void,
) {
  // Counter, not a boolean: dragleave fires at every child boundary, so the overlay only
  // goes away when as many leaves as enters have fired (or on drop).
  const [dropTarget, setDropTarget] = useState(false)
  const dragDepth = useRef(0)
  const [bodyInsert, setBodyInsert] = useState(false)
  const bodyDepth = useRef(0)

  function onDragEnter(event: React.DragEvent) {
    if (!carriesFiles(event)) return
    event.preventDefault()
    dragDepth.current += 1
    setDropTarget(true)
  }
  function onDragOver(event: React.DragEvent) {
    if (carriesFiles(event)) event.preventDefault()
  }
  function onDragLeave(event: React.DragEvent) {
    if (!carriesFiles(event)) return
    dragDepth.current = Math.max(0, dragDepth.current - 1)
    if (dragDepth.current === 0) setDropTarget(false)
  }
  function onDrop(event: React.DragEvent) {
    if (!carriesFiles(event)) return
    event.preventDefault()
    resetDrag()
    const files = Array.from(event.dataTransfer.files)
    if (files.length > 0) addFiles(files)
  }
  function resetDrag() {
    dragDepth.current = 0
    bodyDepth.current = 0
    setDropTarget(false)
    setBodyInsert(false)
  }
  function onBodyDragEnter(event: React.DragEvent) {
    if (!carriesFiles(event)) return
    bodyDepth.current += 1
    // Plain text has no body to show an image in, so there a drop attaches like any other.
    setBodyInsert(!plainText && carriesImage(event))
  }
  function onBodyDragLeave(event: React.DragEvent) {
    if (!carriesFiles(event)) return
    bodyDepth.current = Math.max(0, bodyDepth.current - 1)
    if (bodyDepth.current === 0) setBodyInsert(false)
  }
  // Stops here: the surface handler below would attach what the body has just taken. It therefore
  // owes the surface its own reset, which is what resetDrag is for.
  function onBodyDrop(event: React.DragEvent) {
    if (!carriesFiles(event)) return
    event.preventDefault()
    event.stopPropagation()
    resetDrag()
    const files = Array.from(event.dataTransfer.files)
    if (files.length > 0) routeFiles(files)
  }
  function onBodyPaste(event: React.ClipboardEvent) {
    const files = Array.from(event.clipboardData.files)
    if (files.length === 0) return
    // Capture phase, and stopped rather than merely prevented: Squire's _onPaste never reads
    // defaultPrevented, so a clipboard carrying an image plus text/plain (Explorer) or plus
    // rtf+html (Word) would insert its own artefact beside ours.
    event.preventDefault()
    event.stopPropagation()
    routeFiles(files)
  }

  return {
    dropTarget, bodyInsert,
    surface: { onDragEnter, onDragOver, onDragLeave, onDrop },
    body: { onDragEnter: onBodyDragEnter, onDragLeave: onBodyDragLeave, onDrop: onBodyDrop, onPasteCapture: onBodyPaste },
  }
}
