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
