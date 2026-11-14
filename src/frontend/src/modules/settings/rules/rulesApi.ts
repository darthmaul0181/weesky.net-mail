import { type RequestOptions, request } from '../../../lib/http'
import type {
  CompatibilityCheckResult, RuleProvider, SieveRawScript, SieveRuleSet, SieveRuleWrite,
} from './rulesTypes'

/** The Sieve rules. */
export const rulesApi = {
  getRuleProviders: (options?: RequestOptions) =>
    request<RuleProvider[]>('GET', '/api/Rules/Providers', undefined, options),

  getRules: (options?: RequestOptions) =>
    request<SieveRuleSet>('GET', '/api/Rules', undefined, options),

  saveRules: (
    rules: SieveRuleWrite[], providerId: string | null | undefined, scriptName: string | null | undefined,
    options?: RequestOptions,
  ) =>
    request<null>('PUT', '/api/Rules', { rules, providerId, scriptName }, options),

  deleteRules: (options?: RequestOptions) =>
    request<null>('DELETE', '/api/Rules', undefined, options),

  checkCompatibility: (providerId: string, rules: SieveRuleWrite[], options?: RequestOptions) =>
    request<CompatibilityCheckResult>('POST', '/api/Rules/CompatibilityCheck', { providerId, rules }, options),

  getRawScript: (options?: RequestOptions) =>
    request<SieveRawScript>('GET', '/api/Rules/Raw', undefined, options),
}
