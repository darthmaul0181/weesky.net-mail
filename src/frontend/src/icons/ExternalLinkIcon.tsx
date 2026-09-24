import Icon from './Icon'
export default function ExternalLinkIcon({ size = 11 }: { size?: number }) {
  return (
    <Icon size={size} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.7">
      <path d="M9 3H3.5v9.5H13V7" strokeLinecap="round" />
      <path d="M11 2.5h3v3M14 2.5 8.5 8" strokeLinecap="round" strokeLinejoin="round" />
    </Icon>
  )
}
