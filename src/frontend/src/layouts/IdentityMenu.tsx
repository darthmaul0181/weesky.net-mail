import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth, type ActiveAccount } from '../contexts/AuthContext'
import { confirmLeave } from '../lib/leaveGuard'
import ChevronRightIcon from '../icons/ChevronRightIcon'
import PersonPlusIcon from '../icons/PersonPlusIcon.jsx'
import SignOutIcon from '../icons/SignOutIcon'

const LINKED_ACCOUNTS = '/settings/accounts'

/** First letters of the label's first two words, an address counting as name + domain. */
function initialsOf(label: string): string {
  return label.split(/[\s.@_-]+/).filter(Boolean).slice(0, 2)
    .map(part => part[0].toUpperCase()).join('')
}

function labelOf(acc: ActiveAccount): string {
  return acc.displayName || acc.email
}

/**
 * The account block at the foot of the folder column and of the settings nav, where the topbar
 * avatar used to be. Its menu opens upward — the block sits at the bottom of the screen — and is
 * where the session's mailbox is chosen. Settings is not repeated here: the rail's gear owns it.
 */
export default function IdentityMenu() {
  const { identity, accounts, activeAccount, switchAccount, logout } = useAuth()
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const navigate = useNavigate()

  useEffect(() => {
    if (!open) return
    function onMouseDown(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false)
    }
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onMouseDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onMouseDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  if (!identity) return null

  async function handleSignOut() {
    await logout()
    navigate('/login', { replace: true })
  }

  function goToLinkedAccounts() {
    setOpen(false)
    navigate(LINKED_ACCOUNTS)
  }

  // switchAccount refuses a target whose password no longer decrypts, so that row leads to the
  // page where it is re-entered rather than pretending to be a switch.
  //
  // The guard is asked before anything changes: a switch is a state change, not a navigation, so
  // the composer's router blocker cannot see it and an open draft would be left behind in silence.
  async function pickAccount(acc: ActiveAccount) {
    if (!acc.credentialsValid) return goToLinkedAccounts()
    setOpen(false)
    if (!(await confirmLeave())) return
    switchAccount(acc.id)
  }

  const bandLabel = activeAccount ? labelOf(activeAccount) : null
  const bandSub = !activeAccount || activeAccount.isPrimary
    ? activeAccount?.email
    : `${activeAccount.email} · ${activeAccount.domainName ?? 'Weesky'}`
  const pillClass = !activeAccount
    ? 'identity-pill is-pending'
    : activeAccount.isPrimary ? 'identity-pill' : 'identity-pill is-connected'

  return (
    <div className="identity-root" ref={rootRef}>
      <span className={pillClass} aria-hidden="true">
        {bandLabel ? initialsOf(bandLabel) : ''}
      </span>
      <span className="identity-text">
        <span className="identity-name">{bandLabel ?? 'Loading…'}</span>
        {bandSub && bandSub !== bandLabel && <span className="identity-email">{bandSub}</span>}
      </span>
      <button
        type="button"
        className="identity-toggle"
        aria-label="Account menu"
        aria-expanded={open}
        onClick={() => setOpen(o => !o)}
      >
        <ChevronRightIcon size={15} />
      </button>

      {open && (
        <div className="identity-menu" role="menu">
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
                onClick={() => pickAccount(acc)}
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
                    {acc.authMode === 'OAuth2' ? 'Sign-in needed' : 'Password needed'}
                  </span>
                )}
              </button>
            )
          })}
          <button type="button" role="menuitem" className="identity-action"
            onClick={goToLinkedAccounts}>
            <PersonPlusIcon /> Connected accounts…
          </button>
          <button type="button" role="menuitem" className="identity-action" onClick={handleSignOut}>
            <SignOutIcon size={15} /> Sign out
          </button>
        </div>
      )}
    </div>
  )
}
