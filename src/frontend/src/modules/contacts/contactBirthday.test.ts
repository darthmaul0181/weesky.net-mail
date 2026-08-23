import { describe, expect, it } from 'vitest'
import { birthdayToInput, formatBirthday, inputToBirthday } from './contactBirthday'

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

describe('birthdayToInput', () => {
  /* The defect: a card exported by a phone reads as its own timestamp in the field, beside a
     placeholder inviting 27/10/1979, while the fiche next door showed 21 juin 1993 all along. */
  it('reads the date half of the date-time a phone exports', () => {
    expect(birthdayToInput('19930621T115900Z')).toBe('21/06/1993')
  })

  it('reads both vCard date spellings', () => {
    expect(birthdayToInput('19930621')).toBe('21/06/1993')
    expect(birthdayToInput('1993-06-21')).toBe('21/06/1993')
  })

  it('drops no month or day from a year-less birthday', () => {
    expect(birthdayToInput('--0315')).toBe('15/03')
    expect(birthdayToInput('--03-15')).toBe('15/03')
  })

  /* formatBirthday's rule: a birthday that vanishes reads as data lost. */
  it('shows verbatim what it cannot read', () => {
    expect(birthdayToInput('printemps 1993')).toBe('printemps 1993')
  })

  it('is empty for a card carrying none', () => {
    expect(birthdayToInput(null)).toBe('')
    expect(birthdayToInput('  ')).toBe('')
  })
})

describe('inputToBirthday', () => {
  /* The backend bounds this string's length and nothing else, so what leaves here lands after
     BDAY: — the placeholder's own shape used to be written verbatim and no reader could parse it. */
  it('stores a typed date as the vCard spelling', () => {
    expect(inputToBirthday('27/10/1979')).toBe('1979-10-27')
    expect(inputToBirthday('3/4/1990')).toBe('1990-04-03')
  })

  it('takes the separators a keyboard offers', () => {
    expect(inputToBirthday('27-10-1979')).toBe('1979-10-27')
    expect(inputToBirthday('27.10.1979')).toBe('1979-10-27')
  })

  it('stores a year-less birthday as vCard writes one', () => {
    expect(inputToBirthday('15/03')).toBe('--0315')
    expect(inputToBirthday('29/02')).toBe('--0229')
  })

  /* Décision 7's escape hatch: the vCard admits forms neither this nor a picker can express, and
     refusing them would make a card carrying one permanently unsaveable. */
  it('passes through what it cannot read', () => {
    expect(inputToBirthday('printemps 1993')).toBe('printemps 1993')
    expect(inputToBirthday('1993-06-21')).toBe('1993-06-21')
  })

  it('refuses to invent a day that does not exist', () => {
    expect(inputToBirthday('31/02/1993')).toBe('31/02/1993')
  })

  it('is empty for an emptied field', () => {
    expect(inputToBirthday('   ')).toBe('')
  })
})

/* The pair has to survive the trip, or an edit to another field rewrites the birthday. */
describe('the round trip', () => {
  it.each(['1993-06-21', '--0315'])('leaves %s unchanged', stored => {
    expect(inputToBirthday(birthdayToInput(stored))).toBe(stored)
  })

  /* The one form that does not round-trip, and deliberately: the time is not a birthday. An
     untouched field is never rewritten at all — ContactEditView.test asserts that separately. */
  it('drops the time a phone exported once the field is edited', () => {
    expect(inputToBirthday(birthdayToInput('19930621T115900Z'))).toBe('1993-06-21')
  })
})
