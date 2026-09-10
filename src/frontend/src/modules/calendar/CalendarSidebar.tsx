import { type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import DropdownMenu from '../../components/DropdownMenu'
import LoadingBlock from '../../components/LoadingBlock'
import KebabIcon from '../../icons/KebabIcon'
import type { WeekRules } from './calendarLocale'
import type { Calendar } from './calendarTypes'
import MiniMonth from './MiniMonth'
import type { PlainDate } from './plainDate'

export interface CalendarSidebarProps {
  calendars: Calendar[]
  anchor: PlainDate
  today: PlainDate
  rules: WeekRules
  /** Already region-grafted by `dateLocaleOf`, resolved once by the layout. */
  locale: string
  loading: boolean
  failed: boolean
  onPickDay: (day: PlainDate) => void
  onNewEvent: () => void
  onNewCalendar: () => void
  onRename: (calendar: Calendar) => void
  onRecolour: (calendar: Calendar) => void
  onImport: (calendar: Calendar) => void
  onExport: (calendar: Calendar) => void
  onDelete: (calendar: Calendar) => void
  onToggleVisible: (calendar: Calendar, visible: boolean) => void
}

/**
 * The module's navigation column: the primary action, the month picker, then one row per
 * calendar. It runs no query of its own — the layout owns every one of them and hands the list
 * down, which is what lets this mount in a test with neither a router nor a query client.
 */
export default function CalendarSidebar({
  calendars, anchor, today, rules, locale, loading, failed, onPickDay, onNewEvent, onNewCalendar,
  onRename, onRecolour, onImport, onExport, onDelete, onToggleVisible,
}: CalendarSidebarProps) {
  const { t } = useTranslation('calendar')

  return (
    <div className="calendar-sidebar">
      <div className="column-actions">
        <button type="button" className="btn btn-primary column-actions-main" onClick={onNewEvent}>
          {t('sidebar.newEvent')}
        </button>
      </div>

      <div className="calendar-sidebar-scroll">
        <MiniMonth anchor={anchor} today={today} rules={rules} locale={locale} onPick={onPickDay} />

        <div className="calendar-sidebar-heading">
          <h2>{t('sidebar.calendars')}</h2>
          <button type="button" className="calendar-sidebar-add" aria-label={t('sidebar.newCalendar')}
            title={t('sidebar.newCalendar')} onClick={onNewCalendar}>+</button>
        </div>

        {/* A refused list is said out loud: an empty section would claim the account holds none. */}
        {failed && <p className="calendar-sidebar-error">{t('errors.load')}</p>}
        {loading && !failed && <LoadingBlock />}

        <div className="calendar-list">
          {calendars.map(one => (
            <div key={one.id} className="calendar-row">
              {/* The box is visually hidden rather than unmounted, so the swatch is what is seen
                  while the checkbox keeps the tab order, the keyboard and the accessible name. */}
              <label className="calendar-row-label" title={one.displayName}>
                <input type="checkbox" checked={one.isVisible}
                  onChange={event => onToggleVisible(one, event.target.checked)} />
                <span className="calendar-swatch" aria-hidden="true"
                  style={{ '--cal': one.color } as CSSProperties} />
                <span className="calendar-name">{one.displayName}</span>
              </label>
              {/* `auto`: the band scrolls, so the last row's menu would otherwise open under the
                  fold. The name is in the label but not alone: the box beside it is already
                  called after the calendar, and two controls of one name is one too many. */}
              <DropdownMenu ariaLabel={t('sidebar.actions', { name: one.displayName })}
                className="admin-icon-btn" direction="auto"
                trigger={<KebabIcon size={14} />}
                items={[
                  { label: t('sidebar.rename'), onSelect: () => onRename(one) },
                  { label: t('sidebar.colour'), onSelect: () => onRecolour(one) },
                  { label: t('sidebar.import'), onSelect: () => onImport(one) },
                  { label: t('sidebar.export'), onSelect: () => onExport(one) },
                  'separator',
                  {
                    label: t('sidebar.delete'), onSelect: () => onDelete(one),
                    disabled: one.isDefault,
                    title: one.isDefault ? t('sidebar.deleteDefault') : undefined,
                  },
                ]} />
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
