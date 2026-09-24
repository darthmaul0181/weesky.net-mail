import Icon from './Icon'
export default function TextColourIcon({ size = 16 }: { size?: number }) {
  return (
    <Icon size={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
      <path d="M5 19 12 5l7 14" />
      <path d="M7.6 14.6h8.8" />
    </Icon>
  )
}
