import { useId, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import Modal from '../../../components/Modal'
import DropletIcon from '../../../icons/DropletIcon'
import { PREFERENCE_KEYS, useSetPreference } from '../../../hooks/usePreferences'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import {
  formatCustomPalette, generateCustomPalette, hueGradient, INTENSITIES, isNearDanger, type CustomPaletteDef,
} from '../../../lib/customPalette'
import PalettePreview from './PalettePreview'

// Structure track: the intensity's own chroma, lifted so a neutral track still reads as hues.
const TRACK_CHROMA = { neutral: 0.02, muted: 0.06, vivid: 0.14 } as const

export default function CustomPaletteEditor({ initial, onClose, onSaved }: {
  initial: CustomPaletteDef; onClose: () => void; onSaved: () => void
}) {
  const { t } = useTranslation('settings')
  const setPreference = useSetPreference()
  const [draft, setDraft] = useState(initial)
  const [error, setError] = useState<string | null>(null)
  const tokens = useMemo(() => generateCustomPalette(draft), [draft])
  const ids = { structure: useId(), accent: useId(), intensity: useId() }

  async function save() {
    setError(null)
    try {
      await setPreference.mutateAsync({ key: PREFERENCE_KEYS.customPalette, value: formatCustomPalette(draft) })
      onSaved()
    } catch (e) {
      setError(apiErrorMessage(e, t('appearance.custom.saveFailed')))
    }
  }

  return (
    <Modal icon={<DropletIcon size={16} />} title={t('appearance.custom.name')} onClose={onClose}
      busy={setPreference.isPending} className="custom-palette-modal">
      <div className="custom-palette-editor">
        <div className="custom-palette-controls">
          <label htmlFor={ids.structure} className="custom-palette-label">{t('appearance.custom.structure')}</label>
          <input id={ids.structure} className="hue-slider" type="range" min={0} max={359} value={draft.structure}
            style={{ background: hueGradient(0.58, TRACK_CHROMA[draft.intensity]) }}
            onChange={e => setDraft({ ...draft, structure: Number(e.target.value) })} />
          <span id={ids.intensity} className="custom-palette-label">{t('appearance.custom.intensity')}</span>
          <div className="seg" role="radiogroup" aria-labelledby={ids.intensity}>
            {INTENSITIES.map(intensity => (
              <label key={intensity}>
                <input type="radio" name="custom-palette-intensity" checked={draft.intensity === intensity}
                  onChange={() => setDraft({ ...draft, intensity })} />
                {t(`appearance.custom.${intensity}`)}
              </label>
            ))}
          </div>
          <label htmlFor={ids.accent} className="custom-palette-label">{t('appearance.custom.accent')}</label>
          <input id={ids.accent} className="hue-slider" type="range" min={0} max={359} value={draft.accent}
            style={{ background: hueGradient(0.66, 0.15) }}
            onChange={e => setDraft({ ...draft, accent: Number(e.target.value) })} />
          {isNearDanger(draft.accent) && <p className="custom-palette-warning">{t('appearance.custom.nearDanger')}</p>}
        </div>
        <div className="palette-zoom-pair">
          {[false, true].map(dark => (
            <figure key={String(dark)}>
              <PalettePreview value="custom" dark={dark} large tokens={dark ? tokens.dark : tokens.light} />
              <figcaption>{t(dark ? 'appearance.theme.dark' : 'appearance.theme.light')}</figcaption>
            </figure>
          ))}
        </div>
      </div>
      {error && <p className="alert-error" role="alert">{error}</p>}
      <div className="folder-pick-submit">
        <button type="button" className="btn btn-ghost" onClick={onClose}>{t('appearance.custom.cancel')}</button>
        <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={setPreference.isPending}>
          {t('appearance.custom.save')}
        </button>
      </div>
    </Modal>
  )
}
