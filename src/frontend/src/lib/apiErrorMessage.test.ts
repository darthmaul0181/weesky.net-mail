import i18next from 'i18next'
import { afterEach, describe, expect, it } from 'vitest'
import { apiErrorMessage, messageForCode } from './apiErrorMessage'
import { RequestTimeoutError } from './withTimeout'

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

  it('says a request timed out, in the active language', async () => {
    expect(apiErrorMessage(new RequestTimeoutError(), 'nope')).toBe('The server is not responding. Try again in a moment.')
    await i18next.changeLanguage('fr')
    expect(apiErrorMessage(new RequestTimeoutError(), 'nope')).toBe('Le serveur ne répond pas. Réessayez dans un instant.')
  })
})

// The sending-account dialog's codes: a connection test answers them in its body, a save refuses
// with them. Each says what to do in a sentence of our own.
describe('messageForCode — the sending account', () => {
  it.each([
    ['smtp_unreachable', 'The server could not be reached. Check the host and port.'],
    ['smtp_auth_failed', 'The server refused the login or password.'],
    ['smtp_auth_unsupported', 'The server offers no compatible way to authenticate.'],
    ['smtp_tls_failed', 'A secure connection could not be established.'],
    ['smtp_timeout', 'The server did not answer in time.'],
    ['password_unreadable', 'The stored password can no longer be read: enter it again.'],
    ['stored_account_invalid', 'The stored account is no longer valid: save it again.'],
    ['security_none_disallowed', 'Unencrypted connections are not allowed: choose STARTTLS or SSL/TLS.'],
    ['password_required_for_new_endpoint', 'A password is required: the host or port no longer matches the saved account.'],
    ['password_required', 'A password is required.'],
  ])('says %s in a sentence', (code, sentence) => {
    expect(messageForCode(code, 'nope')).toBe(sentence)
  })
})
