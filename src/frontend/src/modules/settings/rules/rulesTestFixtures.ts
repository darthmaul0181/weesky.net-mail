import type { SieveRule, SieveRuleSet } from './rulesTypes'

export function fileIntoRule(id: string, name: string): SieveRule {
  return {
    id,
    name,
    enabled: true,
    matchAll: false,
    stopAfter: false,
    conditions: [{ field: 'Subject', operator: 'Contains', value: 'x' }],
    actions: [{ type: 'FileInto', argument: 'X', autoCreate: false }],
  }
}

export function ruleSet(providerId: string, rules: SieveRule[]): SieveRuleSet {
  return { kind: 'Structured', providerId, rules, rawScript: '' }
}

/** A lookup the test depends on: a miss fails here, by name, rather than as a TypeError later. */
export function found<T>(value: T | null | undefined, what: string): T {
  if (value === null || value === undefined) throw new Error(`${what} not found`)
  return value
}

export function wizardNameInput(): HTMLInputElement {
  return found(document.querySelector<HTMLInputElement>('.rule-wizard-input'), 'name input')
}

export function checkboxIn(element: Element): HTMLInputElement {
  return found(element.querySelector<HTMLInputElement>('input[type="checkbox"]'), 'checkbox')
}
