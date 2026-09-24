import { Fragment, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import TrashIcon from '../../icons/TrashIcon'
import { typeLabel, typeOptions } from './contactLineTypes'
import type { LineListState } from './useLineList'

interface LineRowProps {
  className: string
  testId?: string
  removeLabel: string
  onRemove: () => void
  children: ReactNode
}

/** One line's inputs, then its bin; `removeLabel` names the line, the tooltip does not have to. */
export function LineRow({ className, testId, removeLabel, onRemove, children }: LineRowProps) {
  const { t } = useTranslation('contacts')
  return (
    <div className={className} data-testid={testId}>
      {children}
      <button type="button" className="admin-icon-btn is-danger" title={t('actions.remove', { ns: 'common' })}
        onClick={onRemove}>
        <TrashIcon size={14} />
        <span className="visually-hidden">{removeLabel}</span>
      </button>
    </div>
  )
}

interface LineTypeSelectProps {
  id: string
  label: string
  types: readonly string[]
  value: string
  className?: string
  onChange: (value: string) => void
}

export function LineTypeSelect({ id, label, types, value, className, onChange }: LineTypeSelectProps) {
  const { t } = useTranslation('contacts')
  return (
    <>
      <label className="visually-hidden" htmlFor={id}>{label}</label>
      <select id={id} value={value} className={className} onChange={event => onChange(event.target.value)}>
        {typeOptions(types, value).map(option => (
          <option key={option} value={option}>{typeLabel(option, t)}</option>
        ))}
      </select>
    </>
  )
}

interface LineListProps<T> {
  icon: ReactNode
  label: string
  addLabel: string
  list: LineListState<T>
  children: (line: T, index: number) => ReactNode
}

/** A family's heading, its lines, and the add button, which disappears at the family's cap. */
export function LineList<T>({ icon, label, addLabel, list, children }: LineListProps<T>) {
  return (
    <div className="field-v contact-editor-addresses">
      <span className="field-v-label">{icon}{label}</span>
      <div className="contact-address-list">
        {list.lines.map((line, index) => <Fragment key={index}>{children(line, index)}</Fragment>)}
        {list.canAdd && (
          <button type="button" className="contact-address-add" onClick={list.add}>{addLabel}</button>
        )}
      </div>
    </div>
  )
}
