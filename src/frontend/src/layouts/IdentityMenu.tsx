import { useId, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { useAuth, type ActiveAccount } from '../contexts/AuthContext'
import { useDismiss } from '../hooks/useDismiss'
import { useRovingFocus } from '../hooks/useRovingFocus'
import { confirmLeave } from '../lib/leaveGuard'
import ChevronRightIcon from '../icons/ChevronRightIcon'
import PersonPlusIcon from '../icons/PersonPlusIcon'
import SignOutIcon from '../icons/SignOutIcon'

const LINKED_ACCOUNTS = '/settings/accounts'

/** First letters of the label's first two words, an address counting as name + domain. */
function initialsOf(label: string): string {
  return label.split(/[\s.@_-]+/).filter(Boolean).slice(0, 2)
    .map(part => part.charAt(0).toUpperCase()).join('')
}

function labelOf(acc: ActiveAccount): string {
  return acc.displayName || acc.email
}

/** The account block at the foot of the folder column and the settings nav; its upward menu is
 * where the session's mailbox is chosen. No Settings entry: the rail's gear owns it. */
export default function IdentityMenu() {
  const { identity, accounts, activeAccount, switchAccount, logout } = useAuth()
  const { t } = useTranslation()
  const [open, setOpen] = useState(false)
  // Named by the chevron that opens it, the way `DropdownMenu`'s menu is.
  const toggleId = useId()
  const rootRef = useRef<HTMLDivElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const toggleRef = useRef<HTMLButtonElement>(null)
  const navigate = useNavigate()

  // An open menu is the topmost layer, so the context drawer this block sits in below 1024px
  // keeps its own Escape and the column stands whatever holds the focus.
  useDismiss({ open, rootRef, onDismiss: () => setOpen(false), refocusRef: toggleRef })
  const menuKeys = useRovingFocus({ active: open, containerRef: menuRef })

  if (!identity) return null

  async function handleSignOut() {
    await logout()
    void navigate('/login', { replace: true })
  }

  function goToLinkedAccounts() {
    setOpen(false)
    void navigate(LINKED_ACCOUNTS)
  }

  // A row whose password no longer decrypts leads to its repair page. The leave guard is asked
  // first: a switch is a state change the composer's router blocker cannot see.
  async function pickAccount(acc: ActiveAccount) {
    if (!acc.credentialsValid) return goToLinkedAccounts()
    setOpen(false)
    if (!(await confirmLeave())) return
    switchAccount(acc.id)
  }

  const bandLabel = activeAccount ? labelOf(activeAccount) : null
  const bandSub = !activeAccount || activeAccount.isPrimary || !activeAccount.domainName
    ? activeAccount?.email
    : `${activeAccount.email} · ${activeAccount.domainName}`
  const pillClass = !activeAccount
    ? 'identity-pill is-pending'
    : activeAccount.isPrimary ? 'identity-pill' : 'identity-pill is-connected'

  return (
    <div className="identity-root" ref={rootRef}>
      <span className={pillClass} aria-hidden="true">
        {bandLabel ? initialsOf(bandLabel) : ''}
      </span>
      <span className="identity-text">
        <span className="identity-name">{bandLabel ?? t('identity.loading')}</span>
        {bandSub && bandSub !== bandLabel && <span className="identity-email">{bandSub}</span>}
      </span>
      <button
        type="button"
        className="identity-toggle"
        id={toggleId}
        ref={toggleRef}
        aria-label={t('identity.menu')}
        aria-expanded={open}
        onClick={() => setOpen(o => !o)}
      >
        <ChevronRightIcon size={15} />
      </button>

      {open && (
        <div className="identity-menu" role="menu" aria-labelledby={toggleId} ref={menuRef}
          tabIndex={-1} onKeyDown={menuKeys}>
          {accounts.map(acc => {
            const label = labelOf(acc)
            const isActive = acc.id === activeAccount?.id
            return (
              <button
                key={acc.id}
                type="button"
                role="menuitem"
                className={isActive ? 'identity-account is-active' : 'identity-account'}
                aria-current={isActive || undefined}
                onClick={() => void pickAccount(acc)}
              >
                {/* The dot is the only thing saying "you are here" to anything not reading the
                    fill, so the inactive rows hold its width rather than closing the gap. */}
                {isActive
                  ? <span className="identity-active-dot" aria-hidden="true" />
                  : <span className="identity-dot-slot" aria-hidden="true" />}
                <span className="identity-account-text">
                  <span>{label}</span>
                  {label !== acc.email && <span className="identity-account-sub">{acc.email}</span>}
                </span>
                {/* The row is a dead end until it is repaired: without the chip the click looks
                    like a switch that silently did nothing. It names the repair, which is a fresh
                    consent rather than a password on a provider mailbox. */}
                {!acc.credentialsValid && (
                  <span className="row-tag is-warn">
                    {t(acc.authMode === 'OAuth2' ? 'identity.signInNeeded' : 'identity.passwordNeeded')}
                  </span>
                )}
              </button>
            )
          })}
          <button type="button" role="menuitem" className="identity-action"
            onClick={goToLinkedAccounts}>
            <PersonPlusIcon /> {t('identity.connectedAccounts')}
          </button>
          <button type="button" role="menuitem" className="identity-action" onClick={() => void handleSignOut()}>
            <SignOutIcon size={15} /> {t('identity.signOut')}
          </button>
        </div>
      )}
    </div>
  )
}
