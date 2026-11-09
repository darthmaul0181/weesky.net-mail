import { useEffect } from 'react'
import { useBlocker, type Blocker, type BlockerFunction } from 'react-router'

interface Props {
  shouldBlock: BlockerFunction
  onBlocker: (blocker: Blocker) => void
}

// A router consults only the last blocker registered, so this is rendered by the page composer
// alone: a window's always-false one, registered later, would silence the page's question.
export default function LeaveBlocker({ shouldBlock, onBlocker }: Props) {
  const blocker = useBlocker(shouldBlock)
  useEffect(() => { onBlocker(blocker) }, [blocker, onBlocker])
  return null
}
