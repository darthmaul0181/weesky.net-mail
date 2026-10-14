import { dailyImageOf, type AppSettings } from '../../hooks/useAppSettings'
import type texts from '../../locales/en/whatsNew.json'

export interface WhatsNewContext {
  appSettings: AppSettings
}

/** Only an id the English bundle writes texts for: `items.<id>.title` then type-checks in `t()`. */
export type NoveltyId = keyof typeof texts.items

export interface WhatsNewEntry {
  id: NoveltyId
  /** The web version (src/frontend/VERSION) that ships it. */
  version: string
  /** Anchors carrying a dot, outermost first. */
  path: readonly string[]
  /** The anchor framed on arrival. */
  target: string
  /** Where Show me leads; absent when the target sits in any piece of content. */
  href?: string
  when?: (context: WhatsNewContext) => boolean
}

/** Title and body: `items.<id>` in locales/{en,fr}/whatsNew.json. */
export const WHATS_NEW: readonly WhatsNewEntry[] = [
  {
    id: 'daily-image', version: '2.4.0',
    path: ['rail:settings', 'settings:appearance'], target: 'appearance:daily-image',
    href: '/settings/appearance',
    when: ({ appSettings }) => dailyImageOf(appSettings),
  },
]
