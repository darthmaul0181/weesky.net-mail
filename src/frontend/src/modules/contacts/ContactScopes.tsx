import { useTranslation } from 'react-i18next'
import ContactsIcon from '../../icons/ContactsIcon'
import StarIcon from '../../icons/StarIcon'

export type ContactScope = 'all' | 'favorites'

interface Props {
  scope: ContactScope
  total: number
  favorites: number
  onScope: (scope: ContactScope) => void
}

/**
 * The module's navigation band, on the same surface as the mail folder tree and the settings
 * context pane. It marks its active row with a fill and heavier weight and **no accent bar**: the
 * bar belongs to content lists, and keeping the two languages apart is how a reader tells a
 * navigation pane from a list of rows at a glance.
 *
 * Two scopes today, with import and export in the column's footer below and CardDAV address books
 * the next thing to land here — the reason the module has a band at all rather than starting flush
 * against the rail.
 */
export default function ContactScopes({ scope, total, favorites, onScope }: Props) {
  const { t } = useTranslation('contacts')

  return (
    <nav className="contact-scopes">
      <button type="button" className={`contact-scope${scope === 'all' ? ' is-active' : ''}`}
        aria-current={scope === 'all' ? 'true' : undefined}
        onClick={() => onScope('all')}>
        <ContactsIcon size={15} />
        <span className="contact-scope-label">{t('scopes.all')}</span>
        <span className="contact-scope-count">{total}</span>
      </button>
      <button type="button" className={`contact-scope${scope === 'favorites' ? ' is-active' : ''}`}
        aria-current={scope === 'favorites' ? 'true' : undefined}
        onClick={() => onScope('favorites')}>
        <StarIcon size={15} />
        <span className="contact-scope-label">{t('scopes.favourites')}</span>
        <span className="contact-scope-count">{favorites}</span>
      </button>
    </nav>
  )
}
