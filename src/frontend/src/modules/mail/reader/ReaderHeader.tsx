import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import ArrowLeftIcon from '../../../icons/ArrowLeftIcon'
import ChevronRightIcon from '../../../icons/ChevronRightIcon'
import ExternalLinkIcon from '../../../icons/ExternalLinkIcon'
import Tooltip from '../../../components/Tooltip'
import type { MailMessageDetail } from '../api/mailTypes'
import AddressLabel, { AddressList } from './AddressLabel'
import AuthBadge from './AuthBadge'
import SpamGauge from './SpamGauge'
import ReaderDetails from './ReaderDetails'
import { formatReaderDate, formatReaderDateShort } from './formatReaderDate'

interface Props {
  data: MailMessageDetail
  onBack?: () => void
  viewportNarrow: boolean
  detailsOpen: boolean
  onToggleDetails: () => void
  unsubscribe: string | null
  spamOn: boolean
  bottomActions?: boolean
  readerActions: ReactNode
}

/** Subject, then sender + badge + date, then To/Cc — collapsing into `ReaderDetails` behind the
    chevron. The actions zone sits beside the stack, unless the phone tier draws it in the foot
    band instead. */
export default function ReaderHeader({
  data, onBack, viewportNarrow, detailsOpen, onToggleDetails, unsubscribe, spamOn,
  bottomActions, readerActions,
}: Props) {
  const { t } = useTranslation('mail')
  // A phone header spends four of its lines on metadata before the body starts. Two of them are
  // recovered here: the date shrinks to its locale's short form and joins the recipients line,
  // and the gauge moves behind the chevron. Both stay in full inside the details grid.
  const compactDate = viewportNarrow
    ? <span className="reader-date">{formatReaderDateShort(data.date)}</span> : null

  return (
    <header className="reader-header">
      <div className="reader-stack">
        <h1 className="reader-subject">
          {onBack && (
            <button
              type="button"
              className="reader-back"
              aria-label={t('reader.back')}
              onClick={onBack}
            >
              <ArrowLeftIcon size={16} />
            </button>
          )}
          {/* Its own element so the phone block can ellipsise the SUBJECT rather than the h1:
              a box with element children that clips is a real defect everywhere else in this
              app, and probes/mobile-layout.html only forgives an ellipsised leaf. */}
          <span className="reader-subject-text">{data.subject || t('list.noSubject')}</span>
          {data.priority !== 'normal' && (
            <Tooltip
              placement="bottom-left"
              content={t(data.priority === 'high' ? 'reader.priorityHigh' : 'reader.priorityLow')}
            >
              <span className={`reader-priority is-${data.priority}`}>
                {t(data.priority === 'high' ? 'list.highPriority' : 'list.lowPriority')}
              </span>
            </Tooltip>
          )}
        </h1>
        <div className="reader-meta">
          <div className="reader-from">
            <AddressLabel sender name={data.fromName} address={data.fromAddress} />
            <AuthBadge authentication={data.authentication} />
            {!viewportNarrow && <span className="reader-date">({formatReaderDate(data.date)})</span>}
            <button
              type="button"
              className={`details-toggle${detailsOpen ? ' is-open' : ''}`}
              aria-expanded={detailsOpen}
              aria-label={t(detailsOpen ? 'reader.hideDetails' : 'reader.showDetails')}
              onClick={onToggleDetails}
            >
              <ChevronRightIcon size={12} />
            </button>
            {/* With the sender: unsubscribing acts on the sender, not on this message. Not on a phone,
                where the pill always cost a whole line and the details grid already lists it. */}
            {!viewportNarrow && unsubscribe && (
              <a className="unsub-btn" href={unsubscribe} target="_blank" rel="noopener noreferrer">
                <ExternalLinkIcon />
                {t('reader.unsubscribe')}
              </a>
            )}
          </div>
          {detailsOpen ? (
            <ReaderDetails
              message={data}
              showSubject={viewportNarrow}
              showSpamScore={viewportNarrow && spamOn}
            />
          ) : (
            <>
              {(data.to.length > 0 || compactDate) && (
                <div className="reader-recipients reader-to-row">
                  {data.to.length > 0 && (
                    <span className="reader-to">{t('reader.details.to')} <AddressList addresses={data.to} /></span>
                  )}
                  {compactDate}
                </div>
              )}
              {data.cc.length > 0 && (
                <div className="reader-recipients">{t('reader.details.cc')} <AddressList addresses={data.cc} /></div>
              )}
            </>
          )}
          {spamOn && !viewportNarrow && <SpamGauge spamScore={data.spamScore} />}
        </div>
      </div>
      {!bottomActions && readerActions}
    </header>
  )
}
