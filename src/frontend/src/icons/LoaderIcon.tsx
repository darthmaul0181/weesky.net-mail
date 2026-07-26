/** Eight rays with graduated opacity — the classic spinner, readable at rest too. The rays span
    r=3.6..8 rather than 4..7 so that at 16px the painted extent (14.2px, measured) matches the
    neighbouring RocketIcon's 13.4px instead of falling 12% under it, and the fade bottoms out at
    0.6: below that the trailing rays vanished on both surfaces and the icon read as missing. */
export default function LoaderIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.8">
      <g strokeLinecap="round">
        <path d="M10 2v4.4" />
        <path d="M15.66 4.34l-3.11 3.11" opacity="0.95" />
        <path d="M18 10h-4.4" opacity="0.88" />
        <path d="M15.66 15.66l-3.11-3.11" opacity="0.8" />
        <path d="M4.34 4.34l3.11 3.11" opacity="0.85" />
        <path d="M2 10h4.4" opacity="0.75" />
        <path d="M4.34 15.66l3.11-3.11" opacity="0.68" />
        <path d="M10 13.6V18" opacity="0.6" />
      </g>
    </svg>
  )
}
