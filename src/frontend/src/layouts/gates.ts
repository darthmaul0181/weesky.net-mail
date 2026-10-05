import type { AuthContextValue } from '../contexts/AuthContext'

// capabilities reads `!== false` like every other gate: null while it is still loading (or on a
// backend that predates the endpoint) must read as "available", never as a flash-redirect.
export function allowAdmin({ isAdmin, accountLoaded, capabilities }: AuthContextValue): boolean | 'wait' {
  if (!accountLoaded) return 'wait' // account still loading — decide once known
  return isAdmin && capabilities?.admin !== false
}

// Nested inside allowPrimary's gate in routes.tsx, so a connected account never reaches here —
// this only has to answer the platform's own capabilities.aliases, `!== false` like every other
// gate (null while it loads, or on a backend that predates the endpoint, reads as "available").
export function allowAliases({ capabilities }: AuthContextValue): boolean {
  return capabilities?.aliases !== false
}

// `!== false`, not `=== true`: activeAccount is null while the account list loads, and the
// primary-only screens must stay reachable during that window rather than flash-redirect away.
export function allowPrimary({ activeAccount }: AuthContextValue): boolean {
  return activeAccount?.isPrimary !== false
}

// `!== false`, as in allowPrimary: activeAccount and capabilities are null while they load. A
// connected account answers to its own sieveSupported, the primary also to capabilities.rules, and
// that second check waits on accountsLoading (docs/architecture-shell.md).
export function allowSieve(
  { activeAccount, accountsLoading, capabilities }: AuthContextValue,
): boolean {
  if (activeAccount?.sieveSupported === false) return false
  const isPrimary = activeAccount?.isPrimary !== false
  if (!accountsLoading && isPrimary && capabilities?.rules === false) return false
  return true
}
