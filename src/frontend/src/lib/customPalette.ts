import type { Palette } from '../contexts/ThemeContext'

export type Intensity = 'neutral' | 'muted' | 'vivid'
export const INTENSITIES: readonly Intensity[] = ['neutral', 'muted', 'vivid']
/** What the light-mode buttons wear; dark ones wear the accent either way, a dark structure dissolving there. */
export type Buttons = 'structure' | 'accent'
export const BUTTONS: readonly Buttons[] = ['structure', 'accent']
export interface CustomPaletteDef { structure: number; intensity: Intensity; accent: number; buttons: Buttons }
export type TokenSet = Record<`--${string}`, string>
export interface CustomPaletteTokens { light: TokenSet; dark: TokenSet }

const PATTERN = /^(0|[1-9]\d{0,2}),(neutral|muted|vivid),(0|[1-9]\d{0,2})(?:,(structure|accent))?$/
const WHITE = '#ffffff'
const DANGER_HUE = 27

export function parseCustomPalette(value: string | null | undefined): CustomPaletteDef | null {
  const match = PATTERN.exec(value ?? '')
  if (!match) return null
  const structure = Number(match[1]), accent = Number(match[3])
  if (structure >= 360 || accent >= 360) return null
  return { structure, intensity: match[2] as Intensity, accent, buttons: (match[4] ?? 'structure') as Buttons }
}

export function formatCustomPalette({ structure, intensity, accent, buttons }: CustomPaletteDef): string {
  return `${structure},${intensity},${accent},${buttons}`
}

function hueGap(a: number, b: number): number {
  const d = Math.abs(a - b) % 360
  return d > 180 ? 360 - d : d
}

export function isNearDanger(accent: number): boolean {
  return hueGap(accent, DANGER_HUE) < 8
}

const lin = (v: number) => v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4
const gam = (v: number) => v <= 0.0031308 ? 12.92 * v : 1.055 * v ** (1 / 2.4) - 0.055

function oklchToRgb(L: number, C: number, h: number): number[] {
  const a = C * Math.cos(h * Math.PI / 180), b = C * Math.sin(h * Math.PI / 180)
  const l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3
  const m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3
  const s = (L - 0.0894841775 * a - 1.291485548 * b) ** 3
  return [
    4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s,
  ]
}

/** Chroma gives way until the colour exists in sRGB, so the asked hue and lightness are kept. */
function oklch(L: number, C: number, h: number): string {
  let rgb = oklchToRgb(L, C, h)
  while (rgb.some(v => v < -0.0005 || v > 1.0005) && C > 0) {
    C = Math.max(0, C - 0.004)
    rgb = oklchToRgb(L, C, h)
  }
  return '#' + rgb.map(v => Math.round(gam(Math.min(1, Math.max(0, v))) * 255).toString(16).padStart(2, '0')).join('')
}

const channels = (hex: string) => [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255)
const luminance = (hex: string) => {
  const [r, g, b] = channels(hex).map(lin) as [number, number, number]
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

export function contrast(a: string, b: string): number {
  const x = luminance(a), y = luminance(b)
  return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05)
}

function mix(a: string, b: string, t: number): string {
  const A = channels(a), B = channels(b)
  return '#' + A.map((v, i) => Math.round((v + (B[i]! - v) * t) * 255).toString(16).padStart(2, '0')).join('')
}

/** Walks the lightness by `step` until `ok` holds: each threshold is imposed, not hoped for. */
function solve(L: number, C: number, h: number, step: number, ok: (c: string) => boolean): string {
  let colour = oklch(L, C, h)
  for (let i = 0; i < 80 && !ok(colour); i++) colour = oklch(L += step, C, h)
  return colour
}

const STRUCTURE: Record<Intensity, { L: number; C: number; tint: number }> = {
  neutral: { L: 0.27, C: 0.012, tint: 0.004 },
  muted: { L: 0.30, C: 0.06, tint: 0.008 },
  vivid: { L: 0.40, C: 0.14, tint: 0.012 },
}

function inkOn(fill: string, hue: number): string {
  const dark = oklch(0.2, 0.03, hue)
  return contrast(dark, fill) >= contrast(WHITE, fill) ? dark : WHITE
}

// Same values as night: status colours and swipe bands do not depend on the palette.
const SEMANTIC_LIGHT = {
  '--icon-hover-danger': '#f87171', '--danger': '#dc2626', '--danger-hover': '#b91c1c',
  '--success': '#16a34a', '--warning': '#b45309', '--status-fg': '#ffffff', '--scrim': 'rgba(0, 0, 0, 0.35)',
  '--swipe-seen': '#2563eb', '--swipe-flag': '#b45309', '--swipe-archive': '#15803d', '--swipe-delete': '#dc2626',
}
const SEMANTIC_DARK = {
  '--danger': '#f87171', '--danger-hover': '#ef4444', '--success': '#4ade80', '--warning': '#d97706',
  '--scrim': 'rgba(0, 0, 0, 0.35)',
}

