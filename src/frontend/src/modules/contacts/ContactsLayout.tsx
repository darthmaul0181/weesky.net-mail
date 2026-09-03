import { useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMatch, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../api.js'
import { newMessageSeed } from '../mail/compose/composeSeed'
import { DeleteConfirmModal } from '../../components/DeleteConfirmModal.jsx'
import FloatingAction from '../../components/FloatingAction'
import Toasts from '../../components/Toasts.jsx'
import { useToasts } from '../../hooks/useToasts.js'
import { useViewport } from '../../hooks/useViewport'
import PersonPlusIcon from '../../icons/PersonPlusIcon.jsx'
import ContextDrawer, { DrawerToggle, useContextDrawer } from '../../layouts/ContextDrawer'
import { apiErrorMessage } from '../../lib/apiErrorMessage'
import PaneSplitter from '../mail/split/PaneSplitter'
import { usePaneSize } from '../mail/split/usePaneSize'
import ContactCard from './ContactCard'
import ContactEditView from './ContactEditView'
import { useContactPhotoUrl } from './useContactPhotoUrl'
import ContactList from './ContactList'
import { displayNameOf } from './contactName'
import { groupOptionsOf } from './contactSearch'
import ContactScopes, { groupIdOf, type ContactScope } from './ContactScopes'
import ContactsTransfer from './ContactsTransfer'
import GroupNameModal from './GroupNameModal'
import type { Contact, ContactDraft } from './contactTypes'
import type { ContactGroup } from './contactGroupTypes'
import {
  useAddContactGroupMembers, useContact, useContacts, useContactGroups, useCreateContact,
  useCreateContactGroup, useDeleteContact, useDeleteContactGroup, useDeleteContacts,
  useRemoveContactGroupMembers, useRenameContactGroup, useSetContactFavorite,
  useSetContactsFavorite, useUpdateContact,
} from './queries'
import type { ContactDragPayload } from './dragContacts'

/** The scope the URL names. Anything else — a stale name, a truncated value — is the whole book,
    the fallback an obsolete `?id=` already gets. */
function scopeOf(raw: string | null): ContactScope {
  if (raw === 'favorites') return 'favorites'
  return raw?.startsWith('group:') ? raw as ContactScope : 'all'
}

/** Every navigation inside the module asks the same question — does the scope survive? — and the
    answer is the same for favourites and for a group: everything but `all` stays in the URL. */
function paramsForScope(scope: ContactScope, extra?: Record<string, string>) {
  return { ...(scope === 'all' ? {} : { scope }), ...extra }
}

/**
 * The contacts module's three columns. The shell hands a module one outlet, so the module builds
 * its own columns inside it — the same way the mail module and the settings section do.
 *
 * Each column is a band stack: `min-height: 0` on the one scrolling band is the load-bearing
 * part, without which the scroll escapes to the whole column and the pinned heading drifts away.
 */
export default function ContactsLayout() {
  const { t } = useTranslation('contacts')
  const [params, setParams] = useSearchParams()
  const { id: routeId } = useParams()
  const navigate = useNavigate()

  /* The composer is a route, not a dialog, so writing to a contact is a navigation carrying a
     seed — the shape a reply and a mailto: already arrive in. `backTo` sends the ✕ and the leave
     guard back to where the writing started — a fiche, or the group it was written to — instead of
     to a mailbox the reader never opened. */
  const writeTo = (addresses: string | string[]) => navigate('/mail/compose', {
    state: {
      seed: newMessageSeed(Array.isArray(addresses) ? addresses : [addresses]),
      backTo: backToHere(),
    },
  })

  function backToHere() {
    const query = new URLSearchParams(
      paramsForScope(scope, selectedId ? { id: selectedId } : {})).toString()
    return query ? `/contacts?${query}` : '/contacts'
  }
  const { toasts, addToast, removeToast } = useToasts()
  const { data: contacts, isLoading, isError } = useContacts()
  const {
    data: detail, isLoading: detailLoading, isError: detailError, refetch: refetchDetail,
  } = useContact(routeId ?? null)
  const createContact = useCreateContact()
  const updateContact = useUpdateContact()
  const deleteContact = useDeleteContact()
  const deleteMany = useDeleteContacts()
  const setFavorite = useSetContactFavorite()
  const setManyFavorite = useSetContactsFavorite()
  const groups = useContactGroups()
  const createGroup = useCreateContactGroup()
  const renameGroup = useRenameContactGroup()
  const deleteGroup = useDeleteContactGroup()
  const addMembers = useAddContactGroupMembers()
  const removeMembers = useRemoveContactGroupMembers()

  // The editor takes the two content columns and leaves the band standing, exactly as the
  // composer does inside the mail module. Two routes, one layout — not a layout of its own.
  const creating = useMatch('/contacts/new') != null
  const editing = useMatch('/contacts/:id/edit') != null
  const inEditor = creating || editing

  const scope: ContactScope = scopeOf(params.get('scope'))
  const selectedId = params.get('id')
  const openGroupId = groupIdOf(scope)
  const openGroup = openGroupId ? groups.data?.find(one => one.id === openGroupId) ?? null : null
  // A refused list answers the question too — the scope cannot resolve — where waiting on `data`
  // alone would hold the column on its loading line for the rest of the session.
  const groupsSettled = groups.data != null || groups.isError
  // The list is filtered on nothing until the group resolves, and an unfiltered book under a group
  // scope would read as the group holding everybody.
  const groupPending = openGroupId != null && !groupsSettled

  const phone = useViewport() === 'phone'
  const drawer = useContextDrawer()
  const [listWidth, setListWidth] = usePaneSize('contacts.split.right', 380, 240)
  const [pendingDelete, setPendingDelete] = useState<Contact | null>(null)
  const [groupModal, setGroupModal] =
    useState<{ mode: 'create' } | { mode: 'rename'; group: ContactGroup } | null>(null)
  const [pendingGroupDelete, setPendingGroupDelete] = useState<ContactGroup | null>(null)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [conflict, setConflict] = useState(false)

  // A refusal belongs to the form it happened in: opening another contact's editor, or coming
  // back to a fresh one, must not inherit it (render-time reset, the MailLayout pattern). The
  // reload counter rides the key so recharging reseeds the form, which nothing else can do — the
  // editor takes its values from its contact once, at mount.
  const [reloads, setReloads] = useState(0)
  const editorKey = inEditor ? `${routeId ?? 'new'}#${reloads}` : null
  const [errorKey, setErrorKey] = useState(editorKey)
  if (editorKey !== errorKey) {
    setErrorKey(editorKey)
    setSaveError(null)
    setConflict(false)
  }

  // Resolved here rather than in the form: the editor stays free of queries, so its tests mount
  // it without an auth or a query provider.
  const editorPhoto = useContactPhotoUrl(
    routeId ?? null, detail?.hasPhoto ?? false, detail?.cardHash ?? null)

  const total = contacts?.length ?? 0
  const favorites = contacts?.filter(contact => contact.isFavorite).length ?? 0

  // A Set rather than `includes`: a group is a membership test run once per contact in the book.
  const members = openGroup ? new Set(openGroup.memberIds) : null
  const scoped = groupPending ? [] : (contacts ?? []).filter(contact =>
    scope === 'favorites' ? contact.isFavorite : members ? members.has(contact.id) : true)

  /** The addresses writing to a group would actually reach — a member the book no longer holds,
      or one carrying no address, brings nothing. Resolved by the function the composer's field
      calls, so the menu entry and the dropdown row cannot disagree about who a group holds.
      Keyed rather than scanned: every group row asks this on every render. */
  // Same guard as the composer's: a book still in flight is not an empty one, so no group row
  // should claim addresses it hasn't checked yet.
  const groupOptions = useMemo(
    () => new Map(contacts
      ? groupOptionsOf(groups.data ?? [], contacts).map(one => [one.id, one])
      : []),
    [groups.data, contacts])
  const groupAddresses = (group: ContactGroup) => groupOptions.get(group.id)?.addresses ?? []
  // The groups holding one contact — the card's chips.
  const groupsOf = (contactId: string) =>
    groups.data?.filter(group => group.memberIds.includes(contactId)) ?? []
  const selected = contacts?.find(contact => contact.id === selectedId) ?? null
  const edited = routeId ? contacts?.find(contact => contact.id === routeId) ?? null : null
  // An id the loaded book does not resolve is a target that no longer exists, never a create: an
  // obsolete bookmark would otherwise let a save fabricate a second contact. Only once the book has
  // answered — an unresolved id is normal while the request is in flight.
  const missing = routeId != null && contacts != null && edited == null
  // The form seeds from its contact once, at mount, so an edit route waits for the book — and for
  // the card, without which the positions would arrive after the seed.
  const editorReady = (!routeId || (contacts != null && detail != null)) && !missing

  // The hash of the card the open form was seeded from, captured at the render the editor mounts
  // and held until it is reseeded. Never read live at save time: the invalidation every write
  // fires reaches the card too, so a refused save would hand the retry the very version that
  // refused it — a claim to have read what the user never saw.
  const [seededHash, setSeededHash] = useState<string | undefined>(undefined)
  const [hashKey, setHashKey] = useState<string | null>(null)
  if (editorReady && editorKey !== hashKey) {
    setHashKey(editorKey)
    setSeededHash(detail?.cardHash)
  }

  useEffect(() => {
    if (!missing) return
    addToast(t('layout.notFound'), 'error')
    // Replace: Back must leave the module, not bounce off the dead route.
    navigate('/contacts', { replace: true })
  }, [missing, addToast, navigate, t])

  // The scope falls back, the open card does not: a fiche is a selection of its own, and one that
  // vanished because a group did would read as the contact having gone with it. Read off the URL
  // rather than closed over, so the callback stays stable — `backToList`'s reason.
  const fallBackToAll = useCallback(() => {
    setParams(previous => {
      const id = previous.get('id')
      return paramsForScope('all', id ? { id } : {})
    }, { replace: true })
  }, [setParams])

  // A scope naming a group nobody holds any more — deleted from another device, a foreign GUID
  // pasted into the URL — falls back to the whole book once the list has answered, the fallback an
  // obsolete `?id=` already gets. Replace: Back must not bounce off the dead scope.
  useEffect(() => {
    if (openGroupId != null && groupsSettled && openGroup == null) fallBackToAll()
  }, [openGroupId, groupsSettled, openGroup, fallBackToAll])

  function changeScope(next: ContactScope) {
    // Dropping the selected id: a contact filtered out of the new scope must not stay open, the
    // same reason choosing a folder drops the open message's uid.
    setParams(paramsForScope(next))
  }

  function select(id: string) {
    setParams(paramsForScope(scope, { id }))
  }

  // Dropping the open contact is what puts the list back on screen where the card had replaced it.
  // The scope is read off the URL rather than closed over, so the callback stays stable and the
  // card's Escape listener is bound once instead of on every render.
  const backToList = useCallback(() => {
    setParams(previous => paramsForScope(scopeOf(previous.get('scope'))))
  }, [setParams])

  async function save(draft: ContactDraft) {
    setSaveError(null)
    try {
      // Spread rather than `cardHash: seededHash`: a card the backfill never reached has none, and
      // the key present at undefined is a version claim the API cannot match.
      if (edited) {
        await updateContact.mutateAsync({
          id: edited.id, contact: seededHash ? { ...draft, cardHash: seededHash } : draft,
        })
      } else await createContact.mutateAsync(draft)
      navigate('/contacts')
      addToast(t('layout.saved'), 'success')
    } catch (error) {
      // Stay in the form carrying the reason: bouncing back to a list that kept nothing is how a
      // user loses what they typed without being told why. A stale write gets the box instead of
      // the banner — it has a way out to offer, and two messages read as two failures.
      if (error instanceof ApiError && error.status === 409) setConflict(true)
      else setSaveError(apiErrorMessage(error, t('layout.saveFailed')))
    }
  }

  // Recharging is the user's choice and never a consequence of the refusal: the form stands
  // untouched behind the box until this runs. A refetch that failed has nothing to seed, so the
  // box stays open rather than closing over the same stale form.
  async function reloadEdited() {
    const { isError: failed } = await refetchDetail()
    if (!failed) setReloads(previous => previous + 1)
  }

  async function confirmDelete() {
    if (!pendingDelete) return
    const name = displayNameOf(pendingDelete)
    try {
      await deleteContact.mutateAsync(pendingDelete.id)
      // The open card must not survive its contact.
      if (selectedId === pendingDelete.id) setParams(paramsForScope(scope))
      addToast(t('layout.deleted', { name }), 'success')
    } catch (error) {
      addToast(apiErrorMessage(error, t('layout.deleteFailed')), 'error')
    } finally {
      setPendingDelete(null)
    }
  }

  // One call for the whole batch: fifty contacts would otherwise be fifty requests, and a failure
  // at the thirtieth leaves a half-state nobody can word. The list clears its own boxes on confirm.
  function deleteSelection(ids: string[]) {
    deleteMany.mutate(ids, {
      onError: error => addToast(apiErrorMessage(error, t('layout.deleteManyFailed')), 'error'),
    })
  }

  // The drop adds — the favourite, or the membership — and never removes: a gesture that added or
  // removed per row would land a different result on each contact it carried.
  function dropOnScope(target: ContactScope, payload: ContactDragPayload) {
    const groupId = groupIdOf(target)
    if (groupId) {
      addMembers.mutate({ id: groupId, contactIds: payload.ids }, {
        onError: error => addToast(apiErrorMessage(error, t('groups.addFailed')), 'error'),
      })
      return
    }
    if (target !== 'favorites') return
    setManyFavorite.mutate({ ids: payload.ids, isFavorite: true }, {
      onError: error => addToast(apiErrorMessage(error, t('layout.favouriteFailed')), 'error'),
    })
  }

  // No dialog: a group's membership is what a drop restores, never a loss the way deleting the
  // contact itself is.
  function removeFromOpenGroup(ids: string[]) {
    if (!openGroup) return
    removeMembers.mutate({ id: openGroup.id, contactIds: ids }, {
      onError: error => addToast(apiErrorMessage(error, t('groups.removeFailed')), 'error'),
    })
  }

  function removeFromGroup(groupId: string) {
    if (!selected) return
    removeMembers.mutate({ id: groupId, contactIds: [selected.id] }, {
      onError: error => addToast(apiErrorMessage(error, t('groups.removeFailed')), 'error'),
    })
  }

  async function submitGroupName(name: string) {
    if (!groupModal) return
    try {
      if (groupModal.mode === 'create') {
        await createGroup.mutateAsync(name)
        addToast(t('groups.created', { name }), 'success')
      } else {
        await renameGroup.mutateAsync({ id: groupModal.group.id, name })
        addToast(t('groups.renamed', { name }), 'success')
      }
      setGroupModal(null)
    } catch (error) {
      // The dialog stays open carrying what was typed: a refusal that closed it would make the
      // user retype the name to find out whether it was the name that was refused.
      addToast(apiErrorMessage(error, t('groups.saveFailed')), 'error')
    }
  }

  async function confirmGroupDelete() {
    if (!pendingGroupDelete) return
    const { id, name } = pendingGroupDelete
    try {
      await deleteGroup.mutateAsync(id)
      // The open scope must not survive its group; the fallback effect only fires once the
      // refetched list has landed.
      if (openGroupId === id) fallBackToAll()
      addToast(t('groups.deleted', { name }), 'success')
    } catch (error) {
      addToast(apiErrorMessage(error, t('groups.deleteFailed')), 'error')
    } finally {
      setPendingGroupDelete(null)
    }
  }

  function toggleFavorite(contact: Contact) {
    setFavorite.mutate({ id: contact.id, isFavorite: !contact.isFavorite }, {
      onError: error => addToast(apiErrorMessage(error, t('layout.favouriteFailed')), 'error'),
    })
  }

  // One instance, never two: it owns the hidden file input and the import report, and a second
  // copy behind a media query would be a second modal nobody closed. Below 1024px the row it
  // normally sits in is hidden — Add contact is the floating button there — so the trigger follows
  // the same road Refresh takes out of the mail's folder column: into the list's own band.
  const transfer = (className: string) => (
    <ContactsTransfer contacts={contacts} triggerClassName={className}
      onError={message => addToast(message, 'error')} />
  )

  const scopeColumn = (
    <div className="contacts-scopes-column">
      <div className="column-actions">
        <button type="button" className="btn btn-primary column-actions-main"
          onClick={() => navigate('/contacts/new')}>
          {t('layout.add')}
        </button>
        {!drawer.inDrawer && transfer('btn btn-primary column-actions-square')}
      </div>
      <div className="contacts-scopes-scroll">
        <ContactScopes scope={scope} total={total} favorites={favorites}
          groups={groups.data ?? []} onScope={changeScope} onDropContacts={dropOnScope}
          onCreateGroup={() => setGroupModal({ mode: 'create' })}
          onRenameGroup={group => setGroupModal({ mode: 'rename', group })}
          onDeleteGroup={setPendingGroupDelete}
          onWriteToGroup={group => writeTo(groupAddresses(group))}
          groupHasAddresses={group => groupAddresses(group).length > 0}
          groupsError={groups.isError} />
      </div>
    </div>
  )

  return (
    <div className="contacts-layout">
      {drawer.inDrawer
        ? <ContextDrawer open={drawer.open} onClose={drawer.close}>{scopeColumn}</ContextDrawer>
        : scopeColumn}

      {inEditor ? (
        <div className="contacts-editor" data-testid="contact-editor">
          {/* Two queries feed this pane, so the two lines have to exclude each other: a refused
              card while the book is still in flight would otherwise paint both at once, and the
              refusal is the one the user can act on. */}
          {!editorReady && (isError || detailError
            ? <p className="contacts-empty">{t('layout.loadFailed')}</p>
            : (isLoading || detailLoading) && <p className="contacts-empty">{t('layout.loading')}</p>)}
          {editorReady && (
            /* Keyed on the contact being edited so switching from one edit to another reseeds the
               form rather than carrying the previous contact's values into it. */
            <ContactEditView key={editorKey} contact={detail ?? null} photo={editorPhoto} error={saveError}
              saving={createContact.isPending || updateContact.isPending}
              onSave={save} onCancel={() => navigate('/contacts')} />
          )}
        </div>
      ) : (
        /* One pane at a time on a phone: 360px split between a tile list and a reading card
           leaves neither readable. Elsewhere the two share the row as they always have. */
        <div className="contacts-row">
          {/* Hidden, never unmounted — MailLayout's rule for the same swap: the search query is
              ContactList's own state and the scroll offset is the DOM's, and opening a contact
              and coming back would throw both away. */}
          <div className={`contacts-list${phone && selectedId ? ' is-hidden' : ''}`}
            style={phone ? undefined : { width: listWidth }} data-testid="contact-list">
            {/* A group scope waits for its group too: filtered on nothing, the list would say the
                book is empty for as long as that query is in flight. */}
            {(isLoading || groupPending) && <p className="contacts-empty">{t('layout.loading')}</p>}
            {isError && <p className="contacts-empty">{t('layout.loadFailed')}</p>}
            {contacts && !groupPending && (
              <ContactList contacts={scoped} selectedId={selectedId} scope={scope} onSelect={select}
                leading={drawer.inDrawer ? <DrawerToggle onClick={drawer.toggle} /> : null}
                actions={drawer.inDrawer ? transfer('selection-btn') : null}
                onToggleFavorite={toggleFavorite} onDelete={setPendingDelete}
                onDeleteMany={deleteSelection}
                onRemoveFromGroup={openGroup ? removeFromOpenGroup : undefined}
                onEdit={id => navigate(`/contacts/${id}/edit`)} />
            )}
          </div>
          {!phone && (
            <PaneSplitter orientation="vertical" size={listWidth} defaultSize={380} min={240}
              reserve={320} onResize={setListWidth} />
          )}
          {!(phone && !selectedId) && (
            <div className="contacts-card" data-testid="contact-card">
              {/* Withheld while the confirm is open so its Escape does not back out from under
                  the dialog — the ← is behind the overlay by then and comes back with it. */}
              <ContactCard contact={selected} onToggleFavorite={toggleFavorite}
                onBack={phone && !pendingDelete ? backToList : undefined}
                bottomActions={phone}
                onDelete={setPendingDelete} onEdit={id => navigate(`/contacts/${id}/edit`)}
                onWrite={writeTo}
                groups={selected ? groupsOf(selected.id) : undefined}
                onRemoveFromGroup={removeFromGroup} />
            </div>
          )}
        </div>
      )}

      {conflict && (
        <div className="modal-overlay" onClick={() => setConflict(false)}>
          <div className="modal" onClick={event => event.stopPropagation()}>
            <div className="modal-header">
              <span className="modal-title">{t('layout.conflictTitle')}</span>
              <button className="modal-close" onClick={() => setConflict(false)}>✕</button>
            </div>
            <p>{t('layout.conflictBody')}</p>
            <div className="modal-actions">
              <button type="button" className="btn btn-primary" onClick={reloadEdited}>
                {t('layout.conflictReload')}
              </button>
            </div>
          </div>
        </div>
      )}

      {pendingDelete && (
        <DeleteConfirmModal entityLabel={displayNameOf(pendingDelete)}
          loading={deleteContact.isPending}
          onConfirm={confirmDelete} onClose={() => setPendingDelete(null)} />
      )}

      {groupModal && (
        <GroupNameModal
          title={t(groupModal.mode === 'create' ? 'groups.createTitle' : 'groups.renameTitle')}
          initialName={groupModal.mode === 'rename' ? groupModal.group.name : ''}
          saving={createGroup.isPending || renameGroup.isPending}
          onSubmit={submitGroupName} onClose={() => setGroupModal(null)} />
      )}

      {/* The body says what the deletion leaves behind: a group is a view onto contacts, and
          nobody should have to guess whether they are about to lose them. */}
      {pendingGroupDelete && (
        <DeleteConfirmModal message={t('groups.deleteBody', { name: pendingGroupDelete.name })}
          loading={deleteGroup.isPending}
          onConfirm={confirmGroupDelete} onClose={() => setPendingGroupDelete(null)} />
      )}

      {/* Never over the editor: that surface already is the create form, and the button would
          navigate out of a half-typed contact with nothing to ask about it. And never over an open
          card on a phone, where it is anchored 73px up from an edge the action band now owns —
          MailLayout drops it under the same condition for the same collision. */}
      {!inEditor && !(phone && selectedId) && (
        <FloatingAction label={t('layout.add')} onClick={() => navigate('/contacts/new')}>
          <PersonPlusIcon size={22} />
        </FloatingAction>
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
