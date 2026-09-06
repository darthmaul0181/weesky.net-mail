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

/**
 * jsdom has no `TouchEvent` constructor; a plain `Event` carrying a `touches` array is what
 * `usePullToRefresh` actually reads, and it dispatches through the same listeners. Shared between
 * its own test and `MessageList`'s so the two cannot drift on what a touch event needs to carry —
 * does not wrap in `act()` itself, since callers batch a whole gesture into one `act()` or split
 * it across several depending on what they are testing.
 */
export function fireTouch(element: HTMLElement, type: string, y: number) {
  const event = new Event(type, { bubbles: true, cancelable: true })
  Object.defineProperty(event, 'touches', { value: [{ clientY: y }] })
  element.dispatchEvent(event)
}

/**
 * jsdom carries no `PointerEvent` constructor and leaves the three capture calls undefined on
 * every element. A `MouseEvent` with a `pointerId` is the whole of what a gesture reads, and the
 * capture stubs are what stop `setPointerCapture` throwing under the first drag.
 */
class SyntheticPointerEvent extends MouseEvent {
  pointerId: number

  constructor(type: string, init: MouseEventInit & { pointerId?: number } = {}) {
    super(type, init)
    this.pointerId = init.pointerId ?? 1
  }
}

export function installPointerEvents() {
  const global = globalThis as { PointerEvent?: unknown }
  global.PointerEvent ??= SyntheticPointerEvent
  const proto = HTMLElement.prototype as unknown as Record<string, unknown>
  proto.setPointerCapture ??= function setPointerCapture() {}
  proto.releasePointerCapture ??= function releasePointerCapture() {}
  proto.hasPointerCapture ??= function hasPointerCapture() { return true }
}

/** One move, release or cancellation of a gesture in flight — dispatched on `window`, which is
    where every one of these hooks listens once the pointer is down. */
export function firePointer(type: 'pointermove' | 'pointerup' | 'pointercancel', x = 0, y = 0) {
  act(() => {
    window.dispatchEvent(new SyntheticPointerEvent(type, { clientX: x, clientY: y }))
  })
}

/** The React synthetic a `onPointerDown` prop is called with: the six fields the gestures read,
    and nothing invented around them. */
export function pointerDownOn(element: HTMLElement, x = 0, y = 0, target: HTMLElement = element) {
  return {
    button: 0, pointerId: 1, clientX: x, clientY: y, currentTarget: element, target,
  } as unknown as import('react').PointerEvent
}

/** Escape as a gesture in flight hears it: on `document`, which is where each of them listens.
    Wrapped like the pointer helpers, so the abandonment is on screen before it is asserted. */
export function fireEscape() {
  act(() => { document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })) })
}
