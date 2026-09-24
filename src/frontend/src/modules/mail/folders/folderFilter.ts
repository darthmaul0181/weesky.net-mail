import { fold } from '../../../lib/fold'

/** Substring match on the folder's own name — blind to case and accents both ways. */
export function folderMatches(name: string, query: string): boolean {
  return fold(name).includes(fold(query))
}
