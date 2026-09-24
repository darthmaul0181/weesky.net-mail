import type { TFunction } from 'i18next'
import { partStatOf } from './partStat'

/** The user's own answer to an invitation, in words — what the preview and the editor say under
    the attendees. Null for a value the screen has no sentence for. The keys are written as
    literals with their namespace so `locales/keys.test.ts` can read them off this file. */
export function myAnswerOf(partStat: string | undefined, t: TFunction<'calendar'>): string | null {
  switch (partStatOf(partStat)) {
    case 'accepted': return t('myAnswer.accepted', { ns: 'calendar' })
    case 'tentative': return t('myAnswer.tentative', { ns: 'calendar' })
    case 'declined': return t('myAnswer.declined', { ns: 'calendar' })
    case 'needs-action': return t('myAnswer.pending', { ns: 'calendar' })
    case null: return null
  }
}
