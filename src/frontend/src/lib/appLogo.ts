import favicon32 from '../assets/favicon-32.png'
import logo192 from '../assets/logo-192.png'
import { configuredApiBase } from './runtimeConfig'
import { readStored, writeStored } from './safeStorage'

export const LOGO_SIZES = [32, 192, 512] as const
export type LogoSize = typeof LOGO_SIZES[number]
export type LogoUrls = Record<LogoSize, string>

/** `AppLogo.MaxBytes` on the API, mirrored so a too-detailed image is refused before upload. */
export const LOGO_MAX_BYTES: Record<LogoSize, number> = { 32: 16 * 1024, 192: 256 * 1024, 512: 1024 * 1024 }

const SCOTTY: LogoUrls = { 32: favicon32, 192: logo192, 512: '/icon-512.png' }
const VERSION_KEY = 'app.logoVersion'
const BASE: string = configuredApiBase

export function logoUrls(version: string): LogoUrls {
  if (!version) return SCOTTY
  const at = (size: LogoSize) => `${BASE}/api/AppSettings/logo/${size}?v=${encodeURIComponent(version)}`
  return { 32: at(32), 192: at(192), 512: at(512) }
}

export function rememberedLogoVersion(): string {
  return readStored(VERSION_KEY) ?? ''
}

export function rememberLogoVersion(version: string): void {
  writeStored(VERSION_KEY, version)
}

let current = SCOTTY

/** For a plain module such as the desktop notifications, which cannot call a hook. */
export function currentLogo(): LogoUrls {
  return current
}

export function setCurrentLogo(urls: LogoUrls): void {
  current = urls
}
