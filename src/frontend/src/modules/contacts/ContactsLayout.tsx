import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMatch, useNavigate, useParams, useSearchParams } from 'react-router'
import { newMessageSeed } from '../mail/compose/composeSeed'
import DeleteConfirmModal from '../../components/DeleteConfirmModal'
import FloatingAction from '../../components/FloatingAction'
import Toasts from '../../components/Toasts'
import { useToasts } from '../../hooks/useToasts'
import { useViewport } from '../../hooks/useViewport'
import PersonPlusIcon from '../../icons/PersonPlusIcon'
import ContextDrawer, { DrawerToggle, useContextDrawer } from '../../layouts/ContextDrawer'
import { apiErrorMessage } from '../../lib/apiErrorMessage'
import PaneSplitter from '../mail/split/PaneSplitter'
import { usePaneSize } from '../mail/split/usePaneSize'
import ContactCard from './ContactCard'
import ContactConflictDialog from './ContactConflictDialog'
import ContactEditView from './ContactEditView'
import { useContactActions } from './useContactActions'
import { useContactPhotoUrl } from './useContactPhotoUrl'
import ContactList from './ContactList'
import { displayNameOf } from './contactName'
import { groupOptionsOf } from './contactSearch'
import ContactScopes, { groupIdOf, type ContactScope } from './ContactScopes'
import ContactsTransfer from './ContactsTransfer'
import GroupNameModal from './GroupNameModal'
import type { ContactGroup } from './contactGroupTypes'
import {
  useContact, useContacts, useContactGroups, useCreateContactGroup, useDeleteContactGroup,
  useRenameContactGroup,
} from './queries'

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

/** The contacts module's three columns inside the shell's one outlet, each a band stack whose one
 * scrolling band carries `min-height: 0`, or the scroll escapes to the column. */
