import './test-runtime-config'
import { vi } from 'vitest'
import './test-i18n'

vi.mock('./modules/whatsnew/catalog', async () => ({ WHATS_NEW: (await import('./modules/whatsnew/testCatalog')).TEST_CATALOG }))
