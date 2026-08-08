import { useTranslation } from 'react-i18next'
import Tooltip from '../../../components/Tooltip'
import type { MailAuthentication } from '../api/mailTypes'
import { authVerdict } from './authVerdict'
import ShieldCheckIcon from '../../../icons/ShieldCheckIcon'
import ShieldAlertIcon from '../../../icons/ShieldAlertIcon'

export default function AuthBadge({ authentication }: { authentication: MailAuthentication | null }) {
  const { t } = useTranslation('mail')
  const verdict = authVerdict(authentication)
  if (!verdict || !authentication) return null

  // The raw header is what lets a suspicious reader check for themselves; the summary line is
  // what serves everyone else.
  const none = t('reader.auth.none')
  const detail = `SPF: ${authentication.spf ?? none} · DKIM: ${authentication.dkim ?? none}\n${authentication.raw}`
  const label = t(verdict === 'pass' ? 'reader.auth.passed' : 'reader.auth.failed')

  return (
    <Tooltip content={detail} placement="bottom-left">
      <span className={`auth-badge is-${verdict}`} tabIndex={0} role="img" aria-label={label}>
        {verdict === 'pass' ? <ShieldCheckIcon size={20} /> : <ShieldAlertIcon size={20} />}
      </span>
    </Tooltip>
  )
}
