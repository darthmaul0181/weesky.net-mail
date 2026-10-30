import { useEffect, useRef, useState, type RefObject } from 'react'

/** True from the first time the element comes within `rootMargin` of its `rootSelector` ancestor (else the
    viewport, which every scrolling ancestor clips, margin included); always true without IntersectionObserver. */
export function useInView<T extends Element>(rootMargin: string, rootSelector?: string): [RefObject<T>, boolean] {
  const ref = useRef<T>(null)
  const [inView, setInView] = useState(typeof IntersectionObserver === 'undefined')

  useEffect(() => {
    const node = ref.current
    if (inView || !node) return
    const observer = new IntersectionObserver(entries => {
      if (entries.some(entry => entry.isIntersecting)) { setInView(true); observer.disconnect() }
    }, { root: rootSelector ? node.closest(rootSelector) : null, rootMargin })
    observer.observe(node)
    return () => observer.disconnect()
  }, [inView, rootMargin, rootSelector])

  return [ref, inView]
}
