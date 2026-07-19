import { describe, it, expect } from 'vitest'
import { roleLabel } from './roleLabel'

describe('roleLabel', () => {
  it.each([
    ['inbox', 'Inbox'], ['sent', 'Sent'], ['drafts', 'Drafts'],
    ['trash', 'Trash'], ['junk', 'Junk'], ['archive', 'Archive'],
  ])('labels %s as %s', (role, label) => {
    expect(roleLabel(role)).toBe(label)
  })

  it('falls back to the raw value for an unknown role', () => {
    expect(roleLabel('mystery')).toBe('mystery')
  })
})
