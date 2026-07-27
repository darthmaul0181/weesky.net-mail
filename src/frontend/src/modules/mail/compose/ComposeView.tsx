import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useBlocker, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../../../contexts/AuthContext'
import { useContacts } from '../../contacts/queries'
import { capturable } from '../../contacts/captureModel'
import { useCaptureContacts } from '../../contacts/useCaptureContacts'
import { displayNameOf } from '../../contacts/contactName'
import { captureRecipientsOf, usePreferences } from '../../../hooks/usePreferences'
import { useDeleteMessages, useIdentities, useSaveDraft, useSendMessage } from '../queries'
import RocketIcon from '../../../icons/RocketIcon'
import AttachmentTray from './AttachmentTray'
import EditorToolbar from './EditorToolbar'
import IdentitySelect from './IdentitySelect'
import RecipientsField, { isValidAddress } from './RecipientsField'
import SquireEditor, { type ActiveFormats, type EditorHandle } from './SquireEditor'
import type { ComposeAction, ComposeSeed } from './composeSeed'
import { relativizeStagedUrls } from './stagedUrls'
import { useStagedAttachments } from './useStagedAttachments'

const NO_FORMATS: ActiveFormats = {
  bold: false, italic: false, underline: false, strikethrough: false,
  unorderedList: false, orderedList: false,
}

// Edit-as-new is left out on purpose: it starts a message of its own, threaded to nothing.
const TITLES: Record<ComposeAction, string> = {
  reply: 'Reply', replyAll: 'Reply', forward: 'Forward', editAsNew: 'New message', draft: 'Draft',
}

interface Props {
  onNotify: (
    message: string, kind?: string, action?: { label: string; onClick: () => void }) => void
}

/**
 * The compose surface, replacing list+reader inside the mail module. Leaving with unsaved
 * changes loses them, so a router blocker owns every exit — folder click, ✕, Back — offering
 * to file a draft instead; beforeunload covers the tab.
 * A reply/forward/draft arrives as `location.state.seed`; a plain new message carries none.
 */
