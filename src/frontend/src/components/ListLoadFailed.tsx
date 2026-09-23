import type { ReactNode } from 'react'

/** What a page draws in place of a list it could not load: saying so, where "(0)" would claim an
    empty list nobody has seen. Not live: the error toast is the one announcement. */
export default function ListLoadFailed({ children }: { children: ReactNode }) {
  return <p className="settings-note">{children}</p>
}
