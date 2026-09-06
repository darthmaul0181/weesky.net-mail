import { CALENDAR_COLORS } from './calendarColors'
import type { Calendar, Occurrence } from './calendarTypes'

export type Rendering = 'busy' | 'free' | 'tentative' | 'cancelled'

/** The four renderings the mockup fixes. A cancellation is read first: an event called off is
    called off whatever it was going to be, and drawing it as tentative would say the opposite. */
export function renderingOf(o: Pick<Occurrence, 'status' | 'transparency'>): Rendering {
  const status = o.status?.toUpperCase()
  if (status === 'CANCELLED') return 'cancelled'
  if (status === 'TENTATIVE') return 'tentative'
  return o.transparency?.toUpperCase() === 'TRANSPARENT' ? 'free' : 'busy'
}

/** What a chip is found by, however many days it is drawn across: one instance of one event. */
export function occurrenceKey(o: Pick<Occurrence, 'eventId' | 'instanceId'>): string {
  return `${o.eventId}#${o.instanceId}`
}

/** A calendar the list has not answered for yet still has to be drawn in something: an empty
    `--cal` paints a busy chip's fill transparent and its bar invisible. */
export function colorOf(
  o: Pick<Occurrence, 'calendarId'>, calendars: Map<string, Calendar>,
): string {
  return calendars.get(o.calendarId)?.color || CALENDAR_COLORS[0]
}
