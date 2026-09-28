import { useTranslation } from 'react-i18next'
import MenuSelect from '../../../components/MenuSelect'
import ToggleRow from '../../../components/ToggleRow'
import {
  PREFERENCE_KEYS, birthdaysOnOf, firstDayOfWeekOf, type FirstDayOfWeek,
} from '../../../hooks/usePreferences'
import { useToasts } from '../../../hooks/useToasts'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { useSetBirthdays } from '../../calendar/queries'
import PreferencePage from './PreferencePage'
import { useSavePreference } from './useSavePreference'

const FIRST_DAYS = [
  { value: 'monday', labelKey: 'calendarGeneral.firstDayOfWeek.monday', toastKey: 'calendarGeneral.firstDayOfWeek.mondayToast' },
  { value: 'sunday', labelKey: 'calendarGeneral.firstDayOfWeek.sunday', toastKey: 'calendarGeneral.firstDayOfWeek.sundayToast' },
] as const satisfies { value: FirstDayOfWeek; labelKey: string; toastKey: string }[]

export default function CalendarGeneralPage() {
  const { t } = useTranslation('settings')
  const toasts = useToasts()
  const { addToast } = toasts
  const { save, saving } = useSavePreference(addToast)
  const setBirthdays = useSetBirthdays()

  return (
    <PreferencePage group={t('nav.calendar')} toasts={toasts}>
      {preferences => (
        <>
          <section className="account-section">
            <h2>{t('calendarGeneral.calendars')}</h2>
            {/* Its own route, never a preference write: switching it creates or removes a calendar. */}
            <ToggleRow
              id="birthdays-calendar"
              label={t('calendarGeneral.birthdays.label')}
              hint={t('calendarGeneral.birthdays.hint')}
              checked={birthdaysOnOf(preferences)}
              disabled={setBirthdays.isPending}
              onChange={on => setBirthdays.mutate(
                { enabled: on, tz: Intl.DateTimeFormat().resolvedOptions().timeZone },
                {
                  onSuccess: () => addToast(t(on ? 'calendarGeneral.birthdays.on' : 'calendarGeneral.birthdays.off')),
                  onError: error => addToast(apiErrorMessage(error, t('general.saveFailed')), 'error'),
                })}
            />
          </section>

          <section className="account-section">
            <h2>{t('calendarGeneral.week')}</h2>
            <div className="field-h is-setting">
              <span className="setting-label">
                <label htmlFor="first-day-of-week">{t('calendarGeneral.firstDayOfWeek.label')}</label>
                <span className="setting-hint">{t('calendarGeneral.firstDayOfWeek.hint')}</span>
              </span>
              <MenuSelect
                id="first-day-of-week"
                value={firstDayOfWeekOf(preferences)}
                options={FIRST_DAYS.map(day => ({ value: day.value, label: t(day.labelKey) }))}
                disabled={saving}
                onChange={day => void save(PREFERENCE_KEYS.firstDayOfWeek, day,
                  t(FIRST_DAYS.find(one => one.value === day)!.toastKey))}
              />
            </div>
          </section>
        </>
      )}
    </PreferencePage>
  )
}
