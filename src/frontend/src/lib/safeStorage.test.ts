import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { readStored, removeStored, storedKeys, writeStored } from './safeStorage'

describe.each([['local', () => localStorage], ['session', () => sessionStorage]] as const)(
  '%s storage, working', (area, storage) => {
    beforeEach(() => storage().clear())

    it('reads back what was written', () => {
      writeStored('k', 'v', area)
      expect(readStored('k', area)).toBe('v')
    })

    it('reads null for an absent key', () => {
      expect(readStored('missing', area)).toBeNull()
    })

    it('removes a key', () => {
      writeStored('k', 'v', area)
      removeStored('k', area)
      expect(readStored('k', area)).toBeNull()
    })

    it('lists the stored keys', () => {
      writeStored('a', '1', area)
      writeStored('b', '2', area)
      expect(storedKeys(area)).toEqual(expect.arrayContaining(['a', 'b']))
    })
  })

describe.each([['local'], ['session']] as const)('%s storage, blocked', area => {
  afterEach(() => vi.restoreAllMocks())

  it('readStored returns null when getItem throws', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new Error('blocked') })
    expect(readStored('k', area)).toBeNull()
  })

  it('writeStored is a no-op when setItem throws', () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('blocked') })
    expect(() => writeStored('k', 'v', area)).not.toThrow()
  })

  it('removeStored is a no-op when removeItem throws', () => {
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => { throw new Error('blocked') })
    expect(() => removeStored('k', area)).not.toThrow()
  })

  it('storedKeys returns an empty array when the accessor throws', () => {
    vi.spyOn(window, area === 'session' ? 'sessionStorage' : 'localStorage', 'get')
      .mockImplementation(() => { throw new Error('blocked') })
    expect(storedKeys(area)).toEqual([])
  })
})
