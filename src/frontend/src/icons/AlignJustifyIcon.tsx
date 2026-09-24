import Icon from './Icon'
export default function AlignJustifyIcon({ size = 16 }: { size?: number }) {
  return (
    <Icon size={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
      <path d="M3 6h18M3 12h18M3 18h18" />
    </Icon>
  )
}
