import Icon from './Icon'
export default function JunkIcon({ size = 16 }: { size?: number }) {
  return (
    <Icon size={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" xmlns="http://www.w3.org/2000/svg">
      <circle cx="12" cy="12" r="8" />
      <line x1="6.34" y1="6.34" x2="17.66" y2="17.66" />
    </Icon>
  )
}
