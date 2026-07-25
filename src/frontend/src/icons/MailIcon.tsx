export default function MailIcon({ size = 20 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6">
      <rect x="2" y="4" width="16" height="12" rx="2" />
      <path d="M2.5 5l7.5 6 7.5-6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}
