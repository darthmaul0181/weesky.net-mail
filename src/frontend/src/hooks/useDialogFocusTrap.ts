import { useEffect, type RefObject } from 'react'

const FOCUSABLE = 'a[href], button:not([disabled]), textarea:not([disabled]), '
  + 'input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'

interface Options {
  active?: boolean
  initialFocusRef?: RefObject<HTMLElement | null>
  /** Takes the focus back on close when the element that had it before opening is gone. */
  fallbackFocusRef?: RefObject<HTMLElement | null>
}

/** Moves focus into an `aria-modal` container, keeps Tab inside it, and gives focus back on close. */
export function useDialogFocusTrap(
  containerRef: RefObject<HTMLElement | null>,
  { active = true, initialFocusRef, fallbackFocusRef }: Options = {},
) {
  useEffect(() => {
    const container = containerRef.current
    if (!active || !container) return undefined

    const previouslyFocused = document.activeElement as HTMLElement | null
    const focusable = () => Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE))

    ;(initialFocusRef?.current ?? focusable()[0] ?? container).focus()

    function onKeyDown(event: KeyboardEvent) {
      if (event.key !== 'Tab') return
      const items = focusable()
      if (items.length === 0) { event.preventDefault(); return }
      const at = items.indexOf(document.activeElement as HTMLElement)
      const last = items.length - 1
      // -1: focus is on the trigger, on <body>, or on a button disabled under it; Tab would walk out.
      const target = at === -1 ? 0 : event.shiftKey && at === 0 ? last : !event.shiftKey && at === last ? 0 : null
      if (target !== null) { event.preventDefault(); items[target].focus() }
    }

    // Read at close, not at open: the fallback is whatever that ref holds once the dialog goes.
    const restoreFocus = () => (previouslyFocused?.isConnected ? previouslyFocused : fallbackFocusRef?.current)?.focus()

    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      restoreFocus()
    }
  }, [active, containerRef, initialFocusRef, fallbackFocusRef])
}
