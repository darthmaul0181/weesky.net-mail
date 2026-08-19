import { useState } from 'react'
import type { KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import ChevronRightIcon from '../../../icons/ChevronRightIcon'

interface Props {
  folderTitle: string
  onSearch: (text: string) => void
  onOpenAdvanced: (text: string) => void
  onClose: () => void
}

/** The collapsible quick-search band. Enter searches subject OR sender in the open folder. */
export default function SearchBar({ folderTitle, onSearch, onOpenAdvanced, onClose }: Props) {
  const { t } = useTranslation('mail')
  const [text, setText] = useState('')

  function onKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Enter' && text.trim()) onSearch(text.trim())
    // Escape must not also clear the list selection behind the bar.
    if (event.key === 'Escape') { event.stopPropagation(); onClose() }
  }

  return (
    <div className="search-bar">
      <input
        type="search"
        className="search-input"
        placeholder={t('search.placeholder', { folder: folderTitle })}
        value={text}
        autoFocus
        onChange={event => setText(event.target.value)}
        onKeyDown={onKeyDown}
      />
      <button
        type="button"
        className="search-bar-advanced"
        aria-label={t('search.advanced.title')}
        title={t('search.advanced.title')}
        onClick={() => onOpenAdvanced(text)}
      >
        <ChevronRightIcon size={16} />
      </button>
    </div>
  )
}
