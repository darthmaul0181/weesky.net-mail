import './test-runtime-config'
import './test-polyfills'
import { afterEach, beforeEach, vi } from 'vitest'
import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import './test-i18n'

// A frozen catalogue: a novelty added to the real one moves no test. catalog.test reads the real one.
vi.mock('./modules/whatsnew/catalog', async () => ({ WHATS_NEW: (await import('./modules/whatsnew/testCatalog')).TEST_CATALOG }))

// Six routes sit behind `lazy()`: in a loaded parallel run their import crossed the 1000ms default
// (measured ~575ms idle) and reddened tests at random. The cost: a broken assertion takes 5s.
configure({ asyncUtilTimeout: 5000 })

Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: vi.fn().mockImplementation((query: string) => ({
    matches: false,
    media: query,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  })),
})

type IntersectionCallback = (entries: Array<{ isIntersecting: boolean; target?: Element }>) => void

// jsdom has no IntersectionObserver. Tests drive this one by hand:
//   IntersectionObserver.instances[0].trigger()
class FakeIntersectionObserver {
  static instances: FakeIntersectionObserver[] = []

  callback: IntersectionCallback
  options?: IntersectionObserverInit
  element?: Element

  constructor(callback: IntersectionCallback, options?: IntersectionObserverInit) {
    this.callback = callback
    this.options = options
    FakeIntersectionObserver.instances.push(this)
  }

  observe(element: Element) { this.element = element }
  unobserve() {}
  disconnect() {
    FakeIntersectionObserver.instances =
      FakeIntersectionObserver.instances.filter(observer => observer !== this)
  }

  trigger(isIntersecting = true) {
    this.callback([{ isIntersecting, target: this.element }])
  }
}

// A hand-rolled double narrower than the real constructor type; nothing here treats it as one.
window.IntersectionObserver = FakeIntersectionObserver as unknown as typeof IntersectionObserver
globalThis.IntersectionObserver = FakeIntersectionObserver as unknown as typeof IntersectionObserver

beforeEach(() => { FakeIntersectionObserver.instances = [] })
afterEach(cleanup)
