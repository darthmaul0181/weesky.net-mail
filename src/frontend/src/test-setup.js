import { beforeEach, vi } from 'vitest'
import '@testing-library/jest-dom'
import { configure } from '@testing-library/react'
import i18next from 'i18next'
import { initReactI18next } from 'react-i18next'
import en from './locales/en'
import fr from './locales/fr'
import { I18N_OPTIONS } from './lib/i18n'

// English, synchronously, with the real catalogues. The suite carries 1817 assertions against
// visible text; initialising in English is what lets every one of them stand unchanged, and makes
// the whole suite the English catalogue's coverage as a side effect. `initImmediate: false` is
// what makes init synchronous — this is the one place lib/i18n.ts's dynamic import is bypassed,
// because a test must not await a catalogue before it can query the DOM. I18N_OPTIONS is shared
// with lib/i18n.ts so `fallbackLng`/`defaultNS`/escaping cannot drift between the two.
//
// The French bundle is loaded too, alongside English rather than instead of it: the active
// language stays English, so no existing assertion moves, but a test that calls
// `i18next.changeLanguage('fr')` to exercise locale-following behaviour finds real translations
// rather than raw keys — the production `loadLocale` fetches it lazily; a test has no such step.
i18next.use(initReactI18next).init({
  ...I18N_OPTIONS,
  lng: 'en',
  resources: { en, fr },
  initImmediate: false,
})

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
