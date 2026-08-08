import { describe, expect, it } from 'vitest'
import en from './en'
import fr from './fr'
import { SUPPORTED_LOCALES } from '../lib/locale'

type Node = string | { [key: string]: Node }

/** Every leaf path, so a key nested three deep is compared as precisely as a top-level one. */
function paths(node: Node, prefix = ''): string[] {
  if (typeof node === 'string') return [prefix]
  return Object.entries(node).flatMap(([key, value]) =>
    paths(value, prefix ? `${prefix}.${key}` : key))
}

function leaf(bundle: Node, path: string): Node {
  return path.split('.').reduce<Node>((node, key) => (node as Record<string, Node>)[key], bundle)
}

describe('catalogue parity', () => {
  // There is no runtime fallbackLng: a key present in one catalogue and missing from the other
  // would render as its own key on screen. This test is what makes the fallback unnecessary.
  it('gives both catalogues the same namespaces', () => {
    expect(Object.keys(fr).sort()).toEqual(Object.keys(en).sort())
  })

  for (const namespace of Object.keys(en) as (keyof typeof en)[]) {
    it(`gives ${namespace} the same keys in both languages`, () => {
      expect(paths(fr[namespace]).sort()).toEqual(paths(en[namespace]).sort())
    })
  }

  /**
   * French sets `; : ? !` and the inside of a guillemet pair with a no-break space, and that is
   * layout rather than taste: a plain U+0020 there is a legal break point, so a long interpolated
   * value wraps between an opening guillemet and its own content, or leaves a `?` stranded at the
   * head of a line. The two spaces look identical on screen, so the fixtures below come in
   * pairs: whichever one an editor silently normalises, one of the two assertions reddens.
   *
   * `; : ? !` also has to carry *some* space before it, not just the right kind — a bare
   * `"Enregistrer?"` is exactly as wrong as `"Enregistrer ?"` and used to pass unnoticed. Three
   * carve-outs stay silent: `}}` right before the mark is an interpolated value's own suffix
   * (a clock face, `{{value}}:00`, not sentence punctuation); `<code>` right before it is a
   * literal example character (`<code>?</code>` for the glob wildcard); and `:` immediately
   * followed by `//` is a URL scheme.
   */
  const LOOSE = /(?<!\u00A0)(?<!\}\})(?<!<code>)[;:?!](?!\/\/)|« | »/

  it('catches a plain space where French wants a no-break one', () => {
    expect('Score de spam :').toMatch(LOOSE)
    expect('Enregistrer ?').toMatch(LOOSE)
    expect('« Brouillons »').toMatch(LOOSE)
    expect('Score de spam :').not.toMatch(LOOSE)
    expect('« Brouillons »').not.toMatch(LOOSE)
    // An ordinary space before any other character is untouched, ':' inside a URL included.
    expect('Voir https://exemple.fr et la suite').not.toMatch(LOOSE)
  })

  it('catches punctuation with no space at all before it, not only the wrong space', () => {
    expect('Enregistrer?').toMatch(LOOSE)
    // The interpolated suffix in `{{value}}:00` is a clock face, not sentence punctuation.
    expect('Heure {{operator}} {{value}}:00').not.toMatch(LOOSE)
    // The wildcard character shown in `<code>?</code>` is example text, not a real question mark.
    expect('correspond à un seul <code>?</code>').not.toMatch(LOOSE)
  })

  it('spaces French punctuation with a no-break space', () => {
    for (const [name, bundle] of Object.entries(fr)) {
      for (const path of paths(bundle as Node)) {
        expect(leaf(bundle as Node, path) as string, `${name}:${path}`).not.toMatch(LOOSE)
      }
    }
  })

  /**
   * The French apostrophe is `’` (U+2019), never the ASCII `'`. It is a rule and not a taste for
   * the same reason as the space above: the straight one is a typewriter substitute that renders
   * as an upright tick beside the curved ones already in every other sentence, and at 13px that
   * difference is too small to survive review while being perfectly visible on screen. English
   * keeps the ASCII one — `this account's password` is nine live strings — so the sweep is over
   * the French bundles alone. The fixtures pair for the same reason the ones above do.
   */
  const STRAIGHT = /'/

  it('catches a straight apostrophe where French wants a typographic one', () => {
    expect("Impossible d'ouvrir le brouillon").toMatch(STRAIGHT)
    expect('Impossible d’ouvrir le brouillon').not.toMatch(STRAIGHT)
  })

  it('writes every French apostrophe as ’', () => {
    for (const [name, bundle] of Object.entries(fr)) {
      for (const path of paths(bundle as Node)) {
        expect(leaf(bundle as Node, path) as string, `${name}:${path}`).not.toMatch(STRAIGHT)
      }
    }
  })

  it('leaves no empty translation', () => {
    for (const [name, bundle] of Object.entries(fr)) {
      for (const path of paths(bundle as Node)) {
        expect(leaf(bundle as Node, path), `${name}:${path}`).not.toBe('')
      }
    }
  })

  it('lists exactly the locales the resolver supports', () => {
    expect([...SUPPORTED_LOCALES].sort()).toEqual(['en', 'fr'])
  })
})
