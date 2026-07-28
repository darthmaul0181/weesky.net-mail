import { useEffect, useState } from 'react'
import { useMatch, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { DeleteConfirmModal } from '../../components/DeleteConfirmModal.jsx'
import Toasts from '../../components/Toasts.jsx'
import { useToasts } from '../../hooks/useToasts.js'
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
  useContacts, useCreateContact, useDeleteContact, useSetContactFavorite, useUpdateContact,
} from './queries'

/**
 * The contacts module's three columns. The shell hands a module one outlet, so the module builds
 * its own columns inside it — the same way the mail module and the settings section do.
 *
 * Each column is a band stack: `min-height: 0` on the one scrolling band is the load-bearing
 * part, without which the scroll escapes to the whole column and the pinned heading drifts away.
 */
export default function ContactsLayout() {
  const [params, setParams] = useSearchParams()
  const { id: routeId } = useParams()
  const navigate = useNavigate()
  const { toasts, addToast, removeToast } = useToasts()
  const { data: contacts, isLoading, isError } = useContacts()
  const createContact = useCreateContact()
  const updateContact = useUpdateContact()
  const deleteContact = useDeleteContact()
  const setFavorite = useSetContactFavorite()

  // The editor takes the two content columns and leaves the band standing, exactly as the
  // composer does inside the mail module. Two routes, one layout — not a layout of its own.
  const creating = useMatch('/contacts/new') != null
  const editing = useMatch('/contacts/:id/edit') != null
  const inEditor = creating || editing

  const scope: ContactScope = params.get('scope') === 'favorites' ? 'favorites' : 'all'
  const selectedId = params.get('id')

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
  // The form seeds from its contact once, at mount, so an edit route waits for the book rather
  // than mounting an empty form the arriving data would no longer reseed.
  const editorReady = (!routeId || contacts != null) && !missing

  useEffect(() => {
    if (!missing) return
    addToast('Contact not found', 'error')
    // Replace: Back must leave the module, not bounce off the dead route.
    navigate('/contacts', { replace: true })
  }, [missing, addToast, navigate])

  function changeScope(next: ContactScope) {
    // Dropping the selected id: a contact filtered out of the new scope must not stay open, the
    // same reason choosing a folder drops the open message's uid.
    setParams(next === 'favorites' ? { scope: next } : {})
  }

  function select(id: string) {
    setParams(scope === 'favorites' ? { scope, id } : { id })
  }

  async function save(draft: ContactDraft) {
    setSaveError(null)
    try {
      if (edited) await updateContact.mutateAsync({ id: edited.id, contact: draft })
      else await createContact.mutateAsync(draft)
      navigate('/contacts')
      addToast('Contact saved', 'success')
    } catch (error) {
      // Stay in the form carrying the reason: bouncing back to a list that kept nothing is how a
      // user loses what they typed without being told why.
      setSaveError((error as Error).message || 'Could not save the contact')
    }
  }

  async function confirmDelete() {
    if (!pendingDelete) return
    const name = displayNameOf(pendingDelete)
    try {
      await deleteContact.mutateAsync(pendingDelete.id)
      // The open card must not survive its contact.
      if (selectedId === pendingDelete.id) setParams(scope === 'favorites' ? { scope } : {})
      addToast(`${name} deleted`, 'success')
    } catch (error) {
      addToast((error as Error).message || 'Could not delete the contact', 'error')
    } finally {
      setPendingDelete(null)
    }
  }

  function toggleFavorite(contact: Contact) {
    setFavorite.mutate({ id: contact.id, isFavorite: !contact.isFavorite }, {
      onError: error => addToast((error as Error).message || 'Could not save the favourite', 'error'),
    })
  }

  return (
    <div className="contacts-layout">
      <div className="contacts-scopes-column">
        <div className="contacts-scopes-add">
          <button type="button" className="btn btn-primary contacts-add-btn"
            onClick={() => navigate('/contacts/new')}>
            + Add contact
          </button>
        </div>
        <div className="contacts-scopes-scroll">
          <ContactScopes scope={scope} total={total} favorites={favorites} onScope={changeScope} />
        </div>
        <ContactsTransfer contacts={contacts}
          onError={message => addToast(message, 'error')} />
      </div>

      {inEditor ? (
        <div className="contacts-editor" data-testid="contact-editor">
          {!editorReady && isLoading && <p className="contacts-empty">Loading contacts…</p>}
          {!editorReady && isError && <p className="contacts-empty">Could not load contacts.</p>}
          {editorReady && (
            /* Keyed on the contact being edited so switching from one edit to another reseeds the
               form rather than carrying the previous contact's values into it. */
            <ContactEditView key={editorKey} contact={edited} error={saveError}
              saving={createContact.isPending || updateContact.isPending}
              onSave={save} onCancel={() => navigate('/contacts')} />
          )}
        </div>
      ) : (
        <div className="contacts-row">
          <div className="contacts-list" style={{ width: listWidth }} data-testid="contact-list">
            {isLoading && <p className="contacts-empty">Loading contacts…</p>}
            {isError && <p className="contacts-empty">Could not load contacts.</p>}
            {contacts && (
              <ContactList contacts={scoped} selectedId={selectedId} onSelect={select}
                onToggleFavorite={toggleFavorite} onDelete={setPendingDelete}
                onEdit={id => navigate(`/contacts/${id}/edit`)} />
            )}
          </div>
          <PaneSplitter orientation="vertical" size={listWidth} defaultSize={380} min={240}
            reserve={320} onResize={setListWidth} />
          <div className="contacts-card" data-testid="contact-card">
            <ContactCard contact={selected} onToggleFavorite={toggleFavorite}
              onDelete={setPendingDelete} onEdit={id => navigate(`/contacts/${id}/edit`)} />
          </div>
        </div>
      )}

      {pendingDelete && (
        <DeleteConfirmModal entityLabel={displayNameOf(pendingDelete)}
          loading={deleteContact.isPending}
          onConfirm={confirmDelete} onClose={() => setPendingDelete(null)} />
      )}

      <Toasts toasts={toasts} onRemove={removeToast} />
    </div>
  )
}
