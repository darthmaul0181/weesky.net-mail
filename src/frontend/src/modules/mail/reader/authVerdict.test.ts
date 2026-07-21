import { describe, it, expect } from 'vitest'
import { authVerdict } from './authVerdict'

const auth = (spf: string | null, dkim: string | null) => ({ spf, dkim, raw: 'mx.weesky.net; …' })

describe('authVerdict', () => {
  it('passes only when both methods passed', () => {
    expect(authVerdict(auth('pass', 'pass'))).toBe('pass')
  })

  it('fails when either method failed explicitly', () => {
    expect(authVerdict(auth('fail', 'pass'))).toBe('fail')
    expect(authVerdict(auth('pass', 'fail'))).toBe('fail')
    expect(authVerdict(auth('fail', 'fail'))).toBe('fail')
  })

  // A softfail or a neutral is not a failure, and painting either signal onto an ambiguous
  // result is worse than painting none: the reader learns to ignore the badge.
  it('says nothing about a result that is neither a pass nor a failure', () => {
    expect(authVerdict(auth('softfail', 'pass'))).toBeNull()
    expect(authVerdict(auth('neutral', 'neutral'))).toBeNull()
    expect(authVerdict(auth('temperror', 'permerror'))).toBeNull()
  })

  it('says nothing when a method is missing', () => {
    expect(authVerdict(auth('pass', null))).toBeNull()
    expect(authVerdict(auth(null, null))).toBeNull()
  })

  it('says nothing when the message carries no authentication at all', () => {
    expect(authVerdict(null)).toBeNull()
    expect(authVerdict(undefined)).toBeNull()
  })
})
