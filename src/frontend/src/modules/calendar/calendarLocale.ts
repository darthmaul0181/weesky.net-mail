import { dateFormat } from '../../lib/intl'
import {
  addDays, DAY_MS, daysBetween, isoWeekdayOf, MONDAY_UTC_MS, type PlainDate, utcMidnightOf,
} from './plainDate'
import type { View } from './windowOf'

/**
 * Two sources: the month and day *names* follow the interface language, the first day of the
 * week and the clock format follow the browser's region — a bare `en` says Sunday and 12 hours,
 * and a Belgian reading an English interface still counts his weeks from Monday.
 */
export interface WeekRules {
  /** 1 = Monday … 7 = Sunday, ISO-8601's own numbering. */
  firstDay: 1 | 2 | 3 | 4 | 5 | 6 | 7
  /** How many days of January week 1 must hold: 4 for ISO, 1 where the week starts on Sunday. */
  minimalDays: 1 | 4 | 7
}

/** What the engines answer, under neither name the lib this project compiles against declares. */
interface WeekInfo { firstDay?: number; minimalDays?: number }
interface WeekInfoCarrier { getWeekInfo?: () => WeekInfo; weekInfo?: WeekInfo }

export type WeekInfoReader = (locale: Intl.Locale) => WeekInfo | undefined

/** The one seam onto a datum three platforms spell three ways: a method from V8 13 (Node 22+),
    the older accessor on Safari and Node 20, and nothing at all before either. Reading it here
    is what lets a test exercise the fallback table without touching `Intl.Locale`'s prototype. */
export const weekInfoOf: WeekInfoReader = locale => {
  const carrier = locale as unknown as WeekInfoCarrier
  return carrier.getWeekInfo?.() ?? carrier.weekInfo
}

/** The regions CLDR gives a Sunday week, for the engines with no `getWeekInfo` of their own. */
const SUNDAY_REGIONS = new Set(
  ['US', 'CA', 'JP', 'BR', 'IL', 'MX', 'PH', 'ZA', 'KR', 'TW', 'HK', 'AU'])

/**
 * The interface language with the browser's own region grafted on (décision 14): the month names
 * follow the language, the field order and the separators the region, and neither alone is right.
 * A browser naming no region leaves English on `en-GB`, the day-first English this product reads.
 */
export function dateLocaleOf(lang: string, navigatorLanguage: string): string {
  const language = lang.split('-')[0]
  let region: string | undefined
  try {
    region = new Intl.Locale(navigatorLanguage).region
  } catch {
    region = undefined
  }
  if (region) return `${language}-${region}`
  return language === 'en' ? 'en-GB' : language
}

/** `minimalDays` is not in every engine's `getWeekInfo`, and it follows the first day anyway:
    a Sunday week counts from the week holding 1 January, a Monday one from ISO's 4 January. */
function minimalDaysFor(firstDay: number): 1 | 4 {
  return firstDay === 7 ? 1 : 4
}

/** `readWeekInfo` is the seam, and it is a parameter rather than a spy: a test that stubbed
    `Intl.Locale`'s prototype would exercise the table on one engine and skip it on the next. */
export function weekRulesOf(region: string, readWeekInfo = weekInfoOf): WeekRules {
  let locale: Intl.Locale
  try {
    locale = new Intl.Locale(region)
  } catch {
    return { firstDay: 1, minimalDays: 4 }
  }

  const info = readWeekInfo(locale)
  const firstDay = info?.firstDay
    ?? (SUNDAY_REGIONS.has(locale.maximize().region ?? '') ? 7 : 1)

  return {
    firstDay: firstDay as WeekRules['firstDay'],
    minimalDays: (info?.minimalDays ?? minimalDaysFor(firstDay)) as WeekRules['minimalDays'],
  }
}

export function hourCycleOf(region: string): 'h12' | 'h23' {
  const cycle = dateFormat({ hour: 'numeric' }, region).resolvedOptions().hourCycle
  return cycle === 'h11' || cycle === 'h12' ? 'h12' : 'h23'
}

export function startOfWeek(day: PlainDate, rules: WeekRules): PlainDate {
  return addDays(day, -((isoWeekdayOf(day) - rules.firstDay + 7) % 7))
}

/** Week 1 is the one holding the `minimalDays`-th day of January — ISO's 4 January, or the 1st
    where a single day is enough. */
