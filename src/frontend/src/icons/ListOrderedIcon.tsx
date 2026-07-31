export default function ListOrderedIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
      strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M10 6h10M10 12h10M10 18h10" />
      <path d="M4 5.2 5.4 4.4V9M4 9h2.6" strokeWidth="1.6" />
      <path d="M4 15.6c0-1.6 2.8-1.4 2.8 0 0 .9-2.8 1.9-2.8 3.2h3" strokeWidth="1.6" />
    </svg>
  )
}
