import { useCallback, useRef, type RefObject } from 'react'

// React detaches a ref before removing its node, so focus can still be seen inside the outgoing
// node here and handed on to the target before the node is gone for good.
export function useFocusReturnOnUnmount(targetRef: RefObject<HTMLElement | null>) {
  const node = useRef<HTMLElement | null>(null)
  return useCallback((next: HTMLElement | null) => {
    if (!next && node.current?.contains(document.activeElement)) targetRef.current?.focus()
    node.current = next
  }, [targetRef])
}
