import DropdownMenu from '../../../components/DropdownMenu'
import ChevronRightIcon from '../../../icons/ChevronRightIcon'
import { useAuth } from '../../../contexts/AuthContext'
import type { SendingIdentity } from '../api/mailTypes'

interface Props {
  identities: SendingIdentity[]
  /** Already resolved by the caller, which needs the same address for the send payload and so
      is the only place the "explicit, else default, else first" fallback may live. */
  value: string
  onChange: (address: string) => void
}

/** The From line. One identity renders as plain text — the 2c1 look for whoever curated nothing;
    several become a menu. A stale identity is never offered, but the chosen one keeps being named
    when a refetch turns it stale mid-compose: the send would carry that address, so the line says
    so, flagged `unavailable` the way the identities settings page flags it. */
export default function IdentitySelect({ identities, value, onChange }: Props) {
  const { identity } = useAuth()
  // The primary's name is the live account FullName, like the identities settings page.
  const nameOf = (i: SendingIdentity) => (i.isPrimary ? identity?.displayName ?? i.displayName : i.displayName)
  const label = (i: SendingIdentity) => <><strong>{nameOf(i)}</strong> ({i.address})</>

  const usable = identities.filter(i => !i.stale)
  const current = identities.find(i => i.address === value)
  const caption = current ? label(current) : value
  const tag = !current || current.stale
    ? <span className="identity-tag">unavailable</span>
    : null

  // A menu wherever it can change something, the stale case included: its usable rows are the
  // way back to an address that can actually be sent from.
  if (!usable.some(i => i.address !== value)) {
    return <><span className="compose-from-value">{caption}</span>{tag}</>
  }

  return (
    <>
      <DropdownMenu
        ariaLabel="From identity"
        className="compose-from-select"
        // The trigger sits at the left of the From row, with the whole composer to its right.
        align="left"
        trigger={<>{caption} <ChevronRightIcon size={13} /></>}
        items={usable.map(i => ({ label: i.address, node: label(i), onSelect: () => onChange(i.address) }))}
      />
      {tag}
    </>
  )
}
