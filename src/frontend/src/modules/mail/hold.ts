import type { QueryClient } from '@tanstack/react-query'
import { mailKeys } from './mailKeys'

/** The rejection an Undo produces: a move that was never sent, rolled back in silence. */
export class HoldCancelled extends Error {
  constructor() { super('Undone'); this.name = 'HoldCancelled' }
}

export interface Hold { promise: Promise<void>; release: () => void; cancel: () => void }

export function createHold(): Hold {
  let release: () => void = () => {}
  let cancel: () => void = () => {}
  const promise = new Promise<void>((resolve, reject) => {
    release = () => resolve()
    cancel = () => reject(new HoldCancelled())
  })
  // An Undo before the mutation awaits it must not surface as an unhandled rejection.
  promise.catch(() => {})
  return { promise, release, cancel }
}

// TanStack runs onSettled while the mutation still reads 'pending': the move marks its hold here
// first, so the tree refresh it then asks for is really fetched.
const settledHolds = new WeakSet<Promise<void>>()

export function markHoldSettled(hold: Promise<void>) {
  settledHolds.add(hold)
}

/** A held move has already patched the counts; a poll answered now would put them back. */
export function holdsPendingMove(client: QueryClient, accountId: string): boolean {
  return client.getMutationCache()
    .findAll({ mutationKey: mailKeys.writes(accountId), status: 'pending' })
    .some(mutation => {
      const hold = (mutation.state.variables as { hold?: Promise<void> } | undefined)?.hold
      return hold !== undefined && !settledHolds.has(hold)
    })
}
