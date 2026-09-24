import Icon from './Icon'
export default function ChevronRightIcon({ size = 14 }: { size?: number }) {
  return (
    <Icon size={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M7.5 4.5l6 5.5-6 5.5" strokeLinecap="round" strokeLinejoin="round" />
    </Icon>
  )
}
