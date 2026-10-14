import { useTranslation } from 'react-i18next'
import { useWhatsNew } from './useWhatsNew'

/** `anchor`: lit while an active novelty passes through it. `unlisted`: lit while the list holds one not yet shown. */
export default function WhatsNewDot({ anchor, unlisted = false }: { anchor?: string; unlisted?: boolean }) {
  const { t } = useTranslation('whatsNew')
  const { lights, unlistedCount } = useWhatsNew()
  const lit = (anchor !== undefined && lights(anchor)) || (unlisted && unlistedCount > 0)
  // role="img", not visible text: a row's label stays its textContent.
  return lit ? <span className="new-dot" role="img" aria-label={t('new')} /> : null
}
