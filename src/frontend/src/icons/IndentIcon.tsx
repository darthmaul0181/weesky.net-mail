export default function IndentIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M4 5h16M11 10h9M11 14h9M4 19h16" />
      <path d="m4 9.4 2.8 2.6L4 14.6" />
    </svg>
  )
}
