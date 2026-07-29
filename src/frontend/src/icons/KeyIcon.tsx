export default function KeyIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor"
      strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="5.5" cy="10.5" r="3" />
      <path d="M7.6 8.4 13.5 2.5" />
      <path d="M10.6 5.4 12.4 7.2" />
    </svg>
  )
}
