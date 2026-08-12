import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../contexts/AuthContext'

// Same `!== false` shape as RequirePrimary, and for the same reason: activeAccount is null
// while the account list loads, and the primary account always carries sieveSupported: true.
// The two gates never conflate: a connected account answers to its own sieveSupported only, the
// primary additionally to the platform's capabilities.rules (also `!== false`, so a still-loading
// or absent capabilities response reads as "available" rather than flash-redirecting).
//
// The capabilities redirect additionally waits on `accountsLoading`. `activeAccount` is null for
// the width of that load on a CONNECTED account (its row hasn't landed yet), and `isPrimary`'s
// `!== false` default reads that null as primary — the right call for a nav row, which just
// re-renders once the list lands, but wrong for a redirect: a connected, Sieve-capable account
// deep-linking in while `capabilities.rules` resolves first would be bounced off the page it just
// asked for. Once `accountsLoading` is false, `activeAccount`/`isPrimary` are the real answer and
// a genuine primary account with `rules: false` still redirects.
export default function RequireSieve() {
  const { activeAccount, accountsLoading, capabilities } = useAuth()
  const isPrimary = activeAccount?.isPrimary !== false
  if (activeAccount?.sieveSupported === false) return <Navigate to="/settings/general" replace />
  if (!accountsLoading && isPrimary && capabilities?.rules === false) {
    return <Navigate to="/settings/general" replace />
  }
  return <Outlet />
}
