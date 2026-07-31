export default function TextSizeIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M2.5 19 7 6l4.5 13" />
      <path d="M4 15h6" />
      <path d="m14.5 19 3-8.5 3 8.5" />
      <path d="M15.6 16.2h3.8" />
    </svg>
  )
}
