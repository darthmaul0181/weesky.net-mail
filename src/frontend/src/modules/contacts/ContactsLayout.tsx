import { useCallback, useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMatch, useNavigate, useParams, useSearchParams } from 'react-router-dom'
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
import ContactList from './ContactList'
import { displayNameOf } from './contactName'
import ContactScopes, { type ContactScope } from './ContactScopes'
import ContactsTransfer from './ContactsTransfer'
import type { Contact, ContactDraft } from './contactTypes'
import {
  useContact, useContacts, useCreateContact, useDeleteContact, useDeleteContacts,
  useSetContactFavorite, useSetContactsFavorite, useUpdateContact,
} from './queries'
import type { ContactDragPayload } from './dragContacts'

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
  const { toasts, addToast, removeToast } = useToasts()
  const { data: contacts, isLoading, isError } = useContacts()
  const { data: detail, isLoading: detailLoading, isError: detailError } = useContact(routeId ?? null)
  const createContact = useCreateContact()
  const updateContact = useUpdateContact()
  const deleteContact = useDeleteContact()
  const deleteMany = useDeleteContacts()
  const setFavorite = useSetContactFavorite()
  const setManyFavorite = useSetContactsFavorite()

  // The editor takes the two content columns and leaves the band standing, exactly as the
  // composer does inside the mail module. Two routes, one layout — not a layout of its own.
  const creating = useMatch('/contacts/new') != null
  const editing = useMatch('/contacts/:id/edit') != null
  const inEditor = creating || editing

  const scope: ContactScope = params.get('scope') === 'favorites' ? 'favorites' : 'all'
  const selectedId = params.get('id')

  const phone = useViewport() === 'phone'
  const drawer = useContextDrawer()
  const [listWidth, setListWidth] = usePaneSize('contacts.split.right', 380, 240)
  const [pendingDelete, setPendingDelete] = useState<Contact | null>(null)
  const [saveError, setSaveError] = useState<string | null>(null)

  // A refusal belongs to the form it happened in: opening another contact's editor, or coming
  // back to a fresh one, must not inherit it (render-time reset, the MailLayout pattern).
  const editorKey = inEditor ? routeId ?? 'new' : null
  const [errorKey, setErrorKey] = useState(editorKey)
  if (editorKey !== errorKey) {
    setErrorKey(editorKey)
    setSaveError(null)
  }

  const total = contacts?.length ?? 0
  const favorites = contacts?.filter(contact => contact.isFavorite).length ?? 0

  const scoped = (contacts ?? []).filter(contact => scope !== 'favorites' || contact.isFavorite)
  const selected = contacts?.find(contact => contact.id === selectedId) ?? null
  const edited = routeId ? contacts?.find(contact => contact.id === routeId) ?? null : null
  // An id the loaded book does not resolve is a target that no longer exists, never a create: an
  // obsolete bookmark would otherwise let a save fabricate a second contact. Only once the book has
  // answered — an unresolved id is normal while the request is in flight.
  const missing = routeId != null && contacts != null && edited == null
  // The form seeds from its contact once, at mount, so an edit route waits for the book — and for
  // the card, without which the positions would arrive after the seed.
  const editorReady = (!routeId || (contacts != null && detail != null)) && !missing

  useEffect(() => {
    if (!missing) return
    addToast(t('layout.notFound'), 'error')
    // Replace: Back must leave the module, not bounce off the dead route.
    navigate('/contacts', { replace: true })
  }, [missing, addToast, navigate, t])

  function changeScope(next: ContactScope) {
    // Dropping the selected id: a contact filtered out of the new scope must not stay open, the
    // same reason choosing a folder drops the open message's uid.
    setParams(next === 'favorites' ? { scope: next } : {})
  }

  function select(id: string) {
    setParams(scope === 'favorites' ? { scope, id } : { id })
  }

  // Dropping the open contact is what puts the list back on screen where the card had replaced it.
  // The scope is read off the URL rather than closed over, so the callback stays stable and the
  // card's Escape listener is bound once instead of on every render.
  const backToList = useCallback(() => {
    setParams(previous => {
      const next: Record<string, string> = {}
      if (previous.get('scope') === 'favorites') next.scope = 'favorites'
      return next
    })
  }, [setParams])

  async function save(draft: ContactDraft) {
    setSaveError(null)
    try {
      if (edited) await updateContact.mutateAsync({ id: edited.id, contact: draft })
      else await createContact.mutateAsync(draft)
      navigate('/contacts')
      addToast(t('layout.saved'), 'success')
    } catch (error) {
      // Stay in the form carrying the reason: bouncing back to a list that kept nothing is how a
      // user loses what they typed without being told why.
      setSaveError(apiErrorMessage(error, t('layout.saveFailed')))
    }
  }

  async function confirmDelete() {
    if (!pendingDelete) return
    const name = displayNameOf(pendingDelete)
    try {
      await deleteContact.mutateAsync(pendingDelete.id)
      // The open card must not survive its contact.
      if (selectedId === pendingDelete.id) setParams(scope === 'favorites' ? { scope } : {})
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

  // The drop adds the favourite and never removes it: a gesture that added or removed per row
  // would land a different result on each contact it carried.
  function dropOnScope(target: ContactScope, payload: ContactDragPayload) {
    if (target !== 'favorites') return
    setManyFavorite.mutate({ ids: payload.ids, isFavorite: true }, {
      onError: error => addToast(apiErrorMessage(error, t('layout.favouriteFailed')), 'error'),
    })
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
        <ContactScopes scope={scope} total={total} favorites={favorites} onScope={changeScope}
          onDropContacts={dropOnScope} />
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
            <ContactEditView key={editorKey} contact={detail ?? null} error={saveError}
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
            {isLoading && <p className="contacts-empty">{t('layout.loading')}</p>}
            {isError && <p className="contacts-empty">{t('layout.loadFailed')}</p>}
            {contacts && (
              <ContactList contacts={scoped} selectedId={selectedId} scope={scope} onSelect={select}
                leading={drawer.inDrawer ? <DrawerToggle onClick={drawer.toggle} /> : null}
                actions={drawer.inDrawer ? transfer('selection-btn') : null}
                onToggleFavorite={toggleFavorite} onDelete={setPendingDelete}
                onDeleteMany={deleteSelection}
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
                onDelete={setPendingDelete} onEdit={id => navigate(`/contacts/${id}/edit`)} />
            </div>
          )}
        </div>
      )}

      {pendingDelete && (
        <DeleteConfirmModal entityLabel={displayNameOf(pendingDelete)}
          loading={deleteContact.isPending}
          onConfirm={confirmDelete} onClose={() => setPendingDelete(null)} />
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
