import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import DropdownMenu, { type MenuItem, type MenuEntry } from './DropdownMenu'

function items(overrides?: Partial<{ onSelect: () => void; disabled: boolean; title: string }>[]): MenuItem[] {
  return [
    { label: 'Mark as read', onSelect: vi.fn(), ...(overrides?.[0] ?? {}) },
    { label: 'Star', onSelect: vi.fn(), ...(overrides?.[1] ?? {}) },
  ]
}

function open() {
  fireEvent.click(screen.getByRole('button', { name: 'Menu' }))
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

  it('renders a separator between groups', () => {
    const entries: MenuEntry[] = [
      { label: 'Mark as read', onSelect: vi.fn() },
      'separator',
      { label: 'Archive', onSelect: vi.fn() },
    ]
    render(<DropdownMenu ariaLabel="Message actions" trigger="..." items={entries} />)

    fireEvent.click(screen.getByLabelText('Message actions'))

    const menu = screen.getByRole('menu')
    const order = Array.from(menu.children).map(el => (el.tagName === 'HR' ? 'separator' : el.textContent))
    expect(order).toEqual(['Mark as read', 'separator', 'Archive'])
  })

  it('shows a disabled item as a disabled button with its title', () => {
    const entries: MenuEntry[] = [
      { label: 'Move', onSelect: vi.fn(), disabled: true, title: 'No archive folder configured' },
    ]
    render(<DropdownMenu ariaLabel="Message actions" trigger="..." items={entries} />)

    fireEvent.click(screen.getByLabelText('Message actions'))

    const button = screen.getByRole('menuitem', { name: 'Move' })
    expect(button).toBeDisabled()
    expect(button).toHaveAttribute('title', 'No archive folder configured')
  })

  it('does not fire onSelect or close the menu when a disabled item is clicked', () => {
    const onSelect = vi.fn()
    const entries: MenuEntry[] = [
      { label: 'Move', onSelect, disabled: true, title: 'No archive folder configured' },
    ]
    render(<DropdownMenu ariaLabel="Message actions" trigger="..." items={entries} />)

    fireEvent.click(screen.getByLabelText('Message actions'))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Move' }))

    expect(onSelect).not.toHaveBeenCalled()
    expect(screen.getByRole('menu')).toBeInTheDocument()
  })

  describe('direction="up"', () => {
    // Inside a scroll container (the reader's attachment band), an absolutely-positioned
    // upward menu lands past the band's own block-start edge — the one edge a scroll
    // container can never reveal. Fixed positioning, measured off the trigger, escapes it.
    it('positions the menu fixed off the trigger, clear of any scroll-container clip', () => {
      const rectSpy = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect')
        .mockReturnValue({ top: 500, right: 620, bottom: 520, left: 580, width: 40, height: 20 } as DOMRect)
      vi.stubGlobal('innerWidth', 1000)
      vi.stubGlobal('innerHeight', 800)

      render(<DropdownMenu ariaLabel="More actions" trigger="..." items={items()} direction="up" />)
      fireEvent.click(screen.getByLabelText('More actions'))

      const menu = screen.getByRole('menu')
      expect(menu).toHaveStyle({ position: 'fixed', bottom: '304px', right: '380px' })

      rectSpy.mockRestore()
      vi.unstubAllGlobals()
    })

    // A fixed menu does not track the trigger the way an absolutely-positioned one does, so it
    // must not be left floating over the wrong spot after the page moves under it.
    it('closes on a window scroll while open', () => {
      render(<DropdownMenu ariaLabel="More actions" trigger="..." items={items()} direction="up" />)
      fireEvent.click(screen.getByLabelText('More actions'))
      expect(screen.getByRole('menu')).toBeInTheDocument()

      fireEvent.scroll(window)

      expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    })
  })

  /* jsdom lays nothing out, so every number here is stubbed — the real geometry was measured in
     the browser on the contact editor: a 106px "add a field" link at y 522 of a 763px viewport,
     whose 290px menu ran to 838, seventy-five pixels under the fold. */
  describe('direction="auto"', () => {
    function place(triggerTop: number, viewport: number, menuHeight: number) {
      const rectSpy = vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect')
        .mockReturnValue({
          top: triggerTop, right: 1026, bottom: triggerTop + 22, left: 920, width: 106, height: 22,
        } as DOMRect)
      const heightSpy = vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get')
        .mockReturnValue(menuHeight)
      vi.stubGlobal('innerWidth', 1479)
      vi.stubGlobal('innerHeight', viewport)
      return () => { rectSpy.mockRestore(); heightSpy.mockRestore(); vi.unstubAllGlobals() }
    }

    it('opens upward, aligned on the trigger, when the menu would run past the fold', () => {
      const restore = place(522, 763, 290)

      render(<DropdownMenu ariaLabel="Add a field" trigger="+" items={items()}
        direction="auto" align="left" />)
      fireEvent.click(screen.getByLabelText('Add a field'))

      expect(screen.getByRole('menu'))
        .toHaveStyle({ position: 'fixed', bottom: '245px', left: '920px' })
      restore()
    })

    it('leaves the menu below, unpositioned, when there is room for it', () => {
      const restore = place(200, 900, 290)

      render(<DropdownMenu ariaLabel="Add a field" trigger="+" items={items()}
        direction="auto" align="left" />)
      fireEvent.click(screen.getByLabelText('Add a field'))

      expect(screen.getByRole('menu')).not.toHaveAttribute('style')
      restore()
    })

    /* Flipping a menu that fits neither way trades a clipped bottom, which a scroll can still
       reach, for a clipped top, which nothing can. */
    it('stays below when the menu fits on neither side', () => {
      const restore = place(300, 500, 900)

      render(<DropdownMenu ariaLabel="Add a field" trigger="+" items={items()}
        direction="auto" align="left" />)
      fireEvent.click(screen.getByLabelText('Add a field'))

      expect(screen.getByRole('menu')).not.toHaveAttribute('style')
      restore()
    })
  })

  // Regression pin: the default ('down') path must render exactly as it did before direction
  // existed — no inline position style, no scroll-close wiring.
  it('renders the down menu with no inline positioning, unaffected by scroll', () => {
    render(<DropdownMenu ariaLabel="Message actions" trigger="..." items={items()} />)
    fireEvent.click(screen.getByLabelText('Message actions'))

    const menu = screen.getByRole('menu')
    expect(menu).not.toHaveAttribute('style')

    fireEvent.scroll(window)
    expect(screen.getByRole('menu')).toBeInTheDocument()
  })

  it('renders an item carrying href as a link opening in a new tab', () => {
    render(<DropdownMenu ariaLabel="Menu" trigger="⋮"
      items={[{ label: 'View source', href: '/mail/source?folder=INBOX&uid=42' }]} />)
    open()

    const link = screen.getByRole('menuitem', { name: 'View source' })
    expect(link.tagName).toBe('A')
    expect(link).toHaveAttribute('href', '/mail/source?folder=INBOX&uid=42')
    expect(link).toHaveAttribute('target', '_blank')
    // Without it the opened tab gets a handle on window.opener.
    expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'))
  })

  it('closes the menu when a link item is activated', () => {
    render(<DropdownMenu ariaLabel="Menu" trigger="⋮"
      items={[{ label: 'View source', href: '/mail/source' }]} />)
    open()

    fireEvent.click(screen.getByRole('menuitem', { name: 'View source' }))

    expect(screen.queryByRole('menuitem')).not.toBeInTheDocument()
  })

  it('closes the menu when a link item is middle-clicked', () => {
    render(<DropdownMenu ariaLabel="Menu" trigger="⋮"
      items={[{ label: 'View source', href: '/mail/source' }]} />)
    open()

    // testing-library's fireEvent has no auxClick alias (unlike click); dispatch the native
    // event directly, the same one a real middle-click raises instead of onClick.
    fireEvent(screen.getByRole('menuitem', { name: 'View source' }),
      new MouseEvent('auxclick', { bubbles: true, cancelable: true, button: 1 }))

    expect(screen.queryByRole('menuitem')).not.toBeInTheDocument()
  })

  it('still renders an item carrying onSelect as a button', () => {
    const onSelect = vi.fn()
    render(<DropdownMenu ariaLabel="Menu" trigger="⋮" items={[{ label: 'Archive', onSelect }]} />)
    open()

    const item = screen.getByRole('menuitem', { name: 'Archive' })
    expect(item.tagName).toBe('BUTTON')

    fireEvent.click(item)
    expect(onSelect).toHaveBeenCalledOnce()
  })
})
