import 'i18next'
import type en from '../locales/en'

/** A key that does not exist becomes a compile error. `tsc --noEmit` already runs in CI, so
    this is what replaces a key-extraction tool rather than adding one. */
declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: 'common'
    resources: typeof en
  }
}
