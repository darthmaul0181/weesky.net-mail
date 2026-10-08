import { configuredApiBase } from './runtimeConfig'
import type { Locale } from './locale'

/** `DailyImageInfo` on the API. */
export interface DailyImageInfo {
  version: string
  title: string
  copyright: string
}

export function dailyImageUrl(version: string, lang: Locale): string {
  return `${configuredApiBase}/api/AppSettings/daily-image/${encodeURIComponent(version)}?lang=${lang}`
}
