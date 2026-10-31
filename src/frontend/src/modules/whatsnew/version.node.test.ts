import { describe, expect, it } from 'vitest'
import { compareVersions } from './version'

describe('compareVersions', () => {
  it.each([
    ['2.4.0', '2.3.9', 1], ['2.10.0', '2.9.0', 1], ['2.4.0', '2.4.0', 0],
    ['2.4.0-dev', '2.4.0', 0], ['0.0.0', '2.4.0', -1], ['3.0.0', '2.99.99', 1],
  ])('%s against %s', (a, b, sign) => {
    expect(Math.sign(compareVersions(a, b))).toBe(sign)
  })
})
