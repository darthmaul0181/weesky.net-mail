import { afterEach, beforeEach, vi } from 'vitest'
import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import i18next from 'i18next'
import { initReactI18next } from 'react-i18next'
import en from './locales/en'
import fr from './locales/fr'
import { I18N_OPTIONS } from './lib/i18n'

// English, synchronously (`initImmediate: false`), against the real catalogues and the shared
// I18N_OPTIONS: a test must not await a catalogue. French is loaded alongside, so a test calling
// `changeLanguage('fr')` finds real strings (CLAUDE.md, Localisation).
void i18next.use(initReactI18next).init({
  ...I18N_OPTIONS,
  lng: 'en',
  resources: { en, fr },
  initImmediate: false,
})

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
