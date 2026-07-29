import { describe, it, expect } from 'vitest'
import { tabTitle } from './useTabTitle'

describe('tabTitle', () => {
  it('puts the mailbox in front of the base title', () => {
    expect(tabTitle('mick@weesky.be', 'account.mail.weesky.net'))
      .toBe('mick@weesky.be · account.mail.weesky.net')
  })

  // The account list has not landed yet; naming the signed-in address here would announce the
  // primary mailbox on a tab that is about to open a connected one.
  it('leaves the base title alone while no account is resolved', () => {
    expect(tabTitle(null, 'account.mail.weesky.net')).toBe('account.mail.weesky.net')
  })
})
