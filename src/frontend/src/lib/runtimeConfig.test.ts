import { describe, expect, it } from 'vitest'
import { parseRuntimeConfig, showConfigError, type RuntimeConfig } from './runtimeConfig'

function errorOf(result: RuntimeConfig): string {
  expect(result).toHaveProperty('error')
  return (result as { error: string }).error
}

describe('parseRuntimeConfig', () => {
  it.each([
    ['https://api.example.net', 'https://api.example.net'],
    ['http://localhost:5000', 'http://localhost:5000'],
    ['https://api.example.net/', 'https://api.example.net'],
    ['https://api.example.net//', 'https://api.example.net'],
    ['  https://api.example.net/  ', 'https://api.example.net'],
    ['https://example.net/webmail/', 'https://example.net/webmail'],
    ['HTTPS://API.example.net', 'HTTPS://API.example.net'],
  ])('keeps the absolute address %j as %j', (value, expected) => {
    expect(parseRuntimeConfig({ apiBase: value })).toEqual({ apiBase: expected })
  })

  it.each([[''], ['   ']])('reads %j as this site\'s own address', value => {
    expect(parseRuntimeConfig({ apiBase: value })).toEqual({ apiBase: '' })
  })

  it('reads a missing apiBase as this site\'s own address', () => {
    expect(parseRuntimeConfig({})).toEqual({ apiBase: '' })
  })

  it.each([
    ['api.example.net'],
    ['//api.example.net'],
    ['ftp://api.example.net'],
    ['https://'],
    ['https://api.example.net/?x=1'],
    ['https://api.example.net/#top'],
    ['https://user:secret@api.example.net'],
  ])('refuses %j and quotes it', value => {
    expect(errorOf(parseRuntimeConfig({ apiBase: value }))).toContain(JSON.stringify(value))
  })

  it('refuses a value that is not a string', () => {
    expect(errorOf(parseRuntimeConfig({ apiBase: 42 }))).toContain('42')
  })

  it.each([[undefined], [null], ['https://api.example.net']])(
    'reports a missing configuration when SCOTTY_CONFIG is %j',
    config => {
      expect(errorOf(parseRuntimeConfig(config))).toContain('/config.js')
    },
  )
})

describe('showConfigError', () => {
  it('shows the message as text, never as markup', () => {
    const root = document.createElement('div')
    root.innerHTML = '<span>app</span>'

    showConfigError(root, 'Invalid API address in /config.js: "<img src=x onerror=alert(1)>"')

    expect(root.textContent).toContain('<img src=x onerror=alert(1)>')
    expect(root.querySelector('img')).toBeNull()
    expect(root.querySelector('span')).toBeNull()
  })
})
