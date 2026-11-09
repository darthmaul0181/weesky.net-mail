import Icon from './Icon'
export default function CollapseIcon({ size = 14 }: { size?: number }) {
  return (
    <Icon size={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M16 8h-4V4M4 12h4v4M12 8l5-5M8 12l-5 5" strokeLinecap="round" strokeLinejoin="round" />
    </Icon>
  )
}
