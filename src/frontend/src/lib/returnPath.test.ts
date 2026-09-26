import { describe, it, expect } from 'vitest'
import { returnPathOf } from './returnPath'

const from = (pathname: string, search = '', hash = '') => ({ from: { pathname, search, hash } })

describe('returnPathOf', () => {
  it('keeps a deep link with its query and hash', () => {
    expect(returnPathOf(from('/mail', '?folder=INBOX&uid=7', '#top'))).toBe('/mail?folder=INBOX&uid=7#top')
    expect(returnPathOf(from('/mail/compose', '?mailto=mailto%3Abob%40example.com')))
      .toBe('/mail/compose?mailto=mailto%3Abob%40example.com')
  })

  it('goes home without a remembered location', () => {
    expect(returnPathOf(null)).toBe('/')
    expect(returnPathOf({})).toBe('/')
    expect(returnPathOf({ from: '/mail' })).toBe('/')
    expect(returnPathOf({ from: { pathname: 42 } })).toBe('/')
  })

  it.each([
    ['//evil.example'], ['//evil.example/mail'], ['/\\evil.example'], ['https://evil.example'], ['javascript:alert(1)'],
    ['/\t/evil.example'], ['evil.example'], [''], ['//['],
    ['/.//evil.example'], ['/..//evil.example'], ['/%2e//evil.example'], ['/./\\evil.example'],
  ])('refuses %j', pathname => {
    expect(returnPathOf(from(pathname))).toBe('/')
  })

  // The path is checked whole: a search or hash must not be able to lengthen it into another host.
  it('refuses a search or hash that does not start as one', () => {
    expect(returnPathOf(from('/', '\\evil.example'))).toBe('/')
    expect(returnPathOf(from('/', '', '/evil.example'))).toBe('/')
  })

  it('does not return to the login page itself', () => {
    expect(returnPathOf(from('/login'))).toBe('/')
    expect(returnPathOf(from('/login', '?x=1'))).toBe('/')
    expect(returnPathOf(from('/Login/'))).toBe('/')
  })
})
