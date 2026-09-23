import type { TFunction } from 'i18next'
import { dateFormat } from '../../lib/intl'
import { dateLocaleOf, WEEKDAY_TOKENS, weekdayNameOf } from './calendarLocale'
import type { RecurrenceWrite } from './calendarTypes'

const FREQUENCY_KEYS: Record<string, string> = {
  DAILY: 'daily', WEEKLY: 'weekly', MONTHLY: 'monthly', YEARLY: 'yearly',
}

/** The positions a picker offers and a sentence has a word for. `-1` is the rule's own way of
    saying "the last one"; every other value is spelled as an ordinal rather than guessed at. */
const NAMED_POSITIONS = ['first', 'second', 'third', 'fourth', 'fifth'] as const

function dayName(token: string, locale: string): string {
  // token comes off the wire and is not guaranteed to be one of the seven known codes.
  const index = (WEEKDAY_TOKENS as readonly string[]).indexOf(token)
  return index === -1 ? token : weekdayNameOf(index, 'long', locale)
}

/** `21st`, not `21th`: the suffix is the one CLDR's ordinal rules pick for the language. */
function ordinal(n: number, t: TFunction<'calendar'>, locale: string): string {
  const rule = new Intl.PluralRules(locale, { type: 'ordinal' }).select(n)
  return t(`repeat.ordinal.${rule}` as 'repeat.ordinal.other', { n, ns: 'calendar' })
}

/** `bySetPos` runs -366 to 366, so an iPhone's rule may arrive here. What the table cannot name
 * is counted, not approximated: reading `-2` as "last" would state what the rule does not. */
function positionText(position: number, t: TFunction<'calendar'>, locale: string): string {
  if (position === -1) return t('repeat.position.last', { ns: 'calendar' })
  if (position >= 1 && position <= NAMED_POSITIONS.length) {
    return t(`repeat.position.${NAMED_POSITIONS[position - 1]}` as 'repeat.position.first',
      { ns: 'calendar' })
  }
  const n = ordinal(Math.abs(position), t, locale)
  return position < 0
    ? t('repeat.position.nthFromEnd', { n, ns: 'calendar' })
    : t('repeat.position.nth', { n, ns: 'calendar' })
}

/** A stored rule as a sentence, the days named by `Intl` and joined by `Intl.ListFormat` (the
 * language's own conjunction). `{ ns: 'calendar' }` is for `keys.test.ts`, which finds no
 * `useTranslation` here to bind a namespace from: do not tidy it away. */
export function recurrenceSummary(
  rule: RecurrenceWrite, t: TFunction<'calendar'>, lang: string, navigatorLanguage: string,
): string {
  const locale = dateLocaleOf(lang, navigatorLanguage)
  const frequency = FREQUENCY_KEYS[rule.frequency.toUpperCase()]
  if (!frequency) return rule.frequency

  let base = t(`repeat.frequency.${frequency}` as 'repeat.frequency.daily',
    { count: rule.interval || 1, ns: 'calendar' })

  if (rule.byDay.length > 0) {
    const days = new Intl.ListFormat(locale, { type: 'conjunction' })
      .format(rule.byDay.map(token => dayName(token, locale)))
    base = t('repeat.onDays', { base, days, ns: 'calendar' })
  } else if (rule.bySetPos !== undefined && rule.bySetPosDay) {
    base = t('repeat.onSetPos', {
      base,
      position: positionText(rule.bySetPos, t, locale),
      day: dayName(rule.bySetPosDay, locale),
      ns: 'calendar',
    })
  } else if (rule.byMonthDay !== undefined) {
    base = t('repeat.onMonthDay', { base, day: rule.byMonthDay, ns: 'calendar' })
  }

  if (rule.end === 'Count' && rule.count) {
    return t('repeat.count', { base, count: rule.count, ns: 'calendar' })
  }

  if (rule.end === 'Until' && rule.until) {
    // The rule may spell its end as a plain date or as an instant; only the day is ever read.
    const date = dateFormat({ day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' },
      locale).format(new Date(`${rule.until.slice(0, 10)}T00:00:00Z`))
    return t('repeat.until', { base, date, ns: 'calendar' })
  }

  return base
}
