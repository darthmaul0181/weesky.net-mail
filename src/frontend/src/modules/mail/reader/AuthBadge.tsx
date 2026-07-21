import Tooltip from '../../../components/Tooltip'
import type { MailAuthentication } from '../api/mailTypes'
import { authVerdict } from './authVerdict'

export default function AuthBadge({ authentication }: { authentication: MailAuthentication | null }) {
  const verdict = authVerdict(authentication)
  if (!verdict || !authentication) return null

  // The raw header is what lets a suspicious reader check for themselves; the summary line is
  // what serves everyone else.
  const detail = `SPF: ${authentication.spf ?? 'none'} · DKIM: ${authentication.dkim ?? 'none'}\n${authentication.raw}`
  const label = verdict === 'pass' ? 'Passed SPF and DKIM' : 'Failed SPF or DKIM'

  return (
    <Tooltip content={detail} placement="bottom-left">
      <span className={`auth-badge is-${verdict}`} tabIndex={0} role="img" aria-label={label}>
        {verdict === 'pass' ? '✓' : '!'}
      </span>
    </Tooltip>
  )
}
