import { forwardRef, useImperativeHandle } from 'react'
import type { EditorHandle } from './SquireEditor'

// Squire needs a real browser, so the composer's tests mount this instead: SquireEditor.mount already
// covers the real one. `vi.mock('./SquireEditor', () => import('./squireEditorStub'))`.
export const editorState = { html: '', commands: [] as string[], images: [] as string[], empty: null as boolean | null }

const SquireEditorStub = forwardRef<EditorHandle, { onChange: () => void; initialHtml?: string }>(
  function SquireEditorStub({ onChange, initialHtml }, ref) {
    useImperativeHandle(ref, () => ({
      getHTML: () => editorState.html,
      // `empty` stands for Squire's own empty markup, which is not an empty string.
      isEmpty: () => editorState.empty ?? editorState.html === '',
      focus: () => {},
      command: (name: string) => { editorState.commands.push(name) },
      setTextColour: () => {}, setHighlightColour: () => {},
      setFontFace: () => {}, setFontSize: () => {}, setAlignment: () => {}, makeLink: () => {},
      insertImage: (src: string) => { editorState.images.push(src) },
    }), [])
    return (
      <textarea
        data-testid="compose-editor"
        defaultValue={initialHtml}
        onChange={event => { editorState.html = event.target.value; onChange() }}
      />
    )
  })

export default SquireEditorStub
