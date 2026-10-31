import { describe, expect, it } from 'vitest'

// ?raw on a .ts source is the mechanism styles/modals.test.ts uses on the .tsx tree.
const source = (import.meta.glob('./queries.ts', {
  query: '?raw', import: 'default', eager: true,
}))['./queries.ts'] as string

/**
 * A source-text guard, because the cost is invisible to a behavioural test: TanStack structurally
 * shares `select`'s result, so an inline arrow answers the SAME array identity — it just re-sorts
 * the whole book and deep-compares it on every render of the hook's owner.
 */
describe('useContacts', () => {
  it('hands select a stable function rather than an arrow built per render', () => {
    expect(source).toMatch(/select: sortBook,/)
    expect(source).toMatch(/select: groupsOf,/)
    // Widened from `select: \(`, which `select: data => …` walked straight past: what is refused
    // is anything but a bare identifier, whatever the arrow is spelled like.
    for (const line of source.match(/select: .*/g) ?? []) {
      expect(line).toMatch(/^select: [A-Za-z_$][\w$]*,$/)
    }
  })
})
