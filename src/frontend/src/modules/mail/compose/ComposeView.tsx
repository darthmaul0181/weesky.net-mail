import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import { useBlocker, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../../../contexts/AuthContext'
import { useViewport } from '../../../hooks/useViewport'
import { useContacts } from '../../contacts/queries'
import { capturable } from '../../contacts/captureModel'
import { useCaptureContacts } from '../../contacts/useCaptureContacts'
import { displayNameOf } from '../../contacts/contactName'
import { captureRecipientsOf, composeFormatOf, usePreferences } from '../../../hooks/usePreferences'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { registerLeaveGuard } from '../../../lib/leaveGuard'
import { useAccountId, useDeleteMessages, useIdentities, useSaveDraft, useSendMessage } from '../queries'
import { stagedAttachmentUrl, uploadAttachment } from '../../../api.js'
import DropdownMenu from '../../../components/DropdownMenu'
import ChevronDownIcon from '../../../icons/ChevronDownIcon'
import KebabIcon from '../../../icons/KebabIcon'
import RocketIcon from '../../../icons/RocketIcon'
import type { MailPriority } from '../api/mailTypes'
import AttachmentTray from './AttachmentTray'
import { htmlToText, losesFormatting, textToHtml } from './bodyFormat'
import EditorToolbar from './EditorToolbar'
import IdentitySelect from './IdentitySelect'
import { mailtoSeedFrom } from './mailtoSeed'
import RecipientsField, { isValidAddress } from './RecipientsField'
import SquireEditor, { type ActiveFormats, type EditorHandle } from './SquireEditor'
import { applyComposeFormat, type ComposeAction, type ComposeSeed } from './composeSeed'
import LoadingBlock from '../../../components/LoadingBlock'
import { relativizeStagedUrls } from './stagedUrls'
import { useStagedAttachments } from './useStagedAttachments'

const NO_FORMATS: ActiveFormats = {
  bold: false, italic: false, underline: false, strikethrough: false,
  unorderedList: false, orderedList: false,
}

// Edit-as-new is left out on purpose: it starts a message of its own, threaded to nothing.
// The keys are written out rather than held in a map: one that reaches `t()` only as a variable
// is invisible to `src/locales/keys.test.ts`.
function composeTitle(action: ComposeAction | undefined, t: TFunction<'compose'>): string {
  return action === 'reply' || action === 'replyAll' ? t('titles.reply')
    : action === 'forward' ? t('titles.forward')
      : action === 'draft' ? t('titles.draft') : t('titles.newMessage')
}

/** The single predicate for "goes in the body", so the drop overlay and routeFiles cannot drift. */
const isImage = (type: string) => type.startsWith('image/')

const PRIORITIES: MailPriority[] = ['high', 'normal', 'low']

function priorityLabel(value: MailPriority, t: TFunction<'compose'>): string {
  return value === 'high' ? t('priority.high')
    : value === 'low' ? t('priority.low') : t('priority.normal')
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
  const { t } = useTranslation('compose')
  const narrow = useViewport() === 'phone'
  const { identity } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  // Pinned at mount: this draft belongs to the mailbox it was opened in. Its staged files live in
  // that account's namespace, so a switch under an open composer would send ids the new account
  // has never heard of — a message out with its attachments silently missing.
  const activeAccountId = useAccountId()
  const [accountId] = useState(activeAccountId)
  const send = useSendMessage(accountId)
  const saveDraftMutation = useSaveDraft(accountId)
  // The mutation's own callback, not a per-call one: the send navigates away first, and TanStack
  // drops per-call callbacks once the observer unmounts. A silent failure would leave a
  // re-sendable draft of a message that has already gone out.
  const deleteDraft = useDeleteMessages(
    () => onNotify(t('toast.sentDraftKept'), 'error'), accountId)
  // Pinned like the send: an address only the newly active account owns would be refused by the
  // one this draft is bound to, leaving it neither sendable nor savable.
  const { data: identityList } = useIdentities(accountId)
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
  // A mailto: arrives through the URL, not through the history state: the operating system opens
  // a cold address, with no React navigation behind it.
  const rawSeed = useMemo(
    () => state?.seed ?? mailtoSeedFrom(location.search), [state?.seed, location.search])
  const openedFormat = preferences ? composeFormatOf(preferences) : 'html'
  // Empty deps deliberately: the editor is chosen when the composer opens. Changing the setting
  // mid-message must never reformat what is already being written.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const seed = useMemo(() => applyComposeFormat(rawSeed, openedFormat), [])

  const [to, setTo] = useState<string[]>(seed?.to ?? [])
  const [cc, setCc] = useState<string[]>(seed?.cc ?? [])
  const [bcc, setBcc] = useState<string[]>(seed?.bcc ?? [])
  const [showCc, setShowCc] = useState((seed?.cc.length ?? 0) > 0)
  const [showBcc, setShowBcc] = useState((seed?.bcc.length ?? 0) > 0)
  const [priority, setPriority] = useState<MailPriority>(seed?.priority ?? 'normal')
  const [showPriority, setShowPriority] = useState((seed?.priority ?? 'normal') !== 'normal')
  const [subject, setSubject] = useState(seed?.subject ?? '')
  // A seeded body is content the user can lose — dirty from the first render.
  const [bodyTouched, setBodyTouched] = useState(Boolean(seed?.html || seed?.text))
  // Non-null is the plain-text mode itself, mirroring the wire: it holds the body the textarea
  // owns and the field the payload carries. Null means Squire owns the body.
  // applyComposeFormat has already settled every seeded case, so this only answers the no-seed one.
  const [text, setText] = useState<string | null>(
    seed ? seed.text : (preferences && composeFormatOf(preferences) === 'text' ? '' : null))
  // Squire reads initialHtml once, at mount, so a switch back has to hand it the converted body
  // here — a prop change would never reach the editor it already built.
  const [editorHtml, setEditorHtml] = useState(seed?.html)
  const [fromAddress, setFromAddress] = useState<string | null>(seed?.fromAddress ?? null)
  // Inline resources live in the body, not the tray; their ids still ride the send payload.
  const seedTray = useMemo(() => (seed?.attachments ?? []).filter(a => !a.contentId), [seed])
  const seedInline = useMemo(() => (seed?.attachments ?? []).filter(a => a.contentId), [seed])
  // An adopted id now rides the payload as a tray id; left in here it would ride it twice, and
  // the backend packs one staged file per id it is handed.
  const [inlineAdopted, setInlineAdopted] = useState(false)
  // Held here as well as in the hook: the hook holds them to release them, this list rides the
  // payload and names the files the tray needs if the composer ever switches to plain text.
  const [insertedInline, setInsertedInline] = useState<{ id: string; fileName: string; size: number }[]>([])
  // The seed alone: an inserted id reaches the hook through addInline, and handed in here as well
  // it would be held twice — released twice, and adopted into two tray rows the send packs twice.
  const seedInlineIds = useMemo(
    () => (inlineAdopted ? [] : seedInline.map(a => a.id)), [seedInline, inlineAdopted])
  const attachments = useStagedAttachments(accountId, seedTray, seedInlineIds)
  // Not gated on inlineAdopted: that flag speaks for the seed. A switch empties this list instead,
  // so an image inserted after a switch back to HTML is inline again and rides the payload again.
  const inlineIds = useMemo(
    () => [...seedInlineIds, ...insertedInline.map(a => a.id)],
    [seedInlineIds, insertedInline])
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
  // An inserted inline image counts here as well as in the tray: it is content in the body, and
  // resting on Squire having fired `input` for it would be resting on something asserted nowhere.
  const dirty = changed && (draftRef !== null || to.length > 0 || cc.length > 0 || bcc.length > 0
    || subject !== '' || bodyTouched || attachments.items.length > 0 || insertedInline.length > 0)
  const dirtyRef = useRef(dirty)
  const leavingRef = useRef(false)
  useEffect(() => { dirtyRef.current = dirty }, [dirty])
  const markDirty = useCallback(() => { dirtyRef.current = true; setChanged(true) }, [])
  const changeFrom = useCallback((v: string | null) => { markDirty(); setFromAddress(v) }, [markDirty])
  const changeTo = useCallback((v: string[]) => { markDirty(); setTo(v) }, [markDirty])
  const changeCc = useCallback((v: string[]) => { markDirty(); setCc(v) }, [markDirty])
  const changeBcc = useCallback((v: string[]) => { markDirty(); setBcc(v) }, [markDirty])
  const changeSubject = useCallback((v: string) => { markDirty(); setSubject(v) }, [markDirty])
  const changePriority = useCallback((v: MailPriority) => { markDirty(); setPriority(v) }, [markDirty])
  useEffect(() => { if (priority === 'normal') setShowPriority(false) }, [priority])
  const stageFiles = attachments.addFiles
  const removeStaged = attachments.remove
  const addFiles = useCallback((files: File[]) => { markDirty(); stageFiles(files) }, [markDirty, stageFiles])
  // The one edit that shrinks the form, so nothing else can notice it.
  const removeFile = useCallback((key: string) => { markDirty(); removeStaged(key) }, [markDirty, removeStaged])

  // An inline upload never reaches the tray, so attachments.uploading cannot see it. Until it
  // resolves there is no id for a send to carry, for an adoption to move, or for a discard to
  // release, which is why everything that consumes those ids waits on this count.
  const [inlineUploads, setInlineUploads] = useState(0)
  const addInline = attachments.addInline
  const insertImages = useCallback(async (files: File[]) => {
    for (const file of files) {
      setInlineUploads(n => n + 1)
      try {
        const info = await uploadAttachment(file, { accountId, inline: true })
        addInline(info.id)
        setInsertedInline(previous => [...previous, info])
        editor?.insertImage(stagedAttachmentUrl(info.id, accountId))
        // Only once something is actually in the body: a refused upload leaves nothing to lose,
        // and a composer dirtied by it would still ask to save on the way out.
        markDirty()
      } catch (error) {
        onNotify(apiErrorMessage(error, t('toast.imageFailed')), 'error')
      } finally {
        setInlineUploads(n => n - 1)
      }
    }
  }, [markDirty, accountId, addInline, editor, onNotify, t])

  // An image goes in the body, anything else in the tray. Plain text has no body to put one in,
  // so there everything is an attachment.
  const routeFiles = useCallback((files: File[]) => {
    if (text !== null) { addFiles(files); return }
    const images = files.filter(file => isImage(file.type))
    const rest = files.filter(file => !isImage(file.type))
    if (rest.length > 0) addFiles(rest)
    if (images.length > 0) void insertImages(images)
  }, [text, addFiles, insertImages])

  // Counter, not a boolean: dragleave fires at every child boundary, so the overlay only
  // goes away when as many leaves as enters have fired (or on drop).
  const [dropTarget, setDropTarget] = useState(false)
  const dragDepth = useRef(0)
  const [bodyInsert, setBodyInsert] = useState(false)
  const bodyDepth = useRef(0)

  function carriesFiles(event: React.DragEvent) {
    return Array.from(event.dataTransfer.types).includes('Files')
  }
  // A drag withholds its bytes until the drop, but not the types — enough to word the overlay on
  // the predicate routeFiles will sort by, so it never promises an insertion the drop won't make.
  function carriesImage(event: React.DragEvent) {
    return Array.from(event.dataTransfer.items).some(
      item => item.kind === 'file' && isImage(item.type))
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
    resetDrag()
    const files = Array.from(event.dataTransfer.files)
    if (files.length > 0) addFiles(files)
  }
  function resetDrag() {
    dragDepth.current = 0
    bodyDepth.current = 0
    setDropTarget(false)
    setBodyInsert(false)
  }
  function onBodyDragEnter(event: React.DragEvent) {
    if (!carriesFiles(event)) return
    bodyDepth.current += 1
    // Plain text has no body to show an image in, so there a drop attaches like any other.
    setBodyInsert(text === null && carriesImage(event))
  }
  function onBodyDragLeave(event: React.DragEvent) {
    if (!carriesFiles(event)) return
    bodyDepth.current = Math.max(0, bodyDepth.current - 1)
    if (bodyDepth.current === 0) setBodyInsert(false)
  }
  // Stops here: the surface handler below would attach what the body has just taken. It therefore
  // owes the surface its own reset, which is what resetDrag is for.
  function onBodyDrop(event: React.DragEvent) {
    if (!carriesFiles(event)) return
    event.preventDefault()
    event.stopPropagation()
    resetDrag()
    const files = Array.from(event.dataTransfer.files)
    if (files.length > 0) routeFiles(files)
  }
  function onBodyPaste(event: React.ClipboardEvent) {
    const files = Array.from(event.clipboardData.files)
    if (files.length === 0) return
    // Capture phase, and stopped rather than merely prevented: Squire's _onPaste never reads
    // defaultPrevented, so a clipboard carrying an image plus text/plain (Explorer) or plus
    // rtf+html (Word) would insert its own artefact beside ours.
    event.preventDefault()
    event.stopPropagation()
    routeFiles(files)
  }
  const blocker = useBlocker(useCallback(() => dirtyRef.current && !leavingRef.current, []))

  // The same question, asked by something the router cannot see — today, the mailbox switch in
  // the identity menu. It resolves on the dialog's buttons, so both roads end in one prompt.
  const [leaveAsk, setLeaveAsk] = useState<((ok: boolean) => void) | null>(null)
  const [confirmPlain, setConfirmPlain] = useState(false)

  useEffect(() => {
    registerLeaveGuard(() => dirtyRef.current && !leavingRef.current
      ? new Promise<boolean>(resolve => setLeaveAsk(() => resolve))
      : Promise.resolve(true))
    return () => registerLeaveGuard(null)
  }, [])

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

  // The dialog serves two callers, so its buttons answer whichever one opened it: the blocker
  // holds a navigation to release, the guard holds a promise to settle.
  function keepEditing() {
    if (leaveAsk) { setLeaveAsk(null); leaveAsk(false); return }
    blocker.reset?.()
  }

  function leaveBehind() {
    leavingRef.current = true
    if (leaveAsk) { setLeaveAsk(null); leaveAsk(true); navigate(backTarget); return }
    blocker.proceed?.()
  }

  // SquireEditor binds onChange once at mount, so the callback has to be stable.
  const touchBody = useCallback(() => { markDirty(); setBodyTouched(true) }, [markDirty])

  function switchToPlainText() {
    setText(htmlToText(editor?.getHTML() ?? ''))
    attachments.adoptInline([...seedInline, ...insertedInline])
    setInlineAdopted(true)
    setInsertedInline([])
    setConfirmPlain(false)
    markDirty()
  }

  function toPlainText() {
    if (losesFormatting(editor?.getHTML() ?? '')) { setConfirmPlain(true); return }
    switchToPlainText()
  }

  function toHtml() {
    setEditorHtml(textToHtml(text ?? ''))
    setText(null)
    markDirty()
  }

  const allValid = [...to, ...cc, ...bcc].every(isValidAddress)
  // Send and Save both file the message and consume the staged ids; the loser of a race would
  // leave a draft behind that the winner's cleanup never knew about.
  const busy = attachments.uploading || inlineUploads > 0
    || send.isPending || saveDraftMutation.isPending
  const canSend = to.length > 0 && allValid && !busy
  const canSaveDraft = allValid && !busy

  const buildPayload = () => ({
    to, cc, bcc, subject,
    htmlBody: text === null ? relativizeStagedUrls(editor?.getHTML() ?? '') : '',
    textBody: text ?? undefined,
    attachmentIds: [...inlineIds, ...attachments.ids],
    fromAddress: effectiveFrom ?? undefined,
    inReplyTo: seed?.inReplyTo ?? undefined,
    references: seed?.references && seed.references.length > 0 ? seed.references : undefined,
    priority,
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
        ? t('toast.captured', { name: displayNameOf(created[0]) })
        : t('toast.capturedMany', { count: created.length })
      onNotify(message, 'success', {
        label: t('toast.undo'),
        onClick: () => void capture.remove(created.map(c => c.id))
          .then(ok => { if (!ok) onNotify(t('toast.undoFailed'), 'error') }),
      })
    })
  }

  function submit() {
    send.mutate(buildPayload(), {
      onSuccess: (result) => {
        onNotify(t(result.appendedToSent ? 'toast.sent' : 'toast.sentNoCopy'))
        // The draft is now a duplicate of a message that already left.
        if (draftRef) deleteDraft.mutate({ folderPath: draftRef.folderPath, uids: [draftRef.uid] })
        captureNewRecipients()
        leave()
      },
      onError: (error: Error) => onNotify(apiErrorMessage(error, t('toast.sendFailed')), 'error'),
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
          onNotify(t('toast.draftSaved'))
          onSaved?.()
        },
        onError: (error: Error) => onNotify(apiErrorMessage(error, t('toast.draftSaveFailed')), 'error'),
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

  // Read at mount, unlike captureRecipientsOf which is read at send. Without this the composer
  // opens in HTML and flips to a textarea a moment later, under whoever has started typing.
  // It sits after every hook: an early return above one changes the hook order between renders.
  if (!preferences) return <LoadingBlock />

  return (
    <div className="compose-view" data-testid="compose-view"
      onDragEnter={onDragEnter} onDragOver={onDragOver} onDragLeave={onDragLeave} onDrop={onDrop}>
      {/* Measured at 360: title + Send + Save draft + ✕ came to 462px of content in a 360px band,
          which has no `flex-wrap` — Save draft was cut off and the ✕, the only visible way out of
          the composer, ended a hundred pixels past the screen. The title goes because the subject
          field says the same thing one row down, and Save draft into a menu because Send is the
          action this surface exists for. */}
      <div className="compose-header">
        {!narrow && <span className="modal-title">{composeTitle(seed?.action, t)}</span>}
        <button type="button" className="btn btn-primary compose-send" disabled={!canSend} onClick={submit}>
          <RocketIcon size={15} /> {t(send.isPending ? 'header.sending' : 'header.send')}
        </button>
        {narrow ? (
          <DropdownMenu
            ariaLabel={t('header.moreActions')}
            className="btn btn-ghost compose-header-more"
            trigger={<KebabIcon size={16} />}
            items={[{
              label: t(saveDraftMutation.isPending ? 'header.saving' : 'header.saveDraft'),
              disabled: !canSaveDraft,
              onSelect: () => saveDraft(),
            }]}
          />
        ) : (
          <button type="button" className="btn btn-ghost" disabled={!canSaveDraft} onClick={() => saveDraft()}>
            {t(saveDraftMutation.isPending ? 'header.saving' : 'header.saveDraft')}
          </button>
        )}
        <button className="modal-close" aria-label={t('actions.close', { ns: 'common' })} onClick={close}>✕</button>
      </div>

      <div className="compose-fields">
        <div className="compose-from">
          <span className="compose-from-label">{t('fields.from')}</span>
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
          <RecipientsField id="compose-to" label={t('fields.to')} tokens={to} onChange={changeTo}
            autoFocus={!seed} contacts={contacts} />
          <span className="compose-cc-links">
            {!showCc && <button type="button" className="compose-link-btn" onClick={() => setShowCc(true)}>{t('fields.cc')}</button>}
            {!showBcc && <button type="button" className="compose-link-btn" onClick={() => setShowBcc(true)}>{t('fields.bcc')}</button>}
            {!showPriority && (
              <button type="button" className="compose-link-btn" onClick={() => setShowPriority(true)}>{t('fields.priority')}</button>
            )}
          </span>
        </div>
        {showCc && <RecipientsField id="compose-cc" label={t('fields.cc')} tokens={cc} onChange={changeCc}
          contacts={contacts} />}
        {showBcc && <RecipientsField id="compose-bcc" label={t('fields.bcc')} tokens={bcc} onChange={changeBcc}
          contacts={contacts} />}
        {/* Stays open while the value is not Normal: folding it would take a live setting off the
            screen while it kept riding on the message. Cc and Bcc are safe to fold — their tokens
            stay visible either way. */}
        {(showPriority || priority !== 'normal') && (
          <div className="compose-priority">
            <span className="compose-priority-label">{t('fields.priority')}</span>
            <DropdownMenu
              ariaLabel={t('fields.priority')}
              className="compose-priority-select"
              align="left"
              // priorityLabel falls back to Normal, so an out-of-union value cannot blank the app.
              trigger={<>{priorityLabel(priority, t)} <ChevronDownIcon size={13} /></>}
              items={PRIORITIES.map(p => ({ label: priorityLabel(p, t), onSelect: () => changePriority(p) }))}
            />
          </div>
        )}
        <div className="field-h">
          <label htmlFor="compose-subject">{t('fields.subject')}</label>
          <input id="compose-subject" type="text" value={subject} onChange={e => changeSubject(e.target.value)} />
        </div>
      </div>

      <EditorToolbar editor={editor} active={active} plainText={text !== null}
        switchLocked={inlineUploads > 0}
        onPickImages={routeFiles}
        onAddFiles={addFiles}
        onTogglePlainText={() => (text === null ? toPlainText() : toHtml())} />
      <div className="compose-body" onDragEnter={onBodyDragEnter} onDragLeave={onBodyDragLeave}
        onDrop={onBodyDrop} onPasteCapture={onBodyPaste}>
        {text === null ? (
          <SquireEditor ref={setEditor} initialHtml={editorHtml} onChange={touchBody} onFormatChange={setActive} />
        ) : (
          <textarea className="compose-editor" data-testid="compose-text-editor" aria-label={t('fields.body')}
            value={text} onChange={e => { setText(e.target.value); touchBody() }} />
        )}
      </div>

      <AttachmentTray items={attachments.items} onRemove={removeFile} />

      {(blocker.state === 'blocked' || leaveAsk !== null) && (
        <div className="modal-overlay">
          <div className="modal">
            <div className="modal-header">
              <span className="modal-title">{t('leave.title')}</span>
            </div>
            <p>{t('leave.body')}</p>
            <div className="folder-pick-submit">
              <button type="button" className="btn btn-ghost" onClick={keepEditing}>{t('leave.keepEditing')}</button>
              {/* Locked while busy: it deletes the staged ids a save, send or upload may still be reading. */}
              <button type="button" className="btn btn-ghost" disabled={busy}
                onClick={() => {
                  // The staged copies are scratch either way: a saved draft holds its own bytes in IMAP.
                  attachments.discardAll()
                  leaveBehind()
                }}>
                {t('leave.discard')}
              </button>
              {/* Locked on an invalid token too, where the reason is not on screen: say it. */}
              <button type="button" className="btn btn-primary" disabled={!canSaveDraft}
                title={allValid ? undefined : t('leave.fixAddress')}
                onClick={() => saveDraft(() => {
                  attachments.discardAll()
                  leaveBehind()
                })}>
                {t('leave.saveDraft')}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* No ✕, like the leave guard: both of its answers are on its buttons. */}
      {confirmPlain && (
        <div className="modal-overlay">
          <div className="modal">
            <div className="modal-header">
              <span className="modal-title">{t('plainText.title')}</span>
            </div>
            <p>{t('plainText.body')}</p>
            <div className="folder-pick-submit">
              <button type="button" className="btn btn-ghost" onClick={() => setConfirmPlain(false)}>
                {t('plainText.keepFormatting')}
              </button>
              <button type="button" className="btn btn-primary" onClick={switchToPlainText}>{t('plainText.confirm')}</button>
            </div>
          </div>
        </div>
      )}

      {dropTarget && (
        <div className="compose-drop-overlay">
          {t(bodyInsert ? 'drop.image' : 'drop.files')}
        </div>
      )}
    </div>
  )
}
