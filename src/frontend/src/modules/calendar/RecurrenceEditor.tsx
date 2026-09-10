import { useTranslation } from 'react-i18next'
import { useCalendar } from './calendarContext'
import { dateLocaleOf, WEEKDAY_TOKENS, weekdayNameOf } from './calendarLocale'
import type { RecurrenceWrite } from './calendarTypes'
import { isoWeekdayOf, type PlainDate } from './plainDate'

export interface RecurrenceEditorProps {
  value: RecurrenceWrite
  /** The event's own start: the weekday a rule falls back on when it names none. */
  startDate: PlainDate
  onChange(rule: RecurrenceWrite): void
}

const DEFAULT_COUNT = 10

export default function RecurrenceEditor({ value, startDate, onChange }: RecurrenceEditorProps) {
  const { t } = useTranslation('calendar')
  const { rules, lang, region } = useCalendar()
  const locale = dateLocaleOf(lang, region)

  const frequency = value.frequency.toUpperCase()
  const daily = frequency === 'DAILY'
  const startDay = WEEKDAY_TOKENS[isoWeekdayOf(startDate) - 1]

  const unit: Record<string, string> = {
    DAILY: t('repeat.unitDay', { count: value.interval }),
    WEEKLY: t('repeat.unitWeek', { count: value.interval }),
    MONTHLY: t('repeat.unitMonth', { count: value.interval }),
    YEARLY: t('repeat.unitYear', { count: value.interval }),
  }

  /** Every branch answers with a whole `RecurrenceWrite`: the API takes a rule, never a patch. */
  const emit = (patch: Partial<RecurrenceWrite>) => onChange({ ...value, ...patch })

  // "Every day" names no weekday, so the rule carries none; every other unit starts from the
  // start's own day rather than from nothing, which is what a rule with no day would repeat on.
  const pickUnit = (next: string) => emit({
    frequency: next,
    byDay: next === 'DAILY' ? [] : value.byDay.length ? value.byDay : [startDay],
  })

  const toggleDay = (token: string) => emit({
    byDay: value.byDay.includes(token)
      ? value.byDay.filter(one => one !== token)
      : WEEKDAY_TOKENS.filter(one => one === token || value.byDay.includes(one)),
  })

  return (
    <div className="recurrence-editor">
      <div className="field-h">
        <label htmlFor="repeat-interval">{t('repeat.interval')}</label>
        <input id="repeat-interval" type="number" min={1} max={999} value={value.interval}
          className="recurrence-interval"
          onChange={event => emit({ interval: Math.max(1, Number(event.target.value) || 1) })} />
        <select aria-label={t('repeat.unitLabel')} value={frequency}
          onChange={event => pickUnit(event.target.value)}>
          {['DAILY', 'WEEKLY', 'MONTHLY', 'YEARLY'].map(one => (
            <option key={one} value={one}>{unit[one]}</option>
          ))}
        </select>
      </div>

      {/* Drawn for every unit, never withheld: under "every day" the seven boxes are all lit and
          disabled — choosing days makes no sense when it is every day, and a row that comes and
          goes makes the block jump under the pointer. */}
      <div className="field-h">
        <span className="field-h-label">{t('repeat.byDay')}</span>
        <div className="recurrence-days">
          {Array.from({ length: 7 }, (_, index) => (rules.firstDay - 1 + index) % 7)
            .map(offset => (
              <label key={WEEKDAY_TOKENS[offset]}>
                {/* The whole name is the box's own; one letter is what is drawn. */}
                <input type="checkbox" aria-label={weekdayNameOf(offset, 'long', locale)}
                  disabled={daily}
                  checked={daily || value.byDay.includes(WEEKDAY_TOKENS[offset])}
                  onChange={() => toggleDay(WEEKDAY_TOKENS[offset])} />
                <span className="recurrence-day">{weekdayNameOf(offset, 'narrow', locale)}</span>
              </label>
            ))}
        </div>
      </div>

      {/* One line: the label, the three choices as the segmented control the rest of the app
          draws for a choice among a few, then the one value the choice needs — a counter after
          "after", a date after "on", nothing after "never". */}
      <div className="field-h">
        <span className="field-h-label" id="repeat-ends-label">{t('repeat.ends')}</span>
        <div className="seg" role="radiogroup" aria-labelledby="repeat-ends-label">
          <label>
            <input type="radio" name="repeat-end" checked={value.end === 'Never'}
              onChange={() => emit({ end: 'Never', count: undefined, until: undefined })} />
            {t('repeat.endNever')}
          </label>
          <label>
            <input type="radio" name="repeat-end" checked={value.end === 'Count'}
              onChange={() => emit({
                end: 'Count', count: value.count ?? DEFAULT_COUNT, until: undefined,
              })} />
            {t('repeat.endCount')}
          </label>
          <label>
            <input type="radio" name="repeat-end" checked={value.end === 'Until'}
              onChange={() => emit({ end: 'Until', until: value.until ?? startDate,
                count: undefined })} />
            {t('repeat.endUntil')}
          </label>
        </div>
        {value.end === 'Count' && (
          <>
            <input type="number" min={1} max={999} aria-label={t('repeat.countValue')}
              value={value.count ?? DEFAULT_COUNT} className="recurrence-interval"
              onChange={event => emit({ count: Math.max(1, Number(event.target.value) || 1) })} />
            <span className="recurrence-times">{t('repeat.times')}</span>
          </>
        )}
        {value.end === 'Until' && (
          <input type="date" aria-label={t('repeat.untilDate')}
            value={(value.until ?? startDate).slice(0, 10)}
            onChange={event => emit({ until: event.target.value })} />
        )}
      </div>
    </div>
  )
}
