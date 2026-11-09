import { useMemo } from 'react'
import { useLocation, useNavigate } from 'react-router'
import type { AddToast } from '../../../hooks/useToasts'
import { mailtoSeedFrom } from './mailtoSeed'
import { backTargetOf, type ComposeState } from './composeRoute'
import ComposeForm from './ComposeForm'
import ComposePreferencesGate from './ComposePreferencesGate'

interface Props {
  onNotify: AddToast
}

// Replaces list+reader inside the mail module; `useLeaveGuard` owns every exit while something is
// unsaved. A reply/forward/draft arrives as `location.state.seed`, a new message carries none.
export default function ComposeView(props: Props) {
  const navigate = useNavigate()
  const location = useLocation()
  const state = location.state as ComposeState
  const backTarget = backTargetOf(state)
  // A mailto: arrives through the URL, not through the history state: the operating system opens
  // a cold address, with no React navigation behind it.
  const seed = useMemo(
    () => state?.seed ?? mailtoSeedFrom(location.search), [state?.seed, location.search])
  return (
    <ComposePreferencesGate onClose={() => void navigate(backTarget)}>
      {preferences => (
        // A hand-over lands on a page that may already be open: a key of its own remounts the form.
        <ComposeForm key={state?.handedOver ? location.key : 'page'} {...props} preferences={preferences}
          seed={seed} frame="page"
          handedOver={state?.handedOver} onLeave={() => void navigate(backTarget)} />
      )}
    </ComposePreferencesGate>
  )
}