function firstWeekOf(year: number, rules: WeekRules): PlainDate {
  return startOfWeek(`${year}-01-${String(rules.minimalDays).padStart(2, '0')}`, rules)
}

/** Generic rather than ISO-only. The year is tried downwards: a day in late December can belong
    to the next year's week 1, one in early January to the previous year's week 52 or 53. */
export function weekNumberOf(day: PlainDate, rules: WeekRules): number {
  const year = Number(day.slice(0, 4))
  for (const candidate of [year + 1, year, year - 1]) {
    const start = firstWeekOf(candidate, rules)
    if (day >= start) return Math.floor(daysBetween(start, day) / 7) + 1
  }
  return 1
}

/** Always six rows: a month grid that changed height between September and October would move
    every row under the cursor. A view that wants five drops the last row itself. */
export function monthGrid(year: number, month: number, rules: WeekRules): PlainDate[][] {
  const first = startOfWeek(`${year}-${String(month).padStart(2, '0')}-01`, rules)
  return Array.from({ length: 6 }, (_, row) =>
    Array.from({ length: 7 }, (_, column) => addDays(first, row * 7 + column)))
}

const longDayFormat = (locale: string, withYear: boolean) => dateFormat({
  weekday: 'long', day: 'numeric', month: 'long',
  ...(withYear ? { year: 'numeric' as const } : {}), timeZone: 'UTC',
}, locale)

/** “Monday 14 September” — the bubble's date, an upcoming day's heading and, with its year, the
    toolbar's day title. One formatter, so three screens cannot spell one day three ways. */
export function formatLongDay(day: PlainDate, locale: string, withYear = false): string {
  return longDayFormat(locale, withYear).format(utcMidnightOf(day))
}

/** The same, as the range a whole day of several days spans. */
export function formatLongDayRange(from: PlainDate, to: PlainDate, locale: string): string {
  return longDayFormat(locale, false).formatRange(utcMidnightOf(from), utcMidnightOf(to))
}

/** The toolbar's title. A month is named by itself rather than by the grid it spans — that
    opens in August and closes in October — so the middle of the range is what is read. */
export function formatRangeTitle(
  from: PlainDate, to: PlainDate, view: View, lang: string, navigatorLanguage: string,
): string {
  const locale = dateLocaleOf(lang, navigatorLanguage)

  if (view === 'day') return formatLongDay(from, locale, true)

  if (view === 'month') {
    const middle = addDays(from, Math.floor(daysBetween(from, to) / 2))
    return dateFormat({ month: 'long', year: 'numeric', timeZone: 'UTC' }, locale)
      .format(utcMidnightOf(middle))
  }

  // formatRange picks the dash and drops whatever the two ends share; pinning either here would
  // make the test the formatter's mirror rather than the product's contract.
  return dateFormat({ day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' }, locale)
    .formatRange(utcMidnightOf(from), utcMidnightOf(to))
}

export function formatTime(
  instant: Date, lang: string, cycle: 'h12' | 'h23', tz: string, navigatorLanguage: string,
): string {
  return dateFormat({
    hour: cycle === 'h23' ? '2-digit' : 'numeric', minute: '2-digit', hourCycle: cycle,
    timeZone: tz,
  }, dateLocaleOf(lang, navigatorLanguage)).format(instant)
}

/** iCalendar's two-letter weekdays, in the order `weekdayNameOf` counts its offsets: the custom
    rule's checkboxes and the sentence a rule is read as are indexed by this one list. */
export const WEEKDAY_TOKENS = ['MO', 'TU', 'WE', 'TH', 'FR', 'SA', 'SU']

/** The name of the weekday `offset` days after `MONDAY_UTC_MS`, the module's single anchor. */
export function weekdayNameOf(
  offset: number, style: 'long' | 'short' | 'narrow', locale: string,
): string {
  return dateFormat({ weekday: style, timeZone: 'UTC' }, locale)
    .format(new Date(MONDAY_UTC_MS + offset * DAY_MS))
}

/**
 * The seven column heads, already in the order the week is drawn in. It takes the **resolved**
 * locale rather than a language and a region: every caller has already grafted one through
 * `dateLocaleOf`, and doing it a second time inside here is how the two answers drift.
 */
export function dayNames(
  locale: string, rules: WeekRules, style: 'short' | 'narrow',
): string[] {
  return Array.from({ length: 7 }, (_, index) =>
    weekdayNameOf((rules.firstDay - 1 + index) % 7, style, locale))
}
