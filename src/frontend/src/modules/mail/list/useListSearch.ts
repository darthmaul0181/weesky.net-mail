import { useMemo, useState } from 'react'
import type { MailMessageSummary } from '../api/mailTypes'
import { useSearchMessages } from '../queries'
import { criteriaFromForm, isEmptyCriteria } from './searchCriteria'
import type { AdvancedForm, SearchCriteria } from './searchCriteria'
import type { ThreadGroup } from './threading'

/** The quick bar, the advanced dialog and the starred filter, all writing the one lifted search. */
export function useListSearch(
  folderPath: string | null,
  search: SearchCriteria | null,
  onSearchChange: (criteria: SearchCriteria | null) => void,
  pageSize: number,
) {
  const [searchOpen, setSearchOpen] = useState(false)
  const [advanced, setAdvanced] = useState<{ subject: string } | null>(null)
  const [searchPage, setSearchPage] = useState(0)

  // Render-time resets, the useMessageList pattern: no effect-lag page or stale-bar frame.
  const [shownSearch, setShownSearch] = useState(search)
  if (search !== shownSearch) { setShownSearch(search); setSearchPage(0) }
  const [shownFolder, setShownFolder] = useState(folderPath)
  if (folderPath !== shownFolder) { setShownFolder(folderPath); setSearchOpen(false); setAdvanced(null) }

  const searchQuery = useSearchMessages(search, searchPage, pageSize)
  const searching = search !== null
  const crossFolder = searching && search.allFolders
  const starred = searching && search.flagged === true

  // Memoised rather than rebuilt: a fresh `groups` would rebuild `loadedUids` and `selectedUids`
  // with it on every render. No row is ever handed `members` here — a hit is its own group.
  const searchView = useMemo(() => {
    const results = (searchQuery.data?.results ?? []) as MailMessageSummary[]
    const found = searchQuery.data?.total ?? 0
    return {
      // Search hits are never threaded: each result is its own one-member group.
      groups: results.map((result): ThreadGroup => ({ key: result.uid, messages: [result] })),
      messages: results,
      total: found,
      isLoading: searchQuery.isLoading,
      isError: searchQuery.isError,
      paging: {
        page: searchPage,
        lastPage: pageSize > 0 ? Math.max(0, Math.ceil(found / pageSize) - 1) : 0,
        onSelect: setSearchPage,
      },
      streaming: null,
      // Results are never threaded, whatever the grouping setting says: one hit, one row.
      rowTotal: found,
    }
  }, [searchQuery.data, searchQuery.isLoading, searchQuery.isError, searchPage, pageSize])

  function toggleSearch() {
    if (searchOpen) closeSearch()
    else setSearchOpen(true)
  }

  function closeSearch() {
    setSearchOpen(false)
    setAdvanced(null)
    onSearchChange(null)
  }

  function quickSearch(text: string) {
    if (folderPath) onSearchChange({ folderPath, allFolders: false, quick: text })
  }

  /** The star writes the same `flagged` criterion the advanced form's checkbox does, on the
      search already running when there is one — so the two are one state, not two filters. */
  function toggleStarred() {
    if (search?.flagged) {
      const rest: SearchCriteria = { ...search }
      delete rest.flagged
      onSearchChange(isEmptyCriteria(rest) ? null : rest)
      return
    }
    if (search) { onSearchChange({ ...search, flagged: true }); return }
    if (folderPath) onSearchChange({ folderPath, allFolders: false, flagged: true })
  }

  function advancedSearch(form: AdvancedForm) {
    setAdvanced(null)
    if (folderPath) onSearchChange(criteriaFromForm(folderPath, form))
  }

  return {
    searchOpen, advanced, setAdvanced, searchPage, searchTotal: searchQuery.data?.total ?? null,
    searching, crossFolder, starred, searchView,
    toggleSearch, closeSearch, quickSearch, toggleStarred, advancedSearch,
  }
}
