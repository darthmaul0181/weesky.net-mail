import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import LoaderIcon from '../../../icons/LoaderIcon'

interface Props {
  /** True while the folders query fetches — a manual click and the 60s poll tick alike. */
  fetching: boolean
  onRefresh: () => void
}

/**
 * The manual face of the poll, beside the compose button. The icon spins while the folders
 * query fetches and always finishes the rotation it started, so a fast answer still reads as
 * one full turn: the class releases at the animation's iteration boundary, with a timer as
 * the fallback for environments where the animation never iterates (reduced motion).
 */
export default function RefreshButton({ fetching, onRefresh }: Props) {
  const { t } = useTranslation('mail')
  const [spinning, setSpinning] = useState(false)
  const fetchingRef = useRef(fetching)
  const iconRef = useRef<HTMLSpanElement>(null)

  useEffect(() => {
    fetchingRef.current = fetching
    if (fetching) { setSpinning(true); return }
    const fallback = setTimeout(() => setSpinning(false), 800)
    return () => clearTimeout(fallback)
  }, [fetching])

  // A native listener, not React's onAnimationIteration: the synthetic animation events rest
  // on feature detection that fails outside a real browser, and the ref reads the latest
  // fetching without re-binding per render.
  useEffect(() => {
    const el = iconRef.current
    if (!el) return
    const release = () => { if (!fetchingRef.current) setSpinning(false) }
    el.addEventListener('animationiteration', release)
    return () => el.removeEventListener('animationiteration', release)
  }, [])

  return (
    <button type="button" className="btn btn-primary column-actions-square"
      aria-label={t('folders.refresh')}
      title={t('folders.refreshHint')}
      onClick={() => { if (!spinning) onRefresh() }}>
      <span ref={iconRef} className={`mail-refresh-icon${spinning ? ' is-spinning' : ''}`}>
        <LoaderIcon size={16} />
      </span>
    </button>
  )
}
