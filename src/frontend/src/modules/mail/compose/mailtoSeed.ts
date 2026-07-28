import type { ComposeSeed } from './composeSeed'
import { escapeHtml } from '../../../lib/escapeHtml'
import { isValidAddress } from './RecipientsField'

/** A malformed escape is a link somebody typed by hand, not a reason to open no composer at all. */
function decode(value: string): string {
  try {
    return decodeURIComponent(value)
  } catch {
    return value
  }
}

/**
 * The hfields, still percent-encoded, keyed lower-case, first occurrence winning.
 *
 * Hand-parsed rather than read through URLSearchParams, which applies form encoding and so reads
 * a `+` as a space. RFC 6068 hfields are RFC 3986 query components, where `+` is a literal: read
 * the form way, `alice+tag@weesky.be` becomes `alice tag@weesky.be`, fails the address gate and
 * is dropped — the composer opens one recipient short with nothing on screen to say so.
 *
 * An hfname is an RFC 5322 header name, so it is case-insensitive: `?Subject=` and `?CC=` are the
 * same fields as `?subject=` and `?cc=`. Matched case-sensitively they were dropped in silence and
 * the composer opened visibly incomplete. Folding runs before the first-occurrence rule, so a link
 * carrying both `?subject=` and `?Subject=` keeps the leftmost of the two, whichever its spelling.
 */
function hfieldsOf(search: string): Map<string, string> {
  const fields = new Map<string, string>()
  for (const pair of search.replace(/^\?/, '').split('&')) {
    if (pair === '') continue
    // The first `=` only: a body may hold more of them, and they belong to its value.
    const equals = pair.indexOf('=')
    const name = decode(equals < 0 ? pair : pair.slice(0, equals)).toLowerCase()
    if (!fields.has(name)) fields.set(name, equals < 0 ? '' : pair.slice(equals + 1))
  }
  return fields
}

/** Split first, decode after: a %2C is escaped precisely so it is not the separator. */
function addressesOf(...raw: string[]): string[] {
  return raw
    .flatMap(value => value.split(','))
    .map(value => decode(value).trim())
    .filter(isValidAddress)
}

/**
 * A mailto: URL (RFC 6068) becomes the seed the composer already knows how to open with.
 *
 * It arrives from the operating system, so from the outside world: the body is plain text and is
 * escaped before it enters an HTML editor, and the addresses go through the same check as the
 * ones a user types. Headers other than to, cc, bcc, subject and body are ignored.
 */
export function mailtoSeedFrom(search: string): ComposeSeed | null {
  const raw = new URLSearchParams(search).get('mailto')
  if (!raw) return null

  let url: URL
  try {
    url = new URL(raw)
  } catch {
    return null
  }
  if (url.protocol !== 'mailto:') return null

  const fields = hfieldsOf(url.search)
  const body = decode(fields.get('body') ?? '')

  return {
    action: 'editAsNew',
    to: addressesOf(url.pathname, fields.get('to') ?? ''),
    cc: addressesOf(fields.get('cc') ?? ''),
    bcc: addressesOf(fields.get('bcc') ?? ''),
    // Left unescaped on purpose: it lands on a controlled input value, and entity-encoding it
    // would put the entities themselves in the Subject header of the message that goes out.
    subject: decode(fields.get('subject') ?? ''),
    html: body ? `<div>${escapeHtml(body).replace(/\r?\n/g, '<br>')}</div>` : '',
    fromAddress: null,
    attachments: [],
    inReplyTo: null,
    references: [],
    draftRef: null,
    nameHints: {},
  }
}
