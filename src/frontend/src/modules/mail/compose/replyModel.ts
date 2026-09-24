import type { AliasInfo, MailAddressInfo, MailMessageDetail, SendingIdentity } from '../api/mailTypes'
import { usableIdentities } from './usableIdentities'

export interface Recipients { to: string[]; cc: string[] }

/** Every address that is the user, lower-cased: `own` (the active account's address and the
 * primary, which no alias list carries), the aliases, and the identities, a connected mailbox's
 * only source. A reply-all drops them all rather than mail the user back. */
export function myAddresses(
  own: readonly (string | null | undefined)[], aliases: AliasInfo[],
  identities: SendingIdentity[] = [],
): Set<string> {
  const mine = new Set<string>()
  for (const address of own) if (address) mine.add(address.toLowerCase())
  for (const alias of aliases) mine.add(`${alias.name}@${alias.domain}`.toLowerCase())
  for (const identity of usableIdentities(identities)) mine.add(identity.address.toLowerCase())
  return mine
}

const isMine = (mine: Set<string>, address: string) => mine.has(address.toLowerCase())
const addressesOf = (list: MailAddressInfo[]) => list.map(a => a.address)

function dedupe(addresses: string[]): string[] {
  const seen = new Set<string>()
  const out: string[] = []
  for (const address of addresses) {
    const key = address.toLowerCase()
    if (!address || seen.has(key)) continue
    seen.add(key)
    out.push(address)
  }
  return out
}

/** The reply target: Reply-To when present, From otherwise. */
function senderOf(detail: MailMessageDetail): string[] {
  return detail.replyTo.length > 0 ? addressesOf(detail.replyTo) : (detail.fromAddress ? [detail.fromAddress] : [])
}

// Replying to my own Sent copy nudges the thread: the original To is the target, not me.
export function replyRecipients(detail: MailMessageDetail, mine: Set<string>): Recipients {
  const sender = senderOf(detail)
  if (sender.length > 0 && sender.every(a => isMine(mine, a))) {
    const to = dedupe(addressesOf(detail.to))
    if (to.length > 0) return { to, cc: [] }
  }
  return { to: dedupe(sender), cc: [] }
}

// Keeps the original To/Cc split minus my addresses. All-mine degenerates to a plain reply, and a
// Cc-only remainder is promoted to To, since a message cannot send without one.
export function replyAllRecipients(detail: MailMessageDetail, mine: Set<string>): Recipients {
  const to = dedupe([...senderOf(detail), ...addressesOf(detail.to)].filter(a => !isMine(mine, a)))
  const inTo = new Set(to.map(a => a.toLowerCase()))
  const cc = dedupe(addressesOf(detail.cc).filter(a => !isMine(mine, a) && !inTo.has(a.toLowerCase())))
  if (to.length === 0 && cc.length === 0) return replyRecipients(detail, mine)
  if (to.length === 0) return { to: cc, cc: [] }
  return { to, cc }
}

/** Re:/Fwd: without stacking — an already-prefixed subject keeps its single prefix. */
export function subjectFor(purpose: 'reply' | 'forward', subject: string): string {
  const trimmed = subject.trim()
  const wanted = purpose === 'reply' ? /^re\s*:/i : /^fwd?\s*:/i
  if (wanted.test(trimmed)) return trimmed
  return `${purpose === 'reply' ? 'Re' : 'Fwd'}: ${trimmed}`
}

// The first usable identity among the original's To then Cc (an owned address with no identity
// cannot be in the From menu), else the default.
export function preselectIdentity(detail: MailMessageDetail, identities: SendingIdentity[]): string | null {
  const usable = usableIdentities(identities)
  const byAddress = new Map(usable.map(i => [i.address.toLowerCase(), i.address]))
  for (const recipient of [...detail.to, ...detail.cc]) {
    const found = byAddress.get(recipient.address.toLowerCase())
    if (found) return found
  }
  return usable.find(i => i.isDefault)?.address ?? null
}

/** Edit-as-new opens from the original's From when it is one of my identities, else the default. */
export function editAsNewFrom(detail: MailMessageDetail, identities: SendingIdentity[]): string | null {
  const usable = usableIdentities(identities)
  const match = usable.find(i => i.address.toLowerCase() === detail.fromAddress.toLowerCase())
  return match?.address ?? usable.find(i => i.isDefault)?.address ?? null
}
