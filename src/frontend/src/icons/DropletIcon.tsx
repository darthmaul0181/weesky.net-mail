/** One closed path, so it stays legible at 17px where a palette's swatches would mush. */
export default function DropletIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6">
      <path d="M10 2.5s5.9 5.4 5.9 9a5.9 5.9 0 0 1-11.8 0C4.1 7.9 10 2.5 10 2.5z" strokeLinejoin="round" />
    </svg>
  )
}
