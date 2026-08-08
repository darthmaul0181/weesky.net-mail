import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import LoadingBlock from '../../../components/LoadingBlock'
import { formatReaderDate } from '../reader/formatReaderDate'
import { formatSize } from '../reader/formatSize'
import { useMessageSource } from '../queries'

/**
 * The message as it arrived, on its own tab. Deliberately chrome-less — no rail, no folder
 * tree, no way back: the route is a sibling of AppShell rather than a child, which is what
 * keeps useFolders' 60-second poll out of a tab whose only job is to show a text file.
 */
export default function MessageSourceView() {
  const { t } = useTranslation('mail')
  const [params] = useSearchParams()
  const folder = params.get('folder')
  const rawUid = params.get('uid')
  // A malformed URL is a hand-edited one: it asks for nothing rather than for message 0 — and
  // an IMAP UID starts at 1, so 0 addresses nothing either.
  const uid = rawUid !== null && /^\d+$/.test(rawUid) && Number(rawUid) > 0
    ? Number(rawUid)
    : null
  const addressed = folder !== null && folder !== '' && uid !== null

  const { data, isLoading, isFetching, error, refetch } = useMessageSource(
    addressed ? folder : null, addressed ? uid : null)

  useEffect(() => {
    if (data) {
      document.title = t('source.documentTitle', { subject: data.subject || t('list.noSubject') })
    }
  }, [data, t])

  // One line for both, but the Retry only where there is something to retry: the query is
  // disabled on a malformed URL, so refetch() there would be a button that cannot act.
  if (!addressed || error) {
    return (
      <div className="source-page">
        <p className="source-error">{t('source.loadFailed')}</p>
        {addressed && (
          // `error` outlives the refetch it triggers, so without isFetching on the label the
          // click would leave the screen untouched for the width of a 25 MB read.
          <button type="button" className="btn btn-ghost" disabled={isFetching}
            onClick={() => void refetch()}>{t(isFetching ? 'source.retrying' : 'list.retry')}</button>
        )}
      </div>
    )
  }

  if (isLoading || !data) return <div className="source-page"><LoadingBlock /></div>

  // Three em dashes are not a datum: the header that would explain them sits in the <pre> below.
  const checks = data.authentication
    ? [data.authentication.spf, data.authentication.dkim, data.authentication.dmarc]
    : []
  const verdicts = checks.some(v => v !== null) && checks.map(v => v ?? '—').join(' · ')

  return (
    <div className="source-page">
      <h1 className="source-title">{t('source.title')}</h1>
      <dl className="source-summary">
        {data.messageId && <><dt>{t('source.messageId')}</dt><dd>{data.messageId}</dd></>}
        <dt>{t('source.createdAt')}</dt><dd>{formatReaderDate(data.date)}</dd>
        <dt>{t('source.from')}</dt>
        <dd>{data.fromName ? `${data.fromName} <${data.fromAddress}>` : data.fromAddress}</dd>
        {data.to.length > 0 && (
          <><dt>{t('source.to')}</dt><dd>{data.to.map(a => a.address).join(', ')}</dd></>
        )}
        {data.subject && <><dt>{t('source.subject')}</dt><dd>{data.subject}</dd></>}
        {verdicts && <><dt>SPF / DKIM / DMARC</dt><dd>{verdicts}</dd></>}
      </dl>
      {/* Text, rendered as text. No dangerouslySetInnerHTML here, ever: what makes the message
          body need an iframe and two sanitising passes is that the browser parses it. */}
      <pre className="source-raw">{data.source}</pre>
      {data.truncated && (
        <p className="source-truncated">
          {t('source.truncated', { size: formatSize(data.totalBytes) })}
        </p>
      )}
    </div>
  )
}
