import Icon from './Icon'
export default function MinimiseIcon({ size = 14 }: { size?: number }) {
  return (
    <Icon size={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M4 15h12" strokeLinecap="round" />
    </Icon>
  )
}
