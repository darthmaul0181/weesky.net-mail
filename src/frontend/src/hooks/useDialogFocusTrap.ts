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

    // Read at close, not at open: the fallback is whatever that ref holds once the dialog goes,
    // and "usable" has to be judged at that same moment — computing it up here, at mount, would
    // freeze it on a captured element that was of course still connected the instant it was
    // clicked. <body> is excluded even though it is always .isConnected: a sibling dialog (not
    // this one's own opener) unmounting in the same commit this one mounts in resets focus to
    // <body> before this effect ever runs, and body.focus() is a silent no-op — the exact
    // "drops to <body>" symptom this hook exists to prevent.
    const restoreFocus = () => {
      const usable = previouslyFocused?.isConnected && previouslyFocused !== document.body
      ;(usable ? previouslyFocused : fallbackFocusRef?.current)?.focus()
    }

    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      restoreFocus()
    }
  }, [active, containerRef, initialFocusRef, fallbackFocusRef])
}
