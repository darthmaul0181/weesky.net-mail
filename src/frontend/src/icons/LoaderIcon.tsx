/** Eight rays with graduated opacity — the classic spinner, readable at rest too. */
export default function LoaderIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7">
      <g strokeLinecap="round">
        <path d="M10 3v3" />
        <path d="M14.9 5.1l-2.1 2.1" opacity="0.95" />
        <path d="M17 10h-3" opacity="0.85" />
        <path d="M14.9 14.9l-2.1-2.1" opacity="0.7" />
        <path d="M6 10H3" opacity="0.5" />
        <path d="M7.2 7.2L5.1 5.1" opacity="0.6" />
        <path d="M7.2 12.8l-2.1 2.1" opacity="0.45" />
        <path d="M10 14v3" opacity="0.35" />
      </g>
    </svg>
  )
}
