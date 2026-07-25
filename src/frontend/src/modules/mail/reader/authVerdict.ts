import type { MailAuthentication } from '../api/mailTypes'

export type AuthVerdict = 'pass' | 'fail' | null

export function authVerdict(auth: MailAuthentication | null | undefined): AuthVerdict {
  if (!auth) return null
  if (auth.spf === 'pass' && auth.dkim === 'pass') return 'pass'
  if (auth.spf === 'fail' || auth.dkim === 'fail') return 'fail'

  return null
}
