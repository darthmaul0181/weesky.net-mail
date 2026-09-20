import { axe } from 'jest-axe'

/** color-contrast needs paint, which jsdom has none of — a browser-recette item, not a unit test.
    iframes:false: axe's cross-frame postMessage scan never resolves in jsdom, which implements
    neither side of that channel. extraRules disables one further rule for one surface, per triage. */
export async function expectNoAxeViolations(
  container: HTMLElement, extraRules: Record<string, { enabled: boolean }> = {},
): Promise<void> {
  const results = await axe(container, {
    iframes: false,
    rules: { 'color-contrast': { enabled: false }, ...extraRules },
  })
  expect(results.violations).toEqual([])
}
