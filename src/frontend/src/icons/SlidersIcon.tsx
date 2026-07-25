/** Reads as "adjust these things", where a gear would read as "application settings". */
export default function SlidersIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6">
      <path d="M3 6h9M15 6h2M3 14h2M8 14h9" strokeLinecap="round" />
      <circle cx="13.5" cy="6" r="2" />
      <circle cx="6.5" cy="14" r="2" />
    </svg>
  )
}
