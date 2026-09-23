import type { ComposeSeed } from './composeSeed'
import { newMessageSeed } from './composeSeed'
import { textToHtml } from './bodyFormat'
import { isValidAddress } from './RecipientsField'

/** A malformed escape is a link somebody typed by hand, not a reason to open no composer at all. */
function decode(value: string): string {
  try {
    return decodeURIComponent(value)
  } catch {
    return value
  }
}

// Hand-parsed: URLSearchParams reads `+` as a space, but RFC 6068 hfields are RFC 3986 queries where it
// is literal (`alice+tag@`). Names are folded to lower case before the first-occurrence rule, since an
// hfname is a case-insensitive header name.
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

// A mailto: arrives from the outside world: the body is plain text, escaped before the HTML editor,
// and the addresses face the typed-address check. Only to, cc, bcc, subject and body are read.
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
    ...newMessageSeed(addressesOf(url.pathname, fields.get('to') ?? '')),
    cc: addressesOf(fields.get('cc') ?? ''),
    bcc: addressesOf(fields.get('bcc') ?? ''),
    // Left unescaped on purpose: it lands on a controlled input value, and entity-encoding it
    // would put the entities themselves in the Subject header of the message that goes out.
    subject: decode(fields.get('subject') ?? ''),
    // The guard stays: textToHtml('') is an empty <div>, and a bodyless mailto opens empty.
    html: body ? textToHtml(body) : '',
  }
}
