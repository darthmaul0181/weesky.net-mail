import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import type { EditScope } from './calendarTypes'
import Modal from '../../components/Modal'

/** The whole question in one sentence. Both halves are chosen here together: glueing the wrong
 * preamble to the wrong question said "repeats" twice. */
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
  onPick: (scope: EditScope) => void
  onClose: () => void
}

/** How far an edit or deletion reaches on a series. All three scopes are drawn; one the change
 * cannot take is greyed with its reason. An alertdialog: every way out means no scope, never a
 * default one. */
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
    <Modal role="alertdialog" title={title} onClose={onClose}>
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
    </Modal>
  )
}
