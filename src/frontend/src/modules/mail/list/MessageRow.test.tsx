import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent, within } from '@testing-library/react'
import MessageRow, { rowUidsOf } from './MessageRow'
import type { RowCallbacks } from './MessageRow'
import type { MailMessageSummary } from '../api/mailTypes'
import type { AvatarColor } from '../../../hooks/usePreferences'
import { avatarHue } from './avatar/senderAvatar'
import { useSenderAvatarUrl } from './avatar/useSenderAvatarUrl'

vi.mock('./avatar/useSenderAvatarUrl', () => ({ useSenderAvatarUrl: vi.fn(() => null) }))

/**
 * The memo contract, which only makes sense at this level: what the list must hand a row for one
 * ticked checkbox to redraw one row instead of the whole folder. Everything the row *draws* is
 * asserted through `MessageList.test.tsx`, where the rows are drawn the way they ship.
 */

const message = (over: Partial<MailMessageSummary> = {}): MailMessageSummary => ({
  uid: 2, subject: 'Re: facture', fromName: 'Alice Martin', fromAddress: 'alice@x.be',
  to: [], date: '2026-07-18T09:00:00Z', seen: false, flagged: false, answered: false,
  hasAttachments: false, size: 100, preview: '', ...over,
} as MailMessageSummary)

function callbacks(): RowCallbacks {
  return {
    open: vi.fn(), check: vi.fn(), setFlag: vi.fn(), archive: vi.fn(), junk: vi.fn(),
    remove: vi.fn(), toggleThread: vi.fn(), dragStart: vi.fn(), dragEnd: vi.fn(),
    swipe: vi.fn(), selecting: vi.fn(() => false),
  }
}

/** A getter on a field only the row reads, so the count is the real component rendering — a
    wrapper of the test's own would be counting its own memo instead of this one's. */
function counted(over: Partial<MailMessageSummary> = {}) {
  const one = message(over)
  const seen = { renders: 0 }
  const { subject } = one
  Object.defineProperty(one, 'subject', { get() { seen.renders += 1; return subject } })
  return { message: one, seen }
}

function props(over: Partial<Parameters<typeof MessageRow>[0]> = {}) {
  return {
    message: message(), groupKey: 2, expanded: false, member: false, rowIndex: 0, ariaRow: 1,
    today: '2026-07-18', wide: false, drafts: false, crossFolder: false, showsPreview: true,
    rowActions: ['seen', 'archive', 'delete'] as const, checked: false, open: false,
    leaving: false, dragging: false, archiveOff: false, archiveReason: 'no archive',
    junkOff: false, junkReason: 'no junk', trashOff: false, trashReason: 'no trash',
    deleteLabel: 'Delete', swipeRight: 'seen' as const, swipeLeft: 'delete' as const,
    avatar: null as AvatarColor | null, on: callbacks(), ...over,
  }
}

