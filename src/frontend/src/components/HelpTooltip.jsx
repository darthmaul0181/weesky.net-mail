import { useId } from 'react'
import Tooltip from './Tooltip'

export function HelpTooltip({ text }) {
  const id = useId()
  return (
    <Tooltip content={text} bubbleId={id}>
      <button type="button" className="help-tooltip-icon" aria-describedby={id}>?</button>
    </Tooltip>
  )
}
export default HelpTooltip
