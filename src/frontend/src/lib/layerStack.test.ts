import { describe, it, expect, vi, afterEach } from 'vitest'
import { pushLayer, hasOpenLayer, isTopLayer, type Layer, type LayerHandle } from './layerStack'
import { fireEscape } from '../test-utils'

const opened: LayerHandle[] = []

function push(layer: Layer) {
  const handle = pushLayer(layer)
  opened.push(handle)
  return handle
}

// Tab has no shared helper of fireEscape's own — its job is cycling a trap, not marking a key as
// spent — so it keeps a local, cancelable event built the same way.
function press(key: string, init: KeyboardEventInit = {}, target: EventTarget = document) {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init })
  target.dispatchEvent(event)
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

    fireEscape()
    expect(upper).toHaveBeenCalledTimes(1)
    expect(lower).not.toHaveBeenCalled()

    top.remove()
    fireEscape()
    expect(lower).toHaveBeenCalledTimes(1)
    expect(upper).toHaveBeenCalledTimes(1)
  })

  it('ignores an Escape a handler nearer the target already prevented', () => {
    const onEscape = vi.fn()
    // Where React delegates its own onKeyDown: nearer the target, so it runs first.
    const root = document.createElement('div')
    document.body.append(root)
    root.addEventListener('keydown', event => event.preventDefault())
    push({ onEscape })

    press('Escape', {}, root)

    expect(onEscape).not.toHaveBeenCalled()
  })

  it('ignores a Tab a handler nearer the target already prevented', () => {
    const root = document.createElement('div')
    document.body.append(root)
    root.addEventListener('keydown', event => event.preventDefault())
    push({ trap: boxOf('trap', 2) })
    ;(document.activeElement as HTMLElement | null)?.blur()

    press('Tab', {}, root)

    expect(document.activeElement).not.toBe(document.getElementById('trap-0'))
  })

  it('hears Escape before any listener the app registers later', () => {
    const onEscape = vi.fn()
    const after = vi.fn<(prevented: boolean) => void>()
    // A menu's own document listener, registered long after this module loaded.
    const later = (event: Event) => after(event.defaultPrevented)
    document.addEventListener('keydown', later)
    push({ onEscape })

    fireEscape()

    document.removeEventListener('keydown', later)
    expect(onEscape).toHaveBeenCalledTimes(1)
    expect(after).toHaveBeenCalledWith(true)
  })

  it('swallows Escape for a layer that declares no handler', () => {
    const seen = vi.fn<(prevented: boolean) => void>()
    const spy = (event: Event) => seen(event.defaultPrevented)
    window.addEventListener('keydown', spy)
    push({})

    fireEscape()

    window.removeEventListener('keydown', spy)
    expect(seen).toHaveBeenCalledWith(true)
  })

  // fireEscape's own contract: idle, it must not read as spent, or a later task's test against it
  // would pass for the same wrong reason the old non-cancelable event did.
  it('marks the Escape fireEscape sends as spent only once a layer is open', () => {
    expect(fireEscape().defaultPrevented).toBe(false)

    push({})

    expect(fireEscape().defaultPrevented).toBe(true)
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

  /* A roving grid demotes widgets that stay perfectly focusable, so the list Tab cycles cannot be
     the focusable one: its last item would sit past the last one Tab can reach, nothing would be
     prevented, and focus would walk out of the trap. */
  it('cycles Tab over the tabbable items alone', () => {
    const box = boxOf('trap', 3)
    ;(box.children[1] as HTMLElement).tabIndex = -1
    ;(box.children[2] as HTMLElement).tabIndex = -1
    push({ trap: box })
    ;(box.children[0] as HTMLElement).focus()

    expect(press('Tab').defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(box.children[0])
  })

  it('holds Tab inside a trap with nothing focusable in it', () => {
    push({ trap: boxOf('empty', 0) })

    expect(press('Tab').defaultPrevented).toBe(true)
  })

  it('lets Tab through when nothing on the stack carries a trap', () => {
    push({ onEscape: () => {} })

    expect(press('Tab').defaultPrevented).toBe(false)
  })

  // A menu or a popover joins the stack with no trap of its own; the dialog or the drawer it was
  // opened from is still the surface the user is inside, so Tab stays in it.
  it('keeps Tab in the trap below a trapless layer', () => {
    const box = boxOf('dialog', 2)
    push({ trap: box })
    push({ onEscape: () => {} })

    ;(box.children[1] as HTMLElement).focus()
    press('Tab')

    expect(document.activeElement).toBe(box.children[0])
  })

  // React commits a child's layout effect before its parent's, so a dialog nested in another's
  // markup pushes first — and would own Escape for the dialog it is drawn inside.
  it('puts a layer holding another one under it, whichever pushed first', () => {
    const outer = boxOf('outer', 1)
    const inner = boxOf('inner', 1)
    outer.append(inner)
    const onOuter = vi.fn()
    const onInner = vi.fn()
    push({ trap: inner, onEscape: onInner })
    push({ trap: outer, onEscape: onOuter })

    fireEscape()

    expect(onInner).toHaveBeenCalledTimes(1)
    expect(onOuter).not.toHaveBeenCalled()
  })

  it('leaves a trapless layer, and a layer holding nothing of the stack, on top', () => {
    const dialog = boxOf('dialog', 1)
    const elsewhere = boxOf('elsewhere', 1)
    const onDialog = vi.fn()
    const onMenu = vi.fn()
    const onSibling = vi.fn()
    push({ trap: dialog, onEscape: onDialog })
    push({ onEscape: onMenu })

    fireEscape()
    expect(onMenu).toHaveBeenCalledTimes(1)

    push({ trap: elsewhere, onEscape: onSibling })
    fireEscape()

    expect(onSibling).toHaveBeenCalledTimes(1)
    expect(onDialog).not.toHaveBeenCalled()
  })

  it('keeps a layer where it is when its handler is replaced', () => {
    const replaced = vi.fn()
    const upper = vi.fn()
    const lower = push({ onEscape: () => {} })
    push({ onEscape: upper })

    lower.update({ onEscape: replaced })
    fireEscape()

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

  it('adds no listener of its own on a push, and reacts to nothing once empty', () => {
    const add = vi.spyOn(document, 'addEventListener')
    const onEscape = vi.fn()

    const handle = push({ onEscape })
    expect(add.mock.calls.filter(([type]) => type === 'keydown')).toHaveLength(0)
    add.mockRestore()

    handle.remove()
    expect(fireEscape().defaultPrevented).toBe(false)
    expect(onEscape).not.toHaveBeenCalled()
  })

  it('ignores a second removal of the same handle', () => {
    const handle = push({})
    push({})

    handle.remove()
    handle.remove()

    expect(hasOpenLayer()).toBe(true)
  })
})
