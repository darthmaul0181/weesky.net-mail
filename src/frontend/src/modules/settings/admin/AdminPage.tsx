import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import { useToasts } from '../../../hooks/useToasts'
import Toasts from '../../../components/Toasts'
import HelpTooltip from '../../../components/HelpTooltip'
import ShieldIcon from '../../../icons/ShieldIcon'
import AccountsTab from './AccountsTab'
import DomainsTab from './DomainsTab'
import VirtualDomainsTab from './VirtualDomainsTab'
import ExternalDomainsTab from './ExternalDomainsTab'
import ApplicationTab from './ApplicationTab'

const TABS = ['accounts', 'domains', 'virtualdomains', 'externaldomains', 'application'] as const
type Tab = typeof TABS[number]

/** Each tab is its own literal `t()` call: a key held in a table and read by variable is
    invisible to `src/locales/keys.test.ts`. */
function tabLabelOf(tab: Tab, t: TFunction<'admin'>) {
  switch (tab) {
    case 'accounts': return t('tabs.accounts')
    case 'domains': return t('tabs.domains')
    case 'virtualdomains': return t('tabs.virtualdomains')
    case 'externaldomains': return t('tabs.externaldomains')
    case 'application': return t('tabs.application')
  }
}

/** Accounts carries no help on purpose: the tab needs none, and the other four say how their
    object relates to the rest. */
function helpTextOf(tab: Tab, t: TFunction<'admin'>) {
  switch (tab) {
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
  const [activeTab, setActiveTab] = useState<Tab>('accounts')
  const helpText = helpTextOf(activeTab, t)
  // Where a confirmed delete hands focus when it takes its own row with it: the row's button
  // leaves with the refetched list, so nothing inside the tab is sure to survive.
  const tabRegion = useRef<HTMLDivElement>(null)

  return (
    <>
      <div className="settings-page admin-page">
        <div className="settings-page-header">
          <h1 className="settings-page-title"><ShieldIcon size={17} />{t('title')}</h1>
        </div>
        <div className="admin-modal-body">
          <nav className="admin-tab-bar">
            {TABS.map(tab => (
              <button key={tab} className={`admin-tab${activeTab === tab ? ' is-active' : ''}`}
                onClick={() => setActiveTab(tab)}>{tabLabelOf(tab, t)}</button>
            ))}
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