export default function ContactsLayout() {
  const { t } = useTranslation('contacts')
  const [params, setParams] = useSearchParams()
  const { id: routeId } = useParams()
  const navigate = useNavigate()

  // The composer is a route, so writing to a contact navigates with a seed. `backTo` sends the ✕
  // and the leave guard back where writing started (a card, a group), not to an unopened mailbox.
  const writeTo = (addresses: string | string[]) => void navigate('/mail/compose', {
    state: {
      seed: newMessageSeed(Array.isArray(addresses) ? addresses : [addresses]),
      backTo: backToHere(),
    },
  })

  // The `/contacts` URL for the scope this render is showing, plus whatever `extra` overrides —
  // `{ id }` to open a particular contact, `{}` to leave the selection as the scope alone reads it.
  function contactsUrl(extra: Record<string, string>) {
    const query = new URLSearchParams(paramsForScope(scope, extra)).toString()
    return query ? `/contacts?${query}` : '/contacts'
  }

  function backToHere() {
    return contactsUrl(selectedId ? { id: selectedId } : {})
  }

  // The editor is a route of its own, so navigating to it would otherwise drop the scope and the
  // open card it was reached from; carrying the current query string is what `backToHere` and a
  // save's own `contactsUrl` read back once the editor closes.
  function openEditor(path: string) {
    const query = params.toString()
    void navigate(query ? `${path}?${query}` : path)
  }
  const { toasts, addToast, removeToast, pauseToast, resumeToast } = useToasts()
  const { data: contacts, isLoading, isError } = useContacts()
  const {
    data: detail, isLoading: detailLoading, isError: detailError, refetch: refetchDetail,
  } = useContact(routeId ?? null)
  const groups = useContactGroups()
  const createGroup = useCreateContactGroup()
  const renameGroup = useRenameContactGroup()
  const deleteGroup = useDeleteContactGroup()

  // The editor takes the two content columns and leaves the band standing, exactly as the
  // composer does inside the mail module. Two routes, one layout — not a layout of its own.
  const creating = useMatch('/contacts/new') != null
  const editing = useMatch('/contacts/:id/edit') != null
  const inEditor = creating || editing

  const urlScope = scopeOf(params.get('scope'))
  const selectedId = params.get('id')
  const openGroupId = groupIdOf(urlScope)
  const openGroup = openGroupId ? groups.data?.find(one => one.id === openGroupId) ?? null : null
  // A refused list answers the question too — the scope cannot resolve — where waiting on `data`
  // alone would hold the column on its loading line for the rest of the session.
  const groupsSettled = groups.data != null || groups.isError
  // A group nobody holds is the whole book from this render on: the URL follows a commit later,
  // and the band must not go a frame without a highlighted scope over an unfiltered list.
  const scope: ContactScope = openGroupId != null && groupsSettled && openGroup == null ? 'all' : urlScope
  // The list is filtered on nothing until the group resolves, and an unfiltered book under a group
  // scope would read as the group holding everybody.
  const groupPending = openGroupId != null && !groupsSettled

  const phone = useViewport() === 'phone'
  const drawer = useContextDrawer()
  // The two columns a confirmed delete hands focus back to when it takes its own button with it —
  // the way `CalendarLayout` holds one for `.calendar-main`. A contact's own delete is the list's,
  // a group's belongs to the band the row was in.
  const listRegion = useRef<HTMLDivElement>(null)
  const scopesRegion = useRef<HTMLDivElement>(null)
  const [listWidth, setListWidth] = usePaneSize('contacts.split.right', 380, 240)
  const [groupModal, setGroupModal] =
    useState<{ mode: 'create' } | { mode: 'rename'; group: ContactGroup } | null>(null)
  const [pendingGroupDelete, setPendingGroupDelete] = useState<ContactGroup | null>(null)

  // The reload counter rides the editor key so recharging reseeds the form, which nothing else can
  // do — the editor takes its values from its contact once, at mount.
  const [reloads, setReloads] = useState(0)
  const editorKey = inEditor ? `${routeId ?? 'new'}#${reloads}` : null

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

  // The addresses writing to a group reaches, resolved by the composer field's own function so the
  // two agree, and keyed since every group row asks. A book still loading is not an empty one, so
  // no row claims addresses it has not checked.
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

  // The seeded card's hash, held until reseeded and never read live: every write's invalidation
  // reaches the card, so a refused save would retry with the very version that refused it.
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
    void navigate('/contacts', { replace: true })
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

  const contactActions = useContactActions({
    edited, selected, openGroup, seededHash, editorKey, addToast,
    // The scope the editor was opened under, with the saved contact now open in it. Replace: a
    // pushed entry would let Back reopen the editor — a blank "New contact" form after a create,
    // where a second Save fabricates a duplicate, the way CalendarLayout's `backToGrid` guards.
    onSaved: id => void navigate(contactsUrl({ id }), { replace: true }),
    // The open card must not survive its contact.
    onDeletedOpen: id => { if (selectedId === id) setParams(paramsForScope(scope)) },
    onReloaded: () => setReloads(previous => previous + 1),
    refetchDetail,
  })

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

  // One instance only: it owns the hidden file input and the import report. Below 1024px its row is
  // hidden, so the trigger moves into the list's own band (docs/architecture-contacts.md).
  const transfer = (className: string) => (
    <ContactsTransfer contacts={contacts} triggerClassName={className}
      onError={message => addToast(message, 'error')} />
  )

  const scopeColumn = (
    <div className="contacts-scopes-column" ref={scopesRegion} tabIndex={-1}>
      <div className="column-actions">
        <button type="button" className="btn btn-primary column-actions-main"
          onClick={() => openEditor('/contacts/new')}>
          {t('layout.add')}
        </button>
        {!drawer.inDrawer && transfer('btn btn-primary column-actions-square')}
      </div>
      <div className="contacts-scopes-scroll">
        <ContactScopes scope={scope} total={total} favorites={favorites}
          groups={groups.data ?? []} onScope={changeScope} onDropContacts={contactActions.dropOnScope}
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
            <ContactEditView key={editorKey} contact={detail ?? null} photo={editorPhoto}
              error={contactActions.saveError} saving={contactActions.saving}
              onSave={draft => void contactActions.save(draft)}
              // Replace, for the same reason as onSaved: Back must leave the editor, not reopen it.
              onCancel={() => void navigate(backToHere(), { replace: true })} />
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
            style={phone ? undefined : { width: listWidth }} data-testid="contact-list"
            ref={listRegion} tabIndex={-1}>
            {/* A group scope waits for its group too: filtered on nothing, the list would say the
                book is empty for as long as that query is in flight. */}
            {(isLoading || groupPending) && <p className="contacts-empty">{t('layout.loading')}</p>}
            {/* Only when there is nothing to show for it: a refetch failing behind a book already
                on screen must not print this line above a list that still works. */}
            {isError && !contacts && <p className="contacts-empty">{t('layout.loadFailed')}</p>}
            {contacts && !groupPending && (
              <ContactList contacts={scoped} selectedId={selectedId} scope={scope} onSelect={select}
                leading={drawer.inDrawer ? <DrawerToggle onClick={drawer.toggle} /> : null}
                actions={drawer.inDrawer ? transfer('selection-btn') : null}
                onToggleFavorite={contactActions.toggleFavorite}
                onDelete={contactActions.setPendingDelete}
                onDeleteMany={contactActions.deleteSelection}
                deletingMany={contactActions.deletingMany}
                onRemoveFromGroup={openGroup ? contactActions.removeFromOpenGroup : undefined}
                onEdit={id => openEditor(`/contacts/${id}/edit`)} regionRef={listRegion} />
            )}
          </div>
          {!phone && (
            <PaneSplitter orientation="vertical" size={listWidth} defaultSize={380} min={240}
              reserve={320} onResize={setListWidth} />
          )}
          {!(phone && !selectedId) && (
            <div className="contacts-card" data-testid="contact-card">
              <ContactCard contact={selected} onToggleFavorite={contactActions.toggleFavorite}
                onBack={phone ? backToList : undefined}
                bottomActions={phone}
                onDelete={contactActions.setPendingDelete}
                onEdit={id => openEditor(`/contacts/${id}/edit`)}
                onWrite={writeTo}
                groups={selected ? groupsOf(selected.id) : undefined}
                onRemoveFromGroup={contactActions.removeFromGroup} />
            </div>
          )}
        </div>
      )}

      {contactActions.conflict && (
        <ContactConflictDialog onClose={contactActions.closeConflict}
          onReload={() => void contactActions.reloadEdited()} />
      )}

      {contactActions.pendingDelete && (
        <DeleteConfirmModal entityLabel={displayNameOf(contactActions.pendingDelete)}
          loading={contactActions.deleting}
          onConfirm={() => void contactActions.confirmDelete()}
          onClose={() => contactActions.setPendingDelete(null)}
          returnFocusRef={listRegion} />
      )}

      {groupModal && (
        <GroupNameModal
          title={t(groupModal.mode === 'create' ? 'groups.createTitle' : 'groups.renameTitle')}
          initialName={groupModal.mode === 'rename' ? groupModal.group.name : ''}
          saving={createGroup.isPending || renameGroup.isPending}
          onSubmit={name => void submitGroupName(name)} onClose={() => setGroupModal(null)} />
      )}

      {/* The body says what the deletion leaves behind: a group is a view onto contacts, and
          nobody should have to guess whether they are about to lose them. */}
      {pendingGroupDelete && (
        <DeleteConfirmModal message={t('groups.deleteBody', { name: pendingGroupDelete.name })}
          loading={deleteGroup.isPending}
          onConfirm={() => void confirmGroupDelete()} onClose={() => setPendingGroupDelete(null)}
          returnFocusRef={scopesRegion} />
      )}

      {/* Never over the editor, which is the create form and would be left half-typed, nor over an
          open card on a phone, where it would sit on the action band (MailLayout does the same). */}
      {!inEditor && !(phone && selectedId) && (
        <FloatingAction label={t('layout.add')} onClick={() => openEditor('/contacts/new')}>
          <PersonPlusIcon size={22} />
        </FloatingAction>
      )}

      <Toasts toasts={toasts} onRemove={removeToast} onPause={pauseToast} onResume={resumeToast} />
    </div>
  )
}
