import Icon from './Icon'
/** Three rules. The drawer's trigger below 1024px. */
export default function MenuIcon({ size = 20 }: { size?: number }) {
  return (
    <Icon size={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
      <path d="M4 6h16M4 12h16M4 18h16" />
    </Icon>
  )
}
