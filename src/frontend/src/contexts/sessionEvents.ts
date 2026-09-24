type SessionEndListener = () => void

const listeners = new Set<SessionEndListener>()

/** Runs `listener` whenever a session ends, by sign-out or by a 401; the returned function unsubscribes. */
export function onSessionEnd(listener: SessionEndListener): () => void {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

export function notifySessionEnd(): void {
  listeners.forEach(listener => {
    try { listener() } catch (error) { console.error(error) }
  })
}
