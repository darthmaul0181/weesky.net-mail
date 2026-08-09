import { describe, expect, it } from 'vitest'
import { effectivePane } from './effectivePane'

describe('effectivePane', () => {
  it('forces one pane at a time on a phone', () => {
    expect(effectivePane('right', 'phone')).toBe('none')
    expect(effectivePane('bottom', 'phone')).toBe('none')
    expect(effectivePane('none', 'phone')).toBe('none')
  })

  // 240 + 320 minimums is 560px, against the 584px a 640px tablet leaves beside the 56px rail.
  // Overriding an explicit choice on a 900px tablet would be arbitrary.
  it('keeps the stored preference on a tablet', () => {
    expect(effectivePane('right', 'tablet')).toBe('right')
    expect(effectivePane('bottom', 'tablet')).toBe('bottom')
  })

  it('keeps the stored preference on a desktop', () => {
    expect(effectivePane('bottom', 'desktop')).toBe('bottom')
  })
})
