export interface Layer {
  /** Called for an Escape the stack gives this layer; absent = Escape is consumed and does nothing. */
  onEscape?: () => void
  /** Element whose focusables Tab cycles through while this layer is on top; absent = no trap. */
  trap?: HTMLElement | null
}

export interface LayerHandle {
  /** Replaces what this layer answers with, without moving it in the stack. */
  update(layer: Layer): void
  remove(): void
}

const FOCUSABLE = 'a[href], button:not([disabled]), textarea:not([disabled]), '
  + 'input:not([disabled]), select:not([disabled]), [tabindex]'

/** Everywhere focus can be *put* inside a container, a widget holding `tabindex="-1"` included:
    what a grid's arrows walk, and never the page's tab order. */
export function focusablesIn(container: HTMLElement) {
  return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE))
}

/** Where Tab actually goes: the same list without a negative `tabindex`, natives included — the
    question a trap and a menu walk ask. A roving grid demotes widgets that are still focusable. */
export function tabbablesIn(container: HTMLElement) {
  return focusablesIn(container).filter(element => element.tabIndex >= 0)
}

interface Entry {
  readonly handle: LayerHandle
  layer: Layer
}

const stack: Entry[] = []

function topEntry(): Entry | undefined {
  return stack[stack.length - 1]
}

function keepTab(event: KeyboardEvent, container: HTMLElement) {
  const items = tabbablesIn(container)
  if (items.length === 0) { event.preventDefault(); return }
  const at = items.indexOf(document.activeElement as HTMLElement)
  const last = items.length - 1
  // -1: focus is on the trigger, on <body>, or on a button disabled under it; Tab would walk out.
  const target = at === -1 ? 0 : event.shiftKey && at === 0 ? last : !event.shiftKey && at === last ? 0 : null
  if (target !== null) { event.preventDefault(); items[target].focus() }
}

function onKeyDown(event: KeyboardEvent) {
  if (event.defaultPrevented) return
  const layer = topEntry()?.layer
  if (!layer) return
  if (event.key === 'Escape') {
    // Prevented even with no handler: the point of a layer is that nothing underneath reacts.
    event.preventDefault()
    layer.onEscape?.()
  } else if (event.key === 'Tab') {
    // Tab belongs to the innermost trap still standing, not to the top layer alone: a menu or a
    // popover holds none of its own and does not suspend the modality of what it was opened from.
    for (let at = stack.length - 1; at >= 0; at -= 1) {
      const trap = stack[at].layer.trap
      if (trap) { keepTab(event, trap); return }
    }
  }
}

// Registered once, at load, and never moved: re-adding it on each push would put it behind every
// listener the app installed in between — an open menu would then eat a dialog's Escape. It
// returns at once while the stack is empty. Bubble phase, so a React handler still goes first.
if (typeof document !== 'undefined') document.addEventListener('keydown', onKeyDown)

// A dev hot reload re-evaluates this module without unloading the old one, which would bind a
// second `document` listener and fire Escape/Tab twice. `dispose` runs just before the replacement
// module's top level does, so the old listener is gone before the new one is added.
if (import.meta.hot) {
  import.meta.hot.dispose(() => {
    if (typeof document !== 'undefined') document.removeEventListener('keydown', onKeyDown)
  })
}

/**
 * Opens a layer on top of the stack: Escape and Tab go to it alone until it is removed or
 * another one is pushed over it.
 */
export function pushLayer(layer: Layer): LayerHandle {
  const entry: Entry = {
    layer,
    handle: {
      update(next: Layer) { entry.layer = next },
      remove() {
        const at = stack.indexOf(entry)
        if (at !== -1) stack.splice(at, 1)
      },
    },
  }
  // React commits a child's layout effect before its parent's, so a surface drawn inside another
  // pushes first. A layer whose container holds one already on the stack therefore belongs under
  // it, not over it. A layer with no container holds nothing and always goes on top.
  const trap = layer.trap
  const under = trap ? stack.findIndex(e => !!e.layer.trap && trap.contains(e.layer.trap)) : -1
  stack.splice(under === -1 ? stack.length : under, 0, entry)
  return entry.handle
}

/** Whether anything at all owns Escape — what a page-level handler asks before acting on one. */
export function hasOpenLayer() {
  return stack.length > 0
}

export function isTopLayer(handle: LayerHandle) {
  return topEntry()?.handle === handle
}

/** Whether a trapped layer stands over this one — what a trapless surface asks before acting on a
    pointer: a menu or a popover above it suspends nothing, a dialog does. */
export function coveredByTrap(handle: LayerHandle) {
  const at = stack.findIndex(entry => entry.handle === handle)
  if (at === -1) return false
  for (let above = at + 1; above < stack.length; above += 1) {
    if (stack[above].layer.trap) return true
  }
  return false
}
