import type { SieveRuleWrite } from './rulesTypes'

export type RuleCondition = SieveRuleWrite['conditions'][number]
export type RuleAction = SieveRuleWrite['actions'][number]
/** The editor's rule: a new one has no id until the page gives it one on save. */
export type RuleDraft = Omit<SieveRuleWrite, 'id'> & { id: string | null }

// A condition counts as "filled in" once it has the data the backend needs.
export function isConditionValid(c: RuleCondition | undefined): boolean {
  if (!c) return false
  if (c.field === 'Duplicate') return true            // seconds window is optional
  if (c.field === 'Header' && !(c.headerName ?? '').trim()) return false
  return (c.value ?? '').toString().trim() !== ''
}

// An action counts as "filled in" once any required argument is present.
export function isActionValid(a: RuleAction | undefined): boolean {
  if (!a) return false
  switch (a.type) {
    case 'FileInto':
    case 'Redirect':
    case 'Reject':
    case 'SetFlag':
      return (a.argument ?? '').trim() !== ''
    case 'Discard':
    case 'Keep':
      return true
    default:
      return false
  }
}

export function makeEmptyRule(): RuleDraft {
  return {
    id: null,
    name: '',
    enabled: true,
    matchAll: false,
    stopAfter: false,
    conditions: [{ field: 'Subject', operator: 'Contains', value: '', headerName: null }],
    actions: [{ type: 'FileInto', argument: '' }],
  }
}
