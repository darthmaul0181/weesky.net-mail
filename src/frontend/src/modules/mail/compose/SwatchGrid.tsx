import { useTranslation } from 'react-i18next'
import ColourGrid from '../../../components/ColourGrid'
import { SWATCH_ROWS, swatchNameKey } from './composeColors'

interface Props {
  /** The colour in force: it carries `aria-pressed`, which is what lands Tab on it. */
  value: string
  /** The trigger that opened the popover, which is what names the grid. */
  labelledBy: string
  onPick: (colour: string) => void
}

/** Eighteen swatches, six by three, on the shared `ColourGrid` the calendar's twelve draw too —
    the two differ only in how they are named and how big they are painted. */
export default function SwatchGrid({ value, labelledBy, onPick }: Props) {
  const { t } = useTranslation('compose')

  return (
    <ColourGrid className="compose-swatches" labelledBy={labelledBy} rows={SWATCH_ROWS}
      value={value} onPick={onPick} nameOf={colour => t(swatchNameKey(colour))} />
  )
}
