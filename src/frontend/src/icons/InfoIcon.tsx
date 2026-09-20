/** The About page's own mark: a letter in a ring, which nothing else in the nav can be read as. */
export default function InfoIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true" focusable="false">
      <circle cx="10" cy="10" r="7.5" />
      <path d="M10 9v4.6M10 6.4h.01" strokeLinecap="round" />
    </svg>
  )
}
