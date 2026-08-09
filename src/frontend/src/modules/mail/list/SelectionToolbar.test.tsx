import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import SelectionToolbar, { type SelectionToolbarProps } from './SelectionToolbar'

const noop = { onRun: vi.fn() }
function props(over: Partial<SelectionToolbarProps> = {}): SelectionToolbarProps {
  return {
    title: 'Inbox', count: 0, allSelected: false, indeterminate: false, onToggleAll: vi.fn(),
    overCap: false, deleteLabel: 'Delete',
    archive: { ...noop }, junk: { ...noop }, del: { ...noop }, move: { ...noop }, copy: { ...noop },
    markRead: { ...noop }, markUnread: { ...noop }, emptyFolder: { ...noop },
    searchOpen: false, onToggleSearch: vi.fn(),
    starred: false, onToggleStarred: vi.fn(), ...over,
  }
}

describe('SelectionToolbar', () => {
  it('shows the folder title when nothing is selected, the count otherwise', () => {
    const { rerender } = render(<SelectionToolbar {...props()} />)
    expect(screen.getByText('Inbox')).toBeInTheDocument()
    rerender(<SelectionToolbar {...props({ count: 3 })} />)
    expect(screen.getByText('3 selected')).toBeInTheDocument()
  })

  it('greys the direct actions with an empty selection', () => {
    render(<SelectionToolbar {...props({ count: 0 })} />)
    expect(screen.getByRole('button', { name: 'Archive' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Delete' })).toBeDisabled()
  })

  it('enables the direct actions and fires them when a selection exists', () => {
    const archive = { onRun: vi.fn() }
    render(<SelectionToolbar {...props({ count: 2, archive })} />)
    const btn = screen.getByRole('button', { name: 'Archive' })
    expect(btn).toBeEnabled()
    fireEvent.click(btn)
    expect(archive.onRun).toHaveBeenCalledOnce()
  })

  it('labels each direct action with a tooltip when enabled', () => {
    render(<SelectionToolbar {...props({ count: 2, deleteLabel: 'Delete permanently' })} />)
    expect(screen.getByRole('button', { name: 'Archive' })).toHaveAttribute('title', 'Archive')
    expect(screen.getByRole('button', { name: 'Report as junk' })).toHaveAttribute('title', 'Report as junk')
    expect(screen.getByRole('button', { name: 'Delete permanently' })).toHaveAttribute('title', 'Delete permanently')
  })

  it('disables selection actions over the 200 cap with a tooltip', () => {
    render(<SelectionToolbar {...props({ count: 201, overCap: true })} />)
    const btn = screen.getByRole('button', { name: 'Archive' })
    expect(btn).toBeDisabled()
    expect(btn).toHaveAttribute('title', 'Select 200 or fewer')
  })

  it('drives the master checkbox from allSelected/indeterminate', () => {
    const onToggleAll = vi.fn()
    render(<SelectionToolbar {...props({ count: 5, allSelected: true, onToggleAll })} />)
    const master = screen.getByRole('checkbox', { name: 'Select all' })
    expect(master).toBeChecked()
    fireEvent.click(master)
    expect(onToggleAll).toHaveBeenCalledOnce()
  })

  it('keeps Empty folder in the kebab enabled with no selection', () => {
    const emptyFolder = { onRun: vi.fn() }
    render(<SelectionToolbar {...props({ count: 0, emptyFolder })} />)
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    const item = screen.getByRole('menuitem', { name: 'Empty folder' })
    expect(item).toBeEnabled()
    fireEvent.click(item)
    expect(emptyFolder.onRun).toHaveBeenCalledOnce()
  })

  it('greys the selection-bound kebab items with no selection', () => {
    render(<SelectionToolbar {...props({ count: 0 })} />)
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    expect(screen.getByRole('menuitem', { name: 'Mark as read' })).toBeDisabled()
    expect(screen.getByRole('menuitem', { name: 'Copy to…' })).toBeDisabled()
  })

  it('puts Move to… in the kebab, not the direct actions', () => {
    const move = { onRun: vi.fn() }
    render(<SelectionToolbar {...props({ count: 2, move })} />)
    expect(screen.queryByRole('button', { name: 'Move to…' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    const item = screen.getByRole('menuitem', { name: 'Move to…' })
    expect(item).toBeEnabled()
    fireEvent.click(item)
    expect(move.onRun).toHaveBeenCalledOnce()
  })

  it('shows an icon before each kebab item, like the reader header', () => {
    render(<SelectionToolbar {...props({ count: 2 })} />)
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    for (const name of ['Mark as read', 'Mark as unread', 'Move to…', 'Copy to…', 'Empty folder']) {
      expect(screen.getByRole('menuitem', { name }).querySelector('svg')).toBeInTheDocument()
    }
  })

  it('orders the kebab: mark read/unread, then move/copy, then empty', () => {
    render(<SelectionToolbar {...props({ count: 2 })} />)
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    expect(screen.getAllByRole('menuitem').map(i => i.textContent)).toEqual([
      'Mark as read', 'Mark as unread', 'Move to…', 'Copy to…', 'Empty folder',
    ])
  })

  it('sets the checkbox DOM indeterminate property', () => {
    render(<SelectionToolbar {...props({ count: 5, indeterminate: true })} />)
    const master = screen.getByRole('checkbox', { name: 'Select all' }) as HTMLInputElement
    expect(master.indeterminate).toBe(true)
  })

  it('honours disabledReason on a kebab item', () => {
    const copy = { onRun: vi.fn(), disabledReason: 'Some reason' }
    render(<SelectionToolbar {...props({ count: 2, copy })} />)
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    const item = screen.getByRole('menuitem', { name: 'Copy to…' })
    expect(item).toBeDisabled()
    expect(item).toHaveAttribute('title', 'Some reason')
  })

  it('lets the cap message win over disabledReason on a kebab item', () => {
    const copy = { onRun: vi.fn(), disabledReason: 'Some reason' }
    render(<SelectionToolbar {...props({ count: 2, overCap: true, copy })} />)
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    expect(screen.getByRole('menuitem', { name: 'Copy to…' })).toHaveAttribute('title', 'Select 200 or fewer')
  })

  it('toggles the search bar from the magnifier', () => {
    const onToggleSearch = vi.fn()
    render(<SelectionToolbar {...props({ searchOpen: false, onToggleSearch })} />)
    fireEvent.click(screen.getByRole('button', { name: 'Search' }))
    expect(onToggleSearch).toHaveBeenCalled()
  })

  it('marks the magnifier active while the bar is open', () => {
    render(<SelectionToolbar {...props({ searchOpen: true })} />)
    expect(screen.getByRole('button', { name: 'Search' }).className).toContain('is-active')
  })

  // A one-click filter beside the folder name, so it never goes through the kebab. Its label
  // names the action to come, like the reader's colour toggle.
  it('offers the starred filter beside the folder name', () => {
    const onToggleStarred = vi.fn()
    render(<SelectionToolbar {...props({ onToggleStarred })} />)

    const star = screen.getByRole('button', { name: 'Show starred only' })
    expect(star).toHaveAttribute('aria-pressed', 'false')
    fireEvent.click(star)

    expect(onToggleStarred).toHaveBeenCalledOnce()
  })

  it('shows the filter as on, offering the way back', () => {
    render(<SelectionToolbar {...props({ starred: true })} />)

    const star = screen.getByRole('button', { name: 'Show all messages' })
    expect(star).toHaveAttribute('aria-pressed', 'true')
    expect(star.className).toContain('is-on')
  })

  // Turning it on needs a folder to search; turning it off never does.
  it('disables the filter only when it is off and there is no folder', () => {
    render(<SelectionToolbar {...props({ starredDisabled: true })} />)

    expect(screen.getByRole('button', { name: 'Show starred only' })).toBeDisabled()
  })

  it('disables the master checkbox when selection is disabled', () => {
    render(<SelectionToolbar {...props({ selectionDisabled: true })} />)
    expect(screen.getByRole('checkbox', { name: 'Select all' })).toBeDisabled()
  })
})

describe('SelectionToolbar narrow states', () => {
  it('marks itself as selecting exactly when rows are selected', () => {
    const { container, rerender } = render(<SelectionToolbar {...props({ count: 0 })} />)
    expect(container.querySelector('.selection-toolbar')!.className).not.toContain('is-selecting')
    rerender(<SelectionToolbar {...props({ count: 3 })} />)
    expect(container.querySelector('.selection-toolbar')!.className).toContain('is-selecting')
  })

  it('renders whatever the leading slot is handed', () => {
    render(<SelectionToolbar {...props({ count: 0 })} leading={<button type="button">Menu</button>} />)
    expect(screen.getByRole('button', { name: 'Menu' })).toBeTruthy()
  })

  it('offers Refresh in the kebab when the layout supplies one', async () => {
    const onRun = vi.fn()
    render(<SelectionToolbar {...props({ count: 0 })} refresh={{ onRun }} />)
    await userEvent.click(screen.getByRole('button', { name: 'More actions' }))
    await userEvent.click(screen.getByRole('menuitem', { name: 'Refresh' }))
    expect(onRun).toHaveBeenCalled()
  })

  // Nothing to refresh with means no entry: a dead row in the kebab reads as a broken action.
  it('leaves Refresh out when no handler was supplied', () => {
    render(<SelectionToolbar {...props({ count: 0 })} />)
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    expect(screen.queryByRole('menuitem', { name: 'Refresh' })).toBeNull()
  })
})
