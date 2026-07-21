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

  const trigger = sender
    ? <button type="button" className={className}>{label}</button>
    : <span className={className} tabIndex={detail ? 0 : undefined}>{label}</span>

  if (!detail) return trigger

  return <Tooltip content={detail} placement="bottom-left">{trigger}</Tooltip>
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
