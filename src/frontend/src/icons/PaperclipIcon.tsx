import Icon from './Icon'
/** Pass `title` where the icon alone carries the meaning (a message row); omit it beside text that
 * says it (an attachment chip) and the icon is hidden from assistive tech. */
export default function PaperclipIcon({ size = 14, title }: { size?: number; title?: string }) {
  return (
    <Icon size={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" title={title}>
      <path d="M21 11l-8.5 8.5a5 5 0 0 1-7-7L14 4a3.5 3.5 0 0 1 5 5l-8.5 8.5a2 2 0 0 1-3-3L15 6"
        strokeLinecap="round" strokeLinejoin="round" />
    </Icon>
  )
}
