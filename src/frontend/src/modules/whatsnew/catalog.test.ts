import { describe, expect, it } from 'vitest'
import { matchRoutes } from 'react-router'
import en from '../../locales/en'
import fr from '../../locales/fr'
import { WEB_VERSION } from '../../lib/appVersion'
import { routes } from '../../routes'
import { WHATS_NEW } from './catalog'
import { compareVersions } from './version'

const SOURCES = import.meta.glob('/src/**/*.{ts,tsx}', { query: '?raw', import: 'default', eager: true })
const code = Object.entries(SOURCES).filter(([path]) => !path.includes('.test.') && !path.endsWith('/catalog.ts')).map(([, s]) => s).join('\n')
const carried = new Set([...code.matchAll(/anchor[=:]\s*["']([a-z0-9-]+:[a-z0-9-]+)["']/g)].map(m => m[1]))
const targets = new Set([...code.matchAll(/<WhatsNewTarget\s+id="([a-z0-9-]+:[a-z0-9-]+)"/g)].map(m => m[1]))

describe('the catalogue', () => {
  it('has unique, well-formed ids and versions no later than this build', () => {
    expect(new Set(WHATS_NEW.map(e => e.id)).size).toBe(WHATS_NEW.length)
    for (const e of WHATS_NEW) {
      expect(e.id).toMatch(/^[a-z0-9-]{1,64}$/)
      expect(e.version).toMatch(/^\d+\.\d+\.\d+$/)
      expect(compareVersions(e.version, WEB_VERSION), e.id).toBeLessThanOrEqual(0)
    }
  })

  it('writes every novelty in both languages', () => {
    for (const { id } of WHATS_NEW)
      for (const bundle of [en, fr]) {
        const item = (bundle.whatsNew.items as Record<string, { title?: string; body?: string }>)[id]
        expect(item?.title, id).toBeTruthy()
        expect(item?.body, id).toBeTruthy()
      }
  })

  it('names only anchors and targets the code carries', () => {
    for (const e of WHATS_NEW) {
      for (const anchor of e.path) expect(carried, `${e.id} ${anchor}`).toContain(anchor)
      expect(targets, e.id).toContain(e.target)
    }
  })

  it('leads Show me to a route that exists', () => {
    for (const e of WHATS_NEW) if (e.href) expect(matchRoutes(routes, e.href), e.id).not.toBeNull()
  })
})
