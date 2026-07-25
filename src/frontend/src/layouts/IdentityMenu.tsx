import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'
import ChevronRightIcon from '../icons/ChevronRightIcon'
import SignOutIcon from '../icons/SignOutIcon'

/**
 * The account block at the foot of the folder column, where the topbar avatar used to be.
 * Its menu opens upward — the block sits at the bottom of the screen. Settings is not repeated
 * here: the rail's gear owns that route.
 */
export default function IdentityMenu() {
  const { identity, accounts, activeAccount, logout } = useAuth()
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

  return (
    <div className="identity-root" ref={rootRef}>
      <span className="identity-pill" aria-hidden="true">{identity.initials}</span>
      <span className="identity-text">
        <span className="identity-name">{identity.displayName}</span>
        <span className="identity-email">{identity.email}</span>
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
          {accounts.map(acc => (
            <div
              key={acc.id}
              role="menuitem"
              className={acc.id === activeAccount?.id ? 'identity-account is-active' : 'identity-account'}
            >
              {acc.email}
            </div>
          ))}
          <button type="button" role="menuitem" className="identity-signout" onClick={handleSignOut}>
            <SignOutIcon size={15} /> Sign out
          </button>
        </div>
      )}
    </div>
  )
}
