import { describe, it, expect, afterEach } from 'vitest'
import { registerLeaveGuard, confirmLeave } from './leaveGuard'

afterEach(() => registerLeaveGuard(null))

describe('leaveGuard', () => {
  it('lets the caller through when nothing is registered', async () => {
    await expect(confirmLeave()).resolves.toBe(true)
  })

  it('answers with the registered guard', async () => {
    registerLeaveGuard(() => Promise.resolve(false))

    await expect(confirmLeave()).resolves.toBe(false)
  })

  // The composer unregisters on unmount; a stale guard would block every later switch.
  it('lets the caller through again once the guard is withdrawn', async () => {
    registerLeaveGuard(() => Promise.resolve(false))
    registerLeaveGuard(null)

    await expect(confirmLeave()).resolves.toBe(true)
  })
})
