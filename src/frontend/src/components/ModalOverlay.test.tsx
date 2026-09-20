import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import ModalOverlay from './ModalOverlay'

function overlay() {
  return screen.getByRole('presentation')
}

/** jsdom computes no layout, so `clientWidth`/`clientHeight` and a native click's `offsetX`/
    `offsetY` all read 0 unless set explicitly — this proves the guard's arithmetic, not what a
    real browser paints; that is the manual recette's job (scenario 13). */
function sized(width: number, height: number) {
  Object.defineProperty(overlay(), 'clientWidth', { value: width, configurable: true })
  Object.defineProperty(overlay(), 'clientHeight', { value: height, configurable: true })
}

function press(type: string, offsetX: number, offsetY: number) {
  const event = new MouseEvent(type, { bubbles: true, cancelable: true })
  Object.defineProperty(event, 'offsetX', { value: offsetX })
  Object.defineProperty(event, 'offsetY', { value: offsetY })
  fireEvent(overlay(), event)
}

function pressAt(offsetX: number, offsetY: number) {
  press('mousedown', offsetX, offsetY)
  press('mouseup', offsetX, offsetY)
  press('click', offsetX, offsetY)
}

describe('ModalOverlay', () => {
  it('closes on a press inside the content box', () => {
    const onClose = vi.fn()
    render(<ModalOverlay onClose={onClose}><p>body</p></ModalOverlay>)
    sized(400, 300)

    pressAt(200, 150)

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  // Scenario 13 of the manual recette: the overlay is what scrolls under a tall dialog, so its
  // own scrollbar satisfies `target === currentTarget` on both mousedown and click exactly like
  // the backdrop does. A press whose offset falls past the content box is on that scrollbar.
  it('does not close on a press past the content box, on the scrollbar the overlay itself owns', () => {
    const onClose = vi.fn()
    render(<ModalOverlay onClose={onClose}><p>body</p></ModalOverlay>)
    sized(400, 300)

    pressAt(410, 150)

    expect(onClose).not.toHaveBeenCalled()
  })

  it('does not close on a press past the content box on the vertical edge either', () => {
    const onClose = vi.fn()
    render(<ModalOverlay onClose={onClose}><p>body</p></ModalOverlay>)
    sized(400, 300)

    pressAt(200, 310)

    expect(onClose).not.toHaveBeenCalled()
  })

  // With no measured content box (every test that does not call `sized`, and every real overlay
  // that carries no scrollbar) the guard must be inert: it must not refuse a press it would have
  // accepted before this guard existed.
  it('is inert when nothing is scrollable: an ordinary backdrop press still closes', () => {
    const onClose = vi.fn()
    render(<ModalOverlay onClose={onClose}><p>body</p></ModalOverlay>)

    fireEvent.mouseDown(overlay())
    fireEvent.mouseUp(overlay())
    fireEvent.click(overlay())

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('still refuses a press that starts inside the dialog and ends on the backdrop', () => {
    const onClose = vi.fn()
    render(
      <ModalOverlay onClose={onClose}><button type="button">field</button></ModalOverlay>,
    )
    sized(400, 300)

    fireEvent.mouseDown(screen.getByRole('button', { name: 'field' }))
    press('click', 200, 150)

    expect(onClose).not.toHaveBeenCalled()
  })
})
