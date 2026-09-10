import { useTranslation } from 'react-i18next'
import LoadingBlock from '../../components/LoadingBlock'
import { useCalendar } from './calendarContext'
import type { Occurrence } from './calendarTypes'
import EventChip from './EventChip'
import { dayOf } from './multiDay'
import { colorOf, occurrenceKey } from './occurrenceStyle'

export interface SearchResultsProps {
  occurrences: Occurrence[]
  loading: boolean
  failed: boolean
  /** Under two characters nothing was asked of the server, and "0 events found" would be a lie. */
  tooShort: boolean
  onClear(): void
  onOpen(o: Occurrence, anchor: HTMLElement): void
  onOpenEditor(o: Occurrence): void
  selectedKey?: string
}

/** What the server stops at; said out loud, because a list silently cut at 200 reads as the
    whole answer. Kept beside the band that would otherwise claim exactly 200 matches. */
const CAP = 200
const MIN_QUERY = 2

/** The stage while a search stands: a band saying what came back, then one row per result,
    each carrying its own date since nothing above it names the day. */
export default function SearchResults({
  occurrences, loading, failed, tooShort, onClear, onOpen, onOpenEditor, selectedKey,
}: SearchResultsProps) {
  const { t } = useTranslation('calendar')
  const { tz, calendarById, setAnchor } = useCalendar()

  // Decision 11: the click goes to that date *in the current view*, so the search is cleared —
  // left standing, the results keep replacing the grid that has just been moved.
  const openAt = (occurrence: Occurrence, anchor: HTMLElement) => {
    const day = dayOf(occurrence, tz)
    if (day) setAnchor(day)
    onClear()
    onOpen(occurrence, anchor)
  }

  const heading = tooShort ? t('views.searchTooShort', { min: MIN_QUERY })
    : loading ? t('views.loading')
      : failed ? t('errors.load')
        : t('views.results', { count: occurrences.length })
  const listed = !tooShort && !loading && !failed

  return (
    <div className="calendar-results">
      <div className="calendar-results-band">
        <span className="calendar-results-count">{heading}</span>
        <button type="button" className="btn" onClick={onClear}>{t('views.clearSearch')}</button>
      </div>

      {loading && !tooShort && <LoadingBlock />}
      {listed && occurrences.length >= CAP && (
        <p className="calendar-results-capped">{t('search.capped', { max: CAP })}</p>
      )}
      {listed && occurrences.map(occurrence => (
        <EventChip key={occurrenceKey(occurrence)} occurrence={occurrence}
          color={colorOf(occurrence, calendarById)} variant="row" showDate
          selected={occurrenceKey(occurrence) === selectedKey}
          onOpen={openAt} onOpenEditor={onOpenEditor} />
      ))}
    </div>
  )
}
