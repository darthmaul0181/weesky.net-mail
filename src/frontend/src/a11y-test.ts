import { expect } from 'vitest'
import { axe } from 'jest-axe'

interface AxeOverrides {
  /** One more rule to disable for one surface only, per triage — never a second global disable. */
  extraRules?: Record<string, { enabled: boolean }>
  /** Axe's default: true. Pass false only for a surface with a real `<iframe>`, whose cross-frame
      postMessage scan jsdom implements neither side of and which never resolves otherwise. */
  iframes?: boolean
}

/** color-contrast needs paint, which jsdom has none of — a browser-recette item, not a unit test. */
export async function expectNoAxeViolations(
  container: HTMLElement, { extraRules = {}, iframes = true }: AxeOverrides = {},
): Promise<void> {
  const results = await axe(container, {
    iframes,
    rules: { 'color-contrast': { enabled: false }, ...extraRules },
  })
  expect(results.violations).toEqual([])
}
