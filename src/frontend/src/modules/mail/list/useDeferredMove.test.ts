import { describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import type { AddToast, ToastAction, ToastOptions } from '../../../hooks/useToasts'
import { HoldCancelled } from '../hold'
import { UNDO_MS, useDeferredMove } from './useDeferredMove'

function state(promise: Promise<void>) {
  const seen = { value: 'pending' as 'pending' | 'released' | 'cancelled' }
  promise.then(() => { seen.value = 'released' },
    error => { seen.value = error instanceof HoldCancelled ? 'cancelled' : 'pending' })
  return seen
}

function setup(flushKey = 'INBOX') {
  const calls: { action?: ToastAction; options?: ToastOptions }[] = []
  let next = 0
  const notify = vi.fn<AddToast>((_m, _t, action, options) => { calls.push({ action, options }); return ++next })
  const dismiss = vi.fn()
  const hook = renderHook(({ key }) => useDeferredMove({ notify, dismiss, flushKey: key }),
    { initialProps: { key: flushKey } })
  return { hook, notify, dismiss, calls }
}

const flushPromises = () => act(async () => { await Promise.resolve() })

describe('useDeferredMove', () => {
  it('raises an Undo toast of five seconds with a countdown', () => {
    const { hook, notify } = setup()
    act(() => { void hook.result.current.start('Moved to Trash', 'Undo') })
    expect(notify).toHaveBeenCalledWith('Moved to Trash', 'success',
      expect.objectContaining({ label: 'Undo' }),
      expect.objectContaining({ durationMs: UNDO_MS, countdown: true }))
  })

  it('releases when the toast expires, cancels on Undo', async () => {
    const { hook, calls } = setup()
    let first!: Promise<void>, second!: Promise<void>
    act(() => { first = hook.result.current.start('a', 'Undo') })
    const a = state(first)
    act(() => { calls[0]!.options!.onExpire!() })
    await flushPromises()
    expect(a.value).toBe('released')

    act(() => { second = hook.result.current.start('b', 'Undo') })
    const b = state(second)
    act(() => { calls[1]!.action!.onClick() })
    await flushPromises()
    expect(b.value).toBe('cancelled')
  })

  it('a second start sends the first and removes its toast', async () => {
    const { hook, dismiss } = setup()
    let first!: Promise<void>
    act(() => { first = hook.result.current.start('a', 'Undo') })
    const a = state(first)
    act(() => { void hook.result.current.start('b', 'Undo') })
    await flushPromises()
    expect(a.value).toBe('released')
    expect(dismiss).toHaveBeenCalledWith(1)
  })

  it('a new flush key sends the pending move', async () => {
    const { hook, dismiss } = setup('INBOX')
    let first!: Promise<void>
    act(() => { first = hook.result.current.start('a', 'Undo') })
    const a = state(first)
    hook.rerender({ key: 'Work' })
    await flushPromises()
    expect(a.value).toBe('released')
    expect(dismiss).toHaveBeenCalledWith(1)
  })

  it('sends on unmount and when the tab is hidden', async () => {
    const { hook } = setup()
    let first!: Promise<void>, second!: Promise<void>
    act(() => { first = hook.result.current.start('a', 'Undo') })
    const a = state(first)
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' })
    act(() => { document.dispatchEvent(new Event('visibilitychange')) })
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' })
    await flushPromises()
    expect(a.value).toBe('released')

    act(() => { second = hook.result.current.start('b', 'Undo') })
    const b = state(second)
    hook.unmount()
    await flushPromises()
    expect(b.value).toBe('released')
  })

  it('keeps the move pending across a caller that passes new handlers each render', async () => {
    const notify = vi.fn<AddToast>(() => 1)
    const hook = renderHook(({ dismiss }) => useDeferredMove({ notify, dismiss, flushKey: 'INBOX' }),
      { initialProps: { dismiss: () => {} } })
    let first!: Promise<void>
    act(() => { first = hook.result.current.start('a', 'Undo') })
    const a = state(first)
    hook.rerender({ dismiss: () => {} })
    await flushPromises()
    expect(a.value).toBe('pending')
  })

  it('sends at once when nothing can show an Undo', async () => {
    const hook = renderHook(() => useDeferredMove({ flushKey: 'INBOX' }))
    let first!: Promise<void>
    act(() => { first = hook.result.current.start('a', 'Undo') })
    const a = state(first)
    await flushPromises()
    expect(a.value).toBe('released')
  })
})
