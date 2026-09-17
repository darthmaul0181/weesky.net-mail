import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { useRef, type FormEvent, type ReactNode } from 'react'
import Modal from './Modal'
import { fireEscape } from '../test-utils'

function backdrop(index = 0) {
  return screen.getAllByRole('presentation')[index]
}

function openModal(props: Partial<Parameters<typeof Modal>[0]> = {}, children: ReactNode = <p>body</p>) {
  const onClose = vi.fn()
  const view = render(<Modal title="Move messages" onClose={onClose} {...props}>{children}</Modal>)
  return { onClose, ...view }
}

describe('Modal', () => {
  it('is a modal dialog named by its own title', () => {
    openModal()

    const dialog = screen.getByRole('dialog', { name: 'Move messages' })
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    expect(dialog).toHaveAttribute('tabindex', '-1')
    expect(dialog).toHaveClass('modal')
  })

  it('takes the alertdialog role when the caller asks for one', () => {
    openModal({ role: 'alertdialog', title: 'Delete this message?' })

    expect(screen.getByRole('alertdialog', { name: 'Delete this message?' })).toBeInTheDocument()
  })

  it('draws the icon and the header extra beside the title', () => {
    openModal({ icon: <svg data-testid="icon" />, headerExtra: <span>2 of 5</span> })

    expect(screen.getByTestId('icon')).toBeInTheDocument()
    expect(screen.getByText('2 of 5')).toBeInTheDocument()
  })

  it('adds the caller\'s classes to the dialog and to the backdrop', () => {
    openModal({ className: 'modal-folders', overlayClassName: 'is-editor' })

    expect(screen.getByRole('dialog')).toHaveClass('modal', 'modal-folders')
    expect(backdrop()).toHaveClass('modal-overlay', 'is-editor')
  })

  it('closes on a ✕ named Close', () => {
    const { onClose } = openModal()

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('names the ✕ what the caller asks for', () => {
    openModal({ closeLabel: 'Close the editor' })

    expect(screen.getByRole('button', { name: 'Close the editor' })).toBeInTheDocument()
  })

  it('closes when the press starts and ends on the backdrop', () => {
    const { onClose } = openModal()

    fireEvent.mouseDown(backdrop())
    fireEvent.mouseUp(backdrop())
    fireEvent.click(backdrop())

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  // A selection dragged out of the dialog: the click still lands on the overlay, their ancestor.
  it('does not close when the press started inside the dialog', () => {
    const { onClose } = openModal({}, <input placeholder="field" />)

    fireEvent.mouseDown(screen.getByPlaceholderText('field'))
    fireEvent.click(backdrop())

    expect(onClose).not.toHaveBeenCalled()
  })

  it('does not close when the press ended inside the dialog', () => {
    const { onClose } = openModal({}, <input placeholder="field" />)

    fireEvent.mouseDown(backdrop())
    fireEvent.mouseUp(screen.getByPlaceholderText('field'))
    fireEvent.click(backdrop())

    expect(onClose).not.toHaveBeenCalled()
  })

  // What used to need stopPropagation on .modal: the backdrop rule reads the target instead.
  it('does not close on a click inside the dialog', () => {
    const { onClose } = openModal({}, <input placeholder="field" />)

    fireEvent.mouseDown(screen.getByPlaceholderText('field'))
    fireEvent.mouseUp(screen.getByPlaceholderText('field'))
    fireEvent.click(screen.getByPlaceholderText('field'))

    expect(onClose).not.toHaveBeenCalled()
  })

  it('closes on Escape', () => {
    const { onClose } = openModal()

    fireEscape()

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('gives Escape to onEscape rather than to onClose when both are given', () => {
    const onEscape = vi.fn()
    const { onClose } = openModal({ onEscape })

    fireEscape()

    expect(onEscape).toHaveBeenCalledTimes(1)
    expect(onClose).not.toHaveBeenCalled()
  })

  describe('without onClose', () => {
    it('draws no ✕ and ignores the backdrop', () => {
      render(<Modal title="Keep editing?"><p>body</p></Modal>)

      expect(screen.queryByRole('button', { name: 'Close' })).not.toBeInTheDocument()
      fireEvent.mouseDown(backdrop())
      fireEvent.mouseUp(backdrop())
      fireEvent.click(backdrop())

      expect(screen.getByRole('dialog', { name: 'Keep editing?' })).toBeInTheDocument()
    })

    it('still answers Escape with onEscape', () => {
      const onEscape = vi.fn()
      render(<Modal title="Keep editing?" onEscape={onEscape}><p>body</p></Modal>)

      fireEscape()

      expect(onEscape).toHaveBeenCalledTimes(1)
    })

    it('swallows an Escape it has no answer for, leaving the dialog under it alone', () => {
      const outer = vi.fn()
      function Stack({ asking }: { asking: boolean }) {
        return (
          <Modal title="Event" onClose={outer}>
            {asking && <Modal title="Discard?"><p>body</p></Modal>}
          </Modal>
        )
      }
      const { rerender } = render(<Stack asking={false} />)
      rerender(<Stack asking />)

      fireEscape()

      expect(outer).not.toHaveBeenCalled()
    })
  })

  describe('while busy', () => {
    it('disables the ✕', () => {
      openModal({ busy: true })

      expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled()
    })

    it('ignores the backdrop and Escape', () => {
      const { onClose } = openModal({ busy: true })

      fireEvent.mouseDown(backdrop())
      fireEvent.mouseUp(backdrop())
      fireEvent.click(backdrop())
      fireEscape()

      expect(onClose).not.toHaveBeenCalled()
    })
  })

  describe('with onSubmit', () => {
    it('makes the dialog root the form, so Enter submits', () => {
      const onSubmit = vi.fn((event: FormEvent<HTMLFormElement>) => event.preventDefault())
      render(
        <Modal title="Create folder" onClose={vi.fn()} onSubmit={onSubmit}>
          <button type="submit">Create</button>
        </Modal>,
      )
      const dialog = screen.getByRole('dialog', { name: 'Create folder' })
      expect(dialog.tagName).toBe('FORM')

      fireEvent.click(screen.getByRole('button', { name: 'Create' }))

      expect(onSubmit).toHaveBeenCalledTimes(1)
    })

    it('does not submit from the ✕', () => {
      const onSubmit = vi.fn((event: FormEvent<HTMLFormElement>) => event.preventDefault())
      const onClose = vi.fn()
      render(
        <Modal title="Create folder" onClose={onClose} onSubmit={onSubmit}>
          <button type="submit">Create</button>
        </Modal>,
      )

      fireEvent.click(screen.getByRole('button', { name: 'Close' }))

      expect(onClose).toHaveBeenCalledTimes(1)
      expect(onSubmit).not.toHaveBeenCalled()
    })
  })

  describe('focus', () => {
    function Host({ open, field }: { open: boolean; field?: boolean }) {
      const input = useRef<HTMLInputElement>(null)
      return (
        <div>
          <button type="button">Move</button>
          {open && (
            <Modal title="Move messages" onClose={vi.fn()} initialFocusRef={field ? input : undefined}>
              <input ref={input} placeholder="filter" />
            </Modal>
          )}
        </div>
      )
    }

    it('moves to the field the caller names', () => {
      const { rerender } = render(<Host open={false} />)

      rerender(<Host open field />)

      expect(screen.getByPlaceholderText('filter')).toHaveFocus()
    })

    // Documented contract: the ✕ is the dialog's first focusable, so a dialog opening on a field
    // asks for it by ref — `autoFocus` is committed before the layer runs and loses to it.
    it('lands on the ✕ when the caller names no field', () => {
      const { rerender } = render(<Host open={false} />)

      rerender(<Host open />)

      expect(screen.getByRole('button', { name: 'Close' })).toHaveFocus()
    })

    it('goes back to the opener on close', () => {
      const { rerender } = render(<Host open={false} />)
      const trigger = screen.getByRole('button', { name: 'Move' })
      trigger.focus()
      rerender(<Host open field />)

      rerender(<Host open={false} />)

      expect(trigger).toHaveFocus()
    })
  })

  describe('nested', () => {
    function Nested({ inner, onOuter, onInner }: {
      inner: boolean; onOuter: () => void; onInner: () => void
    }) {
      return (
        <Modal title="Rules" onClose={onOuter}>
          <button type="button">Help</button>
          {inner && <Modal title="What a rule does" onClose={onInner}><p>help</p></Modal>}
        </Modal>
      )
    }

    /** The inner dialog is opened from the standing one, which is the only way a user gets both. */
    function open(onOuter: () => void, onInner: () => void) {
      const view = render(<Nested inner={false} onOuter={onOuter} onInner={onInner} />)
      view.rerender(<Nested inner onOuter={onOuter} onInner={onInner} />)
      return view
    }

    it('gives Escape to the inner dialog alone', () => {
      const onOuter = vi.fn()
      const onInner = vi.fn()
      open(onOuter, onInner)

      fireEscape()

      expect(onInner).toHaveBeenCalledTimes(1)
      expect(onOuter).not.toHaveBeenCalled()
    })

    it('gives a backdrop press to the inner dialog alone', () => {
      const onOuter = vi.fn()
      const onInner = vi.fn()
      open(onOuter, onInner)

      fireEvent.mouseDown(backdrop(1))
      fireEvent.mouseUp(backdrop(1))
      fireEvent.click(backdrop(1))

      expect(onInner).toHaveBeenCalledTimes(1)
      expect(onOuter).not.toHaveBeenCalled()
    })

    it('keeps Tab inside the inner dialog', () => {
      open(vi.fn(), vi.fn())
      const closes = screen.getAllByRole('button', { name: 'Close' })
      closes[1].focus()

      fireEvent.keyDown(document, { key: 'Tab' })

      expect(closes[1]).toHaveFocus()
      expect(screen.getByRole('button', { name: 'Help' })).not.toHaveFocus()
    })
  })

  describe('without a header', () => {
    it('draws none and takes its name from the element the caller points at', () => {
      render(
        <Modal header={false} labelledBy="editor-title" onClose={vi.fn()} className="calendar-editor">
          <h2 id="editor-title">Lunch</h2>
        </Modal>,
      )

      expect(screen.getByRole('dialog', { name: 'Lunch' })).toBeInTheDocument()
      expect(document.querySelector('.modal-header')).toBeNull()
      expect(screen.queryByRole('button', { name: 'Close' })).not.toBeInTheDocument()
    })
  })
})
