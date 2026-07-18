import { useState, useEffect, useCallback, useRef } from 'react'
import { api } from '../../../api.js'
import { useToasts } from '../../../hooks/useToasts.js'
import Toasts from '../../../components/Toasts.jsx'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import TrashIcon from '../../../icons/TrashIcon.jsx'

export default function AliasesPage() {
  const { toasts, addToast, removeToast } = useToasts()

  const [domains, setDomains] = useState([])
  const [selectedDomain, setSelectedDomain] = useState('')

  const [aliases, setAliases] = useState([])
  const [loadingList, setLoadingList] = useState(true)
  const [listError, setListError] = useState(null)
  const [search, setSearch] = useState('')

  const [adding, setAdding] = useState(false)

  const [deletingKey, setDeletingKey] = useState(null)
  const [pendingDelete, setPendingDelete] = useState(null)
  const [highlightedKey, setHighlightedKey] = useState(null)
  const [alphaMode, setAlphaMode] = useState(() => localStorage.getItem('alias_alpha_mode') === 'true')

  function handleAlphaModeChange(value) {
    setAlphaMode(value)
    localStorage.setItem('alias_alpha_mode', String(value))
  }

  const scrollRef = useRef(null)
  const groupRefs = useRef({})
  const [activeLetter, setActiveLetter] = useState('')

  useEffect(() => {
    api.getAccount().then(data => {
      const list = data?.domains ?? []
      setDomains(list)

      const primaryDomain = list.find(d => d.id === data.mailbox)
      const defaultDomain = primaryDomain ?? list[0]
      const domainName = defaultDomain?.name ?? ''

      if (domainName) setSelectedDomain(domainName)
    }).catch(() => {})
  }, [])

  const fetchAliases = useCallback(async () => {
    setLoadingList(true)
    setListError(null)
    try {
      const data = await api.getAliases()
      setAliases(data ?? [])
    } catch {
      setListError('Failed to load aliases.')
    } finally {
      setLoadingList(false)
    }
  }, [])

  useEffect(() => { fetchAliases() }, [fetchAliases])

  const visibleAliases = aliases
    .filter(a => !selectedDomain || a.domain === selectedDomain)
    .filter(a => !search || `${a.name}@${a.domain}`.includes(search.toLowerCase()))
    .sort((a, b) => a.name.localeCompare(b.name))

  const grouped = []
  const groupMap = {}
  for (const a of visibleAliases) {
    const letter = a.name[0]?.toUpperCase() ?? '#'
    if (!groupMap[letter]) {
      groupMap[letter] = []
      grouped.push([letter, groupMap[letter]])
    }
    groupMap[letter].push(a)
  }
  const availableLetters = grouped.map(([l]) => l)
  const effectiveActiveLetter = availableLetters.includes(activeLetter)
    ? activeLetter
    : (availableLetters[0] ?? '')

  async function handleDelete(name, domain) {
    const key = `${name}@${domain}`
    setDeletingKey(key)
    try {
      await api.deleteAlias(name, domain)
      setAliases(prev => prev.filter(a => !(a.name === name && a.domain === domain)))
      addToast(`${key} deleted`)
    } catch {
      fetchAliases()
    } finally {
      setDeletingKey(null)
    }
  }

  async function handleAdd() {
    setAdding(true)
    try {
      await api.createAlias(search, selectedDomain)
      const key = `${search}@${selectedDomain}`
      addToast(`${key} added`)
      setSearch('')
      await fetchAliases()
      setHighlightedKey(key)
    } catch (err) {
      addToast(err.message || 'Failed to create alias.', 'error')
    } finally {
      setAdding(false)
    }
  }

  function handleScroll() {
    const container = scrollRef.current
    if (!container) return
    const containerTop = container.getBoundingClientRect().top
    let current = availableLetters[0] ?? ''
    for (const letter of availableLetters) {
      const el = groupRefs.current[letter]
      if (el && el.getBoundingClientRect().top - containerTop <= 8) current = letter
    }
    if (current !== activeLetter) setActiveLetter(current)
  }

  function scrollToLetter(letter) {
    const el = groupRefs.current[letter]
    const container = scrollRef.current
    if (!el || !container) return
    container.scrollTop += el.getBoundingClientRect().top - container.getBoundingClientRect().top
  }

  return (
    <div className="settings-page">
      <div className="settings-page-header">
        <span className="settings-page-title">Aliases</span>
      </div>

      <div className="domain-toolbar">
        {domains.length > 1 && (
          <>
            <label htmlFor="domain-select" className="domain-label">Domain</label>
            <select
              id="domain-select"
              className="domain-select"
              value={selectedDomain}
              onChange={e => setSelectedDomain(e.target.value)}
            >
              {domains.map(d => (
                <option key={d.id} value={d.name}>{d.name}</option>
              ))}
            </select>
          </>
        )}
        <input
          className={`search-input${search.length > 30 ? ' is-error' : ''}`}
          type="search"
          placeholder="Search or create…"
          value={search}
          onChange={e => {
            const val = e.target.value
            if (val.length > 30 && search.length <= 30) {
              addToast('An alias cannot exceed 30 characters', 'error')
            }
            setSearch(val)
          }}
          onKeyDown={e => {
            if (e.key === 'Enter' && !adding && selectedDomain && search.trim() && search.length <= 30) {
              handleAdd()
            }
          }}
        />
        <button
          className="btn btn-add"
          onClick={handleAdd}
          disabled={adding || !selectedDomain || !search.trim() || search.length > 30}
        >
          {adding ? <span className="spinner" /> : 'Create alias'}
        </button>
        <label className="toggle-row alias-alpha-toggle" style={{ marginLeft: 'auto' }}>
          <span className="toggle-label">Alphabetical</span>
          <span className="toggle-switch">
            <input
              type="checkbox"
              checked={alphaMode}
              onChange={e => handleAlphaModeChange(e.target.checked)}
            />
            <span className="toggle-track" />
          </span>
        </label>
      </div>

      {listError && <div className="alert alert-error">{listError}</div>}

      {loadingList ? (
        <div className="loading-center">
          <span className="spinner" />
        </div>
      ) : visibleAliases.length === 0 ? (
        <div className="alias-empty-grid">No aliases for this domain.</div>
      ) : alphaMode ? (
        <div className="alias-view-wrapper">
          <div className="alias-scroll-area" ref={scrollRef} onScroll={handleScroll}>
            {grouped.map(([letter, groupAliases]) => (
              <div key={letter} className="alias-group">
                <div
                  className="alias-group-header"
                  ref={el => { groupRefs.current[letter] = el }}
                >
                  <span className="alias-group-letter">{letter}</span>
                  <div className="alias-group-divider" />
                </div>
                <div className="alias-grid">
                  {groupAliases.map(a => {
                    const key = `${a.name}@${a.domain}`
                    const isNew = highlightedKey === key
                    return (
                      <div
                        className={isNew ? 'alias-tile alias-tile-new' : 'alias-tile'}
                        key={key}
                        onAnimationEnd={isNew ? () => setHighlightedKey(null) : undefined}
                      >
                        <span className="alias-tile-name">{a.name}</span>
                        <span className="alias-tile-domain">@{a.domain}</span>
                        <button
                          className="alias-tile-delete"
                          onClick={() => setPendingDelete({ name: a.name, domain: a.domain })}
                          title="Delete"
                        >
                          <TrashIcon />
                        </button>
                      </div>
                    )
                  })}
                </div>
              </div>
            ))}
          </div>
          <div className="alpha-nav">
            {availableLetters.map(letter => (
              <button
                key={letter}
                className={`alpha-nav-letter${effectiveActiveLetter === letter ? ' is-active' : ''}`}
                onClick={() => scrollToLetter(letter)}
              >
                {letter}
              </button>
            ))}
          </div>
        </div>
      ) : (
        <div className="alias-grid">
          {visibleAliases.map(a => {
            const key = `${a.name}@${a.domain}`
            const isNew = highlightedKey === key
            return (
              <div
                className={isNew ? 'alias-tile alias-tile-new' : 'alias-tile'}
                key={key}
                onAnimationEnd={isNew ? () => setHighlightedKey(null) : undefined}
              >
                <span className="alias-tile-name">{a.name}</span>
                <span className="alias-tile-domain">@{a.domain}</span>
                <button
                  className="alias-tile-delete"
                  onClick={() => setPendingDelete({ name: a.name, domain: a.domain })}
                  title="Delete"
                >
                  <TrashIcon />
                </button>
              </div>
            )
          })}
        </div>
      )}

      {pendingDelete && (
        <DeleteConfirmModal
          entityLabel={`${pendingDelete.name}@${pendingDelete.domain}`}
          onConfirm={async () => {
            await handleDelete(pendingDelete.name, pendingDelete.domain)
            setPendingDelete(null)
          }}
          onClose={() => setPendingDelete(null)}
          loading={deletingKey === `${pendingDelete.name}@${pendingDelete.domain}`}
        />
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
