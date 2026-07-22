/** MailIcon's exact style (viewBox 20, stroke 1.6): the two envelopes swap in place. */
export default function MailOpenIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6">
      <path d="M2 8 10 4l8 4V14a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2Z" strokeLinecap="round" strokeLinejoin="round" />
      <path d="m2 8 8 6 8-6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}
