import { useState } from 'react'
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

// The Accounts tab explains itself; the other four earn a help bubble.
const TABS_WITH_HELP = ['domains', 'virtualdomains', 'externaldomains', 'application']

export default function AdminPage() {
  const { t } = useTranslation('admin')
  const { toasts, addToast, removeToast } = useToasts()
  const [activeTab, setActiveTab] = useState('accounts')

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
          <div className="admin-tab-content">
            {activeTab === 'accounts' && <AccountsTab addToast={addToast} />}
            {activeTab === 'domains' && <DomainsTab addToast={addToast} />}
            {activeTab === 'virtualdomains' && <VirtualDomainsTab addToast={addToast} />}
            {activeTab === 'externaldomains' && <ExternalDomainsTab addToast={addToast} />}
            {activeTab === 'application' && <ApplicationTab addToast={addToast} />}
          </div>
        </div>
        {TABS_WITH_HELP.includes(activeTab) && (
          <div className="admin-modal-help">
            <HelpTooltip text={t(`help.${activeTab}`)} />
          </div>
        )}
      </div>

      <Toasts toasts={toasts} onRemove={removeToast} />
    </>
  )
}
