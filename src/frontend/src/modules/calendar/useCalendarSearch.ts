import { useCallback, useEffect, useState } from 'react'
import { useSearch } from './queries'

/** How long the box waits before asking, and how short a question it refuses to ask at all. */
const SEARCH_DEBOUNCE_MS = 300
export const SEARCH_MIN = 2

export function useCalendarSearch() {
  const [query, setQuery] = useState('')
  const [asked, setAsked] = useState('')

  // 300ms after the last keystroke, or the moment Enter is pressed: a request per letter would
  // spend a round trip on every prefix of a word nobody has finished typing.
  useEffect(() => {
    const id = setTimeout(() => setAsked(query), SEARCH_DEBOUNCE_MS)
    return () => clearTimeout(id)
  }, [query])

  const typed = query.trim()
  const term = asked.trim()
  const searchQuery = useSearch(term.length >= SEARCH_MIN ? term : '')
  const clearSearch = useCallback(() => {
    setQuery('')
    setAsked('')
  }, [])
  const commitQuery = () => setAsked(query)

  return { query, setQuery, commitQuery, typed, term, searchQuery, clearSearch }
}
