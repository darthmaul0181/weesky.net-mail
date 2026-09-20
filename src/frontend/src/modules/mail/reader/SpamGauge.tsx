import { useId, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import Tooltip from '../../../components/Tooltip'
import type { MailSpamScore } from '../api/mailTypes'
import { spamRatio } from './spamRatio'

/** The bar alone. The details grid labels its rows with a <dt>, so it takes this one. */
export function SpamBar({ spamScore }: { spamScore: MailSpamScore | null | undefined }) {
  const { t } = useTranslation('mail')
  const bubbleId = useId()
  const ratio = spamRatio(spamScore)
  if (ratio === null || !spamScore) return null

  // The visible "7.0 / 16.0" is what a plain aria-label would hide behind "Spam score:" alone —
  // this interpolates the same two numbers into the name instead.
  const name = t('reader.spamScoreLabel', {
    score: spamScore.score.toFixed(1),
    threshold: spamScore.threshold.toFixed(1),
  })

  return (
    <Tooltip content={spamScore.raw} placement="bottom-left" bubbleId={bubbleId}>
      <button type="button" className="spam-gauge" aria-label={name} aria-describedby={bubbleId}>
        <span
          className="spam-gauge-track"
          style={{ '--gauge-ratio': String(ratio) } as CSSProperties}
        >
          <span className="spam-gauge-fill" />
        </span>
        <span className="spam-gauge-value">
          {spamScore.score.toFixed(1)} / {spamScore.threshold.toFixed(1)}
        </span>
      </button>
    </Tooltip>
  )
}

export default function SpamGauge({ spamScore }: { spamScore: MailSpamScore | null | undefined }) {
  const { t } = useTranslation('mail')
  if (spamRatio(spamScore) === null) return null

  return (
    <div className="reader-spam">
      {t('reader.spamScore')}{' '}
      <SpamBar spamScore={spamScore} />
    </div>
  )
}
