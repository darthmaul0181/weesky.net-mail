import Icon from './Icon'
export default function MoonIcon({ size = 16 }: { size?: number }) {
  return (
    <Icon size={size} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round">
      <path d="M13.5 9.5A5.8 5.8 0 0 1 6.5 2.5a5.8 5.8 0 1 0 7 7Z" />
    </Icon>
  )
}
