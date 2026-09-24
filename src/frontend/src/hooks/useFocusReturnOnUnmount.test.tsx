import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { useLayoutEffect, useRef, type RefObject } from 'react'
import { useFocusReturnOnUnmount } from './useFocusReturnOnUnmount'

function Card({ show, targetRef, captured }: {
  show: boolean; targetRef: RefObject<HTMLElement | null>; captured: Array<(node: HTMLElement | null) => void>
}) {
  const cardRef = useFocusReturnOnUnmount(targetRef)
  useLayoutEffect(() => { captured.push(cardRef) })
  return show ? <div ref={cardRef}><button>Inside</button></div> : null
}

function Harness({ show, captured }: {
  show: boolean; captured: Array<(node: HTMLElement | null) => void>
}) {
  const targetRef = useRef<HTMLButtonElement>(null)
  return (
    <div>
      <button ref={targetRef}>Target</button>
      <button>Elsewhere</button>
      <Card show={show} targetRef={targetRef} captured={captured} />
    </div>
  )
}

describe('useFocusReturnOnUnmount', () => {
  it('moves focus to the target when the node unmounts while holding focus', () => {
    const captured: Array<(node: HTMLElement | null) => void> = []
    const { rerender } = render(<Harness show captured={captured} />)
    screen.getByText('Inside').focus()
    rerender(<Harness show={false} captured={captured} />)
    expect(screen.getByText('Target')).toHaveFocus()
  })

  it('leaves focus alone when the node unmounts without holding it', () => {
    const captured: Array<(node: HTMLElement | null) => void> = []
    const { rerender } = render(<Harness show captured={captured} />)
    screen.getByText('Elsewhere').focus()
    rerender(<Harness show={false} captured={captured} />)
    expect(screen.getByText('Elsewhere')).toHaveFocus()
  })

  it('returns a stable callback ref across renders', () => {
    const captured: Array<(node: HTMLElement | null) => void> = []
    const { rerender } = render(<Harness show captured={captured} />)
    rerender(<Harness show captured={captured} />)
    expect(captured[0]).toBe(captured[captured.length - 1])
  })
})
