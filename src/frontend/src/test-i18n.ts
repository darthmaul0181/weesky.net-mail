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
