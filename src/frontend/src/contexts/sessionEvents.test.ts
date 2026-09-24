import { describe, it, expect, vi } from 'vitest'
import { notifySessionEnd, onSessionEnd } from './sessionEvents'

describe('sessionEvents', () => {
  it('calls every listener once per session end, until it unsubscribes', () => {
    const first = vi.fn()
    const second = vi.fn()
    const offFirst = onSessionEnd(first)
    const offSecond = onSessionEnd(second)

    notifySessionEnd()
    offFirst()
    notifySessionEnd()
    offSecond()

    expect(first).toHaveBeenCalledTimes(1)
    expect(second).toHaveBeenCalledTimes(2)
  })

  it('registers a listener added twice only once', () => {
    const listener = vi.fn()
    const off = onSessionEnd(listener)
    onSessionEnd(listener)

    notifySessionEnd()
    off()

    expect(listener).toHaveBeenCalledTimes(1)
  })

  it('still calls the other listeners when one throws', () => {
    const error = new Error('boom')
    const throwing = vi.fn(() => { throw error })
    const after = vi.fn()
    const offThrowing = onSessionEnd(throwing)
    const offAfter = onSessionEnd(after)
    const onError = vi.spyOn(console, 'error').mockImplementation(() => {})

    notifySessionEnd()
    offThrowing()
    offAfter()

    expect(after).toHaveBeenCalledTimes(1)
    expect(onError).toHaveBeenCalledWith(error)
    onError.mockRestore()
  })
})
