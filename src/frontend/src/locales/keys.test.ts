import { describe, expect, it } from 'vitest'
import en from './en'

/**
 * `tsconfig.json` sets `checkJs: false`, so the typed `t()` only guards the `.ts`/`.tsx` files —
 * a mistyped key in any of the module's `.jsx` pages compiles, and with no runtime `fallbackLng`
 * it renders as its own key on screen. This is that guard, read off the source text.
 *
 * What it checks: every key written as a literal inside a `t(…)` call — including one arm of a
 * ternary, which is this codebase's idiom for a two-state label — and every `<Trans>` `i18nKey`.
 * A key built by interpolation is checked as far as its static prefix, which must name a real
 * group; a renamed group is the failure that class of key actually suffers.
 *
 * What it cannot check: a key that reaches `t()` only as a variable, since nothing static names
 * it. Write the literal in the file that uses it — a helper taking the key as a parameter hides
 * it from here, which is why `RuleHelpModal`'s descriptions are inlined rather than factored.
 *
 * Only English is checked: `parity.test.ts` already fails on any key present in one catalogue and
 * absent from the other, so one direction settles both.
 */
type Node = string | { [key: string]: Node }

const SOURCES = import.meta.glob('/src/**/*.{ts,tsx,jsx}', {
  query: '?raw', import: 'default', eager: true,
}) as Record<string, string>

const catalogue = en as unknown as Record<string, Node>
/** `I18N_OPTIONS.defaultNS`. A `<Trans>` with no `ns` resolves here, whatever the file bound. */
const DEFAULT_NS = 'common'

function at(namespace: string, key: string): Node | undefined {
  let node: Node | undefined = catalogue[namespace]
  for (const part of key.split('.')) {
    if (typeof node !== 'object' || node === undefined || !(part in node)) return undefined
    node = node[part]
  }
  return node
}

/** i18next tries `key_one`/`key_many`/`key_other` before the bare key, so any of them is a hit. */
function resolves(namespace: string, key: string): boolean {
  return ['', '_one', '_many', '_other'].some(s => typeof at(namespace, key + s) === 'string')
}

function qualify(raw: string, fallback: string): [string, string] {
  const colon = raw.indexOf(':')
  return colon === -1 ? [fallback, raw] : [raw.slice(0, colon), raw.slice(colon + 1)]
}

/** Walks a balanced region, skipping over string and template literals. */
function scan(
  text: string, from: number,
  stop: (char: string, depth: number, index: number) => boolean,
): number {
  let depth = 0
  let quote = ''
  for (let i = from; i < text.length; i++) {
    const char = text[i]
    if (quote) {
      if (char === '\\') i++
      else if (char === quote) quote = ''
      continue
    }
    if (char === "'" || char === '"' || char === '`') { quote = char; continue }
    if ('([{'.includes(char)) depth++
    else if (')]}'.includes(char)) depth--
    if (stop(char, depth, i)) return i
  }
  return -1
}

/** A `t(…)` call split at its first top-level comma: the key expression, then the options. */
function callArguments(source: string, open: number): [string, string] {
  const close = scan(source, open, (char, depth) => depth === 0 && char === ')')
  if (close === -1) return ['', '']
  const args = source.slice(open + 1, close)
  const comma = scan(args, 0, (char, depth) => depth === 0 && char === ',')
  return comma === -1 ? [args, ''] : [args.slice(0, comma), args.slice(comma + 1)]
}

/**
 * The value arms of a key expression. Only an arm holds a key: the *condition* of the two-state
 * label idiom carries a literal of its own — `t(acc.authMode === 'OAuth2' ? … : …)` — and reading
 * that as a key is four false positives in this repo alone.
 */
function keyArms(expression: string): string[] {
  // `?.` and `??` are not conditionals; only a lone `?` opens one. Both characters of `??` have
  // to be skipped: stopping on the second one reads `t('a' ?? b)` as a ternary and drops the key.
  const question = scan(expression, 0, (char, depth, index) =>
    depth === 0 && char === '?'
      && !'.?'.includes(expression[index + 1] ?? '') && expression[index - 1] !== '?')
  if (question === -1) return [expression]
  const rest = expression.slice(question + 1)
  const colon = scan(rest, 0, (char, depth) => depth === 0 && char === ':')
  return colon === -1
    ? keyArms(rest)
    : [...keyArms(rest.slice(0, colon)), ...keyArms(rest.slice(colon + 1))]
}

interface Use { file: string; namespace: string; key: string; dynamic: boolean }

