import { runInNewContext } from 'node:vm'
import { describe, expect, it, vi } from 'vitest'
import { assertApiBase, configScript, runtimeConfigPlugin } from './runtimeConfigPlugin'

// Run as the browser would, with a bare `window` and nothing else in reach.
function evaluate(script: string): Record<string, unknown> {
  const window: Record<string, unknown> = {}
  runInNewContext(script, { window })
  return window
}

function emitted(mode: string, apiBase = 'https://api.example.net') {
  const emitFile = vi.fn<(file: { fileName: string; source: string }) => void>()
  const hook = runtimeConfigPlugin(mode, apiBase).generateBundle as unknown as (this: unknown) => void
  hook.call({ emitFile })
  return emitFile.mock.calls.map(([file]) => file)
}

describe('configScript', () => {
  it('writes the address the front reads back', () => {
    expect(evaluate(configScript('https://api.example.net')))
      .toEqual({ SCOTTY_CONFIG: { apiBase: 'https://api.example.net' } })
  })

  it('cannot be broken out of by quotes in the address', () => {
    const hostile = 'https://x"}; window.hacked = 1; //'

    expect(evaluate(configScript(hostile))).toEqual({ SCOTTY_CONFIG: { apiBase: hostile } })
  })
})

describe('runtimeConfigPlugin', () => {
  it.each([['production'], ['dev']])('emits config.js in a %s build', mode => {
    const files = emitted(mode)

    expect(files).toHaveLength(1)
    expect(files[0]!.fileName).toBe('config.js')
    expect(evaluate(files[0]!.source)).toEqual({ SCOTTY_CONFIG: { apiBase: 'https://api.example.net' } })
  })

  it('emits nothing in a container build: the container writes it on start', () => {
    expect(emitted('container', '')).toEqual([])
  })

  it('serves config.js, uncached, from the dev server', () => {
    type Handler = (request: unknown, response: { setHeader: typeof setHeader; end: typeof end }) => void
    const use = vi.fn<(path: string, handler: Handler) => void>()
    const setHeader = vi.fn<(name: string, value: string) => void>()
    const end = vi.fn<(body: string) => void>()
    const hook = runtimeConfigPlugin('development', 'https://api.example.net').configureServer as unknown as
      (server: unknown) => void
    hook({ middlewares: { use } })

    const [path, handler] = use.mock.calls[0]!
    handler({}, { setHeader, end })

    expect(path).toBe('/config.js')
    expect(setHeader).toHaveBeenCalledWith('Cache-Control', 'no-cache')
    expect(evaluate(end.mock.calls[0]![0])).toEqual({ SCOTTY_CONFIG: { apiBase: 'https://api.example.net' } })
  })
})

describe('assertApiBase', () => {
  it.each([[undefined], [''], ['   ']])('stops a production build when VITE_API_BASE is %j', value => {
    expect(() => assertApiBase('build', 'production', value)).toThrow(/VITE_API_BASE is not set/)
  })

  it('lets a container build through without an address', () => {
    expect(() => assertApiBase('build', 'container', undefined)).not.toThrow()
  })

  it('lets the dev server start without an address', () => {
    expect(() => assertApiBase('serve', 'development', undefined)).not.toThrow()
  })
})
