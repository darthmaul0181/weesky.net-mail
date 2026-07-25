// Dots drawn as round-capped zero-length strokes, so the shared icons test's
// stroke="currentColor" assertion holds for this icon like every other.
export default function KebabIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor"
      strokeWidth="3" strokeLinecap="round" aria-hidden="true">
      <path d="M8 3.5h.01M8 8h.01M8 12.5h.01" />
    </svg>
  )
}
