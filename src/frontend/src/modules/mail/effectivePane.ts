import type { ReadingPane } from '../../hooks/usePreferences'
import type { Viewport } from '../../hooks/useViewport'

// Only a phone overrides the account's choice: below 640px two panes do not fit, and `none` keeps the
// list mounted under `is-hidden` while the reader is open.
export function effectivePane(preference: ReadingPane, viewport: Viewport): ReadingPane {
  return viewport === 'phone' ? 'none' : preference
}
