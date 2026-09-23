import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ColorSwatches from './ColorSwatches'
import { CALENDAR_COLORS } from './calendarColors'
import { expectNoAxeViolations } from '../../a11y-test'

const NAMES = ['Blue', 'Lavender', 'Emerald', 'Coral', 'Gold', 'Orange',
  'Teal', 'Raspberry', 'Slate', 'Violet', 'Forest', 'Bronze']

function mount(value = CALENDAR_COLORS[0]!) {
  const onPick = vi.fn()
  return { onPick, ...render(<ColorSwatches value={value} onPick={onPick} />) }
}

const swatch = (name: string) => screen.getByRole('button', { name })

describe('ColorSwatches', () => {
  it('is a grid of two rows of six', () => {
    mount()

    const rows = within(screen.getByRole('grid', { name: 'Calendar colour' })).getAllByRole('row')
    expect(rows).toHaveLength(2)
    for (const row of rows) expect(within(row).getAllByRole('gridcell')).toHaveLength(6)
  })

  it('names each colour instead of reading its hex code', () => {
    mount(CALENDAR_COLORS[3])

    expect(screen.getAllByRole('button').map(button => button.getAttribute('aria-label')))
      .toEqual(NAMES)
    expect(swatch('Coral')).toHaveAttribute('aria-pressed', 'true')
    expect(swatch('Blue')).toHaveAttribute('aria-pressed', 'false')
  })

  it('offers one tab stop for the twelve swatches', () => {
    mount()

    expect(screen.getAllByRole('button').map(button => button.getAttribute('tabindex')))
      .toEqual(['0', ...Array<string>(11).fill('-1')])
  })

  // The stop opens where the state is: a dialog reopened on Coral must not offer Blue to Tab.
  it('puts the tab stop on the picked colour', () => {
    mount(CALENDAR_COLORS[3])

    expect(screen.getAllByRole('button').map(button => button.getAttribute('tabindex')))
      .toEqual(['-1', '-1', '-1', '0', ...Array<string>(8).fill('-1')])
  })

  it('walks the grid with the arrow keys', async () => {
    mount()
    swatch('Blue').focus()

    await userEvent.keyboard('{ArrowRight}')
    expect(swatch('Lavender')).toHaveFocus()

    await userEvent.keyboard('{ArrowDown}')
    expect(swatch('Raspberry')).toHaveFocus()

    await userEvent.keyboard('{End}')
    expect(swatch('Bronze')).toHaveFocus()
  })

  it('picks a colour with Enter', async () => {
    const { onPick } = mount()
    swatch('Blue').focus()

    await userEvent.keyboard('{ArrowRight}{Enter}')

    expect(onPick).toHaveBeenCalledWith(CALENDAR_COLORS[1])
  })

  it('carries no accessibility violation', async () => {
    const { container } = mount()

    await expectNoAxeViolations(container)
  })
})
