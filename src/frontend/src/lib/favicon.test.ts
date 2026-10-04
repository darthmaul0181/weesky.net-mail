import { beforeEach, describe, expect, it, vi } from 'vitest'

let loads: { src: string; crossOrigin: string | null; fire: () => void }[]

beforeEach(async () => {
  vi.resetModules()
  loads = []
  document.head.innerHTML = '<link rel="icon" href="/scotty-32.png">'
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({
    drawImage: vi.fn(), beginPath: vi.fn(), arc: vi.fn(), fill: vi.fn(), stroke: vi.fn(),
  } as unknown as CanvasRenderingContext2D)
  let n = 0
  vi.spyOn(HTMLCanvasElement.prototype, 'toDataURL').mockImplementation(() => `data:badge-${++n}`)
  vi.stubGlobal('Image', class {
    onload: (() => void) | null = null
    onerror: (() => void) | null = null
    crossOrigin: string | null = null
    set src(value: string) { loads.push({ src: value, crossOrigin: this.crossOrigin, fire: () => this.onload?.() }) }
  })
})

const link = () => document.querySelector<HTMLLinkElement>('link[rel="icon"]')!
const flush = () => new Promise(resolve => setTimeout(resolve))

describe('favicon base', () => {
  it('shows the new base when no badge is wanted', async () => {
    const { setFaviconBase } = await import('./favicon')
    setFaviconBase('/org-32.png')
    expect(link().getAttribute('href')).toBe('/org-32.png')
  })

  it('redraws a wanted badge over the new base, never the old one', async () => {
    const { setFaviconBadge, setFaviconBase } = await import('./favicon')
    setFaviconBadge(true)
    setFaviconBase('/org-32.png')
    loads[0]!.fire()            // the drawing over Scotty lands late
    loads[1]!.fire()
    await flush()

    expect(loads.map(l => l.src)).toEqual([expect.stringMatching(/scotty-32/), '/org-32.png'])
    expect(link().getAttribute('href')).toBe('data:badge-2')
  })

  it('turning the badge off shows the new base', async () => {
    const { setFaviconBadge, setFaviconBase } = await import('./favicon')
    setFaviconBase('/org-32.png')
    setFaviconBadge(true)
    setFaviconBadge(false)
    expect(link().getAttribute('href')).toBe('/org-32.png')
  })

  // An API on another origin would taint the canvas and the badge would silently vanish.
  it('loads the base with CORS', async () => {
    const { setFaviconBadge } = await import('./favicon')
    setFaviconBadge(true)
    expect(loads[0]!.crossOrigin).toBe('anonymous')
  })
})
