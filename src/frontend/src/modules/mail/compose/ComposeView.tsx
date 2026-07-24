import { useCallback, useEffect, useRef, useState } from 'react'
import { useBlocker, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../../../contexts/AuthContext'
import { useIdentities, useSendMessage } from '../queries'
import RocketIcon from '../../../icons/RocketIcon'
import AttachmentTray from './AttachmentTray'
import EditorToolbar from './EditorToolbar'
import IdentitySelect from './IdentitySelect'
import RecipientsField, { isValidAddress } from './RecipientsField'
import SquireEditor, { type ActiveFormats, type EditorHandle } from './SquireEditor'
import { useStagedAttachments } from './useStagedAttachments'

const NO_FORMATS: ActiveFormats = {
  bold: false, italic: false, underline: false, strikethrough: false,
  unorderedList: false, orderedList: false,
}

interface Props { onNotify: (message: string, kind?: string) => void }

/**
 * The compose surface, replacing list+reader inside the mail module. No drafts yet (2c3),
 * so leaving means losing the message: a router blocker owns every exit — folder click,
 * ✕, Back — and beforeunload covers the tab.
 */
export default function ComposeView({ onNotify }: Props) {
  const { identity } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const send = useSendMessage()
  const { data: identityList } = useIdentities()
  // State, not a ref: the toolbar sits above the editor, so a ref would still read null on the
  // render that mounts it and the buttons would do nothing until something else re-rendered.
  const [editor, setEditor] = useState<EditorHandle | null>(null)
  const [active, setActive] = useState<ActiveFormats>(NO_FORMATS)

  const [to, setTo] = useState<string[]>([])
  const [cc, setCc] = useState<string[]>([])
  const [bcc, setBcc] = useState<string[]>([])
  const [showCc, setShowCc] = useState(false)
  const [showBcc, setShowBcc] = useState(false)
  const [subject, setSubject] = useState('')
  const [bodyTouched, setBodyTouched] = useState(false)
  const [fromAddress, setFromAddress] = useState<string | null>(null)
  const attachments = useStagedAttachments()

  const usableIdentities = (identityList ?? []).filter(i => !i.stale)
  const effectiveFrom = fromAddress
    ?? usableIdentities.find(i => i.isDefault)?.address
    ?? usableIdentities[0]?.address ?? null

  // The blocker predicate and the unload handler run inside a navigation, which can fire in the
  // same click that dirtied the form — before any passive effect flushes. So the dirtying
  // callbacks set the ref up front; the effect only carries it back to false when the form clears.
  // `dirty` means "the form is non-empty", which is the whole reason a From choice never dirties
  // on its own: with content it is already dirty through it, without content there is nothing to
  // lose. When 2c3's drafts make it "changed since load", a From-only edit must count again.
  const dirty = to.length > 0 || cc.length > 0 || bcc.length > 0
    || subject !== '' || bodyTouched || attachments.items.length > 0
  const dirtyRef = useRef(dirty)
  const leavingRef = useRef(false)
  useEffect(() => { dirtyRef.current = dirty }, [dirty])
  const markDirty = useCallback(() => { dirtyRef.current = true }, [])
  const changeTo = useCallback((v: string[]) => { markDirty(); setTo(v) }, [markDirty])
  const changeCc = useCallback((v: string[]) => { markDirty(); setCc(v) }, [markDirty])
  const changeBcc = useCallback((v: string[]) => { markDirty(); setBcc(v) }, [markDirty])
  const changeSubject = useCallback((v: string) => { markDirty(); setSubject(v) }, [markDirty])
  const stageFiles = attachments.addFiles
  const addFiles = useCallback((files: File[]) => { markDirty(); stageFiles(files) }, [markDirty, stageFiles])

  const blocker = useBlocker(useCallback(() => dirtyRef.current && !leavingRef.current, []))

  useEffect(() => {
    function onBeforeUnload(event: BeforeUnloadEvent) {
      if (dirtyRef.current && !leavingRef.current) event.preventDefault()
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [])

  const from = (location.state as { from?: string } | null)?.from
  const backTarget = from ? `/mail?folder=${encodeURIComponent(from)}` : '/mail'

  const leave = useCallback(() => {
    leavingRef.current = true
    navigate(backTarget)
  }, [navigate, backTarget])

  // SquireEditor binds onChange once at mount, so the callback has to be stable.
  const touchBody = useCallback(() => { markDirty(); setBodyTouched(true) }, [markDirty])

  const allValid = [...to, ...cc, ...bcc].every(isValidAddress)
  const canSend = to.length > 0 && allValid && !attachments.uploading && !send.isPending

  function submit() {
    send.mutate(
      {
        to, cc, bcc, subject, htmlBody: editor?.getHTML() ?? '', attachmentIds: attachments.ids,
        fromAddress: effectiveFrom ?? undefined,
      },
      {
        onSuccess: (result) => {
          onNotify(result.appendedToSent ? 'Message sent' : 'Message sent — no Sent copy could be filed')
          leave()
        },
        onError: (error: Error) => onNotify(error.message || 'Could not send the message', 'error'),
      },
    )
  }

  function close() {
    if (!dirty) { leave(); return }
    // Dirty: navigate anyway — the blocker turns it into the Discard question.
    navigate(backTarget)
  }

  return (
    <div className="compose-view" data-testid="compose-view">
      <div className="compose-header">
        <span className="modal-title">New message</span>
        <button type="button" className="btn btn-primary compose-send" disabled={!canSend} onClick={submit}>
          <RocketIcon size={15} /> {send.isPending ? 'Sending…' : 'Send'}
        </button>
        <button className="modal-close" aria-label="Close" onClick={close}>✕</button>
      </div>

      <div className="compose-fields">
        <div className="compose-from">
          <span className="compose-from-label">From</span>
          {effectiveFrom ? (
            // The whole list, not the usable one: the select keeps stale rows out of the menu
            // itself, and needs them to name a choice that went stale under the composer.
            <IdentitySelect identities={identityList ?? []} value={effectiveFrom} onChange={setFromAddress} />
          ) : (
            <span className="compose-from-value">
              {identity ? `${identity.displayName} (${identity.email})` : ''}
            </span>
          )}
        </div>
        <div className="compose-to-row">
          <RecipientsField id="compose-to" label="To" tokens={to} onChange={changeTo} autoFocus />
          <span className="compose-cc-links">
            {!showCc && <button type="button" className="compose-link-btn" onClick={() => setShowCc(true)}>Cc</button>}
            {!showBcc && <button type="button" className="compose-link-btn" onClick={() => setShowBcc(true)}>Bcc</button>}
          </span>
        </div>
        {showCc && <RecipientsField id="compose-cc" label="Cc" tokens={cc} onChange={changeCc} />}
        {showBcc && <RecipientsField id="compose-bcc" label="Bcc" tokens={bcc} onChange={changeBcc} />}
        <div className="field-h">
          <label htmlFor="compose-subject">Subject</label>
          <input id="compose-subject" type="text" value={subject} onChange={e => changeSubject(e.target.value)} />
        </div>
      </div>

      <EditorToolbar editor={editor} active={active} />
      <SquireEditor ref={setEditor} onChange={touchBody} onFormatChange={setActive} />

      <AttachmentTray items={attachments.items} onAddFiles={addFiles} onRemove={attachments.remove} />

      {blocker.state === 'blocked' && (
        <div className="modal-overlay">
          <div className="modal" style={{ maxWidth: '420px' }}>
            <div className="modal-header">
              <span className="modal-title">Discard this message?</span>
            </div>
            <p>Your message has not been sent and there are no drafts yet. Leaving discards it.</p>
            <div className="folder-pick-actions">
              <button type="button" className="btn btn-ghost" onClick={() => blocker.reset?.()}>Keep editing</button>
              <button type="button" className="btn btn-primary"
                onClick={() => { attachments.discardAll(); leavingRef.current = true; blocker.proceed?.() }}>
                Discard
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
