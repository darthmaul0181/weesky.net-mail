import i18next from 'i18next'
import { afterEach, describe, expect, it } from 'vitest'
import { roleLabel } from './roleLabel'

// getFixedT gives a real TFunction<'mail'> that follows the current language — no cast, and it
// re-reads the active catalogue on every call, so one instance covers both languages here.
const t = i18next.getFixedT(null, 'mail')

describe('roleLabel', () => {
  afterEach(async () => { await i18next.changeLanguage('en') })

  it.each([
    ['inbox', 'Inbox'], ['sent', 'Sent'], ['drafts', 'Drafts'],
    ['trash', 'Trash'], ['junk', 'Junk'], ['archive', 'Archive'],
  ])('labels %s as %s in English', (role, label) => {
    expect(roleLabel(role, t)).toBe(label)
  })

  it('names each well-known role in French too', async () => {
    await i18next.changeLanguage('fr')
    expect(roleLabel('inbox', t)).toBe('Boîte de réception')
    expect(roleLabel('junk', t)).toBe('Indésirables')
  })

  // The role is the language-independent canonical key; an unknown one is not a missing
  // translation, it is a role this build has never heard of.
  it('answers the role itself when it holds no label', () => {
    expect(roleLabel('flagged', t)).toBe('flagged')
  })
})
