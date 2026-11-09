import { lazy, Suspense, useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useLocation, useNavigate } from 'react-router'
import { useToasts, type AddToast } from '../../../../hooks/useToasts'
import Toasts from '../../../../components/Toasts'
import LoadingBlock from '../../../../components/LoadingBlock'
import ComposePreferencesGate from '../ComposePreferencesGate'
import { composeTitle } from '../composeTitle'
import type { ComposeSeed, DraftRef } from '../composeSeed'
import type { ComposeState } from '../composeRoute'
import type { QuickComposeState } from './QuickComposeContext'

// Lazy, so a session that never opens the window never downloads Squire.
const ComposeForm = lazy(() => import('../ComposeForm'))

const BODY = '.compose-body [contenteditable="true"], .compose-body textarea'
const TO = '.compose-to-row input'

interface WindowActions {
  restore: () => void
  minimise: () => void
  close: () => void
  trackDraft: (ref: DraftRef | null) => void
}

interface Props {
  state: QuickComposeState | null
  /** Bumped by every restore(), the one request to move focus into an existing window. */
  focusRequest: number
  actions: WindowActions
}

/** The window, or its minimised bar, plus a toast host of its own: the shell has none, and the
    window's toasts (a send closes it) have to outlive the window itself. */
export default function QuickComposeWindow({ state, focusRequest, actions }: Props) {
  const { toasts, addToast, removeToast, pauseToast, resumeToast } = useToasts()
  return (
    <>
      {state && <OpenWindow key={state.instance} state={state} focusRequest={focusRequest}
        actions={actions} onNotify={addToast} />}
      <Toasts className="quick-compose-toasts" toasts={toasts} onRemove={removeToast}
        onPause={pauseToast} onResume={resumeToast} />
    </>
  )
}

function OpenWindow({ state, focusRequest, actions, onNotify }: Props & { state: QuickComposeState; onNotify: AddToast }) {
  const { t } = useTranslation('compose')
  const navigate = useNavigate()
  const { restore, minimise, close, trackDraft } = actions
  const [title, setTitle] = useState(() => composeTitle(state.seed?.action, t))
  const region = useRef<HTMLDivElement>(null)
  const lastFocus = useRef<HTMLElement | null>(null)
  const openedAt = useRef(focusRequest)

  // Where a window opens: To for a new message, the body for anything that already has content.
  const focusEntry = useCallback(() => {
    region.current?.querySelector<HTMLElement>(state.seed ? BODY : TO)?.focus()
  }, [state.seed])

  useEffect(() => {
    if (focusRequest === openedAt.current) return
    const last = lastFocus.current
    if (last?.isConnected && region.current?.contains(last)) last.focus()
    else focusEntry()
  }, [focusRequest, focusEntry])

  // Minimising leaves the focus on the bar rather than on a field that just disappeared.
  useEffect(() => {
    if (state.minimised) region.current?.querySelector<HTMLElement>('.quick-compose-restore')?.focus()
  }, [state.minimised])

  // The first report is the form's ready signal: Squire and To have taken the focus by then if
  // they were going to, so only a body they leave alone (plain text) is focused here.
  const ready = useRef(false)
  const onStatusChange = useCallback((status: { title: string; draftRef: DraftRef | null }) => {
    setTitle(status.title)
    trackDraft(status.draftRef)
    if (ready.current) return
    ready.current = true
    if (!region.current?.contains(document.activeElement)) focusEntry()
  }, [trackDraft, focusEntry])

  // The window closes once the page holds its content: a page composer's blocker may still refuse
  // the navigation, and Keep editing there must not cost the window its draft.
  const handedTo = useRef<ComposeState>(null)
  const location = useLocation()
  useEffect(() => {
    if (handedTo.current !== null && location.state === handedTo.current) close()
  }, [location.state, close])

  function expand(seed: ComposeSeed, dirty: boolean) {
    handedTo.current = { seed, backTo: state.origin, handedOver: { dirty } }
    void navigate('/mail/compose', { state: handedTo.current })
  }

  return (
    <div ref={region} role="region" aria-label={title}
      className={`quick-compose${state.minimised ? ' is-minimised' : ''}`}
      onFocus={e => {
        if (!(e.target as Element).closest('.quick-compose-header')) lastFocus.current = e.target
      }}>
      <ComposePreferencesGate onClose={close}>
        {preferences => (
          <Suspense fallback={<div className="mail-full-pane"><LoadingBlock /></div>}>
            <ComposeForm preferences={preferences} seed={state.seed} frame="window" onNotify={onNotify}
              onLeave={close} onExpand={expand} onMinimise={state.minimised ? restore : minimise}
              minimised={state.minimised} onStatusChange={onStatusChange} />
          </Suspense>
        )}
      </ComposePreferencesGate>
    </div>
  )
}
