/**
 * Paints a dot on the tab icon while the inbox holds unread mail.
 *
 * The icon is drawn over the one `index.html` already carries, so a rebuilt favicon travels here
 * on its own — the href is read from the link element rather than named, since Vite hashes the
 * asset at build. Everything degrades to "no badge" rather than throwing: a tab icon is not worth
 * a broken shell, and jsdom has no canvas at all.
 */
const SIZE = 32
const RADIUS = 7
const FALLBACK = '#e2674a'

let originalHref: string | null = null
let wanted = false
/** One drawing per colour: the palette can change under a badged tab. */
const drawn = new Map<string, string>()

function iconLink(): HTMLLinkElement | null {
  return document.querySelector<HTMLLinkElement>('link[rel="icon"]')
}

/** The unread colour the palette is currently using, so the dot matches the one in the list. */
function dotColour(): string {
  const value = getComputedStyle(document.documentElement).getPropertyValue('--accent-unread')
  return value.trim() || FALLBACK
}

function paint(href: string, colour: string): Promise<string | null> {
  return new Promise(resolve => {
    const canvas = document.createElement('canvas')
    canvas.width = SIZE
    canvas.height = SIZE
    const context = canvas.getContext('2d')
    if (!context) return resolve(null)

    const image = new Image()
    image.onload = () => {
      try {
        context.drawImage(image, 0, 0, SIZE, SIZE)
        const centre = SIZE - RADIUS - 1
        // A ring in the page's own background: the dot has to read on a light logo and a dark one.
        context.beginPath()
        context.arc(centre, centre, RADIUS, 0, Math.PI * 2)
        context.fillStyle = colour
        context.fill()
        context.lineWidth = 2
        context.strokeStyle = getComputedStyle(document.documentElement)
          .getPropertyValue('--surface').trim() || '#ffffff'
        context.stroke()
        resolve(canvas.toDataURL('image/png'))
      } catch {
        resolve(null)
      }
    }
    image.onerror = () => resolve(null)
    image.src = href
  })
}

/** Shows the dot, or puts the untouched icon back. Safe to call on every render. */
export function setFaviconBadge(on: boolean): void {
  const link = iconLink()
  if (!link) return
  originalHref ??= link.href
  wanted = on

  if (!on) {
    link.href = originalHref
    return
  }

  const colour = dotColour()
  const ready = drawn.get(colour)
  if (ready) {
    link.href = ready
    return
  }

  void paint(originalHref, colour).then(url => {
    if (!url) return
    drawn.set(colour, url)
    // The mail may have been read while the drawing was in flight; the later state wins.
    if (wanted) link.href = url
  })
}
