import { useTranslation } from 'react-i18next'
import ChevronRightIcon from '../../../icons/ChevronRightIcon'
import { buildPageList } from './pageList'

interface Props {
  page: number
  lastPage: number
  onSelect: (page: number) => void
}

/**
 * Numbered pager. Page numbers are one-based on screen and zero-based in the props, because
 * that is the split the rest of the module already lives with: the API pages from zero, and
 * nobody reading a mailbox thinks of the first page as page 0.
 */
export default function Pagination({ page, lastPage, onSelect }: Props) {
  const { t } = useTranslation('mail')
  if (lastPage < 1) return null

  return (
    <nav className="pager" aria-label={t('pager.label')}>
      <button
        type="button"
        className="pager-step"
        aria-label={t('pager.previous')}
        disabled={page === 0}
        onClick={() => onSelect(page - 1)}
      >
        <span className="pager-step-back"><ChevronRightIcon /></span>
      </button>

      {buildPageList(page, lastPage).map((item, index) =>
        item === 'gap' ? (
          // Presentational: a screen reader announcing "ellipsis" between two page numbers
          // adds nothing a reader of the list needs.
          <span key={`gap-${index}`} className="pager-gap" aria-hidden="true">…</span>
        ) : (
          <button
            key={item}
            type="button"
            className={item === page ? 'pager-page is-current' : 'pager-page'}
            aria-label={t('pager.page', { page: item + 1 })}
            aria-current={item === page ? 'page' : undefined}
            onClick={() => onSelect(item)}
          >
            {item + 1}
          </button>
        ))}

      <button
        type="button"
        className="pager-step"
        aria-label={t('pager.next')}
        disabled={page >= lastPage}
        onClick={() => onSelect(page + 1)}
      >
        <ChevronRightIcon />
      </button>
    </nav>
  )
}
