import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles/tokens.css'
import './styles/theme-night.css'
import './styles/theme-classic.css'
import './styles/theme-forest.css'
import './styles/theme-slate.css'
import './styles/theme-plum.css'
import './styles/theme-ink.css'
import './styles/theme-azure.css'
import './styles/theme-indigo.css'
import './index.css'
import './styles/contacts.css'
import './styles/modal.css'
import './styles/selection.css'
import './styles/shell.css'
import './styles/tooltip.css'
import './styles/mail.css'
import './styles/calendar.css'
import App from './App'
import { initI18n } from './lib/i18n'
import { readLanguageMirror, resolveLocale } from './lib/locale'

// Awaited before the first render, or the app paints its own keys; `.then`, not top-level await,
// so the bundle needs no TLA target. The `.catch` covers a hashed chunk failing to load (a
// redeploy under an open tab), which would otherwise leave every route a blank document.
void initI18n(resolveLocale(undefined, readLanguageMirror(), navigator.languages))
  .then(() => {
    createRoot(document.getElementById('root')!).render(
      <StrictMode>
        <App />
      </StrictMode>,
    )
  })
  .catch(() => {
    document.getElementById('root')!.innerHTML =
      '<p style="padding:2rem;font:16px system-ui">Something went wrong loading the app. ' +
      '<button onclick="location.reload()">Reload</button></p>'
  })
