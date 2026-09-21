import { render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import ColourGrid from './ColourGrid'

const ROWS = [['#000000', '#f8e71c'], ['#417505', '#4a90d9']] as const
const NAMES: Record<string, string> = {
  '#000000': 'Black', '#f8e71c': 'Yellow', '#417505': 'Olive', '#4a90d9': 'Blue',
}

const mount = (value: string) => render(
  <ColourGrid className="test-swatches" label="Colour" rows={ROWS} value={value}
    nameOf={colour => NAMES[colour]} onPick={vi.fn()} />)

const pressed = () => screen.getAllByRole('button')
  .filter(button => button.getAttribute('aria-pressed') === 'true')
  .map(button => button.getAttribute('aria-label'))

/** The two callers spell a colour their own way — the calendar's stored hex, the editor's report
    of what the document says — so where the applied colour is read is the one place they could
    silently drift apart, and it is this file's whole subject. The keys, the arrows and the one tab
    stop are exercised through each caller, in ColorSwatches.test.tsx and EditorToolbar.test.tsx. */
describe('ColourGrid', () => {
  it('marks the applied colour however the surface spells it', () => {
    mount(' #F8E71C ')

    expect(pressed()).toEqual(['Yellow'])
  })

  it('presses nothing when the value names no colour of the grid', () => {
    mount('currentColor')

    expect(pressed()).toEqual([])
  })

  it('draws the rows it is handed, one cell per colour', () => {
    mount('#000000')

    const rows = within(screen.getByRole('grid', { name: 'Colour' })).getAllByRole('row')
    expect(rows).toHaveLength(2)
    for (const row of rows) expect(within(row).getAllByRole('gridcell')).toHaveLength(2)
  })
})
