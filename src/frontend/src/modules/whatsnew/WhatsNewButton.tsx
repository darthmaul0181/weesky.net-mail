import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { useDismiss } from '../../hooks/useDismiss'
import SparkleIcon from '../../icons/SparkleIcon'
import WhatsNewList from './WhatsNewList'
import { useWhatsNew } from './useWhatsNew'

const SHOWN = 10

/** The sparkle at the right of the top bar: a dot while a novelty is unlisted, the list on click;
    gone once no novelty is active, but never from under an open list. */
export default function WhatsNewButton() {
  const { t } = useTranslation('whatsNew')
  const { fresh, active, unlistedCount, markListed } = useWhatsNew()
  const shown = fresh.slice(0, SHOWN)
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const buttonRef = useRef<HTMLButtonElement>(null)
  const close = () => setOpen(false)
  useDismiss({ open, rootRef, onDismiss: close, refocusRef: buttonRef })

  const toggle = () => {
    if (!open) markListed(shown.map(n => n.entry.id))
    setOpen(!open)
  }

  if (active.length === 0 && !open) return null

  return (
    <div ref={rootRef} className="whats-new-root">
      <button
        ref={buttonRef}
        type="button"
        className="whats-new-button"
        aria-label={unlistedCount > 0 ? t('buttonCount', { count: unlistedCount }) : t('button')}
        title={t('button')}
        aria-expanded={open}
        onClick={toggle}
      >
        <SparkleIcon />
        {unlistedCount > 0 && <span className="new-dot" aria-hidden="true" />}
      </button>
      {open && (
        <section className="whats-new-popover" aria-labelledby="whats-new-title">
          <h2 id="whats-new-title">{t('title')}</h2>
          <WhatsNewList novelties={shown} onShow={close} />
          <Link to="/settings/whats-new" className="whats-new-all" onClick={close}>{t('seeAll')}</Link>
        </section>
      )}
    </div>
  )
}
