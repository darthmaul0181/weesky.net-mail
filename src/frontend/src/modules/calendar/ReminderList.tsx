import { useTranslation } from 'react-i18next'
import PlusIcon from '../../icons/PlusIcon'
import {
  ALL_DAY_PRESETS, DATED_DEFAULT, DATED_PRESETS, MAX_REMINDERS, reminderLabel,
} from './reminderPresets'

export interface ReminderListProps {
  reminders: number[]
  allDay: boolean
  /** The alarms the bell cannot draw — an e-mail alarm a phone wrote. Kept through a save, so
      they are printed rather than hidden: a list that said nothing would read as a loss. */
  foreignAlarms: string[]
  onChange(reminders: number[]): void
}

/** The ladder, plus whatever the event actually carries: a value a phone wrote that no preset
    holds has to stay selectable, or opening the event would silently retune its bell. */
function options(value: number, allDay: boolean): number[] {
  const presets: readonly number[] = allDay ? ALL_DAY_PRESETS : DATED_PRESETS
  return presets.includes(value) ? [...presets] : [...presets, value].sort((a, b) => a - b)
}

export default function ReminderList({
  reminders, allDay, foreignAlarms, onChange,
}: ReminderListProps) {
  const { t } = useTranslation('calendar')
  const preset = allDay ? ALL_DAY_PRESETS[0] : DATED_DEFAULT

  return (
    <div className="reminder-list">
      {reminders.map((minutes, index) => (
        <div className="reminder-row" key={index}>
          <select value={minutes} aria-label={t('editor.reminder')}
            onChange={event => onChange(reminders.map(
              (one, at) => (at === index ? Number(event.target.value) : one)))}>
            {options(minutes, allDay).map(value => (
              <option key={value} value={value}>{reminderLabel(value, allDay, t)}</option>
            ))}
          </select>
          <button type="button" className="reminder-remove"
            aria-label={t('editor.removeReminder')}
            onClick={() => onChange(reminders.filter((_, at) => at !== index))}>✕</button>
        </div>
      ))}

      {reminders.length < MAX_REMINDERS && (
        <button type="button" className="btn reminder-add"
          onClick={() => onChange([...reminders, preset])}>
          <PlusIcon size={12} />{t('editor.addReminder')}
        </button>
      )}

      {foreignAlarms.length > 0 && (
        <div className="reminder-foreign">
          <span>{t('editor.foreignAlarms')}</span>
          {foreignAlarms.map((alarm, index) => <span key={index}>{alarm}</span>)}
        </div>
      )}
    </div>
  )
}
