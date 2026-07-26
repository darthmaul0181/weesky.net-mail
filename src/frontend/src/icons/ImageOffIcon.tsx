export default function ImageOffIcon({ size = 18 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5">
      <path d="M3.5 4.5h13v11h-13z" strokeLinejoin="round" />
      <path d="M3.5 13l4-3.5 3 2.5" strokeLinecap="round" strokeLinejoin="round" />
      <path d="M2.5 2.5l15 15" strokeLinecap="round" />
    </svg>
  )
}
