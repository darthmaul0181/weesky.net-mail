import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate } from 'react-router'
import { useMarkWhatsNew, useWhatsNew } from './useWhatsNew'

interface Props {
  id: string
  /** The block's own class, so the frame is the block's box rather than a column-wide wrapper. */
  className?: string
  children: ReactNode
}

/** Frames the block a novelty leads to, and reports it seen once half of it is on screen. */
export default function WhatsNewTarget({ id, className, children }: Props) {
  const { t } = useTranslation('whatsNew')
  // Any novelty, seen ones too: the history's Show me still leads here, only the frame is for the active one.
  const novelty = useWhatsNew().targetOf(id, false)
  const entryId = novelty?.entry.id
  const activeId = novelty?.active ? entryId : undefined
  const mark = useMarkWhatsNew()
  const navigate = useNavigate()
  const location = useLocation()
  const ledHere = (location.state as { whatsNew?: string } | null)?.whatsNew
  const ref = useRef<HTMLDivElement>(null)
  // Latched during render: the frame outlives the "seen" it triggers, until the page is left.
  const [framed, setFramed] = useState(false)
  if (activeId && !framed) setFramed(true)

  useEffect(() => {
    const element = ref.current
    if (!activeId || !element) return
    const observer = new IntersectionObserver(([entry]) => {
      if (!entry?.isIntersecting) return
      observer.disconnect()
      mark([activeId], 'seen')
    }, { threshold: 0.5 })
    observer.observe(element)
    return () => observer.disconnect()
  }, [activeId, mark])

  // Each Show me is a new entry, so it scrolls again; the state is spent so reload and Back do not.
  useEffect(() => {
    if (!entryId || ledHere !== entryId) return
    const smooth = !window.matchMedia('(prefers-reduced-motion: reduce)').matches
    ref.current?.scrollIntoView({ block: 'center', behavior: smooth ? 'smooth' : 'auto' })
    const { pathname, search, hash } = location
    void navigate({ pathname, search, hash }, { replace: true, state: null })
  }, [entryId, ledHere, location, navigate])

  const classes = ['whats-new-target', className, framed && 'is-new'].filter(Boolean).join(' ')
  return (
    <div ref={ref} className={classes}>
      {children}
      {framed && <span className="new-tag">{t('new')}</span>}
    </div>
  )
}
