import { useId } from 'react'
import Tooltip from '../../../components/Tooltip'
import type { MailAddressInfo } from '../api/mailTypes'

interface Props {
  name: string
  address: string
  sender?: boolean
}

export default function AddressLabel({ name, address, sender = false }: Props) {
  const label = name || address
  const detail = label === address ? null : `"${name}" <${address}>`
  const className = sender ? 'address-label is-sender' : 'address-label'
  const bubbleId = useId()

  // The sender is always a button (its bubble reachable-by-keyboard precedent predates this
  // task); a recipient only becomes one when it has a bubble to describe.
  const trigger = sender || detail
    ? <button type="button" className={className} aria-describedby={detail ? bubbleId : undefined}>{label}</button>
    : <span className={className}>{label}</span>

  if (!detail) return trigger

  return <Tooltip content={detail} placement="bottom-left" bubbleId={bubbleId}>{trigger}</Tooltip>
}

export function AddressList({ addresses }: { addresses: MailAddressInfo[] }) {
  return (
    <>
      {addresses.map((recipient, index) => (
        <span key={`${recipient.address}-${index}`}>
          {index > 0 && ', '}
          <AddressLabel name={recipient.name} address={recipient.address} />
        </span>
      ))}
    </>
  )
}
