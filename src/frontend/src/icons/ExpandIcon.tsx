import Icon from './Icon'
export default function ExpandIcon({ size = 14 }: { size?: number }) {
  return (
    <Icon size={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.8">
      <path d="M12 4h4v4M8 16H4v-4M16 4l-5 5M4 16l5-5" strokeLinecap="round" strokeLinejoin="round" />
    </Icon>
  )
}
