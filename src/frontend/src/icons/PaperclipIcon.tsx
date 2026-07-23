/**
 * Pass a `title` where the icon is the only thing carrying the meaning — in a message row it
 * is the sole sign of an attachment. Leave it out where adjacent text already says it, as in
 * an attachment chip next to its file name, and the icon is hidden from assistive tech
 * instead of read out twice.
 */
export default function PaperclipIcon({ size = 14, title }: { size?: number; title?: string }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.8"
      role={title ? 'img' : undefined}
      aria-label={title}
      aria-hidden={title ? undefined : true}
    >
      <path d="M21 11l-8.5 8.5a5 5 0 0 1-7-7L14 4a3.5 3.5 0 0 1 5 5l-8.5 8.5a2 2 0 0 1-3-3L15 6"
        strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}
