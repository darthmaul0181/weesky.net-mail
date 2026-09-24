import Icon from './Icon'
export default function CodeIcon({ size = 16 }: { size?: number }) {
  return (
    <Icon size={size} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round">
      <path d="M5.5 4.5 2 8l3.5 3.5M10.5 4.5 14 8l-3.5 3.5" />
    </Icon>
  )
}
