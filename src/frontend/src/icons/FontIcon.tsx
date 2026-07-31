export default function FontIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M5 7V4.5h14V7" />
      <path d="M12 4.5v15M9.5 19.5h5" />
    </svg>
  )
}
