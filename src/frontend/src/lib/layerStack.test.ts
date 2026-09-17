import { describe, it, expect, vi, afterEach } from 'vitest'
import { pushLayer, hasOpenLayer, isTopLayer, type Layer, type LayerHandle } from './layerStack'

const opened: LayerHandle[] = []

function push(layer: Layer) {
  const handle = pushLayer(layer)
  opened.push(handle)
  return handle
}

function press(key: string, init: KeyboardEventInit = {}) {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init })
  document.dispatchEvent(event)
  return event
}

function boxOf(id: string, buttons: number) {
  const box = document.createElement('div')
  box.id = id
  for (let i = 0; i < buttons; i += 1) {
    const button = document.createElement('button')
    button.id = `${id}-${i}`
    box.append(button)
  }
  document.body.append(box)
  return box
}

afterEach(() => {
  opened.splice(0).forEach(handle => handle.remove())
  document.body.innerHTML = ''
})

describe('layerStack', () => {
  it('gives Escape to the top layer, then to the one below once it goes', () => {
    const lower = vi.fn()
    const upper = vi.fn()
    push({ onEscape: lower })
    const top = push({ onEscape: upper })

    press('Escape')
    expect(upper).toHaveBeenCalledTimes(1)
    expect(lower).not.toHaveBeenCalled()

    top.remove()
    press('Escape')
    expect(lower).toHaveBeenCalledTimes(1)
    expect(upper).toHaveBeenCalledTimes(1)
  })

  it('ignores an Escape a handler before it already prevented', () => {
    const onEscape = vi.fn()
    const guard = (event: Event) => event.preventDefault()
    document.addEventListener('keydown', guard)
    push({ onEscape })

    press('Escape')

    document.removeEventListener('keydown', guard)
    expect(onEscape).not.toHaveBeenCalled()
  })

  it('swallows Escape for a layer that declares no handler', () => {
    const seen = vi.fn()
    const spy = (event: Event) => seen(event.defaultPrevented)
    window.addEventListener('keydown', spy)
    push({})

    press('Escape')

    window.removeEventListener('keydown', spy)
    expect(seen).toHaveBeenCalledWith(true)
  })

  it('cycles Tab inside the top trap and leaves the one below it alone', () => {
    const lower = boxOf('lower', 2)
    const upper = boxOf('upper', 2)
    push({ trap: lower })
    push({ trap: upper })

    ;(upper.children[1] as HTMLElement).focus()
    press('Tab')
    expect(document.activeElement).toBe(upper.children[0])

    press('Tab', { shiftKey: true })
    expect(document.activeElement).toBe(upper.children[1])
  })

  it('brings a focus that fell outside the top trap back to its first item', () => {
    const box = boxOf('trap', 2)
    push({ trap: box })
    ;(document.activeElement as HTMLElement | null)?.blur()

    press('Tab')

    expect(document.activeElement).toBe(box.children[0])
  })

  it('holds Tab inside a trap with nothing focusable in it', () => {
    push({ trap: boxOf('empty', 0) })

    expect(press('Tab').defaultPrevented).toBe(true)
  })

  it('lets Tab through when the top layer carries no trap', () => {
    push({ trap: boxOf('lower', 2) })
    push({ onEscape: () => {} })

    expect(press('Tab').defaultPrevented).toBe(false)
  })

  it('keeps a layer where it is when its handler is replaced', () => {
    const replaced = vi.fn()
    const upper = vi.fn()
    const lower = push({ onEscape: () => {} })
    push({ onEscape: upper })

    lower.update({ onEscape: replaced })
    press('Escape')

    expect(upper).toHaveBeenCalledTimes(1)
    expect(replaced).not.toHaveBeenCalled()
  })

  it('reports whether anything is open and which handle is on top', () => {
    expect(hasOpenLayer()).toBe(false)

    const lower = push({})
    const upper = push({})

    expect(hasOpenLayer()).toBe(true)
    expect(isTopLayer(upper)).toBe(true)
    expect(isTopLayer(lower)).toBe(false)

    upper.remove()
    expect(isTopLayer(lower)).toBe(true)
  })

  it('holds one document listener while the stack is not empty', () => {
    const add = vi.spyOn(document, 'addEventListener')
    const remove = vi.spyOn(document, 'removeEventListener')
    const keydown = (calls: [string, ...unknown[]][]) => calls.filter(([type]) => type === 'keydown')

    const lower = push({})
    const upper = push({})
    expect(keydown(add.mock.calls as [string, ...unknown[]][])).toHaveLength(1)

    upper.remove()
    expect(keydown(remove.mock.calls as [string, ...unknown[]][])).toHaveLength(0)

    lower.remove()
    expect(keydown(remove.mock.calls as [string, ...unknown[]][])).toHaveLength(1)
    add.mockRestore()
    remove.mockRestore()
  })

  it('ignores a second removal of the same handle', () => {
    const handle = push({})
    push({})

    handle.remove()
    handle.remove()

    expect(hasOpenLayer()).toBe(true)
  })
})
