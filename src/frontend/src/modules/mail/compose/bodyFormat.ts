import { escapeHtml } from '../../../lib/escapeHtml'

const BLOCK = new Set([
  'P', 'DIV', 'LI', 'TR', 'BLOCKQUOTE', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'PRE', 'TABLE', 'UL', 'OL',
])

/* Anything a text body cannot carry. A blockquote is deliberately absent: it survives as '>'
   lines, so a reply to a plain original must not be interrogated about a loss it will not have. */
const FORMATTED = 'b, strong, i, em, u, s, strike, del, ins, a, img, ul, ol, table, hr, font,'
  + ' code, pre, h1, h2, h3, h4, h5, h6, [style]'

interface Line { text: string; quote: number }
interface Item { node: Node | null; quote: number }

const bodyOf = (html: string) =>
  new DOMParser().parseFromString(`<body>${html}</body>`, 'text/html').body

/** Reversed, so the stack pops the children back in document order. */
function pushChildren(stack: Item[], node: Node, quote: number) {
  const children = node.childNodes
  for (let i = children.length - 1; i >= 0; i--) stack.push({ node: children[i], quote })
}

/**
 * The editor's HTML as the text a recipient would read. Mirrors the backend's own block walk
 * (OutgoingMailSanitizer.ExtractText), plus the part that only exists here: a blockquote comes
 * back as '>'-prefixed lines, nesting included, which is what makes a switched reply readable.
 * Explicit stack, not recursion: a paste can carry thousands of nested elements.
 */
export function htmlToText(html: string): string {
  const lines: Line[] = []
  let current: Line = { text: '', quote: 0 }
  const breakLine = () => { lines.push(current); current = { text: '', quote: 0 } }

  const stack: Item[] = []
  pushChildren(stack, bodyOf(html), 0)

  while (stack.length > 0) {
    const item = stack.pop()!
    // A deferred block boundary, pushed under an element's children and popped after them.
    if (item.node === null) { breakLine(); continue }
    if (item.node.nodeType === Node.TEXT_NODE) {
      const data = item.node.nodeValue ?? ''
      current.text += data
      if (data.trim() !== '') current.quote = item.quote
      continue
    }
    if (item.node.nodeType !== Node.ELEMENT_NODE) continue

    const element = item.node as Element
    if (element.tagName === 'BR') { breakLine(); continue }
    const quote = element.tagName === 'BLOCKQUOTE' ? item.quote + 1 : item.quote
    if (BLOCK.has(element.tagName)) stack.push({ node: null, quote: item.quote })
    pushChildren(stack, element, quote)
  }
  lines.push(current)

  const rendered = lines.map(line => {
    const text = line.text.replace(/\s+/g, ' ').trim()
    if (line.quote === 0) return text
    const prefix = '>'.repeat(line.quote)
    return text === '' ? prefix : `${prefix} ${text}`
  })
  while (rendered.length > 0 && rendered[0] === '') rendered.shift()
  while (rendered.length > 0 && rendered[rendered.length - 1] === '') rendered.pop()
  return rendered.join('\n')
}

/** Escaped text with its line structure rendered — the mirror of QuotePreparer.TextToHtml. */
export function textToHtml(text: string): string {
  return `<div>${escapeHtml(text).replace(/\r?\n/g, '<br>')}</div>`
}

/** Whether switching this body to text would cost the user something it cannot carry. */
export function losesFormatting(html: string): boolean {
  return bodyOf(html).querySelector(FORMATTED) !== null
}
