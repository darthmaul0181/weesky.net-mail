import type { TFunction } from 'i18next'

/** The user's own answer to an invitation, in words — what the preview and the editor say under
    the attendees (spec 5e). Null for a value the screen has no sentence for. The keys are written
    as literals with their namespace so `locales/keys.test.ts` can read them off this file. */
export function myAnswerOf(partStat: string | undefined, t: TFunction<'calendar'>): string | null {
  switch (partStat?.toUpperCase()) {
    case 'ACCEPTED': return t('myAnswer.accepted', { ns: 'calendar' })
    case 'TENTATIVE': return t('myAnswer.tentative', { ns: 'calendar' })
    case 'DECLINED': return t('myAnswer.declined', { ns: 'calendar' })
    case 'NEEDS-ACTION': return t('myAnswer.pending', { ns: 'calendar' })
    default: return null
  }
}
