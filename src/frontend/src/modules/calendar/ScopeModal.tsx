import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import type { EditScope } from './calendarTypes'

/**
 * The whole question, in one sentence. It lives here rather than at the call site because the two
 * halves have to be chosen together: the question that names the series carries no preamble, and
 * the one that has no series to name carries its own — glueing the wrong pair said "repeats" twice.
 */
export function scopeSentence(
  mode: 'save' | 'delete', name: string, repeatText: string | null, t: TFunction<'calendar'>,
): string {
  if (repeatText === null) {
    return mode === 'save' ? t('scope.saveQuestion') : t('scope.deleteQuestion')
  }
  const lead = t('scope.repeats', { name, summary: repeatText })
  return `${lead} ${mode === 'save' ? t('scope.saveChange') : t('scope.deleteChange')}`
}

export interface ScopeModalProps {
  title: string
  sentence: string
  allowed: EditScope[]
  onPick(scope: EditScope): void
  onClose(): void
}

/**
 * How far an edit or a deletion reaches on a series. The three are always drawn — a scope the
 * change cannot take is greyed and says why, because a button that disappears reads as a
 * rendering fault rather than as a rule.
 */
export default function ScopeModal({
  title, sentence, allowed, onPick, onClose,
}: ScopeModalProps) {
  const { t } = useTranslation('calendar')
  // Spelled out rather than `t(\`scope.${scope}\`)`: a key reaching t() as a variable is invisible
  // to both the typed guard and src/locales/keys.test.ts.
  const label: Record<EditScope, string> = {
    This: t('scope.This'),
    ThisAndFollowing: t('scope.ThisAndFollowing'),
    All: t('scope.All'),
  }
  const scopes: EditScope[] = ['This', 'ThisAndFollowing', 'All']

  return (
    <div className="modal-overlay">
      <div className="modal">
        <div className="modal-header">
          <span className="modal-title">{title}</span>
          <button type="button" className="modal-close"
            aria-label={t('actions.close', { ns: 'common' })} onClick={onClose}>✕</button>
        </div>
        <p>{sentence}</p>
        <div className="scope-choices">
          {scopes.map(scope => {
            const off = !allowed.includes(scope)
            return (
              <button key={scope} type="button"
                className={scope === 'This' ? 'btn btn-primary' : 'btn'}
                disabled={off} title={off ? t('scope.unavailable') : undefined}
                onClick={() => onPick(scope)}>{label[scope]}</button>
            )
          })}
        </div>
      </div>
    </div>
  )
}
