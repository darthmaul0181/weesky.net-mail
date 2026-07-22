export default function ArchiveIcon({ size = 16 }: { size?: number }) {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width={size} height={size} viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="19.36 8.44 19.36 20 4.64 20 4.64 8.44" />
      <rect x="3" y="4" width="18" height="4.44" />
      <line x1="10.36" y1="12" x2="13.64" y2="12" />
    </svg>
  )
}
