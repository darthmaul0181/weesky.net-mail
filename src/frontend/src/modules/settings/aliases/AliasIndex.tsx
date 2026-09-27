import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useGridNav } from '../../../hooks/useGridNav'
import CopyIcon from '../../../icons/CopyIcon'
import TrashIcon from '../../../icons/TrashIcon'
import type { AliasInfo } from '../../mail/api/mailTypes'
import type { IndexEntry, IndexLetter } from './aliasFamilies'

interface Props {
  letters: IndexLetter[]
  highlightedKey: string | null
  onHighlightEnd: () => void
  onCopy: (address: string) => void
  onDelete: (alias: AliasInfo) => void
}

/** The bubble hangs above its name unless the scroll box would cut it off there, and shifts to one
    side near an edge. Measured at the moment it opens, when the page has scrolled wherever it has. */
function placeBubble(cell: HTMLElement, area: HTMLElement | null) {
  const bubble = cell.querySelector<HTMLElement>('.alias-bubble')
  if (!bubble || !area) return
  const box = area.getBoundingClientRect(), at = cell.getBoundingClientRect(), size = bubble.getBoundingClientRect()
  cell.dataset.below = String(at.top - size.height - 12 < box.top)
  const middle = at.left + at.width / 2
  cell.dataset.align = middle - size.width / 2 < box.left ? 'start' : middle + size.width / 2 > box.right ? 'end' : 'centre'
}

/** One tab stop for the whole index: the arrows walk the names, F2 enters one to reach its copy
    and delete, Escape comes back out. A name keeps its width when its bubble opens, so nothing
    after it moves under the pointer. */
export default function AliasIndex({ letters, highlightedKey, onHighlightEnd, onCopy, onDelete }: Props) {
  const { t } = useTranslation('settings')
  const grid = useRef<HTMLDivElement>(null)
  const scrollRef = useRef<HTMLDivElement>(null)
  const letterRefs = useRef<Record<string, HTMLDivElement | null>>({})
  const [activeLetter, setActiveLetter] = useState('')
  useGridNav({ ref: grid, cellEntry: true })

  const available = letters.map(l => l.letter)
  const current = available.includes(activeLetter) ? activeLetter : (available[0] ?? '')

  function handleScroll() {
    const area = scrollRef.current
    if (!area) return
    const top = area.getBoundingClientRect().top
    let reached = available[0] ?? ''
    for (const letter of available) {
      const el = letterRefs.current[letter]
      if (el && el.getBoundingClientRect().top - top <= 8) reached = letter
    }
    if (reached !== activeLetter) setActiveLetter(reached)
  }

  function scrollToLetter(letter: string) {
    const el = letterRefs.current[letter], area = scrollRef.current
    if (el && area) area.scrollTop += el.getBoundingClientRect().top - area.getBoundingClientRect().top
  }

  function cell(entry: IndexEntry) {
    const place = (event: { currentTarget: HTMLElement }) => placeBubble(event.currentTarget, scrollRef.current)
    return (
      <div key={entry.key} role="gridcell" tabIndex={-1} aria-label={entry.key}
        className={entry.key === highlightedKey ? 'alias-cell is-new' : 'alias-cell'}
        onMouseEnter={place} onFocus={place}
        onAnimationEnd={entry.key === highlightedKey ? onHighlightEnd : undefined}>
        <span className="alias-cell-name">{entry.shown}</span>
        <span className="alias-bubble">
          <span className="alias-bubble-address">{entry.key}</span>
          <button type="button" className="admin-icon-btn" title={t('aliases.copy')}
            onClick={() => onCopy(entry.key)}><CopyIcon /></button>
          <button type="button" className="admin-icon-btn is-danger" title={t('actions.delete', { ns: 'common' })}
            onClick={() => onDelete(entry.alias)}><TrashIcon /></button>
        </span>
      </div>
    )
  }

  return (
    <div className="alias-view-wrapper">
      <div className="alias-scroll-area" ref={scrollRef} onScroll={handleScroll}>
        <div role="grid" aria-label={t('aliases.indexLabel')} className="alias-index" ref={grid}>
          {letters.map(({ letter, lines }) => (
            <div key={letter} role="rowgroup" className="alias-letter" ref={el => { letterRefs.current[letter] = el }}>
              {lines.map((line, i) => (
                <div key={line.prefix} role="row" className="alias-line">
                  <div role="rowheader" className="alias-line-head" aria-label={line.prefix
                    ? t('aliases.familyHeader', { letter, prefix: line.prefix, size: line.entries.length })
                    : letter}>
                    {i === 0 && <span className="alias-letter-mark" aria-hidden="true">{letter}</span>}
                    {line.prefix && (
                      <span className="alias-family" aria-hidden="true">{line.prefix}… ×{line.entries.length}</span>
                    )}
                  </div>
                  {line.entries.map(cell)}
                </div>
              ))}
            </div>
          ))}
        </div>
      </div>
      <nav className="alpha-nav" aria-label={t('aliases.lettersLabel')}>
        {available.map(letter => (
          <button key={letter} type="button" onClick={() => scrollToLetter(letter)}
            className={`alpha-nav-letter${current === letter ? ' is-active' : ''}`}>
            {letter}
          </button>
        ))}
      </nav>
    </div>
  )
}
