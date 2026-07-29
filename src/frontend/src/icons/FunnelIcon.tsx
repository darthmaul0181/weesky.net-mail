/** Replaces the old RulesIcon, whose stacked bars were indistinguishable from SlidersIcon on
    General — the page right above it in the settings nav. A triangle cannot be confused with one. */
export default function FunnelIcon({ size = 16 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6">
      <path d="M2.2 3.6h15.6L11.4 11v5.6l-2.8-1.4V11z" strokeLinejoin="round" />
    </svg>
  )
}
