import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const initI18n = vi.hoisted(() => vi.fn(() => new Promise<never>(() => {})))
vi.mock('./lib/i18n', async importOriginal => ({ ...await importOriginal<object>(), initI18n }))

describe('main', () => {
  const saved = window.SCOTTY_CONFIG

  beforeEach(() => {
    document.body.innerHTML = '<div id="root"></div>'
    initI18n.mockClear()
    vi.resetModules()
  })

  afterEach(() => {
    window.SCOTTY_CONFIG = saved
  })

  it('shows the configuration error instead of the app when /config.js did not run', async () => {
    delete window.SCOTTY_CONFIG

    await import('./main')

    expect(document.getElementById('root')!.textContent).toContain('/config.js')
    expect(initI18n).not.toHaveBeenCalled()
  })

  it('shows the configuration error, quoting the value, for an invalid address', async () => {
    window.SCOTTY_CONFIG = { apiBase: 'api.example.net' }

    await import('./main')

    expect(document.getElementById('root')!.textContent).toContain('"api.example.net"')
    expect(initI18n).not.toHaveBeenCalled()
  })

  it('starts the app on a valid configuration', async () => {
    window.SCOTTY_CONFIG = { apiBase: 'https://api.example.test' }

    await import('./main')

    expect(initI18n).toHaveBeenCalledOnce()
  })
})
