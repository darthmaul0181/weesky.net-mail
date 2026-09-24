import { describe, it, expect } from 'vitest'
import { isValidAddress } from './emailAddress'

describe('isValidAddress', () => {
  it.each(['a@b.co', 'first.last@sub.domain.org'])('accepts %s', v => expect(isValidAddress(v)).toBe(true))
  it.each(['nope', 'a@b', 'a b@c.d', '@x.y'])('refuses %s', v => expect(isValidAddress(v)).toBe(false))
})
