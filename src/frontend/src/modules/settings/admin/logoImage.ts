// The admin's logo, drawn into the three PNGs the API stores. Pure: no query, React or i18n; it
// throws a translation key.
import { LOGO_MAX_BYTES, LOGO_SIZES, type LogoSize } from '../../../lib/appLogo'

export const LOGO_UNREADABLE = 'application.logoUnreadable'
export const LOGO_TOO_DETAILED = 'application.logoTooDetailed'
export type LogoErrorKey = typeof LOGO_UNREADABLE | typeof LOGO_TOO_DETAILED

export interface PreparedLogo {
  images: Record<LogoSize, Blob>
  /** Under 512 px on its longest side: the installed app's icon will be blurry. */
  lowResolution: boolean
}

export async function prepareLogo(file: File): Promise<PreparedLogo> {
  const svg = isSvg(file)
  let image: HTMLImageElement | undefined
  try {
    image = await decode(svg ? await sized(file) : file)
    const images = {} as Record<LogoSize, Blob>
    for (const size of LOGO_SIZES) {
      images[size] = await render(image, size, svg)
      if (images[size].size > LOGO_MAX_BYTES[size]) throw new Error(LOGO_TOO_DETAILED)
    }
    return { images, lowResolution: !svg && Math.max(image.naturalWidth, image.naturalHeight) < 512 }
  } catch (error) {
    // The browser's own failures (unreadable file, tainted canvas...) all mean the same to the admin.
    throw error instanceof Error && (error.message === LOGO_TOO_DETAILED || error.message === LOGO_UNREADABLE)
      ? error : new Error(LOGO_UNREADABLE)
  } finally {
    if (image) URL.revokeObjectURL(image.src)
  }
}

function isSvg(file: File): boolean {
  return file.type === 'image/svg+xml' || /\.svg$/i.test(file.name)
}

const ABSOLUTE_LENGTH = /^(\d+(?:\.\d+)?)(px|pt|pc|mm|cm|in)?$/
const PX_PER_UNIT: Record<string, number> = { px: 1, pt: 4 / 3, pc: 16, mm: 96 / 25.4, cm: 96 / 2.54, in: 96 }
/** Lent to an SVG's long side: Firefox rasterises at the natural size before scaling, so 24x24 would blur at 512. */
const SVG_LONG_SIDE = 1024

/** Firefox gives an SVG with no width/height a zero natural size: the viewBox lends it one. */
async function sized(file: File): Promise<Blob> {
  const doc = new DOMParser().parseFromString(await file.text(), 'image/svg+xml')
  const root = doc.documentElement
  if (root.localName !== 'svg') throw new Error(LOGO_UNREADABLE)
  const box = root.getAttribute('viewBox')?.trim().split(/[\s,]+/).map(Number)
  const boxed = box?.length === 4 && box.every(Number.isFinite) && box[2]! > 0 && box[3]! > 0
  const width = boxed ? box[2]! : pixels(root.getAttribute('width'))
  const height = boxed ? box[3]! : pixels(root.getAttribute('height'))
  if (!boxed) {
    // Without a viewBox only absolute sizes are well defined: Chrome would crop anything else to 300x150.
    if (!width || !height) throw new Error(LOGO_UNREADABLE)
    root.setAttribute('viewBox', `0 0 ${width} ${height}`)
  }
  const k = SVG_LONG_SIDE / Math.max(width, height)
  root.setAttribute('width', String(width * k))
  root.setAttribute('height', String(height * k))
  return new Blob([new XMLSerializer().serializeToString(doc)], { type: 'image/svg+xml' })
}

/** An absolute length in CSS pixels, the user unit of an SVG with no viewBox; 0 when it is not one. */
function pixels(value: string | null): number {
  const match = ABSOLUTE_LENGTH.exec(value?.trim() ?? '')
  return match ? Number(match[1]) * PX_PER_UNIT[match[2] ?? 'px']! : 0
}

async function decode(blob: Blob): Promise<HTMLImageElement> {
  const image = new Image()
  image.src = URL.createObjectURL(blob)
  try {
    await image.decode()
  } catch {
    URL.revokeObjectURL(image.src)
    throw new Error(LOGO_UNREADABLE)
  }
  if (!image.naturalWidth || !image.naturalHeight) {
    URL.revokeObjectURL(image.src)
    throw new Error(LOGO_UNREADABLE)
  }
  return image
}

/** iOS Safari refuses a canvas over 16 777 216 pixels: a first step that big would read as unreadable. */
const CANVAS_MAX_AREA = 16_000_000

/** Halving until near the target: one big step down aliases in Firefox and Safari. */
function stepDown(image: HTMLImageElement, target: number): CanvasImageSource {
  let source: CanvasImageSource = image
  let width = image.naturalWidth
  let height = image.naturalHeight
  while (Math.max(width, height) > target * 2) {
    const half = document.createElement('canvas')
    const k = Math.min(0.5, Math.sqrt(CANVAS_MAX_AREA / (width * height)))
    width = half.width = Math.max(1, Math.floor(width * k))
    height = half.height = Math.max(1, Math.floor(height * k))
    const context = half.getContext('2d')
    if (!context) throw new Error(LOGO_UNREADABLE)
    context.imageSmoothingQuality = 'high'
    context.drawImage(source, 0, 0, width, height)
    source = half
  }
  return source
}

/** Whole and centred in a transparent square: a logo is never cropped nor stretched. */
function render(image: HTMLImageElement, size: LogoSize, vector: boolean): Promise<Blob> {
  const canvas = document.createElement('canvas')
  canvas.width = size
  canvas.height = size
  const context = canvas.getContext('2d')
  if (!context) throw new Error(LOGO_UNREADABLE)

  const scale = size / Math.max(image.naturalWidth, image.naturalHeight)
  const width = image.naturalWidth * scale
  const height = image.naturalHeight * scale
  context.imageSmoothingQuality = 'high'
  context.drawImage(vector ? image : stepDown(image, size), (size - width) / 2, (size - height) / 2, width, height)

  return new Promise((resolve, reject) => canvas.toBlob(
    blob => blob ? resolve(blob) : reject(new Error(LOGO_UNREADABLE)), 'image/png'))
}
