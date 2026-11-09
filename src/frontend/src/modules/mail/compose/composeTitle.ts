import type { TFunction } from 'i18next'
import type { ComposeAction } from './composeSeed'

// Edit-as-new is left out on purpose: it starts a message of its own, threaded to nothing.
// Each key is written out with its `ns`: no `useTranslation` binds this file, and a key held in a
// map or bound to no namespace is invisible to `src/locales/keys.test.ts`.
export function composeTitle(action: ComposeAction | undefined, t: TFunction<'compose'>): string {
  return action === 'reply' || action === 'replyAll' ? t('titles.reply', { ns: 'compose' })
    : action === 'forward' ? t('titles.forward', { ns: 'compose' })
      : action === 'draft' ? t('titles.draft', { ns: 'compose' }) : t('titles.newMessage', { ns: 'compose' })
}
