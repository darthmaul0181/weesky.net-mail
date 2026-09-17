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
  + 'input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'

export function focusablesIn(container: HTMLElement) {
  return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE))
}

interface Entry {
  readonly handle: LayerHandle
  layer: Layer
}

const stack: Entry[] = []

function topEntry(): Entry | undefined {
  return stack[stack.length - 1]
}

function takeEscape(event: KeyboardEvent, layer: Layer) {
  if (event.defaultPrevented) return
  // Prevented even with no handler: the point of a layer is that nothing underneath reacts.
  event.preventDefault()
  layer.onEscape?.()
}

function keepTab(event: KeyboardEvent, container: HTMLElement) {
  const items = focusablesIn(container)
  if (items.length === 0) { event.preventDefault(); return }
  const at = items.indexOf(document.activeElement as HTMLElement)
  const last = items.length - 1
  // -1: focus is on the trigger, on <body>, or on a button disabled under it; Tab would walk out.
  const target = at === -1 ? 0 : event.shiftKey && at === 0 ? last : !event.shiftKey && at === last ? 0 : null
  if (target !== null) { event.preventDefault(); items[target].focus() }
}

function onKeyDown(event: KeyboardEvent) {
  const layer = topEntry()?.layer
  if (!layer) return
  if (event.key === 'Escape') takeEscape(event, layer)
  else if (event.key === 'Tab' && layer.trap) keepTab(event, layer.trap)
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
        if (at === -1) return
        stack.splice(at, 1)
        if (stack.length === 0) document.removeEventListener('keydown', onKeyDown)
      },
    },
  }
  if (stack.length === 0) document.addEventListener('keydown', onKeyDown)
  stack.push(entry)
  return entry.handle
}

/** Whether anything at all owns Escape — what a page-level handler asks before acting on one. */
export function hasOpenLayer() {
  return stack.length > 0
}

export function isTopLayer(handle: LayerHandle) {
  return topEntry()?.handle === handle
}
