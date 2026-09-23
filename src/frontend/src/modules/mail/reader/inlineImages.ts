// The iframe has no allow-same-origin, so it sends no cookie: cid parts are inlined as data: URIs.
// Through the DOM, never a regex: attribute values are entity-escaped (`&amp;`), so a string match
// would look up an id the map is not keyed by.

const SCHEME = 'cid:'

/** The bare id an <img src> references, or null when the src is not a cid: reference. */
function cidOf(src: string | null): string | null {
  if (!src || src.slice(0, SCHEME.length).toLowerCase() !== SCHEME) return null
  return src.slice(SCHEME.length) || null
}

const parse = (html: string) => new DOMParser().parseFromString(html, 'text/html')

/** `url(cid:X)` in a style attribute, quoted either way or bare. */
const CSS_CID = /url\(\s*(?:"cid:([^"]*)"|'cid:([^']*)'|cid:([^)\s]*))\s*\)/gi

const cidsInStyle = (style: string): string[] =>
  [...style.matchAll(CSS_CID)].map(match => match[1] ?? match[2] ?? match[3])
    .filter((cid): cid is string => Boolean(cid))

const HAS_CID = /cid:/i

/** Bare cid values referenced by <img src="cid:..."> or a css background in an html fragment, deduped. */
export function referencedCids(html: string): string[] {
  if (!html) return []

  const doc = parse(html)
  const cids = new Set<string>()
  for (const img of doc.querySelectorAll('img')) {
    const cid = cidOf(img.getAttribute('src'))
    if (cid) cids.add(cid)
  }
  for (const styled of doc.querySelectorAll('[style]'))
    for (const cid of cidsInStyle(styled.getAttribute('style') ?? '')) cids.add(cid)

  return [...cids]
}

/** Replaces each <img src="cid:X"> or css url(cid:X) whose X is in the map with the data: URI; others untouched. */
export function substituteInlineImages(
  html: string, dataUriByCid: Record<string, string>,
): string {
  if (!html) return ''
  // The overwhelming majority of messages reference no cid at all — no parse for those.
  if (Object.keys(dataUriByCid).length === 0) return html

  const doc = parse(html)
  let substituted = false

  for (const img of doc.querySelectorAll('img')) {
    const cid = cidOf(img.getAttribute('src'))
    const uri = cid ? dataUriByCid[cid] : undefined
    // Typed, not merely truthy: a cid named `constructor` resolves to an inherited function.
    if (typeof uri !== 'string') continue
    img.setAttribute('src', uri)
    substituted = true
  }

  for (const styled of doc.querySelectorAll('[style]')) {
    const style = styled.getAttribute('style') ?? ''
    if (!HAS_CID.test(style)) continue

    const rewritten = style.replace(CSS_CID, (
      whole: string, quoted: string | undefined, single: string | undefined, bare: string | undefined,
    ) => {
      const cid = quoted ?? single ?? bare
      const uri = cid === undefined ? undefined : dataUriByCid[cid]
      // Quotes/backslashes escaped before re-entering CSS — an unescaped " in the uri would
      // close the url("...") string early, same gate sanitizeBody.ts applies on its own write.
      return typeof uri === 'string' ? `url("${uri.replace(/["\\]/g, encodeURIComponent)}")` : whole
    })
    if (rewritten === style) continue

    styled.setAttribute('style', rewritten)
    substituted = true
  }

  // Untouched bodies come back byte-identical rather than re-serialised — which is what the
  // overwhelming majority of messages are.
  return substituted ? doc.body.innerHTML : html
}
