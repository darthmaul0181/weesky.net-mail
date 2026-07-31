import { forwardRef, useEffect, useImperativeHandle, useRef } from 'react'
import DOMPurify from 'dompurify'
import Squire from 'squire-rte'
import { FORBID_TAGS, FORBID_ATTR } from '../sanitizePolicy'

export type EditorCommand =
  | 'undo' | 'redo'
  | 'bold' | 'italic' | 'underline' | 'strikethrough'
  | 'unorderedList' | 'orderedList'
  | 'increaseQuote' | 'decreaseQuote'
  | 'removeLink' | 'clearFormatting'

export interface EditorHandle {
  getHTML: () => string
  isEmpty: () => boolean
  focus: () => void
  command: (name: EditorCommand) => void
  setTextColour: (colour: string) => void
  setHighlightColour: (colour: string) => void
  setFontFace: (face: string) => void
  setFontSize: (size: string) => void
  setAlignment: (alignment: 'left' | 'center' | 'right' | 'justify') => void
  makeLink: (url: string) => void
  insertImage: (src: string) => void
}

/** Which inline/list formats are on at the caret, so the toolbar can light its buttons. */
export interface ActiveFormats {
  bold: boolean; italic: boolean; underline: boolean; strikethrough: boolean
  unorderedList: boolean; orderedList: boolean
}

interface Props {
  onChange: () => void
  onFormatChange?: (active: ActiveFormats) => void
  /** A reply/forward body, loaded once at mount. */
  initialHtml?: string
}

const activeFormats = (squire: Squire): ActiveFormats => ({
  bold: squire.hasFormat('b'), italic: squire.hasFormat('i'),
  underline: squire.hasFormat('u'), strikethrough: squire.hasFormat('s'),
  unorderedList: squire.hasFormat('ul'), orderedList: squire.hasFormat('ol'),
})

// Format toggles pair the apply call with its remove and the tag hasFormat checks.
const toggles: Partial<Record<EditorCommand, [keyof Squire, keyof Squire, string]>> = {
  bold: ['bold', 'removeBold', 'b'],
  italic: ['italic', 'removeItalic', 'i'],
  underline: ['underline', 'removeUnderline', 'u'],
  strikethrough: ['strikethrough', 'removeStrikethrough', 's'],
  unorderedList: ['makeUnorderedList', 'removeList', 'ul'],
  orderedList: ['makeOrderedList', 'removeList', 'ol'],
}

const invoke = (squire: Squire, method: keyof Squire) => (squire[method] as () => void)()

/**
 * Squire sanitises every setHTML and every paste through this, and it otherwise reaches for a
 * *global* DOMPurify the app never defines — without it the constructor throws. Stricter than
 * Squire's own default, which turns the protocol check off; outgoing mail allows http/https/mailto.
 * Shares FORBID_TAGS/FORBID_ATTR with the reader: this div is a plain part of the SPA document,
 * so a surviving <style> would apply document-wide rather than staying scoped to a sandboxed iframe.
 * document.importNode matches Squire's own default — appendChild alone adopts but does not reset
 * internal element state.
 */
const sanitizeToDOMFragment = (html: string): DocumentFragment => {
  const fragment = DOMPurify.sanitize(html, {
    RETURN_DOM_FRAGMENT: true, WHOLE_DOCUMENT: false, FORCE_BODY: false, FORBID_TAGS, FORBID_ATTR,
  })
  return document.importNode(fragment, true)
}

/**
 * Thin React shell over Squire. The canvas follows the app theme (see .compose-editor); the
 * toolbar's active state rides Squire's pathChange event.
 */
const SquireEditor = forwardRef<EditorHandle, Props>(function SquireEditor(
  { onChange, onFormatChange, initialHtml }, ref,
) {
  const root = useRef<HTMLDivElement>(null)
  const editor = useRef<Squire | null>(null)

  useEffect(() => {
    const squire = new Squire(root.current!, { blockTag: 'DIV', sanitizeToDOMFragment })
    squire.addEventListener('input', onChange)
    const report = () => onFormatChange?.(activeFormats(squire))
    squire.addEventListener('pathChange', report)
    report()
    editor.current = squire
    if (initialHtml) {
      // Passes through sanitizeToDOMFragment like every setHTML; the caller's quote/seed markup
      // is treated as untrusted input, same as a paste.
      squire.setHTML(initialHtml)
      squire.moveCursorToStart()
      // A prefilled composer is there to be written in; the To field only owns the focus when blank.
      squire.focus()
    }
    return () => { squire.destroy(); editor.current = null }
    // Mount once: onChange/onFormatChange identity is the caller's concern, rebinding would rebuild
    // the editor; initialHtml is read once here by design, later edits are the user's.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useImperativeHandle(ref, () => ({
    getHTML: () => editor.current?.getHTML() ?? '',
    isEmpty: () => {
      const html = editor.current?.getHTML() ?? ''
      const parsed = new DOMParser().parseFromString(html, 'text/html')
      // Content-bearing tags count as non-empty even with no text of their own; a regex over
      // the markup can't tell these apart from an attribute value that happens to contain '>'.
      if (parsed.body.querySelector('img, hr, table')) return false
      return (parsed.body.textContent ?? '').trim() === ''
    },
    focus: () => { editor.current?.focus() },
    command: (name) => {
      const squire = editor.current
      if (!squire) return
      const toggle = toggles[name]
      if (toggle) {
        const [apply, remove, tag] = toggle
        invoke(squire, squire.hasFormat(tag) ? remove : apply)
        return
      }
      if (name === 'undo') squire.undo()
      else if (name === 'redo') squire.redo()
      else if (name === 'increaseQuote') squire.increaseQuoteLevel()
      else if (name === 'decreaseQuote') squire.decreaseQuoteLevel()
      else if (name === 'removeLink') squire.removeLink()
      else if (name === 'clearFormatting') squire.removeAllFormatting()
    },
    // Squire spells the two colour setters the American way; the handle stays British like the rest of the UI.
    setTextColour: (colour) => { editor.current?.setTextColor(colour) },
    setHighlightColour: (colour) => { editor.current?.setHighlightColor(colour) },
    setFontFace: (face) => { editor.current?.setFontFace(face) },
    setFontSize: (size) => { editor.current?.setFontSize(size) },
    setAlignment: (alignment) => { editor.current?.setTextAlignment(alignment) },
    makeLink: (url) => { editor.current?.makeLink(url) },
    // The bound is on the image itself rather than in a stylesheet: the recipient's client has
    // none of ours, and a 4000px photo would otherwise blow out their reading column.
    insertImage: (src) => { editor.current?.insertImage(src, { style: 'max-width: 100%' }) },
  }), [])

  return <div ref={root} className="compose-editor" data-testid="compose-editor" />
})

export default SquireEditor
