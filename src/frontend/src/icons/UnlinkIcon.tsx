import Icon from './Icon'
export default function UnlinkIcon({ size = 16 }: { size?: number }) {
  return (
    <Icon size={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
      <path d="m18.8 12.3 1.7-1.8a5 5 0 0 0-7-7l-1.7 1.8" />
      <path d="m5.2 11.7-1.7 1.8a5 5 0 0 0 7 7l1.7-1.8" />
      <path d="M8 2v3M2 8h3M16 19v3M19 16h3" />
    </Icon>
  )
}
