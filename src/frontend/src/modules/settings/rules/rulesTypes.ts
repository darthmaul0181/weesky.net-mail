/** The Sieve rules' wire shapes (`/api/Rules`). Enums travel as their C# names. The API omits a
    null field, so every field the server may leave empty is `?:`, never `| null`. */

export type SieveConditionField =
  | 'From' | 'To' | 'Cc' | 'Recipient' | 'Subject' | 'Header' | 'Size' | 'Body'
  | 'EnvelopeFrom' | 'EnvelopeTo' | 'RecipientDetail' | 'Duplicate'
  | 'CurrentDate' | 'MessageDate' | 'CurrentWeekday' | 'CurrentHour'

export type SieveConditionOperator =
  | 'Contains' | 'NotContains' | 'Equals' | 'NotEquals' | 'Matches'
  | 'Larger' | 'Smaller' | 'Regex' | 'Before' | 'OnOrAfter'

export type SieveActionType = 'FileInto' | 'Redirect' | 'Discard' | 'Reject' | 'SetFlag' | 'Keep'

/** `Structured` is a script the editor wrote and can read back; `Advanced`, one it cannot. */
export type SieveScriptKind = 'Structured' | 'Advanced'

export interface SieveCondition {
  field: SieveConditionField
  /** The header a `Header` condition tests. */
  headerName?: string
  operator: SieveConditionOperator
  value: string
}

export interface SieveAction {
  type: SieveActionType
  argument?: string
  /** A `FileInto` that creates its folder when it is missing. */
  autoCreate: boolean
}

export interface SieveRule {
  id: string
  name: string
  enabled: boolean
  matchAll: boolean
  stopAfter: boolean
  conditions: SieveCondition[]
  actions: SieveAction[]
}

/** A rule on its way back: the editor writes null where it cleared a field, and a new action
    carries no `autoCreate` until the box is ticked — the server reads both as the default. */
export interface SieveRuleWrite extends Omit<SieveRule, 'conditions' | 'actions'> {
  conditions: (Omit<SieveCondition, 'headerName'> & { headerName?: string | null })[]
  actions: (Omit<SieveAction, 'argument' | 'autoCreate'> & { argument?: string | null; autoCreate?: boolean })[]
}

export interface SieveRuleSet {
  kind: SieveScriptKind
  rules: SieveRule[]
  rawScript: string
  providerId?: string
  scriptName?: string
}

export interface RuleProvider {
  id: string
  displayName: string
  defaultScriptName: string
  isDefault: boolean
}

export interface IncompatibleRule {
  id: string
  name: string
  reason: string
}

export interface CompatibilityCheckResult {
  compatible: boolean
  incompatible: IncompatibleRule[]
}

export interface SieveRawScript {
  content: string
  scriptName?: string
}
