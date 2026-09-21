import { useTranslation } from 'react-i18next'
import ColourGrid from '../../components/ColourGrid'
import { CALENDAR_COLORS, colourNameKey } from './calendarColors'

interface Props {
  value: string
  onPick: (color: string) => void
}

/** Six per row, the count `.color-swatches` draws: the rows have to match what the eye sees. */
const ROWS = [CALENDAR_COLORS.slice(0, 6), CALENDAR_COLORS.slice(6)]

/** The twelve, six by two. Shared by the calendar dialog and the import one so a colour is
    chosen the same way whichever door the calendar is created from. */
export default function ColorSwatches({ value, onPick }: Props) {
  const { t } = useTranslation('calendar')

  return (
    <ColourGrid className="color-swatches" label={t('dialogs.colourGrid')} rows={ROWS}
      value={value} onPick={onPick} nameOf={color => t(colourNameKey(color))} />
  )
}
