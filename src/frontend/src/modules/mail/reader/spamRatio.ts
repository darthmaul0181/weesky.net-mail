import type { MailSpamScore } from '../api/mailTypes'

export function spamRatio(spam: MailSpamScore | null | undefined): number | null {
  if (!spam || spam.threshold <= 0) return null

  return Math.min(1, Math.max(0, spam.score / spam.threshold))
}
