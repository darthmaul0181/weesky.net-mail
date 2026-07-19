export default function CalendarIcon({ size = 20 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6">
      <rect x="2.5" y="3.5" width="15" height="14" rx="2" />
      <path d="M2.5 8h15" strokeLinecap="round" />
      <path d="M6 2v3" strokeLinecap="round" />
      <path d="M14 2v3" strokeLinecap="round" />
      <path d="M6 11.5h.01M10 11.5h.01M14 11.5h.01M6 14.5h.01M10 14.5h.01M14 14.5h.01" strokeLinecap="round" />
    </svg>
  )
}
