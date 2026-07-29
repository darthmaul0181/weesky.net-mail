import { beforeEach, vi } from 'vitest'
import '@testing-library/jest-dom'
import { configure } from '@testing-library/react'

// Testing Library's 1000ms default is calibrated for a synchronous render. Six routes here sit
// behind `lazy()`, so a test that asserts on one waits for a dynamic import as well — which,
// when the whole suite runs in parallel workers on a loaded machine, crosses that second and
// reddens a test that passes alone and passes again on the next run. Measured, not guessed:
// the route that failed at the 1000ms budget resolves in ~575ms on an idle box.
// The cost is that a genuinely broken assertion now takes five seconds to give up.
configure({ asyncUtilTimeout: 5000 })

Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: vi.fn().mockImplementation(query => ({
    matches: false,
    media: query,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  })),
})

// jsdom has no IntersectionObserver. Tests drive this one by hand:
//   IntersectionObserver.instances[0].trigger()
class FakeIntersectionObserver {
  static instances = []

  constructor(callback, options) {
    this.callback = callback
    this.options = options
    FakeIntersectionObserver.instances.push(this)
  }

  observe(element) { this.element = element }
  unobserve() {}
  disconnect() {
    FakeIntersectionObserver.instances =
      FakeIntersectionObserver.instances.filter(observer => observer !== this)
  }

  trigger(isIntersecting = true) {
    this.callback([{ isIntersecting, target: this.element }])
  }
}

window.IntersectionObserver = FakeIntersectionObserver
globalThis.IntersectionObserver = FakeIntersectionObserver

beforeEach(() => { FakeIntersectionObserver.instances = [] })
