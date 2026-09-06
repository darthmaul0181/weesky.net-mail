import { describe, expect, it } from 'vitest'
import { utcOfLocalMidnight } from './plainDate'
import { windowOf } from './windowOf'

const ISO = { firstDay: 1, minimalDays: 4 } as const
const TZ = 'Europe/Brussels'

describe('windowOf', () => {
  it('gives a week its seven visible days and a day of slack either side', () => {
    const window = windowOf('week', '2026-09-16', TZ, ISO)

    expect(window.firstVisible).toBe('2026-09-14')
    expect(window.lastVisible).toBe('2026-09-20')
    expect(window.from).toBe('2026-09-12T22:00:00.000Z')
    expect(window.to).toBe('2026-09-21T22:00:00.000Z')
  })

  it('gives a day itself, with the same slack', () => {
    const window = windowOf('day', '2026-09-14', TZ, ISO)

    expect(window.firstVisible).toBe('2026-09-14')
    expect(window.lastVisible).toBe('2026-09-14')
    expect(window.from).toBe('2026-09-12T22:00:00.000Z')
    expect(window.to).toBe('2026-09-15T22:00:00.000Z')
  })

  it('gives a month the six rows the grid draws', () => {
    const window = windowOf('month', '2026-09-16', TZ, ISO)

    expect(window.firstVisible).toBe('2026-08-31')
    expect(window.lastVisible).toBe('2026-10-11')
    expect(window.from).toBe('2026-08-29T22:00:00.000Z')
    expect(window.to).toBe('2026-10-12T22:00:00.000Z')
  })

  // The anchor rather than today: the chevrons step it by thirty days and the mini-month moves
  // it, and a list reading the clock instead would leave all three controls dead.
  it('gives the list the anchored day and the thirty after it', () => {
    const window = windowOf('list', '2026-09-16', TZ, ISO)

    expect(window.firstVisible).toBe('2026-09-16')
    expect(window.lastVisible).toBe('2026-10-16')
    expect(window.from).toBe(utcOfLocalMidnight('2026-09-16', TZ).toISOString())
    expect(window.to).toBe(utcOfLocalMidnight('2026-10-17', TZ).toISOString())
  })

  it('moves the list with its anchor', () => {
    const window = windowOf('list', '2026-10-16', TZ, ISO)

    expect(window.firstVisible).toBe('2026-10-16')
    expect(window.lastVisible).toBe('2026-11-15')
  })

  // The clocks go back on 25 October 2026: the offset is +02:00 on the day before and +01:00 on
  // the day after, and a window computed from one offset would drift an hour.
  it('reads each edge offset on its own date', () => {
    const window = windowOf('day', '2026-10-25', TZ, ISO)

    expect(window.from).toBe('2026-10-23T22:00:00.000Z')
    expect(window.to).toBe('2026-10-26T23:00:00.000Z')
  })

  it('follows the rules first day', () => {
    expect(windowOf('week', '2026-09-16', TZ, { firstDay: 7, minimalDays: 1 }).firstVisible)
      .toBe('2026-09-13')
  })
})
