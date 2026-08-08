import { useTranslation } from 'react-i18next'
import SearchIcon from '../../../icons/SearchIcon'

interface Props {
  /** Null while the search is in flight — the banner still shows so Clear stays reachable. */
  total: number | null
  /** The quoted text, or null for a checkbox-only search. */
  label: string | null
  onClear: () => void
}

export default function SearchResultsBanner({ total, label, onClear }: Props) {
  const { t } = useTranslation('mail')
  const text = total === null
    ? t('search.searching')
    : t(label ? 'search.resultsFor' : 'search.results', { count: total, label })

  return (
    <div className="search-results-banner">
      <SearchIcon size={15} />
      <span className="search-results-banner-text">{text}</span>
      <button type="button" className="search-results-banner-clear" aria-label={t('search.clear')} onClick={onClear}>
        <span aria-hidden="true">✕</span>
      </button>
    </div>
  )
}
