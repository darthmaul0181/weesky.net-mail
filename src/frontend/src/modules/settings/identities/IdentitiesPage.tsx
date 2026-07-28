import { useState } from 'react'
import LoadingBlock from '../../../components/LoadingBlock'
import Toasts from '../../../components/Toasts.jsx'
import { useToasts } from '../../../hooks/useToasts.js'
import { useAuth } from '../../../contexts/AuthContext'
import MailIcon from '../../../icons/MailIcon'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import PersonPlusIcon from '../../../icons/PersonPlusIcon.jsx'
import StarIcon from '../../../icons/StarIcon'
import TrashIcon from '../../../icons/TrashIcon.jsx'
import type { SendingIdentity } from '../../mail/api/mailTypes'
import { useAliases, useIdentities, useReplaceIdentities } from '../../mail/queries'
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
  const { data: aliases, isLoading: aliasesLoading, isError: aliasesError } = useAliases()
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

  // An identity is built from an alias; with none owned there is nothing to add. A still-loading
  // or failed alias query is not "none" — the button stays live so a blip does not lock it.
  const noAliases = !aliasesLoading && !aliasesError && (aliases?.length ?? 0) === 0

  // The primary is pinned first; every other identity is ordered alphabetically by display name.
  const tiles = shown
    ? [...shown.filter(i => i.isPrimary), ...sortIdentities(shown.filter(i => !i.isPrimary))]
    : []

  return (
    <div className="settings-page">
      <div className="settings-page-header">
        <span className="settings-page-title"><MailIcon size={17} /> Identities</span>
      </div>
      <p className="identities-hint">
        An identity is an address from which you can send emails.<br />
        Each identity is linked to one of your aliases and has its own name, which will be visible to your recipients.
      </p>
      <p className="identities-hint">
        Deleting an identity does not affect the alias itself in any way.<br />
        Your primary identity cannot be deleted. Its name can be changed via the ‘Account’ page.
      </p>

      {isLoading && <LoadingBlock />}
      {/* Only when there is nothing to show: a failed background refetch must not blank a list that
          is already on screen and still perfectly usable. */}
      {!isLoading && !shown && <p>Could not load your identities.</p>}
      {!isLoading && shown && (
        <div className="identity-panel">
          <div className="admin-list-header">
            <button className="btn btn-primary" style={{ width: 'auto' }} aria-label="Add identity"
              disabled={noAliases} title={noAliases ? 'You have no alias to create an identity from' : undefined}
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
                {/* The star sits first, before the name. A stale row keeps the empty slot so the
                    names stay aligned; it cannot hold the default. */}
                <span className="identity-star-slot">
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
                </span>
                <span className="admin-list-item-email">{i.displayName}</span>
                <span className="admin-list-item-name">{i.address}</span>
                {i.isPrimary && <span className="row-tag">primary</span>}
                {i.stale && <span className="row-tag">unavailable</span>}
                <div className="admin-list-item-actions">
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
        </div>
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
