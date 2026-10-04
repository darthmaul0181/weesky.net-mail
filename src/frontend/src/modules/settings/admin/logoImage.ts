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
  const image = await decode(svg ? await sized(file) : file)
  try {
    const images = {} as Record<LogoSize, Blob>
    for (const size of LOGO_SIZES) {
      images[size] = await render(image, size)
      if (images[size].size > LOGO_MAX_BYTES[size]) throw new Error(LOGO_TOO_DETAILED)
    }
    return { images, lowResolution: !svg && Math.max(image.naturalWidth, image.naturalHeight) < 512 }
  } finally {
    URL.revokeObjectURL(image.src)
  }
}

function isSvg(file: File): boolean {
  return file.type === 'image/svg+xml' || /\.svg$/i.test(file.name)
}

/** Firefox gives an SVG with no width/height a zero natural size: the viewBox lends it one. */
async function sized(file: File): Promise<Blob> {
  const doc = new DOMParser().parseFromString(await file.text(), 'image/svg+xml')
  const root = doc.documentElement
  const box = root.getAttribute('viewBox')?.trim().split(/[\s,]+/).map(Number)
  if (root.localName !== 'svg' || box?.length !== 4 || !box.every(Number.isFinite)) return file
  root.setAttribute('width', String(box[2]))
  root.setAttribute('height', String(box[3]))
  return new Blob([new XMLSerializer().serializeToString(doc)], { type: 'image/svg+xml' })
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

/** Whole and centred in a transparent square: a logo is never cropped nor stretched. */
function render(image: HTMLImageElement, size: LogoSize): Promise<Blob> {
  const canvas = document.createElement('canvas')
  canvas.width = size
  canvas.height = size
  const context = canvas.getContext('2d')
  if (!context) throw new Error(LOGO_UNREADABLE)

  const scale = size / Math.max(image.naturalWidth, image.naturalHeight)
  const width = image.naturalWidth * scale
  const height = image.naturalHeight * scale
  context.imageSmoothingQuality = 'high'
  context.drawImage(image, (size - width) / 2, (size - height) / 2, width, height)

  return new Promise((resolve, reject) => canvas.toBlob(
    blob => blob ? resolve(blob) : reject(new Error(LOGO_UNREADABLE)), 'image/png'))
}
