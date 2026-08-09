import i18next from 'i18next'
import { afterEach, describe, expect, it } from 'vitest'
import { apiErrorMessage } from './apiErrorMessage'

class ApiErrorStub extends Error {
  constructor(public code: string, message: string) { super(message) }
}

describe('apiErrorMessage', () => {
  afterEach(async () => { await i18next.changeLanguage('en') })

  it('translates a code the backend guarantees', () => {
    const error = new ApiErrorStub('credentials_unavailable', 'Credentials unavailable')
    expect(apiErrorMessage(error, 'nope')).toBe('Your mail session expired. Sign in again.')
  })

  it('translates it into the active language', async () => {
    await i18next.changeLanguage('fr')
    const error = new ApiErrorStub('account_not_found', 'Account not found')
    expect(apiErrorMessage(error, 'nope')).toBe("Cette boîte n’est plus disponible.")
  })

  // Server prose is a symbol for the log, never a string for the screen: it is English whatever
  // the interface speaks, so the local message is what the user gets.
  it('answers the local fallback for a code it does not know', () => {
    const error = new ApiErrorStub('something_new', 'Some server prose')
    expect(apiErrorMessage(error, 'Could not delete this domain')).toBe('Could not delete this domain')
  })

  it('answers the fallback for a plain Error and for a non-Error', () => {
    expect(apiErrorMessage(new Error('boom'), 'Could not save')).toBe('Could not save')
    expect(apiErrorMessage('boom', 'Could not save')).toBe('Could not save')
    expect(apiErrorMessage(undefined, 'Could not save')).toBe('Could not save')
  })

  // ApiError sets .code from the envelope message when that message is itself a stable string.
  it('matches on the message when it is one of the stable strings', () => {
    expect(apiErrorMessage(new ApiErrorStub('Message not found', 'Message not found'), 'nope'))
      .toBe('This message no longer exists.')
  })
})
