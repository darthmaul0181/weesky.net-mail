import { describe, expect, it } from 'vitest'
import { calendarHeaderOf } from './icsHeader'

/** The head of a real iCloud export, byte for byte, CRLF included. */
const APPLE = [
  'BEGIN:VCALENDAR',
  'VERSION:2.0',
  'PRODID:-//caldav.icloud.com//CALDAVJ 2514B607//EN',
  'X-WR-CALNAME:Home',
  'X-APPLE-CALENDAR-COLOR:#ff2d55',
  'BEGIN:VEVENT',
  'SUMMARY:New Event',
  'END:VEVENT',
  'END:VCALENDAR',
].join('\r\n')

describe('calendarHeaderOf', () => {
  it('reads the name and the colour an export carries', () => {
    expect(calendarHeaderOf(APPLE)).toEqual({ name: 'Home', color: '#ff2d55' })
  })

  it('reads the RFC 7986 spellings too', () => {
    const text = 'BEGIN:VCALENDAR\r\nNAME:Work\r\nCOLOR:#3b82c4\r\nBEGIN:VEVENT\r\n'

    expect(calendarHeaderOf(text)).toEqual({ name: 'Work', color: '#3b82c4' })
  })

  // A property longer than 75 octets is folded onto a continuation line whose leading whitespace
  // is the fold marker, not part of the value.
  it('unfolds a continued line', () => {
    expect(calendarHeaderOf('BEGIN:VCALENDAR\r\nNAME:Long\r\n  name\r\n').name).toBe('Long name')
  })

  it('drops the alpha channel Apple writes on a colour', () => {
    expect(calendarHeaderOf('X-APPLE-CALENDAR-COLOR:#FF2D55FF\r\n').color).toBe('#FF2D55')
  })

  it('reads a property that carries parameters', () => {
    expect(calendarHeaderOf('X-WR-CALNAME;VALUE=TEXT:Home\r\n').name).toBe('Home')
  })

  it('unescapes the text a name is written in', () => {
    expect(calendarHeaderOf('NAME:Work\\, home and away\r\n').name).toBe('Work, home and away')
  })

  it('answers nothing when the file names neither', () => {
    expect(calendarHeaderOf('BEGIN:VCALENDAR\r\nVERSION:2.0\r\nEND:VCALENDAR\r\n')).toEqual({})
  })

  it('ignores a colour that is not one', () => {
    expect(calendarHeaderOf('COLOR:cornflowerblue\r\n').color).toBeUndefined()
  })

  // A calendar export is tens of megabytes and this runs on the pick, in the dialog: nothing past
  // the first component is ever read.
  it('stops at the first event rather than walking the whole file', () => {
    const text = `BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nEND:VEVENT\r\nNAME:Too late\r\n`

    expect(calendarHeaderOf(text)).toEqual({})
  })
})
