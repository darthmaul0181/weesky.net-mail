import { describe, it, expect } from 'vitest'
import { folderMatches } from './folderFilter'

describe('folderMatches', () => {
  // The whole point of the normalisation: nobody types the accent to find the folder.
  it('matches across accents', () => {
    expect(folderMatches('Courrier indésirable', 'indesirable')).toBe(true)
  })

  it('matches an accented query against a plain name', () => {
    expect(folderMatches('Developpement', 'développement')).toBe(true)
  })

  it('is case-blind', () => {
    expect(folderMatches('Belfius', 'BEL')).toBe(true)
  })

  it('matches anywhere in the name, not only at the start', () => {
    expect(folderMatches('Courrier indésirable', 'rier')).toBe(true)
  })

  it('matches every name on an empty query', () => {
    expect(folderMatches('Anything', '')).toBe(true)
  })

  it('rejects what is genuinely absent', () => {
    expect(folderMatches('Archive', 'zzz')).toBe(false)
  })
})
