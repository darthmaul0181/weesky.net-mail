import type { TFunction } from 'i18next'
import { canonicalAddress } from '../../lib/canonicalAddress'
import type { AttendeeProjection, AttendeeWrite } from './calendarTypes'

/** What a guest answered, in the organizer's calendar — where the answers actually arrive.
    Anything but the three answers is "no answer yet". */
export function guestAnswerOf(partStat: string | undefined, t: TFunction<'calendar'>): string {
  switch (partStat?.toUpperCase()) {
    case 'ACCEPTED': return t('guestAnswer.accepted', { ns: 'calendar' })
    case 'TENTATIVE': return t('guestAnswer.tentative', { ns: 'calendar' })
    case 'DECLINED': return t('guestAnswer.declined', { ns: 'calendar' })
    default: return t('guestAnswer.pending', { ns: 'calendar' })
  }
}

export function dotClassOf(partStat: string | undefined): 'is-accepted' | 'is-tentative' | 'is-declined' | 'is-pending' {
  switch (partStat?.toUpperCase()) {
    case 'ACCEPTED': return 'is-accepted'
    case 'TENTATIVE': return 'is-tentative'
    case 'DECLINED': return 'is-declined'
    default: return 'is-pending'
  }
}

/** The guests the field holds, in its order, each with the answer the stored master guest of that
    address gave — none for a guest the event does not list yet. */
export function answersOf(guests: AttendeeWrite[], stored: AttendeeProjection[]): AttendeeProjection[] {
  const master = new Map<string, AttendeeProjection>()
  for (const one of stored) {
    const key = canonicalAddress(one.email)
    if (!one.isOrganizer && !one.recurrenceId && !master.has(key)) master.set(key, one)
  }
  return guests.map(({ email, name }) => {
    const known = master.get(canonicalAddress(email))
    return { email, name: name ?? known?.name, isOrganizer: false, partStat: known?.partStat }
  })
}
