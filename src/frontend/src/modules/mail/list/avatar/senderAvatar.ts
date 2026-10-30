import { canonicalAddress } from '../../../../lib/canonicalAddress'

export type { AvatarColor } from '../../../../hooks/usePreferences'

let segmenter: Intl.Segmenter | undefined

/** Built on first use: a browser without Intl.Segmenter falls back to the first code point rather than
    failing the app at load, avatars on or off. */
function firstGrapheme(word: string): string {
  if (typeof Intl.Segmenter !== 'function') return Array.from(word)[0] ?? ''
  segmenter ??= new Intl.Segmenter(undefined, { granularity: 'grapheme' })
  return segmenter.segment(word)[Symbol.iterator]().next().value?.segment ?? ''
}

export function avatarInitials(name: string | undefined, address: string | undefined): string {
  const trimmed = name?.trim() ?? ''
  const label = canonicalAddress(trimmed) === canonicalAddress(address) ? '' : trimmed
  const words = (label || (address ?? '').trim()).split(/\s+/).filter(Boolean)
  const initials = words.slice(0, 2).map(firstGrapheme).join('').toUpperCase()
  return initials || '?'
}

export function avatarHue(address: string | undefined): number {
  let hash = 0x811c9dc5
  for (const char of canonicalAddress(address)) {
    hash = Math.imul(hash ^ char.codePointAt(0)!, 0x01000193)
  }
  return (hash >>> 0) % 360
}
