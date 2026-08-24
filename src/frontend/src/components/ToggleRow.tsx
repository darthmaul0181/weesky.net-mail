type ToggleRowProps = {
  id: string
  label: string
  hint: string
  checked: boolean
  disabled?: boolean
  onChange: (on: boolean) => void
  /** Indented under the row it depends on, and greyed while that row makes it moot. */
  nested?: boolean
  covered?: boolean
  /** A lock nothing on this page will lift. `disabled` alone also covers the save in flight, which
      must not look locked — every row carries it for the width of each mutation. */
  locked?: boolean
}

/** The label and the input are siblings under .field-h, so the htmlFor/id pair is the only
    thing naming the control — and the hint stays outside it, or it joins that name. */
export default function ToggleRow(
  { id, label, hint, checked, disabled, onChange, nested, covered, locked }: ToggleRowProps,
) {
  return (
    <div className={`field-h is-setting${nested ? ' is-child' : ''}${covered ? ' is-covered' : ''}`}>
      <span className="setting-label">
        <label htmlFor={id}>{label}</label>
        <span className="setting-hint">{hint}</span>
      </span>
      <label className={`toggle-switch${locked ? ' is-locked' : ''}`}>
        <input
          id={id}
          type="checkbox"
          checked={checked}
          disabled={disabled}
          onChange={event => onChange(event.target.checked)}
        />
        <span className="toggle-track" />
      </label>
    </div>
  )
}
