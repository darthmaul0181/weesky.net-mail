import type { SendingIdentity } from '../api/mailTypes'

/** The identities a message may be sent from: a stale one is still listed so Settings can clear
 * it, but its address no longer belongs to the account. */
export function usableIdentities(list: readonly SendingIdentity[] | undefined): SendingIdentity[] {
  return (list ?? []).filter(identity => !identity.stale)
}
