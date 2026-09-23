import { useId } from 'react'
import { useTranslation } from 'react-i18next'
import Tooltip from './Tooltip'

interface HelpTooltipProps {
  text: string
}

export default function HelpTooltip({ text }: HelpTooltipProps) {
  const { t } = useTranslation('settings')
  const id = useId()
  return (
    <Tooltip content={text} bubbleId={id}>
      <button type="button" className="help-tooltip-icon" aria-label={t('rules.helpTitle')} aria-describedby={id}>?</button>
    </Tooltip>
  )
}
