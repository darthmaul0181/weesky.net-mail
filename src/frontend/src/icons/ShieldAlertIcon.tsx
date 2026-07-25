export default function ShieldAlertIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none"
      stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M12 3l7 2.5v5.5c0 4.2-2.8 7.2-7 9-4.2-1.8-7-4.8-7-9V5.5z" />
      <path d="M12 8.5v4" />
      <path d="M12 15h.01" />
    </svg>
  )
}
