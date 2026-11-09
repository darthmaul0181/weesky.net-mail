import { useCallback, useMemo } from 'react'
import { useMatch, useNavigate } from 'react-router'
import { composeWindowOf, usePreferences } from '../../../../hooks/usePreferences'
import { useViewport } from '../../../../hooks/useViewport'
import { confirmLeave } from '../../../../lib/leaveGuard'
import type { ComposeSeed, DraftRef } from '../composeSeed'
import { backTargetOf } from '../composeRoute'
import { useQuickCompose } from './QuickComposeContext'

export interface ComposerRequest { seed: ComposeSeed | null; from?: string; backTo?: string }

export interface Composer {
  /** 'window' iff preference is window, viewport is not phone, and /mail/compose is not mounted. */
  target: 'page' | 'window'
  /** Call before staging anything. Page target: resolves true. Window target: false when the
      same draft is already in the window (restored + focused) or the user kept editing. */
  prepare: (draft?: DraftRef) => Promise<boolean>
  /** Page: navigate('/mail/compose', { state: { from, seed, backTo } }). Window: quick.open(seed, backTo ?? mail origin of from). */
  open: (request: ComposerRequest) => void
  /** Restores an existing window instead of replacing it; otherwise open({ seed: null, from }). */
  newMessage: (from?: string) => void
}

/** Where every compose entry point sends the user: the page composer, or the quick window the
    account chose. A preference not loaded yet counts as the page. */
export function useComposer(): Composer {
  const navigate = useNavigate()
  const quick = useQuickCompose()
  const { data: preferences } = usePreferences()
  const phone = useViewport() === 'phone'
  const pageMounted = useMatch('/mail/compose') !== null
  const target = preferences && composeWindowOf(preferences) === 'window' && !phone && !pageMounted
    ? 'window' : 'page'
  const windowOpen = target === 'window' && quick.state !== null

  // The window's own leave guard asks when it is dirty and answers true at once when it is clean.
  const prepare = useCallback(async (draft?: DraftRef) => {
    if (!windowOpen) return true
    if (draft && quick.showsDraft(draft)) { quick.restore(); return false }
    return confirmLeave()
  }, [windowOpen, quick])

  const open = useCallback(({ seed, from, backTo }: ComposerRequest) => {
    if (target === 'window') quick.open(seed, backTargetOf({ from, backTo }))
    else void navigate('/mail/compose', { state: { from, seed: seed ?? undefined, backTo } })
  }, [target, quick, navigate])

  const newMessage = useCallback((from?: string) => {
    if (windowOpen) quick.restore()
    else open({ seed: null, from })
  }, [windowOpen, quick, open])

  return useMemo(() => ({ target, prepare, open, newMessage }), [target, prepare, open, newMessage])
}
