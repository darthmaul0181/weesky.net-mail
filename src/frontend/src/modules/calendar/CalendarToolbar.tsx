import { useTranslation } from 'react-i18next'
import ChevronLeftIcon from '../../icons/ChevronLeftIcon'
import ChevronRightIcon from '../../icons/ChevronRightIcon'
import { DrawerToggle } from '../../layouts/ContextDrawer'
import type { View } from './windowOf'

export interface CalendarToolbarProps {
  view: View
  /** Already formatted by `formatRangeTitle`: the toolbar shows a title, it does not build one. */
  title: string
  /** The week under the title, for the two views that sit inside one. */
  weekNumber: number | null
  query: string
  /**
   * The tier, handed down rather than read here. `MessageReader`'s rule: only the layout knows
   * which panes are mounted, and a second `useViewport` is how the two fall out of step.
   */
  phone: boolean
  inDrawer: boolean
  onOpenDrawer: () => void
  onQuery: (value: string) => void
  /** Enter, rather than the 300ms the layout otherwise waits for. */
  onCommitQuery: () => void
  onToday: () => void
  onStep: (delta: 1 | -1) => void
  onView: (view: View) => void
}

/** Day is the phone's week: seven columns in 360px is six unreadable ones and a sideways scroll.
    Month leads, as the mockup has it — a phone opens on the shape of the month. */
const PHONE_VIEWS: View[] = ['month', 'day', 'list']
const VIEWS: View[] = ['day', 'week', 'month', 'list']

export default function CalendarToolbar({
  view, title, weekNumber, query, phone, inDrawer, onOpenDrawer, onQuery, onCommitQuery, onToday,
  onStep, onView,
}: CalendarToolbarProps) {
  const { t } = useTranslation('calendar')
  // Spelled out rather than `t(\`toolbar.views.${view}\`)`: a key reaching t() as a variable is
  // invisible to both the typed guard and src/locales/keys.test.ts.
  const label: Record<View, string> = {
    day: t('toolbar.views.day'), week: t('toolbar.views.week'),
    month: t('toolbar.views.month'), list: t('toolbar.views.list'),
  }

  return (
    <div className="calendar-toolbar">
      {inDrawer && <DrawerToggle onClick={onOpenDrawer} />}

      <button type="button" className="btn calendar-today" onClick={onToday}>
        {t('toolbar.today')}
      </button>
      <button type="button" className="calendar-step" aria-label={t('toolbar.previous')}
        onClick={() => onStep(-1)}><ChevronLeftIcon size={16} /></button>
      <button type="button" className="calendar-step" aria-label={t('toolbar.next')}
        onClick={() => onStep(1)}><ChevronRightIcon size={16} /></button>

      <div className="calendar-heading">
        <span className="calendar-title">{title}</span>
        {weekNumber !== null && (
          <span className="calendar-subtitle">{t('toolbar.week', { number: weekNumber })}</span>
        )}
      </div>

      {/* The phone searches from the head of the list instead: a 30ch box and three segments do
          not share a 360px band, and the box is the half a thumb can do without here. */}
      {!phone && (
        <CalendarSearch className="calendar-search" query={query} onQuery={onQuery}
          onCommitQuery={onCommitQuery} />
      )}

      {/* Its own name, never the module's: the editor's calendar picker is also called
          "Calendar", and a screen reader announcing both the same way names neither. */}
      <div className="seg calendar-views" role="radiogroup" aria-label={t('toolbar.viewsLabel')}>
        {(phone ? PHONE_VIEWS : VIEWS).map(one => (
          <label key={one}>
            <input type="radio" name="calendar-view" value={one} checked={view === one}
              onChange={() => onView(one)} />
            {label[one]}
          </label>
        ))}
      </div>
    </div>
  )
}

/**
 * The one search box the module draws, in whichever band the tier puts it: this toolbar above
 * 640px, a band of its own at the head of the phone's list. Two copies of it would be two
 * placeholders, two labels and two Enter behaviours to keep in step.
 */
export function CalendarSearch({ className, query, onQuery, onCommitQuery }: {
  className: string
  query: string
  onQuery: (value: string) => void
  onCommitQuery: () => void
}) {
  const { t } = useTranslation('calendar')
  return (
    <input type="search" className={`search-input ${className}`} value={query}
      placeholder={t('toolbar.search')} aria-label={t('toolbar.search')}
      onChange={event => onQuery(event.target.value)}
      onKeyDown={event => { if (event.key === 'Enter') onCommitQuery() }} />
  )
}
