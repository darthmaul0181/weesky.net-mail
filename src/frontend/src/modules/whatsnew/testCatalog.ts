import { dailyImageOf } from '../../hooks/useAppSettings'
import type { WhatsNewEntry } from './catalog'

/** What every test but catalog.test reads as the catalogue (test-setup.ts), frozen on its first novelty. */
export const TEST_CATALOG: readonly WhatsNewEntry[] = [
  {
    id: 'daily-image', version: '2.4.0',
    path: ['rail:settings', 'settings:appearance'], target: 'appearance:daily-image',
    href: '/settings/appearance',
    when: ({ appSettings }) => dailyImageOf(appSettings),
  },
]
