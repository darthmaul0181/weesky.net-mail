// Reconciled from two copies: AliasesPage rendered at 15px, RulesPage at 13px.
// `size` prop (default 15) satisfies both call sites; RulesPage passes size={13}.
//
// The 24 grid is Feather's own: cropping it to 20 levels this glyph with the pencil beside it, but
// makes it the largest of the message row's cluster (JunkIcon 12.0 units, MailIcon 10.8). That row
// is the reference every list aligns to, so the mismatch between two compact glyphs is cheaper.
export function TrashIcon({ size = 15 }) {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" width={size} height={size} viewBox="0 0 24 24"
      fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <polyline points="3 6 5 6 21 6" />
      <path d="M19 6l-1 14H6L5 6" />
      <path d="M10 11v6" />
      <path d="M14 11v6" />
      <path d="M9 6V4h6v2" />
    </svg>
  )
}

export default TrashIcon
