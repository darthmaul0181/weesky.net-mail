import { describe, it, expect, afterEach, vi } from 'vitest'
import { registerLeaveGuard, confirmLeave } from './leaveGuard'

const withdrawals: Array<() => void> = []
const register = (fn: () => Promise<boolean>) => {
  const off = registerLeaveGuard(fn)
  withdrawals.push(off)
  return off
}
afterEach(() => { withdrawals.splice(0).forEach(off => off()) })

describe('leaveGuard', () => {
  it('lets the caller through when nothing is registered', async () => {
    await expect(confirmLeave()).resolves.toBe(true)
  })

  it('answers with the registered guard', async () => {
    register(() => Promise.resolve(false))

    await expect(confirmLeave()).resolves.toBe(false)
  })

  // The composer unregisters on unmount; a stale guard would block every later switch.
  it('lets the caller through again once the guard is withdrawn', async () => {
    const off = register(() => Promise.resolve(false))
    off()

    await expect(confirmLeave()).resolves.toBe(true)
  })

  it('asks every registered guard', async () => {
    const first = vi.fn(() => Promise.resolve(true))
    const second = vi.fn(() => Promise.resolve(true))
    register(first)
    register(second)

    await expect(confirmLeave()).resolves.toBe(true)
    expect(first).toHaveBeenCalledOnce()
    expect(second).toHaveBeenCalledOnce()
  })

  it('stops at the first refusal', async () => {
    const second = vi.fn(() => Promise.resolve(true))
    register(() => Promise.resolve(false))
    register(second)

    await expect(confirmLeave()).resolves.toBe(false)
    expect(second).not.toHaveBeenCalled()
  })

  it('unregister removes only its own guard', async () => {
    const off = register(() => Promise.resolve(true))
    register(() => Promise.resolve(false))
    off()

    await expect(confirmLeave()).resolves.toBe(false)
  })

  it('refuses when the second guard refuses after the first allowed', async () => {
    const first = vi.fn(() => Promise.resolve(true))
    const second = vi.fn(() => Promise.resolve(false))
    register(first)
    register(second)

    await expect(confirmLeave()).resolves.toBe(false)
    expect(first).toHaveBeenCalledOnce()
    expect(second).toHaveBeenCalledOnce()
  })

  it('skips a guard withdrawn during the round', async () => {
    const second = vi.fn(() => Promise.resolve(false))
    let offSecond = () => {}
    register(() => { offSecond(); return Promise.resolve(true) })
    offSecond = register(second)

    await expect(confirmLeave()).resolves.toBe(true)
    expect(second).not.toHaveBeenCalled()
  })
})
