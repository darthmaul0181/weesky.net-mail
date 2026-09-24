import Icon from './Icon'
export default function SearchIcon({ size = 20 }: { size?: number }) {
  return (
    <Icon size={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="11" cy="11" r="7" />
      <path d="m20 20-3.5-3.5" />
    </Icon>
  )
}
