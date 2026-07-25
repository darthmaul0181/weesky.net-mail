import { useState } from 'react'
import { useToasts } from '../../../hooks/useToasts.js'
import Toasts from '../../../components/Toasts.jsx'
import HelpTooltip from '../../../components/HelpTooltip.jsx'
import ShieldIcon from '../../../icons/ShieldIcon.jsx'
import AccountsTab from './AccountsTab.jsx'
import DomainsTab from './DomainsTab.jsx'
import VirtualDomainsTab from './VirtualDomainsTab.jsx'

const ADMIN_HELP = {
  domains: 'A domain is a mail domain hosted directly on this server (e.g. example.com). Every user account belongs to exactly one domain. Adding a domain enables creating mailboxes of the form user@example.com.',
  virtualdomains: 'A virtual alias domain is a domain with no mailboxes of its own. Emails sent to any address under this domain are redirected to real mailboxes via alias rules. Each virtual alias domain can have one or more owners — accounts authorised to create and manage aliases under it.',
}

export default function AdminPage() {
  const { toasts, addToast, removeToast } = useToasts()
  const [activeTab, setActiveTab] = useState('accounts')

  return (
    <>
      <div className="settings-page admin-page">
        <div className="settings-page-header">
          <span className="settings-page-title"><ShieldIcon /> Administration</span>
        </div>
        <div className="admin-modal-body">
          <nav className="admin-tab-bar">
            <button className={`admin-tab${activeTab === 'accounts' ? ' is-active' : ''}`}
              onClick={() => setActiveTab('accounts')}>Accounts</button>
            <button className={`admin-tab${activeTab === 'domains' ? ' is-active' : ''}`}
              onClick={() => setActiveTab('domains')}>Domains</button>
            <button className={`admin-tab${activeTab === 'virtualdomains' ? ' is-active' : ''}`}
              onClick={() => setActiveTab('virtualdomains')}>Virtual domains</button>
          </nav>
          <div className="admin-tab-content">
            {activeTab === 'accounts' && <AccountsTab addToast={addToast} />}
            {activeTab === 'domains' && <DomainsTab addToast={addToast} />}
            {activeTab === 'virtualdomains' && <VirtualDomainsTab addToast={addToast} />}
          </div>
        </div>
        {ADMIN_HELP[activeTab] && (
          <div className="admin-modal-help">
            <HelpTooltip text={ADMIN_HELP[activeTab]} />
          </div>
        )}
      </div>

      <Toasts toasts={toasts} onRemove={removeToast} />
    </>
  )
}
