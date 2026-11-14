import { createContext, useCallback, useContext, useMemo, useRef, useState, type ReactNode } from 'react'
import { useAccountId } from '../../../../hooks/useAccountId'
import type { ComposeSeed, DraftRef } from '../composeSeed'
import QuickComposeWindow from './QuickComposeWindow'

export interface QuickComposeState {
  seed: ComposeSeed | null
  origin: string          // '/mail?folder=…' or a contacts path
  minimised: boolean
  instance: number        // bumped on every open → key of ComposeForm
}

export interface QuickComposeApi {
  state: QuickComposeState | null
  open(seed: ComposeSeed | null, origin: string): void   // replaces; caller has already confirmed
  restore(): void                                        // un-minimise and focus
  close(): void
  showsDraft(ref: DraftRef): boolean
}

/** Outside the shell (a module mounted alone, in a test) there is no window and none can open. */
export const QuickComposeContext = createContext<QuickComposeApi>({
  state: null, open: () => {}, restore: () => {}, close: () => {}, showsDraft: () => false,
})

export function useQuickCompose(): QuickComposeApi {
  return useContext(QuickComposeContext)
}

/** Holds the one quick compose window and draws it after `children`: mounted by AppShell above the
    module outlet, so the window outlives every module change. */
export function QuickComposeProvider({ children }: { children: ReactNode }) {
  const accountId = useAccountId()
  const [state, setState] = useState<QuickComposeState | null>(null)
  const [focusRequest, setFocusRequest] = useState(0)
  const instances = useRef(0)
  // A ref, not state: it is only ever read on a click, and a save must not re-render the shell.
  const draftRef = useRef<DraftRef | null>(null)

  const open = useCallback((seed: ComposeSeed | null, origin: string) => {
    draftRef.current = seed?.draftRef ?? null
    setState({ seed, origin, minimised: false, instance: ++instances.current })
  }, [])
  const restore = useCallback(() => {
    setState(prev => prev && { ...prev, minimised: false })
    setFocusRequest(n => n + 1)
  }, [])
  const minimise = useCallback(() => setState(prev => prev && { ...prev, minimised: true }), [])
  const close = useCallback(() => { draftRef.current = null; setState(null) }, [])
  const showsDraft = useCallback((ref: DraftRef) => state !== null && draftRef.current !== null
    && draftRef.current.folderPath === ref.folderPath && draftRef.current.uid === ref.uid, [state])
  const trackDraft = useCallback((ref: DraftRef | null) => { draftRef.current = ref }, [])

  // The draft belongs to the mailbox it was opened in. A dirty one was asked through confirmLeave
  // before the switch; whatever the answer let through, the window goes with the mailbox. Reset
  // during render, so no frame ever draws the old mailbox's draft under the new one.
  const [account, setAccount] = useState(accountId)
  if (account !== accountId) {
    setAccount(accountId)
    setState(null)
  }

  const api = useMemo(
    () => ({ state, open, restore, close, showsDraft }), [state, open, restore, close, showsDraft])

  return (
    <QuickComposeContext.Provider value={api}>
      {children}
      <QuickComposeWindow state={state} focusRequest={focusRequest}
        actions={{ restore, minimise, close, trackDraft }} />
    </QuickComposeContext.Provider>
  )
}
