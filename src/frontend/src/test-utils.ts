import { act } from '@testing-library/react'
import type { Viewport } from './hooks/useViewport'

/**
 * A macrotask boundary, which drains every pending microtask. TanStack v5 notifies its observers
 * on one, and effects fire at the end of an await chain: a silence assertion made before that
 * drains holds against any implementation whatsoever, including one that fires on every render.
 */
export async function settle() {
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 0)) })
}

const VIEWPORT_WIDTH: Record<Viewport, number> = { phone: 360, tablet: 768, desktop: 1280 }

// jsdom answers no media query on its own and test-setup.js stubs every one to matches:false,
// which is what keeps the whole existing suite on the desktop layout. These helpers replace that
// stub for one file at a time; resetViewport puts the original back.
const original = window.matchMedia
const listeners = new Set<() => void>()
let width = VIEWPORT_WIDTH.desktop
let installed = false

/** Puts the environment in one tier. Call before rendering. */
export function mockViewport(tier: Viewport) {
  width = VIEWPORT_WIDTH[tier]
  if (installed) return
  installed = true
  window.matchMedia = ((query: string) => {
    const limit = Number(/max-width:\s*(\d+)px/.exec(query)?.[1] ?? NaN)
    return {
      // A getter, not a value: the same MediaQueryList object is read again after a tier change.
      get matches() { return Number.isNaN(limit) ? false : width <= limit },
      media: query,
      addEventListener: (_event: string, fn: () => void) => { listeners.add(fn) },
      removeEventListener: (_event: string, fn: () => void) => { listeners.delete(fn) },
    } as unknown as MediaQueryList
  }) as typeof window.matchMedia
}

/** Changes tier after a render — a rotation — and lets the subscribers react. */
export async function changeViewport(tier: Viewport) {
  mockViewport(tier)
  await act(async () => { listeners.forEach(fn => fn()) })
}

/** Restores the suite-wide stub. Call in afterEach of any file using the two above. */
export function resetViewport() {
  window.matchMedia = original
  listeners.clear()
  installed = false
  width = VIEWPORT_WIDTH.desktop
}

/** How many subscribers the fake matchMedia is currently holding. A hook that leaks its
    listener on unmount is invisible any other way: React 18 no longer warns on it. */
export function viewportListenerCount() {
  return listeners.size
}
