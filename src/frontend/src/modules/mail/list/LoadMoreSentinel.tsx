import { useEffect, useLayoutEffect, useRef } from 'react'

interface Props {
  onReach: () => void
}

/**
 * An empty marker that reports when it scrolls into view. It is placed among the rows rather
 * than after the last one, so the next block starts before the reader reaches the end.
 */
export default function LoadMoreSentinel({ onReach }: Props) {
  const ref = useRef<HTMLDivElement>(null)
  const latest = useRef(onReach)

  // Refs can't be written during render (react-hooks/refs); a layout effect closes the window
  // before paint, so a real observer firing between commit and paint reads the latest callback.
  useLayoutEffect(() => { latest.current = onReach })

  useEffect(() => {
    const node = ref.current
    if (!node) return

    const observer = new IntersectionObserver(
      entries => { if (entries.some(entry => entry.isIntersecting)) latest.current() },
      { root: node.closest('.mail-list-scroll') },
    )
    observer.observe(node)

    return () => observer.disconnect()
  }, [])

  return <div ref={ref} className="message-list-sentinel" aria-hidden="true" />
}
