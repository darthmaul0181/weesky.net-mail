import { useState, useLayoutEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import i18next from 'i18next'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../../api.js'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import type { AddToast } from '../../../hooks/useToasts'
import type {
  CompatibilityCheckResult, IncompatibleRule, SieveRuleSet, SieveRuleWrite,
} from './rulesTypes'

/** The loaded set as the page keeps it: its rules edited in place, and a provider switch clearing
    the script name to null so the backend writes the new provider's default script. */
type LoadedRuleSet = Omit<SieveRuleSet, 'rules' | 'scriptName'> & {
  rules: SieveRuleWrite[]
  scriptName?: string | null
}
export interface PendingConversion { accountId: string; incompatible: IncompatibleRule[] }

const rulesKey = (accountId: string) => ['rules', accountId] as const

function extractError(err: unknown): string {
  if (!err) return ''
  const mapped = apiErrorMessage(err, '')
  if (mapped) return mapped
  const msg = (typeof err === 'object' && 'message' in err && typeof err.message === 'string' && err.message)
    || (typeof err === 'string' ? err : '')
  try {
    const parsed: unknown = JSON.parse(msg)
    const message = typeof parsed === 'object' && parsed !== null && 'message' in parsed ? parsed.message : undefined
    return (typeof message === 'string' && message) || msg
  } catch {
    return msg
  }
}

/** Writes in flight, counted per account: an account stays busy until its own last write returns. */
function useWritesInFlight(active: string) {
  const [counts, setCounts] = useState<ReadonlyMap<string, number>>(() => new Map())
  const shift = (account: string, by: number) => setCounts(prev => {
    const next = new Map(prev)
    const count = (prev.get(account) ?? 0) + by
    if (count > 0) next.set(account, count)
    else next.delete(account)
    return next
  })
  return {
    busy: counts.has(active),
    begin: (account: string) => shift(account, 1),
    end: (account: string) => shift(account, -1),
  }
}

/** The active account's rule set and every write to it. */
export function useRulesData(accountId: string, addToast: AddToast) {
  const { t } = useTranslation('settings')
  const queryClient = useQueryClient()
  // Keyed by account, so another account's set never shows, not even for the render after a switch.
  // Read once per visit: gcTime 0 drops it on leaving, 'static' stops every automatic refetch.
  const rulesQuery = useQuery({
    queryKey: rulesKey(accountId),
    queryFn: async ({ signal }): Promise<LoadedRuleSet> => {
      try {
        return await api.getRules({ accountId, signal })
      } catch (err) {
        // Aborted: the account was left, and its failure is no longer this screen's news.
        if (!signal.aborted) addToast(extractError(err) || i18next.t('settings:rules.loadFailed'), 'error')
        throw err
      }
    },
    gcTime: 0,
    staleTime: 'static',
    retry: false,
  })
  const ruleSet = rulesQuery.data
  const rules = ruleSet?.rules ?? []
  const loading = rulesQuery.isPending
  // A write's outcome belongs to the account it was made for: its toast and its busy state
  // show only while that account is the active one, never over the account that replaced it.
  const activeAccount = useRef(accountId)
  useLayoutEffect(() => { activeAccount.current = accountId })
  function toastFor(account: string, ...toast: Parameters<AddToast>) {
    if (account === activeAccount.current) addToast(...toast)
  }
  const savingWrites = useWritesInFlight(accountId)
  const deletingWrites = useWritesInFlight(accountId)
  const switchingWrites = useWritesInFlight(accountId)
  const saving = savingWrites.busy
  const deleting = deletingWrites.busy
  const switching = switchingWrites.busy

  // Slider ON = extended (Weesky provider); OFF = Rainloop (Snappymail interop).
  const extended = ruleSet?.providerId === 'weesky'

  // A write refuses until the active account's own set has arrived: while it loads, and after a
  // failed load, there is nothing on screen that is that account's to write.
  function belongsToActiveAccount() {
    if (ruleSet) return true
    addToast(t('rules.wrongAccount'), 'error')
    return false
  }

  // Onto the account the write was made for: a write that returns after a switch patches that
  // account's set, which is no longer on screen, and never the one that replaced it.
  function patchLoaded(patch: Partial<LoadedRuleSet>) {
    queryClient.setQueryData<LoadedRuleSet>(rulesKey(accountId), prev => prev && { ...prev, ...patch })
  }

  async function persistRules(updatedRules: SieveRuleWrite[]) {
    if (!belongsToActiveAccount()) return
    savingWrites.begin(accountId)
    try {
      await api.saveRules(updatedRules, ruleSet?.providerId, ruleSet?.scriptName, { accountId })
      patchLoaded({ rules: updatedRules })
      toastFor(accountId, t('rules.saved'))
    } catch (err) {
      toastFor(accountId, extractError(err) || t('rules.saveFailed'), 'error')
    } finally {
      savingWrites.end(accountId)
    }
  }

  async function deleteAll(onDeleted: () => void) {
    if (!belongsToActiveAccount()) return
    deletingWrites.begin(accountId)
    try {
      await api.deleteRules({ accountId })
      patchLoaded({ kind: 'Structured', rules: [] })
      onDeleted()
      toastFor(accountId, t('rules.scriptDeleted'))
    } catch (err) {
      toastFor(accountId, extractError(err) || t('rules.deleteScriptFailed'), 'error')
    } finally {
      deletingWrites.end(accountId)
    }
  }

  // Switch provider by recompiling the current rules with the target provider. We pass a null
  // script name so the backend writes to the target provider's default script (and cleans up
  // the old one). Then we reflect the new providerId locally so the slider/editor track it.
  async function switchToProvider(targetProviderId: string, rulesToSave: SieveRuleWrite[]) {
    if (!belongsToActiveAccount()) return
    switchingWrites.begin(accountId)
    try {
      await api.saveRules(rulesToSave, targetProviderId, null, { accountId })
      patchLoaded({ rules: rulesToSave, providerId: targetProviderId, scriptName: null })
      toastFor(accountId, t(targetProviderId === 'weesky' ? 'rules.extendedEnabled' : 'rules.switchedRainloop'))
    } catch (err) {
      toastFor(accountId, extractError(err) || t('rules.switchFailed'), 'error')
    } finally {
      switchingWrites.end(accountId)
    }
  }

  async function toggleExtended(nextExtended: boolean, onIncompatible: (conversion: PendingConversion) => void) {
    if (nextExtended === extended || switching || saving) return
    if (nextExtended) {
      // rainloop → weesky: Weesky is a superset, lossless. No confirmation needed.
      await switchToProvider('weesky', rules)
      return
    }
    // weesky → rainloop: preview which rules the Rainloop format can't keep.
    switchingWrites.begin(accountId)
    let res: CompatibilityCheckResult | undefined
    try {
      res = await api.checkCompatibility('rainloop', rules, { accountId })
    } catch (err) {
      toastFor(accountId, extractError(err) || t('rules.compatFailed'), 'error')
      return
    } finally {
      switchingWrites.end(accountId)
    }
    if (res?.compatible) await switchToProvider('rainloop', rules)
    else onIncompatible({ accountId, incompatible: res?.incompatible ?? [] })
  }

  return {
    ruleSet, rules, loading, saving, deleting, switching, extended,
    persistRules, deleteAll, switchToProvider, toggleExtended,
  }
}
