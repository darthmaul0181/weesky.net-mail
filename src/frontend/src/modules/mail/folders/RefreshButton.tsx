import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import LoaderIcon from '../../../icons/LoaderIcon'

interface Props {
  /** True while the folders query fetches — a manual click and the 60s poll tick alike. */
  fetching: boolean
  onRefresh: () => void
}

// Always finishes the turn it started, so a fast answer still reads as one rotation: the class is
// released at the animation's iteration boundary, with a timer for reduced motion, where none comes.
export default function RefreshButton({ fetching, onRefresh }: Props) {
  const { t } = useTranslation('mail')
  // Latched while a fetch runs, so the turn outlives it until the release below.
  const [lingering, setLingering] = useState(false)
  if (fetching && !lingering) setLingering(true)
  const spinning = fetching || lingering
  const fetchingRef = useRef(fetching)
  const iconRef = useRef<HTMLSpanElement>(null)

  useEffect(() => {
    fetchingRef.current = fetching
    if (fetching) return
    const fallback = setTimeout(() => setLingering(false), 800)
    return () => clearTimeout(fallback)
  }, [fetching])

  // A native listener, not React's onAnimationIteration: the synthetic animation events rest
  // on feature detection that fails outside a real browser, and the ref reads the latest
  // fetching without re-binding per render.
  useEffect(() => {
    const el = iconRef.current
    if (!el) return
    const release = () => { if (!fetchingRef.current) setLingering(false) }
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
