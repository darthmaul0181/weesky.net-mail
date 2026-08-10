// KebabIcon laid on its side: same round-capped zero-length strokes, so the shared icons test's
// stroke="currentColor" assertion holds here too. Horizontal because it stands for a row of tools
// that continues off the bar, where the kebab stands for a menu that drops from a button.
export default function EllipsisIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor"
      strokeWidth="3" strokeLinecap="round" aria-hidden="true">
      <path d="M3.5 8h.01M8 8h.01M12.5 8h.01" />
    </svg>
  )
}
