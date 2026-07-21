import { describe, it, expect, vi, beforeEach } from 'vitest'
import {
  claimNotification, desktopPermission, playNewMailSound,
  requestDesktopPermission, showDesktopNotification,
} from './channels'

const play = vi.fn()
// A `function`, not an arrow: vitest 4 constructs the mock implementation via Reflect.construct
// when called with `new`, and arrow functions are never valid ES constructors.
vi.stubGlobal('Audio', vi.fn(function () { return { play, currentTime: 0 } }))

describe('playNewMailSound', () => {
  beforeEach(() => { play.mockReset(); play.mockResolvedValue(undefined) })

  it('plays the sound', () => {
    playNewMailSound()

    expect(play).toHaveBeenCalled()
  })

  // Browsers refuse audio from a page nobody has interacted with, which is exactly when a
  // notification plays. A refusal must not surface: a failed notification must not produce a
  // second interruption announcing the failure.
  it('swallows a blocked play', async () => {
    play.mockRejectedValue(new Error('NotAllowedError'))

    expect(() => playNewMailSound()).not.toThrow()
    await Promise.resolve()
  })
})

describe('desktopPermission', () => {
  it('reports unsupported where the API is absent', () => {
    vi.stubGlobal('Notification', undefined)

    expect(desktopPermission()).toBe('unsupported')
  })

  it('reports what the browser says', () => {
    vi.stubGlobal('Notification', { permission: 'granted' })

    expect(desktopPermission()).toBe('granted')
  })
})

describe('requestDesktopPermission', () => {
  it('asks the browser and returns its answer', async () => {
    const requestPermission = vi.fn().mockResolvedValue('granted')
    vi.stubGlobal('Notification', { permission: 'default', requestPermission })

    await expect(requestDesktopPermission()).resolves.toBe('granted')
    expect(requestPermission).toHaveBeenCalled()
  })

  it('reports unsupported instead of throwing where the API is absent', async () => {
    vi.stubGlobal('Notification', undefined)

    await expect(requestDesktopPermission()).resolves.toBe('unsupported')
  })
})

describe('showDesktopNotification', () => {
  it('raises a tagged notification and wires the click', () => {
    const instance: Record<string, unknown> = {}
    const ctor = vi.fn(function () { return instance })
    vi.stubGlobal('Notification', Object.assign(ctor, { permission: 'granted' }))
    const onClick = vi.fn()

    showDesktopNotification('Alice — Lunch?', 'weesky-mail-42', onClick)

    expect(ctor).toHaveBeenCalledWith('New mail', expect.objectContaining({
      body: 'Alice — Lunch?', tag: 'weesky-mail-42',
    }))
    ;(instance.onclick as () => void)()
    expect(onClick).toHaveBeenCalled()
  })

  it('does nothing without permission', () => {
    const ctor = vi.fn()
    vi.stubGlobal('Notification', Object.assign(ctor, { permission: 'denied' }))

    showDesktopNotification('body', 'tag', vi.fn())

    expect(ctor).not.toHaveBeenCalled()
  })
})

describe('claimNotification', () => {
  beforeEach(() => localStorage.clear())

  it('lets the first caller through', () => {
    expect(claimNotification(42)).toBe(true)
  })

  // Two tabs both poll and both decide to notify. The bubble dedupes itself through its tag;
  // the sound cannot, so the claim is what keeps a second tab quiet.
  it('turns away a second tab for the same arrival', () => {
    claimNotification(42)

    expect(claimNotification(42)).toBe(false)
  })

  it('lets a later arrival through', () => {
    claimNotification(42)

    expect(claimNotification(43)).toBe(true)
  })

  it('turns away an older arrival', () => {
    claimNotification(43)

    expect(claimNotification(42)).toBe(false)
  })
})