export default function ComposeView({ onNotify }: Props) {
  const { identity } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const send = useSendMessage()
  const saveDraftMutation = useSaveDraft()
  // The mutation's own callback, not a per-call one: the send navigates away first, and TanStack
  // drops per-call callbacks once the observer unmounts. A silent failure would leave a
  // re-sendable draft of a message that has already gone out.
  const deleteDraft = useDeleteMessages(
    () => onNotify('Message sent — the draft could not be removed', 'error'))
  const { data: identityList } = useIdentities()
  // One read for the three fields: they would share the cache anyway, but a single call site is
  // easier to follow than three.
  const { data: contacts } = useContacts()
  const { data: preferences } = usePreferences()
  const capture = useCaptureContacts()
  // State, not a ref: the toolbar sits above the editor, so a ref would still read null on the
  // render that mounts it and the buttons would do nothing until something else re-rendered.
  const [editor, setEditor] = useState<EditorHandle | null>(null)
  const [active, setActive] = useState<ActiveFormats>(NO_FORMATS)

  const state = location.state as { from?: string; seed?: ComposeSeed } | null
  const from = state?.from
  const seed = state?.seed ?? null

  const [to, setTo] = useState<string[]>(seed?.to ?? [])
  const [cc, setCc] = useState<string[]>(seed?.cc ?? [])
  const [bcc, setBcc] = useState<string[]>(seed?.bcc ?? [])
  const [showCc, setShowCc] = useState((seed?.cc.length ?? 0) > 0)
  const [showBcc, setShowBcc] = useState((seed?.bcc.length ?? 0) > 0)
  const [subject, setSubject] = useState(seed?.subject ?? '')
  // A seeded body is content the user can lose — dirty from the first render.
  const [bodyTouched, setBodyTouched] = useState(Boolean(seed?.html))
  const [fromAddress, setFromAddress] = useState<string | null>(seed?.fromAddress ?? null)
  // Inline resources live in the body, not the tray; their ids still ride the send payload.
  const seedTray = useMemo(() => (seed?.attachments ?? []).filter(a => !a.contentId), [seed])
  const inlineIds = useMemo(
    () => (seed?.attachments ?? []).filter(a => a.contentId).map(a => a.id), [seed])
  const attachments = useStagedAttachments(seedTray, inlineIds)
  const [draftRef, setDraftRef] = useState(seed?.draftRef ?? null)

  const usableIdentities = (identityList ?? []).filter(i => !i.stale)
  const effectiveFrom = fromAddress
    ?? usableIdentities.find(i => i.isDefault)?.address
    ?? usableIdentities[0]?.address ?? null

  // "Changed since open or the last save". A resumed draft opens clean — its content is
  // already filed; any other seed opens changed, because its content exists nowhere else.
  const [changed, setChanged] = useState(() => Boolean(seed) && seed?.action !== 'draft')

  // The blocker predicate and the unload handler run inside a navigation, which can fire in the
  // same click that dirtied the form — before any passive effect flushes. So the dirtying
  // callbacks set the ref up front; the effect only carries it back to false when nothing is
  // left to lose. A From choice counts like any other edit now: on a resumed draft it is the
  // only change there is, and dropping it would silently send from the wrong address.
  // The non-empty clause was written for a composer that could only gain content, so it only
  // applies where there is no filed version: emptying a draft is a change like any other.
  const dirty = changed && (draftRef !== null || to.length > 0 || cc.length > 0 || bcc.length > 0
    || subject !== '' || bodyTouched || attachments.items.length > 0)
  const dirtyRef = useRef(dirty)
  const leavingRef = useRef(false)
  useEffect(() => { dirtyRef.current = dirty }, [dirty])
  const markDirty = useCallback(() => { dirtyRef.current = true; setChanged(true) }, [])
  const changeFrom = useCallback((v: string | null) => { markDirty(); setFromAddress(v) }, [markDirty])
  const changeTo = useCallback((v: string[]) => { markDirty(); setTo(v) }, [markDirty])
  const changeCc = useCallback((v: string[]) => { markDirty(); setCc(v) }, [markDirty])
  const changeBcc = useCallback((v: string[]) => { markDirty(); setBcc(v) }, [markDirty])
  const changeSubject = useCallback((v: string) => { markDirty(); setSubject(v) }, [markDirty])
  const stageFiles = attachments.addFiles
  const removeStaged = attachments.remove
  const addFiles = useCallback((files: File[]) => { markDirty(); stageFiles(files) }, [markDirty, stageFiles])
  // The one edit that shrinks the form, so nothing else can notice it.
  const removeFile = useCallback((key: string) => { markDirty(); removeStaged(key) }, [markDirty, removeStaged])

  // Counter, not a boolean: dragleave fires at every child boundary, so the overlay only
  // goes away when as many leaves as enters have fired (or on drop).
  const [dropTarget, setDropTarget] = useState(false)
  const dragDepth = useRef(0)

  function carriesFiles(event: React.DragEvent) {
    return Array.from(event.dataTransfer.types).includes('Files')
  }
  function onDragEnter(event: React.DragEvent) {
    if (!carriesFiles(event)) return
    event.preventDefault()
    dragDepth.current += 1
    setDropTarget(true)
  }
  function onDragOver(event: React.DragEvent) {
    if (carriesFiles(event)) event.preventDefault()
  }
  function onDragLeave(event: React.DragEvent) {
    if (!carriesFiles(event)) return
    dragDepth.current = Math.max(0, dragDepth.current - 1)
    if (dragDepth.current === 0) setDropTarget(false)
  }
  function onDrop(event: React.DragEvent) {
    if (!carriesFiles(event)) return
    event.preventDefault()
    dragDepth.current = 0
    setDropTarget(false)
    const files = Array.from(event.dataTransfer.files)
    if (files.length > 0) addFiles(files)
  }

  const blocker = useBlocker(useCallback(() => dirtyRef.current && !leavingRef.current, []))

  useEffect(() => {
    function onBeforeUnload(event: BeforeUnloadEvent) {
      if (dirtyRef.current && !leavingRef.current) event.preventDefault()
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [])

  const backTarget = from ? `/mail?folder=${encodeURIComponent(from)}` : '/mail'

  const leave = useCallback(() => {
    leavingRef.current = true
    navigate(backTarget)
  }, [navigate, backTarget])

  // SquireEditor binds onChange once at mount, so the callback has to be stable.
  const touchBody = useCallback(() => { markDirty(); setBodyTouched(true) }, [markDirty])

  const allValid = [...to, ...cc, ...bcc].every(isValidAddress)
  // Send and Save both file the message and consume the staged ids; the loser of a race would
  // leave a draft behind that the winner's cleanup never knew about.
  const busy = attachments.uploading || send.isPending || saveDraftMutation.isPending
  const canSend = to.length > 0 && allValid && !busy
  const canSaveDraft = allValid && !busy

  const buildPayload = () => ({
    to, cc, bcc, subject, htmlBody: relativizeStagedUrls(editor?.getHTML() ?? ''),
    attachmentIds: [...inlineIds, ...attachments.ids],
    fromAddress: effectiveFrom ?? undefined,
    inReplyTo: seed?.inReplyTo ?? undefined,
    references: seed?.references && seed.references.length > 0 ? seed.references : undefined,
  })

  // A stale identity is still an address that was yours. A live alias carrying no identity is not
  // in this set and would be captured; the undo covers that rather than a second query.
  const mine = useMemo(() => new Set([
    ...(identity ? [identity.email] : []),
    ...(identityList ?? []).map(i => i.address),
  ]), [identity, identityList])

  function captureNewRecipients() {
    if (!preferences || !captureRecipientsOf(preferences) || !contacts) return

    const candidates = capturable(contacts, [...to, ...cc, ...bcc], seed?.nameHints ?? {}, mine)
    if (candidates.length === 0) return

    void capture.create(candidates).then(created => {
      if (created.length === 0) return
      const message = created.length === 1
        ? `${displayNameOf(created[0])} added to contacts`
        : `${created.length} contacts added`
      onNotify(message, 'success', {
        label: 'Undo',
        onClick: () => void capture.remove(created.map(c => c.id))
          .then(ok => { if (!ok) onNotify('Could not undo', 'error') }),
      })
    })
  }

  function submit() {
    send.mutate(buildPayload(), {
      onSuccess: (result) => {
        onNotify(result.appendedToSent ? 'Message sent' : 'Message sent — no Sent copy could be filed')
        // The draft is now a duplicate of a message that already left.
        if (draftRef) deleteDraft.mutate({ folderPath: draftRef.folderPath, uids: [draftRef.uid] })
        captureNewRecipients()
        leave()
      },
      onError: (error: Error) => onNotify(error.message || 'Could not send the message', 'error'),
    })
  }

  function saveDraft(onSaved?: () => void) {
    saveDraftMutation.mutate(
      { ...buildPayload(), replaceUid: draftRef?.uid },
      {
        onSuccess: (saved) => {
          setDraftRef({ folderPath: saved.folderPath, uid: saved.uid })
          setChanged(false)
          dirtyRef.current = false
          onNotify('Draft saved')
          onSaved?.()
        },
        onError: (error: Error) => onNotify(error.message || 'Could not save the draft', 'error'),
      },
    )
  }

  function close() {
    // Clean still means staged: a save embedded the bytes in the IMAP message and a pristine
    // draft open re-staged copies of them, so nothing here is the only copy of anything.
    if (!dirty) { attachments.discardAll(); leave(); return }
    // Dirty: navigate anyway — the blocker turns it into the save-or-discard question.
    navigate(backTarget)
  }

  return (
    <div className="compose-view" data-testid="compose-view"
      onDragEnter={onDragEnter} onDragOver={onDragOver} onDragLeave={onDragLeave} onDrop={onDrop}>
      <div className="compose-header">
        <span className="modal-title">{(seed && TITLES[seed.action]) || 'New message'}</span>
        <button type="button" className="btn btn-primary compose-send" disabled={!canSend} onClick={submit}>
          <RocketIcon size={15} /> {send.isPending ? 'Sending…' : 'Send'}
        </button>
        <button type="button" className="btn btn-ghost" disabled={!canSaveDraft} onClick={() => saveDraft()}>
          {saveDraftMutation.isPending ? 'Saving…' : 'Save draft'}
        </button>
        <button className="modal-close" aria-label="Close" onClick={close}>✕</button>
      </div>

      <div className="compose-fields">
        <div className="compose-from">
          <span className="compose-from-label">From</span>
          {effectiveFrom ? (
            // The whole list, not the usable one: the select keeps stale rows out of the menu
            // itself, and needs them to name a choice that went stale under the composer.
            <IdentitySelect identities={identityList ?? []} value={effectiveFrom} onChange={changeFrom} />
          ) : (
            <span className="compose-from-value">
              {identity ? `${identity.displayName} (${identity.email})` : ''}
            </span>
          )}
        </div>
        <div className="compose-to-row">
          <RecipientsField id="compose-to" label="To" tokens={to} onChange={changeTo}
            autoFocus={!seed} contacts={contacts} />
          <span className="compose-cc-links">
            {!showCc && <button type="button" className="compose-link-btn" onClick={() => setShowCc(true)}>Cc</button>}
            {!showBcc && <button type="button" className="compose-link-btn" onClick={() => setShowBcc(true)}>Bcc</button>}
          </span>
        </div>
        {showCc && <RecipientsField id="compose-cc" label="Cc" tokens={cc} onChange={changeCc}
          contacts={contacts} />}
        {showBcc && <RecipientsField id="compose-bcc" label="Bcc" tokens={bcc} onChange={changeBcc}
          contacts={contacts} />}
        <div className="field-h">
          <label htmlFor="compose-subject">Subject</label>
          <input id="compose-subject" type="text" value={subject} onChange={e => changeSubject(e.target.value)} />
        </div>
      </div>

      <EditorToolbar editor={editor} active={active} />
      <SquireEditor ref={setEditor} initialHtml={seed?.html} onChange={touchBody} onFormatChange={setActive} />

      <AttachmentTray items={attachments.items} onAddFiles={addFiles} onRemove={removeFile} />

      {blocker.state === 'blocked' && (
        <div className="modal-overlay">
          <div className="modal" style={{ maxWidth: '420px' }}>
            <div className="modal-header">
              <span className="modal-title">Save this draft?</span>
            </div>
            <p>Your message has unsaved changes.</p>
            <div className="folder-pick-actions">
              <button type="button" className="btn btn-ghost" onClick={() => blocker.reset?.()}>Keep editing</button>
              {/* Locked while busy: it deletes the staged ids a save, send or upload may still be reading. */}
              <button type="button" className="btn btn-ghost" disabled={busy}
                onClick={() => {
                  // The staged copies are scratch either way: a saved draft holds its own bytes in IMAP.
                  attachments.discardAll()
                  leavingRef.current = true
                  blocker.proceed?.()
                }}>
                Discard
              </button>
              {/* Locked on an invalid token too, where the reason is not on screen: say it. */}
              <button type="button" className="btn btn-primary" disabled={!canSaveDraft}
                title={allValid ? undefined : 'Fix the invalid address first'}
                onClick={() => saveDraft(() => {
                  attachments.discardAll()
                  leavingRef.current = true
                  blocker.proceed?.()
                })}>
                Save draft
              </button>
            </div>
          </div>
        </div>
      )}

      {dropTarget && (
        <div className="compose-drop-overlay">Drop files to attach</div>
      )}
    </div>
  )
}
