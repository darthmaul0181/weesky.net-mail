import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'

export default function AvatarMenu() {
  const { identity, accounts, activeAccount, logout } = useAuth()
  const [open, setOpen] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const navigate = useNavigate()

  useEffect(() => {
    if (!open) return
    function onMouseDown(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', onMouseDown)
    return () => document.removeEventListener('mousedown', onMouseDown)
  }, [open])

  if (!identity) return null

  async function handleSignOut() {
    await logout()
    navigate('/login', { replace: true })
  }

  return (
    <div className="avatar-menu-root" ref={rootRef}>
      <button
        className="topbar-avatar"
        aria-label="Account menu"
        aria-expanded={open}
        onClick={() => setOpen(o => !o)}
      >
        {identity.initials}
      </button>
      {open && (
        <div className="avatar-menu" role="menu">
          <div className="avatar-menu-identity">
            <div className="avatar-menu-name">{identity.displayName}</div>
            <div className="avatar-menu-email">{identity.email}</div>
          </div>
          <div className="avatar-menu-accounts">
            {accounts.map(acc => (
              <div
                key={acc.id}
                role="menuitem"
                className={acc.id === activeAccount?.id ? 'avatar-menu-account is-active' : 'avatar-menu-account'}
              >
                {acc.email}
              </div>
            ))}
          </div>
          <div className="avatar-menu-actions">
            <Link to="/settings" role="menuitem" onClick={() => setOpen(false)}>Settings</Link>
            <button role="menuitem" onClick={handleSignOut}>Sign out</button>
          </div>
        </div>
      )}
    </div>
  )
}
