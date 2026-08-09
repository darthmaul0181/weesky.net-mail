import { describe, it, expect } from 'vitest'

// ?raw on the stylesheets, the mechanism modals.test.ts uses on the components.
const sheets = import.meta.glob('./*.css', {
  query: '?raw', import: 'default', eager: true,
}) as Record<string, string>
const root = import.meta.glob('../index.css', {
  query: '?raw', import: 'default', eager: true,
}) as Record<string, string>
const all = { ...sheets, ...root }

function widthsUsedBy(query: RegExp): string[] {
  return Object.entries(all).flatMap(([path, css]) =>
    [...css.matchAll(query)].map(match => `${path}: ${match[0]}`))
}

// Slices out an at-rule's body by brace depth, the way palettes.test.ts's tokensIn slices a
// selector block — a plain indexOf-to-next-'}' would stop at the first rule inside the query, not
// at the query's own end.
//
// EVERY occurrence, concatenated, and that is not tidiness: mail.css declares
// `@container (max-width: 480px)` twice — once per container column, .mail-list's toolbar states
// and .mail-reader's header — and a scan that stopped at the first would read one block and let a
// hover-keyed rule dropped into the other ship green. That is the precise regression this file's
// hover guard exists to catch, and it took several review rounds to find the first time.
function mediaBlocks(css: string, query: string): string {
  const blocks: string[] = []
  for (let at = css.indexOf(query); at >= 0; at = css.indexOf(query, at + query.length)) {
    let depth = 0
    let i = css.indexOf('{', at)
    if (i < 0) break
    const start = i
    for (; i < css.length; i++) {
      if (css[i] === '{') depth++
      else if (css[i] === '}' && --depth === 0) break
    }
    blocks.push(css.slice(start, i + 1))
  }
  return blocks.join('\n')
}

describe('responsive contract', () => {
  // A key count alone passes on 14 files all holding '' — exactly what an under-inclusive
  // vite.config.js test.css.include mock produces. Real content is what proves the glob read.
  it('reads the stylesheets, not an empty glob', () => {
    const lengths = Object.values(all).map(css => css.length)
    expect(lengths.length).toBeGreaterThan(5)
    expect(Math.min(...lengths)).toBeGreaterThan(0)
  })

  it('holds no desktop floor', () => {
    expect(all['./shell.css']).not.toMatch(/min-width:\s*1024px/)
  })

  // Desktop stays the unqualified base rule. A min-width query means somebody inverted the
  // cascade, and every desktop rule now has to be read through a filter.
  it('uses no min-width media query', () => {
    expect(widthsUsedBy(/@media[^{]*min-width[^{]*/g)).toEqual([])
  })

  // Exactly two breakpoints, spelled one way each. Scoped to @media on purpose: @container
  // queries carry their own widths and answer to the column they measure, not to the window.
  it('uses only the two agreed breakpoint widths', () => {
    const widths = widthsUsedBy(/@media[^{]*max-width:\s*\d+px/g)
      .map(entry => entry.replace(/.*max-width:\s*/, ''))
    expect([...new Set(widths)].sort()).toEqual(['1023px', '639px'])
  })

  it('sizes the full-height roots in dvh', () => {
    expect(all['./shell.css']).toMatch(/height:\s*100dvh/)
    expect(all['../index.css']).toMatch(/min-height:\s*100dvh/)
  })

  // Two concerns, two conditions: the list toolbar's states answer to the column it sits in, the
  // row's hover-revealed controls answer to the input device. Folding the second back into the
  // first is what put a 380px default column inside a 480px container query and took the row
  // cluster away from every mouse. Nothing else can catch it — jsdom computes no layout and no
  // probe can emulate `hover` — so the guard is on the text, the way the whitelist above is.
  it('keeps hover rules out of the column query', () => {
    const mail = all['./mail.css']
    // The .not.toMatch below passes vacuously on an empty string, so the count is pinned first:
    // a named container, a lost space or a re-spelled width would empty the slice silently and
    // this file would go on reporting green over a block nothing is reading. Two blocks, one per
    // container column — a third is a deliberate change and should say so here.
    expect([...mail.matchAll(/@container \(max-width: 480px\)/g)]).toHaveLength(2)
    expect(mediaBlocks(mail, '@container (max-width: 480px)')).not.toMatch(/:hover|:focus-within/)
    expect(mediaBlocks(mail, '@media (hover: none)')).toMatch(/\.message-row:hover \.message-row-cluster/)
    expect(mediaBlocks(all['../index.css'], '@media (hover: none)'))
      .toMatch(/\.contact-tile:hover \.contact-tile-actions/)
  })

  // The archive/junk/delete group answers to 480; the master checkbox answers to nothing at all
  // and is drawn at every column width, down to the 240px floor of `usePaneSize('mail.split.right',
  // 380, 240)`. It is the only door into the selecting state those actions appear in that anything
  // on screen announces — the other one is long-press — so hiding it is what made a 390px phone and
  // a 360px one behave differently. The second assertion is the whole of that rule: a threshold of
  // any width, not only the 360 this replaced, puts the door back behind a column measurement.
  // Text again for the reason the rest of this file is — the difference is a rendered box, which
  // jsdom does not have. probes/mobile-layout.html's toolbar-master-380/-360/-240 trio is the
  // geometry, and all three read a box rather than 'none rendered'.
  it('keeps the master checkbox at every column width', () => {
    const mail = all['./mail.css']
    // Every @container body in the file, not the 480 one alone: what is forbidden is the master
    // answering to a column measurement at all, and a new block is exactly how that comes back.
    // The trailing `(` is what keeps the prose out — four comments in mail.css name `@container`
    // without a condition, and slicing from one of those would run this scan over whichever block
    // happened to follow it. And the first assertion is the same guard the count above is: an
    // empty slice would let the second pass over nothing at all.
    const containers = mediaBlocks(mail, '@container (')
    expect(containers).toMatch(/selection-archive/)
    expect(containers).not.toMatch(/selection-master-hit/)
  })

  // Only the root's value propagates to the viewport, and `.app-shell` declares no `overflow`, so
  // it is not a scroll container: the rule it used to carry could never contain Chrome for
  // Android's own pull-to-refresh, whose reload takes an unsaved draft with it.
  it('contains the native pull-to-refresh at the root', () => {
    expect(all['../index.css']).toMatch(/html\s*\{[^}]*overscroll-behavior-y:\s*contain/)
    expect(all['./shell.css']).not.toMatch(/overscroll-behavior/)
  })

  it('declares the touch floor once, in the phone block', () => {
    const shell = all['./shell.css']
    expect([...shell.matchAll(/--touch:/g)]).toHaveLength(1)
    expect(mediaBlocks(shell, '@media (max-width: 639px)')).toMatch(/--touch:/)
  })
})
