import { isValidAddress } from '../mail/compose/RecipientsField'

/** The server's column and the characters a plain `mailto:` value carries unescaped. */
const MAX_LENGTH = 320
const PLAIN = /^[A-Za-z0-9!#$&'*+./=?_~-]+@[A-Za-z0-9.-]+$/

/** An address the server takes as a new guest (`EventRequestValidator`): its answer can only be
    matched back to a `mailto:` value that needs no escaping. */
export function isInvitableAddress(email: string): boolean {
  return email.length <= MAX_LENGTH && isValidAddress(email) && PLAIN.test(email)
}
