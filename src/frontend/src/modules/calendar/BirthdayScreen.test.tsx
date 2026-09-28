import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { fireEscape } from '../../test-utils'
import BirthdayScreen from './BirthdayScreen'
import { calendarOf, occurrenceOf, renderInCalendar } from './calendarTestHarness'

const ALICE = occurrenceOf({
  eventId: 'b1', calendarId: 'z', summary: '🎂 Alice Martin', isAllDay: true, contactId: 'k',
  birthYear: 1986, startDate: '2026-09-30', endDateExclusive: '2026-10-01',
  transparency: 'TRANSPARENT', hasAlarm: true,
})
const BIRTHDAYS = calendarOf('z', '#be185d', 'Birthdays', {
  kind: 'birthdays', birthdayReminder: 'day_before',
})

function show() {
  const onClose = vi.fn()
  const onOpenContact = vi.fn()
  renderInCalendar(<BirthdayScreen occurrence={ALICE} calendar={BIRTHDAYS} onClose={onClose}
    onOpenContact={onOpenContact} />, { calendars: [BIRTHDAYS] })
  return { onClose, onOpenContact }
}

describe('BirthdayScreen', () => {
  it('is a whole-screen dialog headed Birthday, with the bubble’s lines', () => {
    show()
    const screenDialog = screen.getByRole('dialog', { name: 'Birthday' })
    expect(screenDialog).toHaveAttribute('aria-modal', 'true')
    expect(screenDialog).toHaveTextContent('🎂 Alice Martin')
    expect(screenDialog).toHaveTextContent(/Wednesday.*30.*September/)
    expect(screen.getByText('40 years old')).toBeInTheDocument()
    expect(screenDialog).toHaveTextContent('Reminder the day before at 9:00')
    expect(screenDialog).toHaveTextContent('Every year')
    expect(screenDialog).toHaveTextContent('Birthdays')
  })

  it('opens on its ✕, which closes it, as Escape does', async () => {
    const { onClose } = show()
    const close = screen.getByRole('button', { name: 'Close' })
    expect(close).toHaveFocus()
    await userEvent.click(close)
    expect(onClose).toHaveBeenCalledTimes(1)
    fireEscape()
    expect(onClose).toHaveBeenCalledTimes(2)
  })

  it('hands Open card on', async () => {
    const { onOpenContact } = show()
    await userEvent.click(screen.getByRole('button', { name: 'Open card' }))
    expect(onOpenContact).toHaveBeenCalled()
  })
})