describe('MessageRow', () => {
  it('draws nothing again when the list re-renders with the same props', () => {
    const { message: watched, seen } = counted()
    const base = props({ message: watched })
    const { rerender } = render(<MessageRow {...base} />)
    expect(seen.renders).toBe(1)

    rerender(<MessageRow {...base} />)

    expect(seen.renders).toBe(1)
  })

  it('draws again when its own state changes', () => {
    const { message: watched, seen } = counted()
    const base = props({ message: watched })
    const { rerender } = render(<MessageRow {...base} />)

    rerender(<MessageRow {...base} checked />)

    expect(seen.renders).toBe(2)
    expect(screen.getByRole('checkbox')).toBeChecked()
  })

  /* Why `useSelection` is never handed to a row: it answers a fresh object with fresh closures
     every render, and a row holding one re-renders whenever anything does. */
  it('draws again when it is handed a callback object built afresh', () => {
    const { message: watched, seen } = counted()
    const base = props({ message: watched })
    const { rerender } = render(<MessageRow {...base} />)

    rerender(<MessageRow {...base} on={callbacks()} />)

    expect(seen.renders).toBe(2)
  })

  // The aggregation that moved out of `MessageList`'s `renderRow` with the markup.
  it('a conversation head answers for its members', () => {
    const members = [message({ uid: 30, seen: true }), message({ uid: 10, seen: false })]
    const on = callbacks()
    render(<MessageRow {...props({ members, groupKey: 10, on })} />)

    expect(document.querySelector('.message-row-unread-dot')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('checkbox'))

    expect(on.check).toHaveBeenCalledWith([30, 10], 0, { was: false, whole: true, shift: false })
  })

  it('a plain row acts on its own uid alone', () => {
    const on = callbacks()
    render(<MessageRow {...props({ on })} />)

    fireEvent.click(within(screen.getByRole('row')).getByRole('button', { name: 'Star' }))

    expect(on.setFlag).toHaveBeenCalledWith([2], 'flagged', true)
  })

  it('rowUidsOf is the whole fold, or the one message', () => {
    const one = message()
    expect(rowUidsOf(one)).toEqual([2])
    expect(rowUidsOf(one, [message({ uid: 30 }), message({ uid: 10 })])).toEqual([30, 10])
  })

  describe('the sender avatar', () => {
    const avatar = () => document.querySelector<HTMLElement>('.message-row-avatar')

    it('draws none while the setting is off', () => {
      render(<MessageRow {...props({ avatar: null })} />)
      expect(avatar()).toBeNull()
    })

    it.each([false, true])('draws the sender’s initials first in the content cell (wide: %s)', wide => {
      render(<MessageRow {...props({ avatar: 'mono', wide })} />)
      const content = document.querySelector('.message-row-content')!
      expect(content.firstElementChild).toBe(avatar())
      expect(avatar()).toHaveTextContent('AM')
      expect(avatar()).toHaveClass('is-mono')
      expect(screen.getByRole('row')).toHaveClass('has-avatar')
    })

    it('shows the first recipient in a drafts folder', () => {
      const draft = message({ to: [{ name: 'Bob Durand', address: 'bob@x.be' }, { name: 'Zoé', address: 'z@x.be' }] })
      render(<MessageRow {...props({ message: draft, drafts: true, avatar: 'sender' })} />)
      expect(avatar()).toHaveTextContent('BD')
      expect(avatar()!.style.getPropertyValue('--avatar-h')).toBe(String(avatarHue('bob@x.be')))
    })

    it('hands the row’s BIMI hint to the avatar, and none in a drafts folder', () => {
      const bimi = { bimiDomain: 'brand.com', bimiSelector: 'sel' }
      render(<MessageRow {...props({ message: message(bimi), avatar: 'mono' })} />)
      expect(useSenderAvatarUrl).toHaveBeenLastCalledWith('alice@x.be', false, { domain: 'brand.com', selector: 'sel' })
      const draft = message({ ...bimi, to: [{ name: 'Bob', address: 'bob@x.be' }] })
      render(<MessageRow {...props({ message: draft, drafts: true, avatar: 'mono' })} />)
      expect(useSenderAvatarUrl).toHaveBeenLastCalledWith('bob@x.be', false, undefined)
    })

    it('a collapsed conversation head shows its own sender', () => {
      const head = message({ uid: 30, fromName: 'Carla Rossi', fromAddress: 'carla@x.be' })
      const members = [head, message({ uid: 10 })]
      render(<MessageRow {...props({ message: head, members, groupKey: 10, avatar: 'mono' })} />)
      expect(avatar()).toHaveTextContent('CR')
    })

    it.each([false, true])('keeps four cells and the same name (wide: %s)', wide => {
      const { unmount } = render(<MessageRow {...props({ wide })} />)
      const label = screen.getByRole('gridcell', { name: /facture/ }).getAttribute('aria-label')
      unmount()

      render(<MessageRow {...props({ wide, avatar: 'sender' })} />)

      expect(within(screen.getByRole('row')).getAllByRole('gridcell')).toHaveLength(4)
      expect(screen.getByRole('gridcell', { name: /facture/ }).getAttribute('aria-label')).toBe(label)
    })
  })
})
