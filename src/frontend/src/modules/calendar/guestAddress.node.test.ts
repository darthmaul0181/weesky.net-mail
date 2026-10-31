import { describe, expect, it } from 'vitest'
import { isInvitableAddress } from './guestAddress'

// The server's rule for a new guest, said on the screen before a save is refused for it.
describe('isInvitableAddress', () => {
  it.each(['marc@example.org', "o'brien+team@mail.example.org", 'Marc.Dupont@Example.org'])(
    'takes %s', address => expect(isInvitableAddress(address)).toBe(true))

  it.each([
    'marc', 'josé@example.org', '100%@example.org', '"marc dupont"@example.org', 'a(b)@example.org',
    'a{b}@example.org', /* 321 characters */ `${'a'.repeat(64)}@${'b'.repeat(252)}.org`,
  ])('refuses %s', address => expect(isInvitableAddress(address)).toBe(false))
})
