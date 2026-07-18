export function HelpTooltip({ text }) {
  return (
    <div className="help-tooltip-wrap">
      <div className="help-tooltip-icon">?</div>
      <div className="help-tooltip-bubble">{text}</div>
    </div>
  )
}

export default HelpTooltip
