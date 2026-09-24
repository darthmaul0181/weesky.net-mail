import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQueryClient } from '@tanstack/react-query'
import { api } from '../../../api.js'
import { mailKeys, useAliases } from '../../mail/queries'
import { useAccountId } from '../../../hooks/useAccountId'
import { useListLoadState } from '../../../hooks/useListLoadState'
import { useToasts } from '../../../hooks/useToasts'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { readStored, writeStored } from '../../../lib/safeStorage'
import Toasts from '../../../components/Toasts'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal'
import TrashIcon from '../../../icons/TrashIcon'
import AtSignIcon from '../../../icons/AtSignIcon'
import type { AccountDomain } from '../../../lib/accountIdentity'
import type { AliasInfo } from '../../mail/api/mailTypes'

interface PendingDelete { name: string; domain: string }

export default function AliasesPage() {
  const { t } = useTranslation('settings')
  const { toasts, addToast, removeToast, pauseToast, resumeToast } = useToasts()
  const queryClient = useQueryClient()
  const accountId = useAccountId()

  // The alias list is the authority the identity picker and the composer's From menu are drawn
  // from, and both cache for five minutes: a CRUD here has to drop them or they lie until then.
  const invalidateAliasCaches = useCallback(() => Promise.all([
    queryClient.invalidateQueries({ queryKey: mailKeys.aliases(accountId) }),
    queryClient.invalidateQueries({ queryKey: mailKeys.identities(accountId) }),
  ]), [queryClient, accountId])

  const [domains, setDomains] = useState<AccountDomain[]>([])
  const [selectedDomain, setSelectedDomain] = useState('')

  // The app's thirty-second default, as the admin lists: the readers' five minutes is theirs.
  const aliasesQuery = useAliases(true, { staleTime: 30_000 })
  const aliases = aliasesQuery.data ?? []
  // useListLoadState is the admin tabs' own predicate (useAdminLists.ts's useListLoad wraps it
  // with a toast). Aliases already announces a failure through its own role="alert" banner below,
  // so it uses the state directly rather than the toasting wrapper — one announcement, not two.
  const { firstLoad, failed: loadFailed } = useListLoadState([aliasesQuery])
  const [search, setSearch] = useState('')

  const [adding, setAdding] = useState(false)

  const [deletingKey, setDeletingKey] = useState<string | null>(null)
  const [pendingDelete, setPendingDelete] = useState<PendingDelete | null>(null)
  const [highlightedKey, setHighlightedKey] = useState<string | null>(null)
  const [alphaMode, setAlphaMode] = useState(() => readStored('alias_alpha_mode') === 'true')

  function handleAlphaModeChange(value: boolean) {
    setAlphaMode(value)
    writeStored('alias_alpha_mode', String(value))
  }

  const scrollRef = useRef<HTMLDivElement>(null)
  const groupRefs = useRef<Record<string, HTMLDivElement | null>>({})
  // A confirmed delete takes the tile's own button with it, so focus goes back to the page's name.
  const headingRef = useRef<HTMLHeadingElement>(null)
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

  const visibleAliases = aliases
    .filter(a => !selectedDomain || a.domain === selectedDomain)
    .filter(a => !search || `${a.name}@${a.domain}`.includes(search.toLowerCase()))
    .sort((a, b) => a.name.localeCompare(b.name))

  const grouped: Array<[string, AliasInfo[]]> = []
  const groupMap: Record<string, AliasInfo[]> = {}
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

  async function handleDelete(name: string, domain: string) {
    const key = `${name}@${domain}`
    setDeletingKey(key)
    try {
      await api.deleteAlias(name, domain)
      queryClient.setQueryData<AliasInfo[]>(mailKeys.aliases(accountId), prev =>
        (prev ?? []).filter(a => !(a.name === name && a.domain === domain)))
      // Fire-and-forget: busts the identity picker/From-menu caches without delaying the toast.
      void invalidateAliasCaches()
      addToast(t('aliases.deleted', { alias: key }))
    } catch (err) {
      addToast(apiErrorMessage(err, t('aliases.deleteFailed')), 'error')
      // The optimistic removal above never ran; reload from the server rather than trust stale data.
      void queryClient.refetchQueries({ queryKey: mailKeys.aliases(accountId) })
    } finally {
      setDeletingKey(null)
      setPendingDelete(null)
    }
  }

  async function handleAdd() {
    setAdding(true)
    try {
      await api.createAlias(search, selectedDomain)
      const key = `${search}@${selectedDomain}`
      const refreshed = invalidateAliasCaches()
      addToast(t('aliases.added', { alias: key }))
      setSearch('')
      await refreshed
      setHighlightedKey(key)
    } catch (err) {
      addToast(apiErrorMessage(err, t('aliases.createFailed')), 'error')
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

  function scrollToLetter(letter: string) {
    const el = groupRefs.current[letter]
    const container = scrollRef.current
    if (!el || !container) return
    container.scrollTop += el.getBoundingClientRect().top - container.getBoundingClientRect().top
  }

  return (
    <div className="settings-page">
      <div className="settings-page-header">
        <h1 className="settings-page-title" ref={headingRef} tabIndex={-1}>
          <AtSignIcon size={17} />{t('nav.aliases')}
        </h1>
      </div>

      <div className="domain-toolbar">
        {domains.length > 1 && (
          <>
            <label htmlFor="domain-select" className="domain-label">{t('aliases.domain')}</label>
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
          placeholder={t('aliases.searchPlaceholder')}
          value={search}
          onChange={e => {
            const val = e.target.value
            if (val.length > 30 && search.length <= 30) {
              addToast(t('aliases.tooLong'), 'error')
            }
            setSearch(val)
          }}
          onKeyDown={e => {
            if (e.key === 'Enter' && !adding && selectedDomain && search.trim() && search.length <= 30) {
              void handleAdd()
            }
          }}
        />
        <button
          className="btn btn-add"
          onClick={() => void handleAdd()}
          disabled={adding || !selectedDomain || !search.trim() || search.length > 30}
        >
          {adding ? <span className="spinner" /> : t('aliases.create')}
        </button>
        <label className="toggle-row alias-alpha-toggle" style={{ marginLeft: 'auto' }}>
          <span className="toggle-label">{t('aliases.alphabetical')}</span>
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

      {loadFailed && <div className="alert alert-error" role="alert">{t('aliases.loadFailed')}</div>}

      {firstLoad ? (
        <div className="loading-center">
          <span className="spinner" />
        </div>
      ) : visibleAliases.length === 0 ? (
        <div className="alias-empty-grid">{t('aliases.empty')}</div>
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
                          title={t('actions.delete', { ns: 'common' })}
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
                  title={t('actions.delete', { ns: 'common' })}
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
          onConfirm={() => void handleDelete(pendingDelete.name, pendingDelete.domain)}
          onClose={() => setPendingDelete(null)}
          loading={deletingKey === `${pendingDelete.name}@${pendingDelete.domain}`}
          returnFocusRef={headingRef}
        />
      )}

      <Toasts toasts={toasts} onRemove={removeToast} onPause={pauseToast} onResume={resumeToast} />
    </div>
  )
}
