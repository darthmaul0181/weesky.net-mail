import { useEffect, useState } from 'react'

export type Viewport = 'phone' | 'tablet' | 'desktop'

// The one place these two widths exist in JavaScript. They mirror the stylesheets' only two
// media widths; changing one without the other splits the layout from the mounting decision.
const PHONE = '(max-width: 639px)'
const TABLET = '(max-width: 1023px)'

function read(): Viewport {
  if (typeof window.matchMedia !== 'function') return 'desktop'
  if (window.matchMedia(PHONE).matches) return 'phone'
  if (window.matchMedia(TABLET).matches) return 'tablet'
  return 'desktop'
}

/**
 * Which tier the viewport is in. It decides what MOUNTS — which pane, whether the splitter
 * exists, whether the drawer traps focus — and never how wide anything is: a width computed
 * here would be a second source of truth beside the stylesheet, and the two would drift.
 *
 * Without matchMedia it answers 'desktop', which is the layout that exists today rather than
 * a blank screen.
 */
export function useViewport(): Viewport {
  const [viewport, setViewport] = useState(read)

  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return
    const queries = [window.matchMedia(PHONE), window.matchMedia(TABLET)]
    const apply = () => setViewport(read())
    queries.forEach(query => query.addEventListener('change', apply))
    // The first read happened during useState; this catches a change between render and effect.
    apply()
    return () => queries.forEach(query => query.removeEventListener('change', apply))
  }, [])

  return viewport
}
