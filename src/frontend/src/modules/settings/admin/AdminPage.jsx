import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useToasts } from '../../../hooks/useToasts.js'
import Toasts from '../../../components/Toasts.jsx'
import HelpTooltip from '../../../components/HelpTooltip.jsx'
import ShieldIcon from '../../../icons/ShieldIcon.jsx'
import AccountsTab from './AccountsTab.jsx'
import DomainsTab from './DomainsTab.jsx'
import VirtualDomainsTab from './VirtualDomainsTab.jsx'
import ExternalDomainsTab from './ExternalDomainsTab'
import ApplicationTab from './ApplicationTab'

/** Each tab is its own literal `t()` call: a key held in a table and read by variable is
    invisible to `src/locales/keys.test.ts`, which is what let `help.accounts` go missing. */
function helpTextOf(tab, t) {
  switch (tab) {
    case 'accounts': return t('help.accounts')
    case 'domains': return t('help.domains')
    case 'virtualdomains': return t('help.virtualdomains')
    case 'externaldomains': return t('help.externaldomains')
    case 'application': return t('help.application')
    default: return null
  }
}

export default function AdminPage() {
  const { t } = useTranslation('admin')
  const { toasts, addToast, removeToast, pauseToast, resumeToast } = useToasts()
  const [activeTab, setActiveTab] = useState('accounts')
  const helpText = helpTextOf(activeTab, t)
  // Where a confirmed delete hands focus when it takes its own row with it. The page's own region
  // and not each tab's: a tab reloads its list behind a spinner, so nothing inside one survives.
  const tabRegion = useRef(null)

  return (
    <>
      <div className="settings-page admin-page">
        <div className="settings-page-header">
          <h1 className="settings-page-title"><ShieldIcon size={17} />{t('title')}</h1>
        </div>
        <div className="admin-modal-body">
          <nav className="admin-tab-bar">
            <button className={`admin-tab${activeTab === 'accounts' ? ' is-active' : ''}`}
              onClick={() => setActiveTab('accounts')}>{t('tabs.accounts')}</button>
            <button className={`admin-tab${activeTab === 'domains' ? ' is-active' : ''}`}
              onClick={() => setActiveTab('domains')}>{t('tabs.domains')}</button>
            <button className={`admin-tab${activeTab === 'virtualdomains' ? ' is-active' : ''}`}
              onClick={() => setActiveTab('virtualdomains')}>{t('tabs.virtualdomains')}</button>
            <button className={`admin-tab${activeTab === 'externaldomains' ? ' is-active' : ''}`}
              onClick={() => setActiveTab('externaldomains')}>{t('tabs.externaldomains')}</button>
            <button className={`admin-tab${activeTab === 'application' ? ' is-active' : ''}`}
              onClick={() => setActiveTab('application')}>{t('tabs.application')}</button>
          </nav>
          <div className="admin-tab-content" ref={tabRegion} tabIndex={-1}>
            {activeTab === 'accounts' && <AccountsTab addToast={addToast} returnFocusRef={tabRegion} />}
            {activeTab === 'domains' && <DomainsTab addToast={addToast} returnFocusRef={tabRegion} />}
            {activeTab === 'virtualdomains' && <VirtualDomainsTab addToast={addToast} />}
            {activeTab === 'externaldomains' && (
              <ExternalDomainsTab addToast={addToast} returnFocusRef={tabRegion} />
            )}
            {activeTab === 'application' && <ApplicationTab addToast={addToast} />}
          </div>
        </div>
        {helpText && (
          <div className="admin-modal-help">
            <HelpTooltip text={helpText} />
          </div>
        )}
      </div>

      <Toasts toasts={toasts} onRemove={removeToast} onPause={pauseToast} onResume={resumeToast} />
    </>
  )
}
