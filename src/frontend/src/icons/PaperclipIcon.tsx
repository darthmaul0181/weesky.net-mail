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
      viewBox="0 0 20 20"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.6"
      role={title ? 'img' : undefined}
      aria-label={title}
      aria-hidden={title ? undefined : true}
    >
      <path d="M14.5 9.5l-5 5a3 3 0 0 1-4.2-4.2l6-6a2 2 0 0 1 2.8 2.8l-6 6a1 1 0 0 1-1.4-1.4l5.3-5.3"
        strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  )
}
