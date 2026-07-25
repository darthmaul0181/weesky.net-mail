import type { SendingIdentity } from '../../mail/api/mailTypes'

export interface IdentityRow { address: string; displayName: string; isDefault: boolean }

/** `IdentityResolver.MaxDisplayNameLength`, itself the `display_name VARCHAR(100)` column: the
    inputs cap here so an over-long name costs a keystroke rather than a PUT and a rollback. */
export const MAX_DISPLAY_NAME_LENGTH = 100

/** The PUT payload from the displayed list. The primary is never sent — its label always follows
    the account FullName (set from the Account tab), and absence of any marked row is what "the
    primary is the default" looks like on the wire. */
export function toRows(identities: SendingIdentity[]): IdentityRow[] {
  return identities
    .filter(i => !i.isPrimary)
    .map(i => ({ address: i.address, displayName: i.displayName, isDefault: i.isDefault }))
}

/* The apply* family answers the resolved list rather than the payload. The page keeps showing
   what came back while the PUT is in flight, so the next action builds on it instead of on a
   server snapshot the invalidation has not refreshed yet — a whole-set PUT built on a stale
   snapshot silently reverts the action before it. */

/** Tiles are ordered alphabetically by display name, case-insensitively — the same `localeCompare`
    the folder list and the rest of the site sort names with. Order is purely by name: the default
    is marked with a star on its tile, not by floating to the top. */
export function sortIdentities(identities: SendingIdentity[]): SendingIdentity[] {
  return [...identities].sort((a, b) =>
    a.displayName.localeCompare(b.displayName, undefined, { sensitivity: 'base' }))
}

/**
 * Who holds the star. `marked` is the address of the live alias carrying the default, or null —
 * and null elects the primary, which is what "no marked row" means on the wire. Every path that
 * can leave the list without a default goes through here, so the rule is stated once.
 */
function markDefault(identities: SendingIdentity[], marked: string | null): SendingIdentity[] {
  return identities.map(i => ({ ...i, isDefault: marked ? i.address === marked : i.isPrimary }))
}

export function applyDefault(identities: SendingIdentity[], address: string): SendingIdentity[] {
  const target = identities.find(i => i.address === address)
  return markDefault(identities, target && !target.isPrimary ? address : null)
}

/** Renames an alias. The primary is not renamable here — its name is the account FullName — so
    there is no clear-to-fallback branch; an empty label is a no-op that keeps the old one. */
export function applyLabel(
  identities: SendingIdentity[], address: string, label: string,
): SendingIdentity[] {
  const trimmed = label.trim()
  const target = identities.find(i => i.address === address)
  if (!target || trimmed === '') return identities // a label cannot be empty; keep the old one
  return identities.map(i =>
    i.address === address ? { ...i, displayName: trimmed, labelIsCustom: true } : i)
}

export function applyRemoval(identities: SendingIdentity[], address: string): SendingIdentity[] {
  const rest = identities.filter(i => i.address !== address)
  // Removing the row that held the star hands it back to the primary, exactly as the server
  // elects it — otherwise the list would show no default at all until the refetch lands.
  return markDefault(rest, rest.find(i => i.isDefault && !i.isPrimary)?.address ?? null)
}

export function applyAddition(
  identities: SendingIdentity[], address: string, label: string,
): SendingIdentity[] {
  return [...identities, {
    address, displayName: label.trim(), isDefault: false,
    isPrimary: false, stale: false, labelIsCustom: true,
  }]
}
