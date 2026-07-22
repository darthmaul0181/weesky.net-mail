import { act } from '@testing-library/react'

/**
 * A macrotask boundary, which drains every pending microtask. TanStack v5 notifies its observers
 * on one, and effects fire at the end of an await chain: a silence assertion made before that
 * drains holds against any implementation whatsoever, including one that fires on every render.
 */
export async function settle() {
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 0)) })
}
