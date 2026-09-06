import i18next from 'i18next'
import { afterEach, describe, expect, it } from 'vitest'
import {
  ALL_DAY_PRESETS, convertReminder, DATED_PRESETS, MAX_REMINDERS, reminderLabel,
} from './reminderPresets'

const t = i18next.getFixedT(null, 'calendar')

describe('the preset lists', () => {
  it('offers a dated ladder and an all-day one, and caps how many an event carries', () => {
    expect(DATED_PRESETS).toContain(15)
    expect(ALL_DAY_PRESETS).toContain(900)
    expect(MAX_REMINDERS).toBe(5)
  })
})

describe('reminderLabel', () => {
  afterEach(async () => { await i18next.changeLanguage('en') })

  it('names a dated reminder by the distance it keeps', () => {
    expect(reminderLabel(0, false, t)).toBe('At the time of the event')
    expect(reminderLabel(15, false, t)).toBe('15 minutes before')
    expect(reminderLabel(60, false, t)).toBe('1 hour before')
    expect(reminderLabel(120, false, t)).toBe('2 hours before')
    expect(reminderLabel(1440, false, t)).toBe('1 day before')
    expect(reminderLabel(10080, false, t)).toBe('1 week before')
  })

  // A phone may carry any TRIGGER at all; a value off the ladder still has to read as a sentence.
  it('names a value no preset offers', () => {
    expect(reminderLabel(45, false, t)).toBe('45 minutes before')
    expect(reminderLabel(200, false, t)).toBe('200 minutes before')
  })

  // A whole day has no hour, so an all-day reminder is a day and a time of day, never a distance.
  it('names an all-day reminder by the day and hour it rings on', () => {
    expect(reminderLabel(900, true, t)).toBe('The day before at 09:00')
    expect(reminderLabel(360, true, t)).toBe('The day before at 18:00')
    expect(reminderLabel(2340, true, t)).toBe('2 days before at 09:00')
    expect(reminderLabel(9540, true, t)).toBe('1 week before at 09:00')
    expect(reminderLabel(0, true, t)).toBe('On the day at 00:00')
  })

  it('speaks the interface language', async () => {
    await i18next.changeLanguage('fr')

    expect(reminderLabel(15, false, t)).toBe('15 minutes avant')
  })
})

describe('convertReminder', () => {
  // Switching the all-day toggle must not leave a value the other ladder cannot express: a
  // reminder that does not exist on the target ladder falls back to that ladder's usual one.
  it('falls back to the target ladder when the value has no meaning on it', () => {
    expect(convertReminder(15, true)).toBe(360)
    expect(convertReminder(900, false)).toBe(15)
  })

  it('keeps a value the target ladder already offers', () => {
    expect(convertReminder(0, false)).toBe(0)
    expect(convertReminder(360, true)).toBe(360)
  })
})
