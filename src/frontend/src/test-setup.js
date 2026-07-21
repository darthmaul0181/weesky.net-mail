import { beforeEach, vi } from 'vitest'
import '@testing-library/jest-dom'

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
