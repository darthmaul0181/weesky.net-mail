import { useEffect, useState } from 'react'
import { localDay } from '../lib/intl'

/**
 * The local calendar day, `YYYY-MM-DD`, refreshed at the next local midnight.
 *
 * A date label is read against "today", and a memoised row cannot see a clock its props do not
 * carry: without this the mail list left open overnight prints a time beside yesterday's mail for
 * the rest of the session. A day string rather than a `Date`, so a row redraws once a day.
 */
export function useToday(): string {
  const [today, setToday] = useState(() => localDay(new Date()))

  useEffect(() => {
    let timer: ReturnType<typeof setTimeout>
    const tick = () => {
      const now = new Date()
      setToday(localDay(now))
      // Tomorrow's local midnight is always ahead of `now`, so the delay needs no clamp; a
      // resumed laptop just fires late, reads the day it woke up on and schedules the next one.
      const midnight = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1)
      timer = setTimeout(tick, midnight.getTime() - now.getTime())
    }
    // Called now as well as scheduled: mounting can itself be the other side of midnight.
    tick()
    return () => clearTimeout(timer)
  }, [])

  return today
}
