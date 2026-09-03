import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PHOTO_TOO_LARGE, PHOTO_UNREADABLE, reducePhoto } from './contactPhoto'

const MAX = 512 * 1024

let drawn: number[][]
let filled: number[]
let order: string[]
let sides: number[]
let qualities: number[]
let blobSizes: number[]

function mockBitmap(width: number, height: number) {
  vi.stubGlobal('createImageBitmap', vi.fn(async () => ({ width, height, close: vi.fn() })))
}

beforeEach(() => {
  drawn = []; filled = []; order = []; sides = []; qualities = []; blobSizes = []
  mockBitmap(400, 200)
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({
    fillStyle: '',
    fillRect: (...args: number[]) => { filled = args; order.push('fill') },
    drawImage: (_bitmap: unknown, ...args: number[]) => { drawn.push(args); order.push('draw') },
  } as unknown as CanvasRenderingContext2D)
  vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(function (
    this: HTMLCanvasElement, callback: BlobCallback, _type?: string, quality?: number,
  ) {
    sides.push(this.width)
    qualities.push(quality as number)
    callback(new Blob([new Uint8Array(blobSizes.shift() ?? 10)], { type: 'image/jpeg' }))
  })
})

describe('reducePhoto', () => {
  it('crops a landscape to a centred square', async () => {
    await reducePhoto(new File([], 'p.jpg'))

    // sx, sy, sw, sh : la moitié du débord à gauche, rien en haut, le côté court des deux côtés.
    expect(drawn[0].slice(0, 4)).toEqual([100, 0, 200, 200])
  })

  it('crops a portrait to a centred square', async () => {
    mockBitmap(200, 400)

    await reducePhoto(new File([], 'p.jpg'))

    expect(drawn[0].slice(0, 4)).toEqual([0, 100, 200, 200])
  })

  it('never enlarges a small image', async () => {
    mockBitmap(300, 300)

    await reducePhoto(new File([], 'p.jpg'))

    expect(sides[0]).toBe(300)
  })

  // Un canvas naît noir transparent et le JPEG jette l'alpha : sans ce fond, un logo sur fond
  // transparent devient un carré noir.
  it('paints the white ground before the image', async () => {
    await reducePhoto(new File([], 'p.jpg'))

    expect(order.slice(0, 2)).toEqual(['fill', 'draw'])
    expect(filled).toEqual([0, 0, 200, 200])
  })

  it('asks the browser to apply the EXIF orientation', async () => {
    await reducePhoto(new File([], 'p.jpg'))

    expect(createImageBitmap).toHaveBeenCalledWith(
      expect.anything(), { imageOrientation: 'from-image' })
  })

  it('walks the quality down while the blob is over the ceiling', async () => {
    mockBitmap(2000, 2000)
    blobSizes = [MAX + 1, MAX + 1, 10]

    await reducePhoto(new File([], 'p.jpg'))

    expect(qualities).toEqual([0.85, 0.7, 0.55])
    expect(sides).toEqual([1024, 1024, 1024])
  })

  it('falls back to 512 px when the quality descent is not enough', async () => {
    mockBitmap(2000, 2000)
    blobSizes = [MAX + 1, MAX + 1, MAX + 1, MAX + 1, MAX + 1, 10]

    await reducePhoto(new File([], 'p.jpg'))

    expect(sides).toEqual([1024, 1024, 1024, 512, 512, 512])
  })

  it('refuses rather than sending something bound for a 400', async () => {
    mockBitmap(2000, 2000)
    blobSizes = Array(6).fill(MAX + 1)

    await expect(reducePhoto(new File([], 'p.jpg'))).rejects.toThrow(PHOTO_TOO_LARGE)
  })

  it('reports a file the browser cannot decode', async () => {
    vi.stubGlobal('createImageBitmap', vi.fn(async () => { throw new Error('HEIC') }))

    await expect(reducePhoto(new File([], 'p.heic'))).rejects.toThrow(PHOTO_UNREADABLE)
  })

  it('answers bare base64, not a data URL', async () => {
    blobSizes = [7]

    const { base64, blob } = await reducePhoto(new File([], 'p.jpg'))

    expect(base64.startsWith('data:')).toBe(false)
    expect(base64).not.toContain(',')
    expect(atob(base64).length).toBe(7)
    expect(blob.size).toBe(7)
  })
})
