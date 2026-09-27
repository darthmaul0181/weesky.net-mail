// A message's own <style> blocks, handled as text in and text out. Parsed through a constructable
// sheet: it is never adopted, so nothing here applies to the page or fetches anything.

import { parseBodyFragment } from './parseBodyFragment'

// Always true and always false, both valid inside any `and` chain of a media query.
const ALWAYS = '(min-width: 0)'
const NEVER = '(max-width: 0) and (min-width: 1px)'
// Also matches inside a selector or `content:` string: harmless, the replacement opens no string, block or tag.
const SCHEME_FEATURE = /\(\s*prefers-color-scheme\s*:\s*(dark|light)\s*\)/gi
const DARK_FEATURE = /\(\s*prefers-color-scheme\s*:\s*dark\s*\)/i

/** Whether the body's stylesheets carry a dark design: a `color-scheme` meta alone is often boilerplate,
 * and a newsletter can quote the query in its text. */
export function hasDarkDesign(html: string): boolean {
  if (!DARK_FEATURE.test(html)) return false
  return Array.from(parseBodyFragment(html).body.querySelectorAll('style'))
    .some(style => DARK_FEATURE.test(style.textContent ?? ''))
}

/** Settles each `prefers-color-scheme` condition for the scheme we render, rather than leaving it to
 * whatever the iframe inherits from the page. */
export function resolveColourScheme(html: string, scheme: 'light' | 'dark'): string {
  if (!/prefers-color-scheme/i.test(html)) return html
  return rewriteStyleElements(html, css =>
    css.replace(SCHEME_FEATURE, (_feature, wanted: string) => wanted.toLowerCase() === scheme ? ALWAYS : NEVER))
}

/** Runs `edit` on every style rule's declarations, media blocks included, and serialises the sheet. */
export function forEachStyleRule(sheetText: string, edit: (style: CSSStyleDeclaration) => void): string {
  let sheet: CSSStyleSheet
  try {
    sheet = new CSSStyleSheet()
    sheet.replaceSync(sheetText)
  } catch {
    // Safari before 16.4 cannot construct a sheet: this one is left as the sender wrote it.
    return sheetText
  }
  const walk = (rules: CSSRuleList) => {
    for (const rule of Array.from(rules)) {
      if (rule instanceof CSSStyleRule) edit(rule.style)
      // Nested CSS puts rules inside a style rule too, not only inside a grouping rule.
      if ('cssRules' in rule) walk((rule as CSSGroupingRule).cssRules)
    }
  }
  walk(sheet.cssRules)
  return Array.from(sheet.cssRules, rule => rule.cssText).join('\n')
}

/** Rewrites the text of each <style> in a fragment. */
export function rewriteStyleElements(html: string, rewrite: (css: string) => string): string {
  if (!html.includes('<style')) return html
  const document = parseBodyFragment(html)
  for (const style of document.body.querySelectorAll('style')) style.textContent = rewrite(style.textContent ?? '')
  return document.body.innerHTML
}
