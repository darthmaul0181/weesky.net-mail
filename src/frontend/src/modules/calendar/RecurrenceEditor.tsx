import { useTranslation } from 'react-i18next'
import { useCalendar } from './calendarContext'
import { dateLocaleOf, WEEKDAY_TOKENS, weekdayNameOf } from './calendarLocale'
import type { RecurrenceWrite } from './calendarTypes'
import { isoWeekdayOf, type PlainDate } from './plainDate'

export interface RecurrenceEditorProps {
  value: RecurrenceWrite
  /** The event's own start: what "on day N" and "on the last Friday" default to. */
  startDate: PlainDate
  onChange(rule: RecurrenceWrite): void
}

const POSITIONS = [1, 2, 3, 4, -1]
/** `-1` is the rule's own way of saying "the last one"; the option carries the word rather than
    the number, so a select never has to explain a minus sign. */
const LAST = 'last'
const posValue = (position: number | undefined) =>
  (position === -1 ? LAST : String(position ?? 1))
const DEFAULT_COUNT = 10

export default function RecurrenceEditor({ value, startDate, onChange }: RecurrenceEditorProps) {
  const { t } = useTranslation('calendar')
  const { rules, lang, region } = useCalendar()
  const locale = dateLocaleOf(lang, region)

  const frequency = value.frequency.toUpperCase()
  const dayNumber = Number(startDate.slice(8, 10)) || 1
  const startDay = WEEKDAY_TOKENS[isoWeekdayOf(startDate) - 1]
  const bySetPos = value.bySetPos !== undefined

  const unit: Record<string, string> = {
    DAILY: t('repeat.unitDay', { count: value.interval }),
    WEEKLY: t('repeat.unitWeek', { count: value.interval }),
    MONTHLY: t('repeat.unitMonth', { count: value.interval }),
    YEARLY: t('repeat.unitYear', { count: value.interval }),
  }
  const positionLabel: Record<number, string> = {
    1: t('repeat.position.first'), 2: t('repeat.position.second'),
    3: t('repeat.position.third'), 4: t('repeat.position.fourth'),
    [-1]: t('repeat.position.last'),
  }

  /** Every branch answers with a whole `RecurrenceWrite`: the API takes a rule, never a patch. */
  const emit = (patch: Partial<RecurrenceWrite>) => onChange({ ...value, ...patch })

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
          onChange={event => emit({
            frequency: event.target.value, byDay: [],
            byMonthDay: undefined, bySetPos: undefined, bySetPosDay: undefined,
          })}>
          {['DAILY', 'WEEKLY', 'MONTHLY', 'YEARLY'].map(one => (
            <option key={one} value={one}>{unit[one]}</option>
          ))}
        </select>
      </div>

      {frequency === 'WEEKLY' && (
        <div className="field-h">
          <span className="field-h-label">{t('repeat.byDay')}</span>
          <div className="recurrence-days">
            {Array.from({ length: 7 }, (_, index) => (rules.firstDay - 1 + index) % 7)
              .map(offset => (
                <label key={WEEKDAY_TOKENS[offset]}>
                  {/* The whole name is the box's own, the abbreviation is what is drawn: seven
                      long names unwrapped are the modal's intrinsic width, and a content-sized
                      dialog would take the screen to hold them. */}
                  <input type="checkbox" aria-label={weekdayNameOf(offset, 'long', locale)}
                    checked={value.byDay.includes(WEEKDAY_TOKENS[offset])}
                    onChange={() => toggleDay(WEEKDAY_TOKENS[offset])} />
                  {weekdayNameOf(offset, 'short', locale)}
                </label>
              ))}
          </div>
        </div>
      )}

      {frequency === 'MONTHLY' && (
        <div className="field-h">
          <span className="field-h-label">{t('repeat.bySetPos')}</span>
          <div className="recurrence-monthly">
            <label>
              <input type="radio" name="repeat-monthly" checked={!bySetPos}
                onChange={() => emit({
                  byMonthDay: dayNumber, bySetPos: undefined, bySetPosDay: undefined,
                })} />
              {t('repeat.byMonthDay')}
            </label>
            {!bySetPos && (
              <input type="number" min={1} max={31} aria-label={t('repeat.byMonthDay')}
                value={value.byMonthDay ?? dayNumber} className="recurrence-interval"
                onChange={event => emit({
                  byMonthDay: Math.min(31, Math.max(1, Number(event.target.value) || 1)),
                })} />
            )}
            <label>
              <input type="radio" name="repeat-monthly" checked={bySetPos}
                onChange={() => emit({
                  bySetPos: 1, bySetPosDay: startDay, byMonthDay: undefined,
                })} />
              {t('repeat.bySetPos')}
            </label>
            {bySetPos && (
              <>
                <select aria-label={t('repeat.positionLabel')} value={posValue(value.bySetPos)}
                  onChange={event => emit({
                    bySetPos: event.target.value === LAST ? -1 : Number(event.target.value),
                  })}>
                  {POSITIONS.map(one => (
                    <option key={one} value={posValue(one)}>{positionLabel[one]}</option>
                  ))}
                </select>
                <select aria-label={t('repeat.weekdayLabel')}
                  value={value.bySetPosDay ?? startDay}
                  onChange={event => emit({ bySetPosDay: event.target.value })}>
                  {WEEKDAY_TOKENS.map((token, offset) => (
                    <option key={token} value={token}>{weekdayNameOf(offset, 'long', locale)}</option>
                  ))}
                </select>
              </>
            )}
          </div>
        </div>
      )}

      <div className="field-h">
        <span className="field-h-label">{t('repeat.ends')}</span>
        <div className="recurrence-end">
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
          {value.end === 'Count' && (
            <>
              <input type="number" min={1} max={999} aria-label={t('repeat.endCount')}
                value={value.count ?? DEFAULT_COUNT} className="recurrence-interval"
                onChange={event => emit({ count: Math.max(1, Number(event.target.value) || 1) })} />
              <span>{t('repeat.times')}</span>
            </>
          )}
          <label>
            <input type="radio" name="repeat-end" checked={value.end === 'Until'}
              onChange={() => emit({ end: 'Until', until: startDate, count: undefined })} />
            {t('repeat.endUntil')}
          </label>
          {value.end === 'Until' && (
            <input type="date" aria-label={t('repeat.untilDate')}
              value={(value.until ?? startDate).slice(0, 10)}
              onChange={event => emit({ until: event.target.value })} />
          )}
        </div>
      </div>
    </div>
  )
}
