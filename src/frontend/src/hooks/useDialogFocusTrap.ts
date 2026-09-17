import type { RefObject } from 'react'
import { useLayer } from './useLayer'

interface Options {
  active?: boolean
  initialFocusRef?: RefObject<HTMLElement | null>
  /** Takes the focus back on close when the element that had it before opening is gone. */
  fallbackFocusRef?: RefObject<HTMLElement | null>
}

/** Moves focus into an `aria-modal` container, keeps Tab inside it, and gives focus back on close.
    A layer of the stack like any other, minus Escape: the surface handles that itself. */
export function useDialogFocusTrap(
  containerRef: RefObject<HTMLElement | null>,
  { active = true, initialFocusRef, fallbackFocusRef }: Options = {},
) {
  useLayer({ active, ref: containerRef, initialFocusRef, returnFocusRef: fallbackFocusRef })
}
