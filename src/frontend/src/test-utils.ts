import { act, fireEvent } from '@testing-library/react'
import { QueryClient, QueryClientProvider, type DefaultOptions } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import type { Mock } from 'vitest'
import type { Viewport } from './hooks/useViewport'

/** The one query client every test builds: retries off by default (a mocked rejection would
    otherwise sit behind TanStack's own backoff), any other default named explicitly. */
export function createTestQueryClient(defaultOptions: DefaultOptions = {}): QueryClient {
  return new QueryClient({
    defaultOptions: { ...defaultOptions, queries: { retry: false, ...defaultOptions.queries } },
  })
}

/** A `wrapper` for `render`/`renderHook` around one client, so a test that needs the instance
    (`setQueryData`, a spy, a rerender) still shares this one construction. */
export function withQueryClient(client: QueryClient = createTestQueryClient()) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return createElement(QueryClientProvider, { client }, children)
  }
}

/** A dismissing press on a dialog's backdrop, both halves landing on it. Queried by class, not
 * role: `ContextDrawer`'s scrim and `MessageList`'s root also carry `role="presentation"`. */
export function pressBackdrop() {
  const backdrop = document.querySelector('.modal-overlay') as HTMLElement
  fireEvent.mouseDown(backdrop)
  fireEvent.mouseUp(backdrop)
  fireEvent.click(backdrop)
}

/** A macrotask boundary, draining every pending microtask (TanStack notifies on one): a silence
 * assertion made before it holds against any implementation, even one firing every render. */
export async function settle() {
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 0)) })
}

const VIEWPORT_WIDTH: Record<Viewport, number> = { phone: 360, tablet: 768, desktop: 1280 }

// jsdom answers no media query on its own and test-setup.ts stubs every one to matches:false,
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
  })
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

/** jsdom has no `TouchEvent`; a plain `Event` carrying `touches` is what `usePullToRefresh` reads.
 * Shared by its test and `MessageList`'s so the two cannot drift. No `act()` here: callers batch
 * a gesture into one or split it. */
export function fireTouch(element: HTMLElement, type: string, y: number) {
  const event = new Event(type, { bubbles: true, cancelable: true })
  Object.defineProperty(event, 'touches', { value: [{ clientY: y }] })
  element.dispatchEvent(event)
}

/** jsdom has no `PointerEvent` and no capture calls: a `MouseEvent` with a `pointerId` is all a
 * gesture reads, and the stubs stop `setPointerCapture` throwing under the first drag. */
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

/** Escape on `document`, where every gesture listens; cancelable, as the layer stack's contract
 * needs, so the returned event's `defaultPrevented` tells whether a layer swallowed it. Wrapped
 * like the pointer helpers, so the abandonment is on screen before it is asserted. */
export function fireEscape() {
  const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })
  act(() => { document.dispatchEvent(event) })
  return event
}

/** Holds a mock's next call in flight until the test settles it, so the pending state a real
    network shows between a request and its answer is rendered rather than batched away. */
export function holdNextCall(mock: Mock) {
  let settle: { resolve: (value: unknown) => void; reject: (reason: Error) => void } = {
    resolve: () => {}, reject: () => {},
  }
  mock.mockImplementationOnce(() => new Promise((resolve, reject) => { settle = { resolve, reject } }))
  return {
    resolve: (value: unknown) => settle.resolve(value),
    fail: () => settle.reject(new Error('Server error')),
  }
}
