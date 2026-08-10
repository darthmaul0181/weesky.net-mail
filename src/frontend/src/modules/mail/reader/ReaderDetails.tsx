import { useTranslation } from 'react-i18next'
import type { MailMessageDetail } from '../api/mailTypes'
import LockIcon from '../../../icons/LockIcon'
import { AddressList } from './AddressLabel'
import { formatReaderDate } from './formatReaderDate'
import { SpamBar } from './SpamGauge'
import { spamRatio } from './spamRatio'
import { isWebUnsubscribe } from './unsubscribeLink'

interface Props {
  message: MailMessageDetail
  /** Phone only: the header has no room for the gauge, so it folds in here instead. */
  showSpamScore?: boolean
}

/** The grid the header chevron expands into. A row whose datum is absent renders nothing. */
export default function ReaderDetails({ message, showSpamScore }: Props) {
  const { t } = useTranslation('mail')
  const named = message.fromName && message.fromName !== message.fromAddress
  const isWeb = isWebUnsubscribe(message.unsubscribeUrl)
  const spam = showSpamScore && spamRatio(message.spamScore) !== null

  return (
    <dl className="reader-details">
      <dt>{t('reader.details.from')}</dt>
      <dd>
        {named
          ? <>{message.fromName} <span className="detail-muted">&lt;{message.fromAddress}&gt;</span></>
          : message.fromAddress}
      </dd>
      {message.to.length > 0
        && <><dt>{t('reader.details.to')}</dt><dd><AddressList addresses={message.to} /></dd></>}
      {message.cc.length > 0
        && <><dt>{t('reader.details.cc')}</dt><dd><AddressList addresses={message.cc} /></dd></>}
      <dt>{t('reader.details.date')}</dt>
      <dd>{formatReaderDate(message.date)}</dd>
      {spam && <><dt>{t('reader.spamScore')}</dt><dd><SpamBar spamScore={message.spamScore} /></dd></>}
      {message.mailingList
        && <><dt>{t('reader.details.mailingList')}</dt><dd>{message.mailingList}</dd></>}
      {message.sentBy && <><dt>{t('reader.details.mailedBy')}</dt><dd>{message.sentBy}</dd></>}
      {message.signedBy && <><dt>{t('reader.details.signedBy')}</dt><dd>{message.signedBy}</dd></>}
      {message.unsubscribeUrl && (
        <>
          <dt>{t('reader.details.unsubscribe')}</dt>
          <dd>
            <a href={message.unsubscribeUrl} {...(isWeb ? { target: '_blank', rel: 'noopener noreferrer' } : {})}>
              {t('reader.details.unsubscribeLink')}
            </a>
          </dd>
        </>
      )}
      {typeof message.tlsReceived === 'boolean' && (
        <>
          <dt>{t('reader.details.security')}</dt>
          <dd>
            {message.tlsReceived
              ? <span className="reader-security"><LockIcon /> {t('reader.details.tls')}</span>
              : t('reader.details.noTls')}
          </dd>
        </>
      )}
    </dl>
  )
}
