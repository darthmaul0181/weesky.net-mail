export default function ExternalLinkIcon({ size = 11 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true">
      <path d="M9 3H3.5v9.5H13V7" strokeLinecap="round" />
      <path d="M11 2.5h3v3M14 2.5 8.5 8" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}
