import { describe, it, expect } from 'vitest'
import { buildDragPill } from './dragImage'

describe('buildDragPill', () => {
  it('is a pill carrying the count and an envelope', () => {
    const pill = buildDragPill(3)

    expect(pill).toHaveClass('drag-pill')
    expect(pill.querySelector('svg')).not.toBeNull()
    expect(pill.querySelector('.drag-pill-count')?.textContent).toBe('3')
  })
})