export function generateCustomPalette({ structure: hs, intensity, accent: ha, buttons }: CustomPaletteDef): CustomPaletteTokens {
  const s = STRUCTURE[intensity], t = s.tint, dt = t * 1.4
  const accentLight = solve(0.55, 0.16, ha, -0.01, c => contrast(WHITE, c) >= 4.6)
  const accentDark = solve(0.74, 0.14, ha, -0.01, c => contrast(WHITE, c) >= 2.8)
  const railActiveLight = oklch(0.8, 0.14, ha)

  const surface = oklch(0.99, t / 2, hs), foldersHover = oklch(0.895, t * 2.6, hs)
  const topbar = solve(s.L, s.C, hs, -0.01, c => contrast(WHITE, c) >= 7)
  const selected = mix(surface, accentLight, 0.11), separator = oklch(0.915, t, hs)
  const action = buttons === 'accent' ? accentLight : topbar
  const light: TokenSet = {
    '--bg': oklch(0.958, t, hs), '--folders-bg': oklch(0.925, t * 2.2, hs), '--folders-item-hover': foldersHover,
    '--surface': surface, '--surface-raised': surface, '--surface-sunken': oklch(0.948, t, hs),
    '--border': oklch(0.885, t * 1.2, hs), '--text': oklch(0.2, t * 1.5, hs),
    '--text-muted': solve(0.5, t * 2, hs, -0.01, c => contrast(c, foldersHover) >= 4.6),
    '--topbar-bg': topbar, '--topbar-fg': WHITE, '--rail-bg': topbar,
    '--rail-fg': mix(topbar, WHITE, 0.74), '--rail-item': mix(topbar, WHITE, 0.11),
    '--rail-item-active': railActiveLight, '--rail-item-active-fg': inkOn(railActiveLight, ha),
    '--pane-item-hover': oklch(0.935, t, hs), '--pane-item-active-bg': selected, '--pane-item-active-fg': topbar,
    '--accent-unread': accentLight,
    '--list-row-hover': oklch(0.948, t, hs), '--list-row-selected-bg': selected, '--list-row-selected-fg': topbar,
    '--list-separator': separator, '--badge-count-bg': accentLight, '--badge-count-fg': WHITE,
    '--reader-header-border': separator,
    '--quote-text': solve(0.56, t * 1.5, hs, -0.01, c => contrast(c, surface) >= 4.6),
    '--action-primary': action, '--action-primary-hover': mix(action, WHITE, 0.12), '--action-primary-fg': WHITE,
    '--icon-hover-accent': accentDark,
    ...SEMANTIC_LIGHT,
  }

  const bg = oklch(0.19, dt, hs), darkSurface = oklch(0.222, dt, hs), raised = oklch(0.245, dt, hs)
  const border = oklch(0.29, dt, hs), text = oklch(0.925, 0.006, hs)
  const darkTop = oklch(0.16, Math.min(s.C * 0.5, 0.04), hs)
  const darkInk = inkOn(accentDark, ha), darkSelected = mix(darkSurface, accentDark, 0.16)
  const darkSelectedFg = oklch(0.93, 0.03, ha)
  const dark: TokenSet = {
    '--bg': bg, '--folders-bg': bg, '--folders-item-hover': raised,
    '--surface': darkSurface, '--surface-raised': raised, '--surface-sunken': oklch(0.205, dt, hs),
    '--border': border, '--text': text,
    '--text-muted': solve(0.7, 0.01, hs, 0.01, c => contrast(c, raised) >= 4.6),
    '--topbar-bg': darkTop, '--topbar-fg': text, '--rail-bg': darkTop,
    '--rail-fg': oklch(0.76, 0.02, hs), '--rail-item': mix(darkTop, WHITE, 0.07),
    '--rail-item-active': accentDark, '--rail-item-active-fg': darkInk,
    '--pane-item-hover': raised, '--pane-item-active-bg': darkSelected, '--pane-item-active-fg': darkSelectedFg,
    '--accent-unread': accentDark,
    '--list-row-hover': raised, '--list-row-selected-bg': darkSelected, '--list-row-selected-fg': darkSelectedFg,
    '--list-separator': border, '--badge-count-bg': accentDark, '--badge-count-fg': darkInk,
    '--reader-header-border': border,
    '--quote-text': solve(0.64, 0.01, hs, 0.01, c => contrast(c, darkSurface) >= 4.6),
    '--action-primary': accentDark, '--action-primary-hover': mix(accentDark, WHITE, 0.12), '--action-primary-fg': WHITE,
    '--status-fg': bg,
    ...SEMANTIC_DARK,
  }

  return { light, dark }
}

export function hueGradient(lightness: number, chroma: number): string {
  const stops = Array.from({ length: 19 }, (_, i) => oklch(lightness, chroma, (i * 20) % 360))
  return `linear-gradient(to right, ${stops.join(', ')})`
}

function block(selector: string, set: TokenSet): string {
  return `${selector} {\n${Object.entries(set).map(([role, value]) => `  ${role}: ${value};`).join('\n')}\n}`
}

export function customPaletteCss({ light, dark }: CustomPaletteTokens): string {
  return `${block("[data-palette='custom']", light)}\n${block("[data-palette='custom'][data-theme='dark']", dark)}\n`
}

/** Where the editor starts: the hues of the palette in use, measured from its stylesheet. */
export const PALETTE_SEEDS: Record<Exclude<Palette, 'custom'>, CustomPaletteDef> = {
  night: { structure: 265, intensity: 'muted', accent: 35, buttons: 'structure' },
  classic: { structure: 267, intensity: 'vivid', accent: 267, buttons: 'structure' },
  forest: { structure: 159, intensity: 'muted', accent: 71, buttons: 'structure' },
  slate: { structure: 228, intensity: 'muted', accent: 190, buttons: 'accent' },
  plum: { structure: 320, intensity: 'muted', accent: 81, buttons: 'structure' },
  ink: { structure: 286, intensity: 'neutral', accent: 263, buttons: 'structure' },
  azure: { structure: 242, intensity: 'muted', accent: 244, buttons: 'structure' },
  indigo: { structure: 288, intensity: 'vivid', accent: 288, buttons: 'structure' },
}
