import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './styles/tokens.css'
import './styles/theme-night.css'
import './styles/theme-classic.css'
import './styles/theme-forest.css'
import './styles/theme-slate.css'
import './styles/theme-plum.css'
import './styles/theme-ink.css'
import './index.css'
import './styles/shell.css'
import './styles/tooltip.css'
import './styles/mail.css'
import App from './App'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
