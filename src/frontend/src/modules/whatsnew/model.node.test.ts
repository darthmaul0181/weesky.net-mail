import { describe, expect, it } from 'vitest'
import type { NoveltyId, WhatsNewEntry } from './catalog'
import { noveltiesOf, stamp } from './model'

const NOW = Date.parse('2026-10-14T12:00:00Z')
const DAY = 24 * 60 * 60 * 1000
// Ids off the bundle: the model is tested on its own fixtures, the catalogue's texts elsewhere.
const entry = (id: string, version: string, when?: () => boolean): WhatsNewEntry =>
  ({ id: id as NoveltyId, version, path: ['rail:settings'], target: `t:${id}`, when })
const catalog = [entry('old', '2.3.0'), entry('new', '2.4.0'), entry('off', '2.4.0', () => false)]
const context = { appSettings: {} }

describe('noveltiesOf', () => {
  it('lists every entry whose condition holds, newest first', () => {
    const ids = noveltiesOf(catalog, { baseline: '0.0.0', items: [] }, context, NOW).map(n => n.entry.id)
    expect(ids).toEqual(['new', 'old'])
  })

  it('puts the later of two same-version entries first', () => {
    const ids = noveltiesOf([entry('a', '2.4.0'), entry('b', '2.4.0')], { baseline: '0.0.0', items: [] }, context, NOW).map(n => n.entry.id)
    expect(ids).toEqual(['b', 'a'])
  })

  it('signals only what came after the baseline', () => {
    const [newer, older] = noveltiesOf(catalog, { baseline: '2.3.0', items: [] }, context, NOW)
    expect([newer!.fresh, newer!.active]).toEqual([true, true])
    expect([older!.fresh, older!.active]).toEqual([false, false])
  })

  it('shows nothing before the baseline is known', () => {
    expect(noveltiesOf(catalog, { items: [] }, context, NOW).some(n => n.active)).toBe(false)
  })

  it('ends with the visit, and 30 days after the dot first showed', () => {
    const at = (iso: string) => noveltiesOf(catalog, { baseline: '2.3.0', items: [{ id: 'new', firstShownAt: iso }] }, context, NOW)[0]!
    expect(at(new Date(NOW - 29 * DAY).toISOString()).active).toBe(true)
    expect(at(new Date(NOW - 30 * DAY).toISOString()).active).toBe(false)
    const seen = noveltiesOf(catalog, { baseline: '2.3.0', items: [{ id: 'new', seenAt: '2026-10-14T00:00:00Z' }] }, context, NOW)[0]!
    expect(seen.active).toBe(false)
  })

  it('counts as unlisted until the list showed it', () => {
    const listed = noveltiesOf(catalog, { baseline: '2.3.0', items: [{ id: 'new', listedAt: '2026-10-14T00:00:00Z' }] }, context, NOW)[0]!
    expect([listed.active, listed.unlisted]).toEqual([true, false])
  })
})

describe('stamp', () => {
  it('fills empty stamps only, and always the first shown', () => {
    const state = stamp({ baseline: '0.0.0', items: [{ id: 'a', firstShownAt: 'x' }] }, ['a', 'b'], 'seen', 'now')
    expect(state.items).toEqual([
      { id: 'a', firstShownAt: 'x', seenAt: 'now' },
      { id: 'b', firstShownAt: 'now', seenAt: 'now' },
    ])
  })

  it('adds an unknown id once, however often the batch names it', () => {
    expect(stamp({ items: [] }, ['b', 'b'], 'shown', 'now').items).toEqual([{ id: 'b', firstShownAt: 'now' }])
  })
})
