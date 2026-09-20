import { useState } from 'react'
import type { KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import ChevronRightIcon from '../../../icons/ChevronRightIcon'
import { hasOpenLayer } from '../../../lib/layerStack'

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
    // Escape belongs to whatever layer is open — the caret can still be here while a menu stands,
    // since Safari focuses no button on a click. Otherwise it must not also clear the selection.
    if (event.key === 'Escape' && !hasOpenLayer()) { event.stopPropagation(); onClose() }
  }

  return (
    <div className="search-bar">
      <input
        type="search"
        className="search-input"
        placeholder={t('search.placeholder', { folder: folderTitle })}
        aria-label={t('search.placeholder', { folder: folderTitle })}
        value={text}
        // eslint-disable-next-line jsx-a11y/no-autofocus -- documented exception: the quick search bar autofocuses when it opens, not a dialog Modal's own focus management
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
