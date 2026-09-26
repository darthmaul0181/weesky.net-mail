import { useEffect, useState } from 'react'
import { addDays, todayIn, utcOfLocalMidnight, type PlainDate } from './plainDate'

/** Never longer: a sleeping machine stops the timers' clock, not the wall clock. It is also the
    wait when midnight is past but the day has not turned, a midnight the zone skips. */
const MAX_DELAY_MS = 60_000

/** Today in the calendar's zone, moving on at that zone's midnight, read at least once a minute.
 * A hidden tab's timer is throttled, so the day is read again when the tab comes back too. */
export function useToday(tz: string): PlainDate {
  const [today, setToday] = useState(() => todayIn(tz))

  useEffect(() => {
    let timer: ReturnType<typeof setTimeout> | undefined
    const check = () => {
      const day = todayIn(tz)
      setToday(day)
      clearTimeout(timer)
      const delay = utcOfLocalMidnight(addDays(day, 1), tz).getTime() - Date.now()
      timer = setTimeout(check, delay > 0 ? Math.min(delay, MAX_DELAY_MS) : MAX_DELAY_MS)
    }
    const onVisible = () => { if (document.visibilityState === 'visible') check() }
    check()
    document.addEventListener('visibilitychange', onVisible)
    return () => {
      clearTimeout(timer)
      document.removeEventListener('visibilitychange', onVisible)
    }
  }, [tz])

  return today
}
