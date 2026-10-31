import i18next from 'i18next'
import { afterEach, describe, expect, it } from 'vitest'
import type { RecurrenceWrite } from './calendarTypes'
import { recurrenceSummary } from './recurrenceSummary'

// getFixedT gives a real TFunction<'calendar'> that re-reads the active catalogue on every call,
// so one instance covers both languages here — no cast, no invented string.
const t = i18next.getFixedT(null, 'calendar')

function rule(overrides: Partial<RecurrenceWrite> = {}): RecurrenceWrite {
  return { frequency: 'WEEKLY', interval: 1, byDay: [], end: 'Never', ...overrides }
}

describe('recurrenceSummary', () => {
  afterEach(async () => { await i18next.changeLanguage('en') })

  it('names the days a weekly rule falls on', () => {
    expect(recurrenceSummary(rule({ byDay: ['MO', 'WE'] }), t, 'en', 'fr-BE'))
      .toBe('Every week on Monday and Wednesday')
  })

  it('names an interval in the plural', () => {
    expect(recurrenceSummary(rule({ frequency: 'MONTHLY', interval: 2 }), t, 'en', 'fr-BE'))
      .toBe('Every 2 months')
  })

  it('names a monthly rule taken by position', () => {
    expect(recurrenceSummary(
      rule({ frequency: 'MONTHLY', interval: 2, bySetPos: -1, bySetPosDay: 'FR' }),
      t, 'en', 'fr-BE'))
      .toBe('Every 2 months on the last Friday')
  })

  it('names the five positions a monthly picker can offer', () => {
    for (const [position, word] of [[1, 'first'], [2, 'second'], [3, 'third'], [4, 'fourth'],
      [5, 'fifth'], [-1, 'last']] as const) {
      expect(recurrenceSummary(
        rule({ frequency: 'MONTHLY', bySetPos: position, bySetPosDay: 'FR' }), t, 'en', 'fr-BE'))
        .toBe(`Every month on the ${word} Friday`)
    }
  })

  // The API takes bySetPos from -366 to 366 and gives it back exactly, so a rule a phone wrote
  // arrives here as one the screen believes it can show. Counting beats approximating.
  it('counts a position no word names, with the ordinal the language uses', () => {
    expect(recurrenceSummary(
      rule({ frequency: 'YEARLY', bySetPos: 7, bySetPosDay: 'FR' }), t, 'en', 'fr-BE'))
      .toBe('Every year on the 7th Friday')
    expect(recurrenceSummary(
      rule({ frequency: 'YEARLY', bySetPos: 21, bySetPosDay: 'FR' }), t, 'en', 'fr-BE'))
      .toBe('Every year on the 21st Friday')
  })

  // Reading -2 as "last" states what the rule does not say, and is silent about it.
  it('counts a position back from the end rather than calling it the last', () => {
    expect(recurrenceSummary(
      rule({ frequency: 'MONTHLY', bySetPos: -2, bySetPosDay: 'FR' }), t, 'en', 'fr-BE'))
      .toBe('Every month on the 2nd from the end Friday')
  })

  it('never puts a raw key on the screen, whatever the rule holds', () => {
    for (const position of [-366, -5, 0, 6, 366]) {
      const text = recurrenceSummary(
        rule({ frequency: 'YEARLY', bySetPos: position, bySetPosDay: 'FR' }), t, 'en', 'fr-BE')

      expect(text).not.toContain('repeat.')
      expect(text).toContain('Friday')
    }
  })

  it('names a monthly rule taken by day of the month', () => {
    expect(recurrenceSummary(rule({ frequency: 'MONTHLY', byMonthDay: 12 }), t, 'en', 'fr-BE'))
      .toBe('Every month on day 12')
  })

  it('counts a rule that stops after so many times', () => {
    expect(recurrenceSummary(
      rule({ byDay: ['MO', 'WE'], end: 'Count', count: 10 }), t, 'en', 'fr-BE'))
      .toBe('Every week on Monday and Wednesday, 10 times')
  })

  it('dates a rule that stops on a day', () => {
    expect(recurrenceSummary(
      rule({ frequency: 'DAILY', end: 'Until', until: '2026-12-20' }), t, 'en', 'fr-BE'))
      .toBe('Every day, until 20 December 2026')
  })

  it('reads an until written as an instant', () => {
    expect(recurrenceSummary(
      rule({ frequency: 'DAILY', end: 'Until', until: '2026-12-20T22:59:59Z' }), t, 'en', 'fr-BE'))
      .toContain('20 December 2026')
  })

  it('speaks the interface language', async () => {
    await i18next.changeLanguage('fr')

    expect(recurrenceSummary(rule({ byDay: ['MO', 'WE'] }), t, 'fr', 'fr-BE'))
      .toBe('Toutes les semaines, lundi et mercredi')
  })

  it('counts positions in the interface language too', async () => {
    await i18next.changeLanguage('fr')

    expect(recurrenceSummary(
      rule({ frequency: 'MONTHLY', bySetPos: 5, bySetPosDay: 'FR' }), t, 'fr', 'fr-BE'))
      .toBe('Tous les mois, le cinquième vendredi')
    expect(recurrenceSummary(
      rule({ frequency: 'YEARLY', bySetPos: -2, bySetPosDay: 'FR' }), t, 'fr', 'fr-BE'))
      .toBe('Tous les ans, le 2e en partant de la fin vendredi')
  })
})
