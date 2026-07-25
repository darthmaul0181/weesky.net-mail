import { describe, it, expect } from 'vitest'
import { StrictMode, createRef } from 'react'
import { render } from '@testing-library/react'
import SquireEditor, { type EditorHandle } from './SquireEditor'

// The sibling suite mocks squire-rte; this one deliberately does not. A mocked engine cannot
// notice a config or API mismatch with the real package — the miss that made the constructor
// throw was a *missing* option, invisible to every mock.
describe('SquireEditor against the real engine', () => {
  it('mounts on our div and tears down', () => {
    const ref = createRef<EditorHandle>()
    const view = render(<SquireEditor ref={ref} onChange={() => {}} />)
    expect(view.getByTestId('compose-editor')).toHaveAttribute('contenteditable', 'true')
    expect(ref.current!.isEmpty()).toBe(true)
    view.unmount()
  })

  // The seeded quote carries absolute https srcs for its inline images; the sanitiser every
  // setHTML runs through has to leave them alone, or the composer shows a body full of holes.
  it('loads a seeded body through the real setHTML and moveCursorToStart', () => {
    const ref = createRef<EditorHandle>()
    const src = 'https://api.mail.weesky.net/api/Mail/Attachments/i1/content'
    const view = render(
      <SquireEditor ref={ref} onChange={() => {}}
        initialHtml={`<div><br></div><blockquote><p>original</p><img src="${src}"></blockquote>`} />,
    )
    expect(ref.current!.getHTML()).toContain('original')
    expect(ref.current!.getHTML()).toContain(src)
    expect(ref.current!.isEmpty()).toBe(false)
    view.unmount()
  })

  it('survives StrictMode, which mounts, destroys and mounts again', () => {
    const ref = createRef<EditorHandle>()
    const view = render(
      <StrictMode><SquireEditor ref={ref} onChange={() => {}} /></StrictMode>,
    )
    expect(view.getByTestId('compose-editor')).toHaveAttribute('contenteditable', 'true')
    expect(ref.current!.isEmpty()).toBe(true)
    view.unmount()
  })
})
