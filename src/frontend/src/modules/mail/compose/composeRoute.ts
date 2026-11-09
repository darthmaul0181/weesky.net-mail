import type { ComposeSeed } from './composeSeed'

/** `handedOver` marks a seed the quick compose window expanded into the page, and whether it was unsaved. */
export type ComposeState = { from?: string; seed?: ComposeSeed; backTo?: string; handedOver?: { dirty: boolean } } | null

/* `from` is a folder and only the mail module has one. `backTo` is a whole path, for a caller
   outside the module — the contact card's Write — so leaving returns to the card that opened it. */
export function backTargetOf(state: ComposeState): string {
  return state?.backTo ?? (state?.from ? `/mail?folder=${encodeURIComponent(state.from)}` : '/mail')
}
