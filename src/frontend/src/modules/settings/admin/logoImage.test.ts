import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LOGO_TOO_DETAILED, LOGO_UNREADABLE, prepareLogo } from './logoImage'

let natural = { width: 400, height: 200 }
let decodeFails = false
let drawn: number[][]
let blobBytes: Record<number, number>
let decodedSources: string[]

const imageProto = HTMLImageElement.prototype as unknown as Record<string, unknown>
const saved = {
  width: Object.getOwnPropertyDescriptor(imageProto, 'naturalWidth'),
  height: Object.getOwnPropertyDescriptor(imageProto, 'naturalHeight'),
  decode: imageProto.decode,
  create: (URL as unknown as Record<string, unknown>).createObjectURL,
  revoke: (URL as unknown as Record<string, unknown>).revokeObjectURL,
}

function restore(target: object, key: string, descriptor: PropertyDescriptor | undefined) {
  if (descriptor) Object.defineProperty(target, key, descriptor)
  else Reflect.deleteProperty(target, key)
}

beforeEach(() => {
  natural = { width: 400, height: 200 }
  decodeFails = false
  drawn = []
  blobBytes = {}
  decodedSources = []
  let n = 0
  Object.assign(URL, {
    createObjectURL: vi.fn((blob: Blob) => { decodedSources.push(blob.type); return `blob:${++n}` }),
    revokeObjectURL: vi.fn(),
  })
  Object.defineProperty(imageProto, 'naturalWidth', { configurable: true, get: () => natural.width })
  Object.defineProperty(imageProto, 'naturalHeight', { configurable: true, get: () => natural.height })
  imageProto.decode = vi.fn(() => decodeFails ? Promise.reject(new Error('bad')) : Promise.resolve())
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue({
    imageSmoothingQuality: 'low',
    drawImage: (_image: unknown, ...args: number[]) => { drawn.push(args) },
  } as unknown as CanvasRenderingContext2D)
  vi.spyOn(HTMLCanvasElement.prototype, 'toBlob').mockImplementation(function (this: HTMLCanvasElement, callback: BlobCallback) {
    callback(new Blob([new Uint8Array(blobBytes[this.width] ?? 10)], { type: 'image/png' }))
  })
})

afterEach(() => {
  vi.restoreAllMocks()
  restore(imageProto, 'naturalWidth', saved.width)
  restore(imageProto, 'naturalHeight', saved.height)
  restore(imageProto, 'decode', saved.decode === undefined ? undefined : { value: saved.decode, configurable: true, writable: true })
  restore(URL, 'createObjectURL', saved.create === undefined ? undefined : { value: saved.create, configurable: true, writable: true })
  restore(URL, 'revokeObjectURL', saved.revoke === undefined ? undefined : { value: saved.revoke, configurable: true, writable: true })
})

const png = () => new File([], 'logo.png', { type: 'image/png' })

describe('prepareLogo', () => {
  it('renders three PNGs, 32, 192 and 512', async () => {
    const { images } = await prepareLogo(png())
    expect(Object.keys(images)).toEqual(['32', '192', '512'])
    expect(images[512].type).toBe('image/png')
  })

  // Review Focus 4: centred, never stretched, transparent margins.
  it('fits a long logo whole in the square, centred', async () => {
    natural = { width: 1000, height: 100 }
    await prepareLogo(png())
    expect(drawn[2]).toEqual([0, 230.4, 512, 51.2])
  })

  it('flags an image under 512 px as low resolution', async () => {
    natural = { width: 300, height: 120 }
    expect((await prepareLogo(png())).lowResolution).toBe(true)
    natural = { width: 600, height: 120 }
    expect((await prepareLogo(png())).lowResolution).toBe(false)
  })

  it('refuses a file the browser cannot decode', async () => {
    decodeFails = true
    await expect(prepareLogo(png())).rejects.toThrow(LOGO_UNREADABLE)
  })

  it('refuses an image with no size', async () => {
    natural = { width: 0, height: 0 }
    await expect(prepareLogo(png())).rejects.toThrow(LOGO_UNREADABLE)
  })

  // Review Focus 3.
  it('refuses an image too detailed for the 512 px cap, before any upload', async () => {
    blobBytes = { 512: 1024 * 1024 + 1 }
    await expect(prepareLogo(png())).rejects.toThrow(LOGO_TOO_DETAILED)
  })

  // Review Focus 1: Firefox gives an SVG with only a viewBox a zero natural size.
  it('lends a viewBox-only SVG its size, and never calls it low resolution', async () => {
    natural = { width: 100, height: 50 }
    const svg = new File(['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 50"><rect width="100" height="50"/></svg>'],
      'logo.svg', { type: 'image/svg+xml' })
    const read = vi.spyOn(XMLSerializer.prototype, 'serializeToString')

    const prepared = await prepareLogo(svg)

    expect(read.mock.results[0]!.value).toMatch(/width="100" height="50"|height="50" width="100"/)
    expect(prepared.lowResolution).toBe(false)
    expect(decodedSources).toEqual(['image/svg+xml'])
  })
})
