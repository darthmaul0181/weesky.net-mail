/** PersonPlusIcon without the plus: on Account you are yourself, next door you attach a mailbox. */
export default function UserIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6">
      <circle cx="10" cy="6.6" r="3.2" />
      <path d="M3.9 17c0-3.2 2.7-5.1 6.1-5.1s6.1 1.9 6.1 5.1" strokeLinecap="round" />
    </svg>
  )
}
