import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import i18next from 'i18next'
import { describe, expect, it, vi } from 'vitest'
import type { EditScope } from './calendarTypes'
import ScopeModal, { scopeSentence } from './ScopeModal'

/** The very function the layout calls, so the test reads what a user reads rather than a
    hand-written twin that cannot go stale with it. */
const t = i18next.getFixedT(null, 'calendar')

function draw(allowed: EditScope[], onPick = vi.fn(), onClose = vi.fn()) {
  render(<ScopeModal title="Save a recurring event"
    sentence={scopeSentence('save', 'Dentist', 'Every 6 months', t)}
    allowed={allowed} onPick={onPick} onClose={onClose} />)
  return { onPick, onClose }
}

const ALL: EditScope[] = ['This', 'ThisAndFollowing', 'All']

describe('ScopeModal', () => {
  it('asks the question and offers the three scopes', () => {
    draw(ALL)
    expect(screen.getByText('Save a recurring event')).toBeInTheDocument()
    expect(screen.getByText(
      '“Dentist” repeats: Every 6 months. Which occurrences should take the change?'))
      .toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'This occurrence only' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'This and following occurrences' })).toBeEnabled()
    expect(screen.getByRole('button', { name: 'All occurrences' })).toBeEnabled()
  })

  // Moving an event to another calendar moves the whole file: a narrow scope cannot be asked for
  // at the same time, and a button that vanished would read as a rendering fault.
  it('greys the scopes the change cannot take, and says why', () => {
    draw(['All'])
    const narrow = screen.getByRole('button', { name: 'This occurrence only' })
    expect(narrow).toBeDisabled()
    expect(narrow).toHaveAttribute('title', 'Not available for this change')
    expect(screen.getByRole('button', { name: 'All occurrences' })).toBeEnabled()
  })

  it('hands the picked scope on', async () => {
    const { onPick } = draw(ALL)
    await userEvent.click(screen.getByRole('button', { name: 'This and following occurrences' }))
    expect(onPick).toHaveBeenCalledWith('ThisAndFollowing')
  })

  // The two halves are chosen together: glueing a question that carries its own preamble onto a
  // lead that already named the series said "repeats" twice, in both languages.
  it('says the event repeats exactly once', () => {
    const named = scopeSentence('save', 'Dentist', 'Every 6 months', t)
    expect(named.match(/repeats/g)).toHaveLength(1)
    expect(scopeSentence('delete', 'Dentist', 'Every 6 months', t))
      .toBe('“Dentist” repeats: Every 6 months. Which occurrences should be deleted?')
  })

  // Nothing worded the rule, so the question carries its own preamble instead.
  it('falls back to the plain question when the rule has no words', () => {
    expect(scopeSentence('save', 'Dentist', null, t))
      .toBe('This event repeats. What should the change apply to?')
  })

  it('closes on the ✕ alone', async () => {
    const { onClose } = draw(ALL)
    await userEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalled()
  })
})
