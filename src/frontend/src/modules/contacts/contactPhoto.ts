/**
 * The reducer the editor runs before anything leaves the browser (décision 8). Pure — no query, no
 * React, no i18n: it throws the translation key and lets the caller render it.
 */

/** `ContactValidator.MaxPhotoBytes`, mirrored: what the server accepts once decoded. */
const MAX_BYTES = 512 * 1024

/** 1024 first; 512 is the second chance for an image too noisy to compress at any quality. */
const SIDES = [1024, 512]

const QUALITIES = [0.85, 0.7, 0.55]

export const PHOTO_UNREADABLE = 'editor.photoUnreadable'

export const PHOTO_TOO_LARGE = 'editor.photoTooLarge'

export interface ReducedPhoto {
  /** Bare base64, no data: prefix — the shape `ContactRequest.photo` reads. */
  base64: string
  /** The same bytes, for the editor's preview object URL. */
  blob: Blob
}

export async function reducePhoto(file: File): Promise<ReducedPhoto> {
  let bitmap: ImageBitmap
  try {
    // Asked for explicitly: the re-encoded JPEG loses its EXIF tag, so a portrait taken on a phone
    // would lie down for good if the browser did not apply the orientation while decoding.
    bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' })
  } catch {
    throw new Error(PHOTO_UNREADABLE)
  }

  try {
    for (const side of SIDES) {
      for (const quality of QUALITIES) {
        const blob = await encode(bitmap, side, quality)
        if (blob.size <= MAX_BYTES) return { base64: await toBase64(blob), blob }
      }
    }
  } finally {
    bitmap.close()
  }

  throw new Error(PHOTO_TOO_LARGE)
}

/** Centre-cropped square, never enlarged, drawn on white: a canvas is born transparent black and
    JPEG throws the alpha away, so a logo on a transparent ground would come back a black square. */
async function encode(bitmap: ImageBitmap, side: number, quality: number): Promise<Blob> {
  const crop = Math.min(bitmap.width, bitmap.height)
  const size = Math.min(side, crop)
  const canvas = document.createElement('canvas')
  canvas.width = size
  canvas.height = size

  const context = canvas.getContext('2d')
  if (!context) throw new Error(PHOTO_UNREADABLE)
  context.fillStyle = '#ffffff'
  context.fillRect(0, 0, size, size)
  context.drawImage(bitmap,
    (bitmap.width - crop) / 2, (bitmap.height - crop) / 2, crop, crop, 0, 0, size, size)

  return await new Promise<Blob>((resolve, reject) => canvas.toBlob(
    blob => blob ? resolve(blob) : reject(new Error(PHOTO_UNREADABLE)), 'image/jpeg', quality))
}

/** readAsDataURL and the cut at the first comma, never
    `btoa(String.fromCharCode(...new Uint8Array(buffer)))`: spreading half a million arguments into
    a call is how that idiom blows the stack. */
function toBase64(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onerror = () => reject(new Error(PHOTO_UNREADABLE))
    reader.onload = () => {
      const result = String(reader.result)
      resolve(result.slice(result.indexOf(',') + 1))
    }
    reader.readAsDataURL(blob)
  })
}
