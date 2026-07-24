import { useState } from 'react'
import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import { useAuth } from '../../../contexts/AuthContext'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import PersonPlusIcon from '../../../icons/PersonPlusIcon.jsx'
import StarIcon from '../../../icons/StarIcon'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import type { SendingIdentity } from '../../mail/api/mailTypes'
import { useIdentities, useReplaceIdentities } from '../../mail/queries'
import IdentityDialog from './IdentityDialog'
import { applyAddition, applyDefault, applyLabel, applyRemoval, sortIdentities, toRows } from './identityRows'

/**
 * The curated From list, styled like the Administration tiles. Every action PUTs the whole set, so
 * each one builds on the list the one before it produced — the invalidation's refetch has not landed
 * yet, and a payload computed from the server snapshot would silently revert its predecessor.
 * `edited` is that list; it is dropped the moment the server speaks again, and at once on a refusal.
 */
export default function IdentitiesPage() {
  const { identity } = useAuth()
  const { data: identities, isLoading, isError } = useIdentities()
  const replace = useReplaceIdentities()
  const { toasts, addToast, removeToast } = useToasts()
  const [adding, setAdding] = useState(false)
  const [editing, setEditing] = useState<SendingIdentity | null>(null)
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
    setEdited(next)
    replace.mutate(toRows(next), {
      onError: (error: Error) => {
        setEdited(null)
        addToast(error.message || 'Could not save your identities', 'error')
      },
    })
  }

  const tiles = shown ? sortIdentities(shown) : []

  return (
    <div className="settings-page">
      <h1>Identities</h1>
      <p className="identities-hint">
        The addresses you can write from, each with its own name. Removing an identity never touches
        the alias itself; your primary name comes from the Account tab.
      </p>

      {isLoading && <p>Loading…</p>}
      {/* Only when there is nothing to show: a failed background refetch must not blank a list that
          is already on screen and still perfectly usable. */}
      {!isLoading && !shown && <p>Could not load your identities.</p>}
      {!isLoading && shown && (
        <>
          <div className="admin-list-header">
            <span className="admin-list-title">
              {shown.length} {shown.length === 1 ? 'identity' : 'identities'}
            </span>
            <button className="btn btn-primary" style={{ width: 'auto' }} aria-label="Add identity"
              onClick={() => setAdding(true)}>
              <PersonPlusIcon /> Add
            </button>
          </div>
          {/* Usable but possibly out of date — the page edits this list, and a save built on stale
              rows comes back refused. */}
          {isError && <p className="settings-note">Could not refresh this list — it may be out of date.</p>}
          <div className="admin-list identity-list">
            {tiles.map(i => (
              <div key={i.address} className={`admin-list-item${i.stale ? ' is-stale' : ''}`}>
                <span className="admin-list-item-email">{i.displayName}</span>
                <span className="admin-list-item-name">{i.address}</span>
                {i.isPrimary && <span className="identity-tag">primary</span>}
                {i.stale && <span className="identity-tag">unavailable</span>}
                <div className="admin-list-item-actions">
                  {/* A stale row cannot hold the default, so it carries no star. */}
                  {!i.stale && i.isDefault && (
                    <span className="admin-icon-btn is-default" title="Default identity">
                      <StarIcon size={16} filled />
                      <span className="visually-hidden">{i.address} is the default</span>
                    </span>
                  )}
                  {!i.stale && !i.isDefault && (
                    <button
                      type="button" className="admin-icon-btn" title="Set as default"
                      aria-label={`Make ${i.address} the default`}
                      onClick={() => save(applyDefault(shown, i.address))}
                    >
                      <StarIcon size={16} />
                    </button>
                  )}
                  {/* The primary's name comes from the Account tab, so it is not editable here. */}
                  {!i.stale && !i.isPrimary && (
                    <button
                      type="button" className="admin-icon-btn" title="Edit"
                      aria-label={`Edit ${i.address}`} onClick={() => setEditing(i)}
                    >
                      <PencilIcon />
                    </button>
                  )}
                  {!i.isPrimary && (
                    <button
                      type="button" className="admin-icon-btn is-danger" title="Remove"
                      aria-label={`Remove ${i.address}`}
                      onClick={() => save(applyRemoval(shown, i.address))}
                    >
                      <TrashIcon />
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        </>
      )}

      {adding && shown && (
        <IdentityDialog
          mode="add"
          taken={shown.map(i => i.address)}
          initialName={identity?.displayName ?? ''}
          onSubmit={(address, name) => { save(applyAddition(shown, address, name)); setAdding(false) }}
          onClose={() => setAdding(false)}
        />
      )}
      {editing && shown && (
        <IdentityDialog
          mode="edit"
          taken={shown.map(i => i.address)}
          editAddress={editing.address}
          initialName={editing.displayName}
          onSubmit={(address, name) => { save(applyLabel(shown, address, name)); setEditing(null) }}
          onClose={() => setEditing(null)}
        />
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
