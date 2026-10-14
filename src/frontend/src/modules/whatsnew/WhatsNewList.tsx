import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import type { Novelty } from './model'

/** Shared by the top-bar list and the history page. `onShow` lets the list close before it leaves. */
export default function WhatsNewList({ novelties, onShow }: { novelties: Novelty[]; onShow?: () => void }) {
  const { t } = useTranslation('whatsNew')
  const navigate = useNavigate()
  return (
    <ul className="whats-new-list">
      {novelties.map(({ entry, active }) => (
        <li key={entry.id} className={active ? 'whats-new-item is-active' : 'whats-new-item'}>
          {active && <span className="new-dot" role="img" aria-label={t('new')} />}
          <div className="whats-new-text">
            <h3>{t(`items.${entry.id}.title`)}</h3>
            <p>{t(`items.${entry.id}.body`)}</p>
            {entry.href && (
              <button
                type="button"
                className={active ? 'btn btn-primary btn-auto' : 'btn btn-ghost'}
                onClick={() => { onShow?.(); void navigate(entry.href!, { state: { whatsNew: entry.id } }) }}
              >
                {t('showMe')}
              </button>
            )}
          </div>
        </li>
      ))}
    </ul>
  )
}
