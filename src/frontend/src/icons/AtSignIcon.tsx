/** An alias is an address. The envelope is Identities', and the two pages sit side by side. */
export default function AtSignIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6">
      <circle cx="10" cy="10" r="3.33" />
      <path d="M13.33 6.67v4.17a2.5 2.5 0 0 0 5 0v-.84a8.33 8.33 0 1 0-3.27 6.62" strokeLinecap="round" />
    </svg>
  )
}
