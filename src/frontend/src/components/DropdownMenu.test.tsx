import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import DropdownMenu, { type MenuItem } from './DropdownMenu'

function items(overrides?: Partial<MenuItem>[]): MenuItem[] {
  return [
    { label: 'Mark as read', onSelect: vi.fn(), ...(overrides?.[0] ?? {}) },
    { label: 'Star', onSelect: vi.fn(), ...(overrides?.[1] ?? {}) },
  ]
}

describe('DropdownMenu', () => {
  it('opens on click and lists the items', () => {
    render(<DropdownMenu ariaLabel="Message actions" trigger="..." items={items()} />)

    fireEvent.click(screen.getByLabelText('Message actions'))

    const menu = screen.getByRole('menu')
    expect(menu).toHaveTextContent('Mark as read')
    expect(menu).toHaveTextContent('Star')
  })

  it('closes on outside mousedown', () => {
    render(<DropdownMenu ariaLabel="Message actions" trigger="..." items={items()} />)

    fireEvent.click(screen.getByLabelText('Message actions'))
    expect(screen.getByRole('menu')).toBeInTheDocument()

    fireEvent.mouseDown(document.body)

    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('closes on Escape', () => {
    render(<DropdownMenu ariaLabel="Message actions" trigger="..." items={items()} />)

    fireEvent.click(screen.getByLabelText('Message actions'))
    expect(screen.getByRole('menu')).toBeInTheDocument()

    fireEvent.keyDown(document, { key: 'Escape' })

    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('registers document listeners only while open, and cleans them up on close and unmount', () => {
    const addSpy = vi.spyOn(document, 'addEventListener')
    const removeSpy = vi.spyOn(document, 'removeEventListener')
    const addedFor = (type: string) => addSpy.mock.calls.filter(([t]) => t === type).map(([, h]) => h)
    const removedFor = (type: string) => removeSpy.mock.calls.filter(([t]) => t === type).map(([, h]) => h)

    const { unmount } = render(<DropdownMenu ariaLabel="Message actions" trigger="..." items={items()} />)
    const trigger = screen.getByLabelText('Message actions')

    // Closed at mount: neither listener may be registered yet.
    expect(addedFor('mousedown')).toHaveLength(0)
    expect(addedFor('keydown')).toHaveLength(0)

    fireEvent.click(trigger) // open
    expect(addedFor('mousedown')).toHaveLength(1)
    expect(addedFor('keydown')).toHaveLength(1)
    const [firstMouseHandler] = addedFor('mousedown')
    const [firstKeyHandler] = addedFor('keydown')

    fireEvent.click(trigger) // close
    expect(removedFor('mousedown')).toEqual([firstMouseHandler])
    expect(removedFor('keydown')).toEqual([firstKeyHandler])

    fireEvent.click(trigger) // open again
    expect(addedFor('mousedown')).toHaveLength(2)
    expect(addedFor('keydown')).toHaveLength(2)
    const [, secondMouseHandler] = addedFor('mousedown')
    const [, secondKeyHandler] = addedFor('keydown')

    unmount()
    expect(removedFor('mousedown')).toEqual([firstMouseHandler, secondMouseHandler])
    expect(removedFor('keydown')).toEqual([firstKeyHandler, secondKeyHandler])
  })

  it('an item click closes and fires its action', () => {
    const onSelect = vi.fn()
    render(<DropdownMenu ariaLabel="Message actions" trigger="..." items={items([{ onSelect }])} />)

    fireEvent.click(screen.getByLabelText('Message actions'))
    fireEvent.click(screen.getByText('Mark as read'))

    expect(onSelect).toHaveBeenCalledTimes(1)
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('reflects state through aria-expanded', () => {
    render(<DropdownMenu ariaLabel="Message actions" trigger="..." items={items()} />)

    const trigger = screen.getByLabelText('Message actions')
    expect(trigger).toHaveAttribute('aria-expanded', 'false')

    fireEvent.click(trigger)
    expect(trigger).toHaveAttribute('aria-expanded', 'true')

    fireEvent.click(trigger)
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
  })
})