/** `\bt\(` also catches `i18next.t(` — the module-scope form, which always carries its prefix. */
const CALL = /\bt\(/g
const TRANS = /<Trans\b([^>]*)>/g

function usesIn(file: string, source: string): Use[] {
  // A two-namespace file binds its bare `t` to whichever `useTranslation(...)` call this regex
  // meets first in source order, not necessarily the one the bare `t` actually comes from — a
  // known limit of this deliberately simple parser, not a bug to work around.
  const bound = /useTranslation\(\s*'(\w+)'/.exec(source)?.[1] ?? DEFAULT_NS
  const found: Use[] = []

  for (const call of source.matchAll(CALL)) {
    const open = call.index + call[0].length - 1
    const [expression, options] = callArguments(source, open)
    const option = /ns:\s*'(\w+)'/.exec(options)?.[1]
    // Both arms of `t(cond ? 'a' : 'b')`, this codebase's two-state label idiom.
    for (const arm of keyArms(expression)) {
      for (const [, key] of arm.matchAll(/'([\w.:]+)'/g)) {
        const [namespace, path] = qualify(key, option ?? bound)
        found.push({ file, namespace, key: path, dynamic: false })
      }
      for (const [, template] of arm.matchAll(/`([^`]*)`/g)) {
        const [namespace, path] = qualify(template, option ?? bound)
        found.push({ file, namespace, key: path, dynamic: template.includes('${') })
      }
    }
  }

  for (const [, attributes] of source.matchAll(TRANS)) {
    const namespace = /\bns="(\w+)"/.exec(attributes)?.[1] ?? DEFAULT_NS
    const literal = /i18nKey="([\w.:]+)"/.exec(attributes)?.[1]
    const template = /i18nKey=\{`([^`]*)`\}/.exec(attributes)?.[1]
    // A <Trans> whose key this cannot read must be reported, never skipped: silence here is
    // exactly the hole `Desc` was.
    const raw = literal ?? template ?? '(unreadable i18nKey)'
    const [ns, path] = qualify(raw, namespace)
    found.push({ file, namespace: ns, key: path, dynamic: template !== undefined })
  }
  return found
}

export function unresolvedKeys(sources: Record<string, string>): string[] {
  return Object.entries(sources)
    .filter(([file]) => !file.includes('.test.'))
    .flatMap(([file, source]) => usesIn(file, source))
    .filter(use => {
      if (!use.dynamic) return !resolves(use.namespace, use.key)
      const prefix = use.key.slice(0, use.key.indexOf('${'))
      return !prefix.endsWith('.') || typeof at(use.namespace, prefix.slice(0, -1)) !== 'object'
    })
    .map(use => `${use.file}: ${use.namespace}:${use.key}`)
}

describe('translation keys', () => {
  // Without this, a moved Vite root would empty the glob and leave the check below passing
  // over nothing at all.
  it('reads the source tree', () => {
    expect(Object.keys(SOURCES).length).toBeGreaterThan(50)
  })

  it('resolves every key the source asks for', () => {
    expect(unresolvedKeys(SOURCES)).toEqual([])
  })

  it('catches a key that is not in the catalogue', () => {
    expect(unresolvedKeys({
      '/src/a.tsx': "useTranslation('settings')\nt('rules.nope')",
      '/src/b.tsx': "t('common:actions.nope')",
      '/src/c.tsx': '<Trans i18nKey="folders.nope" ns="settings" />',
      '/src/d.tsx': "useTranslation('settings')\nt(`rules.gone.${value}`)",
      // A ternary label: both arms are keys, and only the bad one is reported.
      '/src/e.jsx': "useTranslation('settings')\nt(on ? 'rules.enable' : 'rules.nope')",
      // A backtick with nothing interpolated is a plain key, not a prefix.
      '/src/f.jsx': "useTranslation('settings')\nt(`rules.nope`)",
      // A bare <Trans> resolves against defaultNS, so a settings key there is wrong.
      '/src/g.jsx': '<Trans i18nKey="rules.empty" />',
      // A <Trans> whose key cannot be read statically is reported rather than skipped.
      '/src/h.jsx': '<Trans i18nKey={someKey} ns="settings" />',
      // `??` is not a ternary: the key sits before it and must still be read.
      '/src/i.jsx': "useTranslation('settings')\nt('rules.nope' ?? fallback)",
    })).toEqual([
      '/src/a.tsx: settings:rules.nope',
      '/src/b.tsx: common:actions.nope',
      '/src/c.tsx: settings:folders.nope',
      '/src/d.tsx: settings:rules.gone.${value}',
      '/src/e.jsx: settings:rules.nope',
      '/src/f.jsx: settings:rules.nope',
      '/src/g.jsx: common:rules.empty',
      '/src/h.jsx: settings:(unreadable i18nKey)',
      '/src/i.jsx: settings:rules.nope',
    ])
  })

  it('accepts the forms the codebase actually uses', () => {
    expect(unresolvedKeys({
      '/src/ok.tsx': [
        "useTranslation('settings')",
        "t('rules.newRule')",                              // default namespace
        "t('actions.save', { ns: 'common' })",             // ns option
        "i18next.t('errors:messageNotFound')",             // module scope, prefixed
        "t(`rules.fields.${value}`)",                      // dynamic, real group
        "t(isEdit ? 'rules.editRule' : 'rules.newRule')",  // ternary label
        "t(chosen ?? 'rules.newRule')",                    // `??`, not a ternary
        "t('rules.count', { count })",                     // plural, only _one/_other exist
        "t(labelKey)",                                     // key held in a variable: unreadable
        '<Trans i18nKey="rules.empty" ns="settings" />',
        '<Trans i18nKey="deleteConfirm.message" components={{ name: <strong>{x}</strong> }} />',
      ].join('\n'),
    })).toEqual([])
  })
})
