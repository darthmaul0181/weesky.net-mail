import { useState } from 'react'
import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import { useAuth } from '../../../contexts/AuthContext'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import StarIcon from '../../../icons/StarIcon'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import type { SendingIdentity } from '../../mail/api/mailTypes'
import { useIdentities, useReplaceIdentities } from '../../mail/queries'
import AddIdentityDialog from './AddIdentityDialog'
import {
  applyAddition, applyDefault, applyLabel, applyRemoval, sortIdentities, toRows,
  MAX_DISPLAY_NAME_LENGTH,
} from './identityRows'

/**
 * The curated From list. Every action PUTs the whole set, so each one has to build on the list
 * the one before it produced — the invalidation's refetch has not landed yet, and a payload
 * computed from the server snapshot would silently revert its predecessor. `edited` is that
 * list; it is dropped the moment the server speaks again, and at once on a refusal, so a
 * refused PUT always ends with the UI showing server state rather than an optimistic lie.
 */
export default function IdentitiesPage() {
  const { identity } = useAuth()
  const { data: identities, isLoading, isError } = useIdentities()
  const replace = useReplaceIdentities()
  const { toasts, addToast, removeToast } = useToasts()
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<string | null>(null)
  const [draft, setDraft] = useState('')
  const [edited, setEdited] = useState<SendingIdentity[] | null>(null)
  const [served, setServed] = useState(identities)

  // Fresh server data wins — unless a save is still in flight, whose optimistic list is the one
  // the next action has to chain onto.
  if (identities !== served) {
    setServed(identities)
    if (!replace.isPending) setEdited(null)
  }

  const base = edited ?? identities
  // The primary's name is the live account FullName (owned by the Account tab), never the
  // possibly-stale label the identities query resolved when it last fetched.
  const shown = base && identity
    ? base.map(i => (i.isPrimary ? { ...i, displayName: identity.displayName } : i))
    : base

  function save(next: SendingIdentity[]) {
    // Sorted like the server sorts, so the list does not reshuffle when the refetch lands.
    const sorted = sortIdentities(next)
    setEdited(sorted)
    replace.mutate(toRows(sorted), {
      onError: (error: Error) => {
        setEdited(null)
        addToast(error.message || 'Could not save your identities', 'error')
      },
    })
  }

  function commitLabel(address: string) {
    setEditing(null)
    if (shown) save(applyLabel(shown, address, draft))
  }

  return (
    <div className="settings-page">
      <h1>Identities</h1>
      <p className="identities-hint">
        The addresses you can write from, each with its own name. Add one from your aliases —
        removing an identity never touches the alias itself.
      </p>

      {isLoading && <p>Loading…</p>}
      {/* Only when there is nothing to show: a failed background refetch must not blank a list
          that is already on screen and still perfectly usable. */}
      {!isLoading && !shown && <p>Could not load your identities.</p>}
      {!isLoading && shown && (
        <>
          {/* Usable but possibly out of date — the page edits this list, and a save built on
              stale rows comes back refused. */}
          {isError && <p className="settings-note">Could not refresh this list — it may be out of date.</p>}
          <ul className="identity-list">
            {shown.map(i => (
              <li key={i.address} className={`identity-row${i.stale ? ' is-stale' : ''}`}>
                {i.stale && <span className="identity-star" aria-hidden="true" />}
                {/* Not a disabled button: a disabled control is unfocusable, so the status it
                    carries would be unreachable by keyboard while still costing a tab stop. */}
                {!i.stale && i.isDefault && (
                  <span className="identity-star">
                    <StarIcon size={18} filled />
                    <span className="visually-hidden">{i.address} is the default</span>
                  </span>
                )}
                {!i.stale && !i.isDefault && (
                  <button
                    type="button" className="identity-star"
                    aria-label={`Make ${i.address} the default`}
                    onClick={() => save(applyDefault(shown, i.address))}
                  >
                    <StarIcon size={18} />
                  </button>
                )}
                {editing === i.address ? (
                  <input
                    autoFocus type="text" className="identity-name-input" value={draft}
                    maxLength={MAX_DISPLAY_NAME_LENGTH}
                    aria-label={`Display name for ${i.address}`}
                    onChange={e => setDraft(e.target.value)}
                    onBlur={() => commitLabel(i.address)}
                    onKeyDown={e => {
                      if (e.key === 'Enter') commitLabel(i.address)
                      if (e.key === 'Escape') setEditing(null)
                    }}
                  />
                ) : (
                  <span className="identity-name">{i.displayName}</span>
                )}
                <span className="identity-address">{i.address}</span>
                {i.isPrimary && <span className="identity-tag">primary</span>}
                {i.stale && <span className="identity-tag">unavailable</span>}
                {/* The primary's display name is the account FullName, editable only from the
                    Account tab — so it carries no rename here. */}
                {!i.stale && !i.isPrimary && (
                  <button
                    type="button" className="identity-action" aria-label={`Rename ${i.address}`}
                    onClick={() => { setEditing(i.address); setDraft(i.displayName) }}
                  >
                    <PencilIcon size={15} />
                  </button>
                )}
                {!i.isPrimary && (
                  <button
                    type="button" className="identity-action is-danger" aria-label={`Remove ${i.address}`}
                    title="Removes the identity only — the alias itself is kept"
                    onClick={() => save(applyRemoval(shown, i.address))}
                  >
                    <TrashIcon size={15} />
                  </button>
                )}
              </li>
            ))}
          </ul>
          <button type="button" className="btn btn-ghost" onClick={() => setAdding(true)}>+ Add identity</button>
          {adding && (
            <AddIdentityDialog
              taken={shown.map(i => i.address)}
              defaultName={identity?.displayName ?? ''}
              onClose={() => setAdding(false)}
              onAdd={(address, name) => { save(applyAddition(shown, address, name)); setAdding(false) }}
            />
          )}
        </>
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
