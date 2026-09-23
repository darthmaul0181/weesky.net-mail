import { useEffect, useState } from 'react'
import { localDay } from '../lib/intl'

/** The local day, `YYYY-MM-DD`, refreshed at local midnight: a memoised row sees no clock, and a
 * list left open overnight printed times beside yesterday's mail. A string, so rows redraw daily. */
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
