import type { ReadingPane } from '../../hooks/usePreferences'
import type { Viewport } from '../../hooks/useViewport'

/**
 * Which arrangement is actually on screen. Only a phone overrides the account's choice: below
 * 640px there is no width for two panes at once, and `none` is the arrangement the module
 * already implements — the list stays mounted under `is-hidden` while the reader is open.
 */
export function effectivePane(preference: ReadingPane, viewport: Viewport): ReadingPane {
  return viewport === 'phone' ? 'none' : preference
}
