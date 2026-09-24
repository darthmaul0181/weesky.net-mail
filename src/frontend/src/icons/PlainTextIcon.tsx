import Icon from './Icon'
export default function PlainTextIcon({ size = 16 }: { size?: number }) {
  return (
    <Icon size={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
      <path d="M5.5 3.5h13v17h-13z" />
      <path d="M9 8.5h6M9 12h6M9 15.5h3.5" />
    </Icon>
  )
}
