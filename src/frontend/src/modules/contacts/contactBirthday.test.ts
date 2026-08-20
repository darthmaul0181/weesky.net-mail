import { describe, expect, it } from 'vitest'
import { formatBirthday } from './contactBirthday'

describe('formatBirthday', () => {
  it('reads the date-time form a phone exports', () => {
    expect(formatBirthday('19930621T115900Z', 'fr')).toBe('21 juin 1993')
  })

  it('reads the basic and extended date forms', () => {
    expect(formatBirthday('19930621', 'fr')).toBe('21 juin 1993')
    expect(formatBirthday('1993-06-21', 'fr')).toBe('21 juin 1993')
  })

  // vCard allows a birthday with no year, and it is the common case for a contact who never
  // volunteered one. Formatting it as 1900 would state something the card does not say.
  it('reads a year-less birthday without inventing a year', () => {
    expect(formatBirthday('--0315', 'fr')).toBe('15 mars')
    expect(formatBirthday('--03-15', 'fr')).toBe('15 mars')
  })

  it('follows the interface language', () => {
    expect(formatBirthday('1993-06-21', 'en')).toBe('June 21, 1993')
  })

  // Anything the parser cannot read is shown as the card wrote it: a birthday that disappears
  // reads as data lost, where an odd-looking one reads as a card to fix.
  it('falls back to the raw value it cannot parse', () => {
    expect(formatBirthday('circa 1993', 'fr')).toBe('circa 1993')
  })

  it('has nothing to show for a blank value', () => {
    expect(formatBirthday(null, 'fr')).toBeNull()
    expect(formatBirthday('  ', 'fr')).toBeNull()
  })
})
