import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import MenuSelect from '../../../components/MenuSelect'
import { useTranslation } from 'react-i18next'
import { useQueryClient } from '@tanstack/react-query'
import { api } from '../../../api.js'
import { mailKeys, useAliases } from '../../mail/queries'
import { useAccountId } from '../../../hooks/useAccountId'
import { useListLoadState } from '../../../hooks/useListLoadState'
import { useToasts } from '../../../hooks/useToasts'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { collator } from '../../../lib/intl'
import Toasts from '../../../components/Toasts'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal'
import AtSignIcon from '../../../icons/AtSignIcon'
import SearchIcon from '../../../icons/SearchIcon'
import type { AccountDomain } from '../../../lib/accountIdentity'
import type { AliasInfo } from '../../mail/api/mailTypes'
import AliasIndex from './AliasIndex'
import { buildIndex } from './aliasFamilies'

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
  // useListLoadState is the admin tabs' own predicate (useAdminLists.ts's useListLoad wraps it
  // with a toast). Aliases already announces a failure through its own role="alert" banner below,
  // so it uses the state directly rather than the toasting wrapper — one announcement, not two.
  const { firstLoad, failed: loadFailed } = useListLoadState([aliasesQuery])
  const [search, setSearch] = useState('')

  const [adding, setAdding] = useState(false)

  const [deletingKey, setDeletingKey] = useState<string | null>(null)
  const [pendingDelete, setPendingDelete] = useState<PendingDelete | null>(null)
  const [highlightedKey, setHighlightedKey] = useState<string | null>(null)

  // A confirmed delete takes the name's own button with it, so focus goes back to the page's name.
  const headingRef = useRef<HTMLHeadingElement>(null)

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

  const query = search.toLowerCase()
  const inDomain = useMemo(() => (aliasesQuery.data ?? [])
    .filter(a => !selectedDomain || a.domain === selectedDomain), [aliasesQuery.data, selectedDomain])
  const matching = useMemo(() => inDomain
    .filter(a => !query || `${a.name}@${a.domain}`.toLowerCase().includes(query)), [inDomain, query])
  const letters = useMemo(() => buildIndex(matching,
    (x, y) => collator({ sensitivity: 'base' }).compare(x, y)), [matching])

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
      const key = `${search}@${selectedDomain}`.toLowerCase()
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

  async function handleCopy(address: string) {
    try {
      await navigator.clipboard.writeText(address)
      addToast(t('aliases.copied', { alias: address }))
    } catch {
      addToast(t('aliases.copyFailed'), 'error')
    }
  }

  return (
    <div className="settings-page">
      <div className="settings-page-header alias-page-header">
        <div className="alias-page-heading">
          <h1 className="settings-page-title" ref={headingRef} tabIndex={-1}>
            <AtSignIcon size={17} />{t('nav.aliases')}
          </h1>
          <p className="alias-page-summary">{query
            ? t('aliases.summaryFiltered', { total: inDomain.length, shown: matching.length })
            : t('aliases.summary', { count: inDomain.length })}</p>
        </div>
        {domains.length > 1 && (
          <MenuSelect ariaLabel={t('aliases.domain')} className="alias-domain-select" align="right"
            value={selectedDomain} onChange={setSelectedDomain}
            options={domains.map(d => ({ value: d.name, label: `@${d.name}` }))} />
        )}
      </div>

      <div className={`alias-composer${domains.length > 1 ? ' has-picker' : ''}${search.length > 30 ? ' is-error' : ''}`}>
        <SearchIcon size={20} />
        <input
          type="search"
          aria-label={t('aliases.searchLabel')}
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
        {selectedDomain && <span className="alias-composer-domain">@{selectedDomain}</span>}
        <button
          className="btn btn-primary btn-auto"
          onClick={() => void handleAdd()}
          disabled={adding || !selectedDomain || !search.trim() || search.length > 30}
        >
          {adding ? <span className="spinner" /> : t('aliases.create')}
        </button>
      </div>

      {loadFailed && <div className="alert alert-error" role="alert">{t('aliases.loadFailed')}</div>}

      {firstLoad ? (
        <div className="loading-center">
          <span className="spinner" />
        </div>
      ) : letters.length === 0 ? (
        <div className="alias-empty-grid">{t('aliases.empty')}</div>
      ) : (
        <AliasIndex letters={letters} highlightedKey={highlightedKey}
          onHighlightEnd={() => setHighlightedKey(null)}
          onCopy={address => void handleCopy(address)}
          onDelete={alias => setPendingDelete({ name: alias.name, domain: alias.domain })} />
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
