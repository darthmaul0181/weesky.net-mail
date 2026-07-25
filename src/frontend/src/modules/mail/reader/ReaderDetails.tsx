import type { MailMessageDetail } from '../api/mailTypes'
import LockIcon from '../../../icons/LockIcon'
import { AddressList } from './AddressLabel'
import { formatReaderDate } from './formatReaderDate'
import { isWebUnsubscribe } from './unsubscribeLink'

interface Props {
  message: MailMessageDetail
}

/** The grid the header chevron expands into. A row whose datum is absent renders nothing. */
export default function ReaderDetails({ message }: Props) {
  const named = message.fromName && message.fromName !== message.fromAddress
  const isWeb = isWebUnsubscribe(message.unsubscribeUrl)

  return (
    <dl className="reader-details">
      <dt>From:</dt>
      <dd>
        {named
          ? <>{message.fromName} <span className="detail-muted">&lt;{message.fromAddress}&gt;</span></>
          : message.fromAddress}
      </dd>
      {message.to.length > 0 && <><dt>To:</dt><dd><AddressList addresses={message.to} /></dd></>}
      {message.cc.length > 0 && <><dt>Cc:</dt><dd><AddressList addresses={message.cc} /></dd></>}
      <dt>Date:</dt>
      <dd>{formatReaderDate(message.date)}</dd>
      {message.mailingList && <><dt>Mailing list:</dt><dd>{message.mailingList}</dd></>}
      {message.sentBy && <><dt>Mailed by:</dt><dd>{message.sentBy}</dd></>}
      {message.signedBy && <><dt>Signed by:</dt><dd>{message.signedBy}</dd></>}
      {message.unsubscribeUrl && (
        <>
          <dt>Unsubscribe:</dt>
          <dd>
            <a href={message.unsubscribeUrl} {...(isWeb ? { target: '_blank', rel: 'noopener noreferrer' } : {})}>
              Unsubscribe from this mailing list
            </a>
          </dd>
        </>
      )}
      {typeof message.tlsReceived === 'boolean' && (
        <>
          <dt>Security:</dt>
          <dd>
            {message.tlsReceived
              ? <span className="reader-security"><LockIcon /> Standard encryption (TLS)</span>
              : 'No encryption'}
          </dd>
        </>
      )}
    </dl>
  )
}
