import Tooltip from './Tooltip'

export function HelpTooltip({ text }) {
  return (
    <Tooltip content={text}>
      <div className="help-tooltip-icon">?</div>
    </Tooltip>
  )
}
export default HelpTooltip
