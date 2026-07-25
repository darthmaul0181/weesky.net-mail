export default function ReplyAllIcon({ size = 20 }: { size?: number }) {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width={size} height={size} viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="7 14 2 9 7 4" />
      <polyline points="12 14 7 9 12 4" />
      <path d="M22 20v-7a4 4 0 0 0-4-4H7" />
    </svg>
  )
}
