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

const ABSOLUTE_LENGTH = /^\d+(\.\d+)?(px)?$/

/** Firefox gives an SVG with no width/height a zero natural size: the viewBox lends it one. */
async function sized(file: File): Promise<Blob> {
  const doc = new DOMParser().parseFromString(await file.text(), 'image/svg+xml')
  const root = doc.documentElement
  if (root.localName !== 'svg') throw new Error(LOGO_UNREADABLE)
  const box = root.getAttribute('viewBox')?.trim().split(/[\s,]+/).map(Number)
  if (box?.length === 4 && box.every(Number.isFinite) && box[2]! > 0 && box[3]! > 0) {
    root.setAttribute('width', String(box[2]))
    root.setAttribute('height', String(box[3]))
    return new Blob([new XMLSerializer().serializeToString(doc)], { type: 'image/svg+xml' })
  }
  // Without a viewBox only absolute sizes are well defined: Chrome would crop anything else to 300x150.
  if (!['width', 'height'].every(name => ABSOLUTE_LENGTH.test(root.getAttribute(name)?.trim() ?? ''))) {
    throw new Error(LOGO_UNREADABLE)
  }
  return file
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

/** Halving until near the target: one big step down aliases in Firefox and Safari. */
function stepDown(image: HTMLImageElement, target: number): CanvasImageSource {
  let source: CanvasImageSource = image
  let width = image.naturalWidth
  let height = image.naturalHeight
  while (Math.max(width, height) > target * 2) {
    const half = document.createElement('canvas')
    width = half.width = Math.ceil(width / 2)
    height = half.height = Math.ceil(height / 2)
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
