import SearchIcon from '../../../icons/SearchIcon'

interface Props {
  /** Null while the search is in flight — the banner still shows so Clear stays reachable. */
  total: number | null
  /** The quoted text, or null for a checkbox-only search. */
  label: string | null
  onClear: () => void
}

export default function SearchResultsBanner({ total, label, onClear }: Props) {
  const text = total === null
    ? 'Searching…'
    : `${total} result${total === 1 ? '' : 's'}${label ? ` for “${label}”` : ''}`

  return (
    <div className="search-results-banner">
      <SearchIcon size={15} />
      <span className="search-results-banner-text">{text}</span>
      <button type="button" className="search-results-banner-clear" aria-label="Clear" onClick={onClear}>
        <span aria-hidden="true">✕</span>
      </button>
    </div>
  )
}
