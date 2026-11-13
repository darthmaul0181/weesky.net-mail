import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMatch } from 'react-router'
import { useAuth } from '../../../contexts/AuthContext'
import type { AddToast } from '../../../hooks/useToasts'
import { useViewport } from '../../../hooks/useViewport'
import { useContactGroups, useContacts } from '../../contacts/queries'
import { groupOptionsOf } from '../../contacts/contactSearch'
import { capturable } from '../../contacts/captureModel'
import { useCaptureContacts } from '../../contacts/useCaptureContacts'
import { displayNameOf } from '../../contacts/contactName'
import { captureRecipientsOf, composeFormatOf, type Preferences } from '../../../hooks/usePreferences'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { isValidAddress } from '../../../lib/emailAddress'
import { useAccountId, useDeleteMessages, useIdentities, useSaveDraft, useSendMessage } from '../queries'
import DropdownMenu from '../../../components/DropdownMenu'
import Modal from '../../../components/Modal'
import KebabIcon from '../../../icons/KebabIcon'
import RocketIcon from '../../../icons/RocketIcon'
import ExpandIcon from '../../../icons/ExpandIcon'
import MinimiseIcon from '../../../icons/MinimiseIcon'
import type { MailPriority, StagedAttachmentInfo } from '../api/mailTypes'
import AttachmentTray from './AttachmentTray'
import { htmlToText, losesFormatting, textToHtml } from './bodyFormat'
import EditorToolbar from './EditorToolbar'
import ComposeFields from './ComposeFields'
import SquireEditor, { type ActiveFormats, type EditorHandle } from './SquireEditor'
import { applyComposeFormat, type ComposeSeed, type DraftRef } from './composeSeed'
import { composeTitle } from './composeTitle'
import { relativizeStagedUrls } from './stagedUrls'
import { useStagedAttachments } from './useStagedAttachments'
import { useComposeDropZone } from './useComposeDropZone'
import { useInlineUploads } from './useInlineUploads'
import { useLeaveGuard } from './useLeaveGuard'
import { usableIdentities } from './usableIdentities'
import LeaveDialog from './LeaveDialog'
import LeaveBlocker from './LeaveBlocker'

const NO_FORMATS: ActiveFormats = {
  bold: false, italic: false, underline: false, strikethrough: false,
  unorderedList: false, orderedList: false,
}

export interface ComposeFormProps {
  preferences: Preferences
  /** Raw seed, before applyComposeFormat. null = new empty message. */
  seed: ComposeSeed | null
  frame: 'page' | 'window'
  onNotify: AddToast
  /** The one exit: after a send, a discard or a ✕. A page navigates, so its blocker asks first; a
      window's owner must unmount the form, since the guard stays disarmed once it has left. */
  onLeave: () => void
  /** Expand hand-over: opens as dirty as the window was and keeps seed.text/html as they are. */
  handedOver?: { dirty: boolean }
  /** Window only: hands the live content over, staged files and dirty state included, to the page. */
  onExpand?: (seed: ComposeSeed, dirty: boolean) => void
  /** Window only: toggles the fold — the Minimise button, the bar's title, or the band's empty space. */
  onMinimise?: () => void
  /** Window only: the header shrinks to the bar, the title becoming the restore button. */
  minimised?: boolean
  /** Window only: what its owner names the region by, and the draft it may be asked about. The
      first call comes after the form's own mount effects, which makes it the owner's ready signal. */
  onStatusChange?: (status: { title: string; draftRef: DraftRef | null }) => void
}

export default function ComposeForm({
  preferences, seed: rawSeed, frame, onNotify, onLeave, handedOver, onExpand, onMinimise, minimised = false,
  onStatusChange,
}: ComposeFormProps) {
  const { t } = useTranslation('compose')
  const windowed = frame === 'window'
  const narrow = useViewport() === 'phone'
  const { identity } = useAuth()
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
  const { data: groups } = useContactGroups()
  const capture = useCaptureContacts()
  // Resolved by the function the contacts band reads, so a group the field expands and one Write to
  // group writes to are one set. A book still in flight offers no groups: a click would report
  // `toast.emptyGroup` on a book that just hasn't answered yet.
  const groupOptions = useMemo(
    () => (contacts ? groupOptionsOf(groups ?? [], contacts) : []), [groups, contacts])
  const notifyEmptyGroup = (name: string) => onNotify(t('toast.emptyGroup', { name }), 'error')
  // State, not a ref: the toolbar sits above the editor, so a ref would still read null on the
  // render that mounts it and the buttons would do nothing until something else re-rendered.
  const [editor, setEditor] = useState<EditorHandle | null>(null)
  const [active, setActive] = useState<ActiveFormats>(NO_FORMATS)

  const openedFormat = composeFormatOf(preferences)
  // Empty deps deliberately: the editor is chosen when the composer opens. Changing the setting
  // mid-message must never reformat what is already being written — nor a handed-over body.
  const seed = useMemo(
    () => (handedOver ? rawSeed : applyComposeFormat(rawSeed, openedFormat)),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [])

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
    seed ? seed.text : (openedFormat === 'text' ? '' : null))
  const plainText = text !== null
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
  const [insertedInline, setInsertedInline] = useState<StagedAttachmentInfo[]>([])
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

  const usable = usableIdentities(identityList)
  const effectiveFrom = fromAddress
    ?? usable.find(i => i.isDefault)?.address
    ?? usable[0]?.address ?? null

  // The non-empty clause only applies where no version is filed: emptying a draft is a change too.
  // An inserted inline image counts on its own rather than resting on Squire having fired `input`.
  const hasContent = draftRef !== null || to.length > 0 || cc.length > 0 || bcc.length > 0
    || subject !== '' || bodyTouched || attachments.items.length > 0 || insertedInline.length > 0
  // A handed-over seed carries the window's own answer: its action says nothing about a save.
  const {
    dirty, markDirty, markClean, leave, asking, keepEditing, leaveBehind, requestClose, blockerProps, setBusy,
  } = useLeaveGuard(
    handedOver ? handedOver.dirty : Boolean(seed) && seed?.action !== 'draft', hasContent,
    { mode: windowed ? 'callback' : 'route', onLeave, onRelease: attachments.discardAll })
  const changeFrom = useCallback((v: string | null) => { markDirty(); setFromAddress(v) }, [markDirty])
  const changeTo = useCallback((v: string[]) => { markDirty(); setTo(v) }, [markDirty])
  const changeCc = useCallback((v: string[]) => { markDirty(); setCc(v) }, [markDirty])
  const changeBcc = useCallback((v: string[]) => { markDirty(); setBcc(v) }, [markDirty])
  const changeSubject = useCallback((v: string) => { markDirty(); setSubject(v) }, [markDirty])
  const changePriority = useCallback((v: MailPriority) => {
    markDirty()
    if (v === 'normal' && priority !== 'normal') setShowPriority(false)
    setPriority(v)
  }, [markDirty, priority])
  const stageFiles = attachments.addFiles
  const removeStaged = attachments.remove
  const addFiles = useCallback((files: File[]) => { markDirty(); stageFiles(files) }, [markDirty, stageFiles])
  // The one edit that shrinks the form, so nothing else can notice it.
  const removeFile = useCallback((key: string) => { markDirty(); removeStaged(key) }, [markDirty, removeStaged])

  const { inlineUploads, routeFiles } = useInlineUploads({
    accountId, plainText, editor, addFiles, addInline: attachments.addInline,
    setInsertedInline, markDirty, onNotify,
  })
  const dropZone = useComposeDropZone(plainText, addFiles, routeFiles)
  const [confirmPlain, setConfirmPlain] = useState(false)

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
  useEffect(() => { setBusy(busy) }, [busy, setBusy])
  const canSend = to.length > 0 && allValid && !busy
  const canSaveDraft = allValid && !busy

  // On a phone the keyboard leaves ~406px, so the fields fold while the caret is in the body.
  // `narrow &&` rather than a flag reset on rotation: growing past 639px brings them back unasked.
  const [fieldsFolded, setFieldsFolded] = useState(false)
  const [refocusTo, setRefocusTo] = useState(false)
  // Phone-only: the window keeps its fields, the screen around it being anything but a phone's.
  const folded = !windowed && narrow && fieldsFolded

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
    if (!captureRecipientsOf(preferences) || !contacts) return

    const candidates = capturable(contacts, [...to, ...cc, ...bcc], seed?.nameHints ?? {}, mine)
    if (candidates.length === 0) return

    void capture.create(candidates).then(created => {
      if (created.length === 0) return
      const message = created.length === 1
        ? t('toast.captured', { name: displayNameOf(created[0]!) })
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
          markClean()
          onNotify(t('toast.draftSaved'))
          onSaved?.()
        },
        onError: (error: Error) => onNotify(apiErrorMessage(error, t('toast.draftSaveFailed')), 'error'),
      },
    )
  }

  function close() {
    // Clean still means staged: a save embedded the bytes in the IMAP message and a pristine
    // draft open re-staged copies of them, so nothing here is the only copy of anything. Hence the
    // ✕ locked while a send or a save of a clean form still reads them.
    if (!dirty) { attachments.discardAll(); leave(); return }
    // Dirty: the leave guard turns it into the save-or-discard question.
    requestClose()
  }

  // What the page composer opens on after an Expand. The staged ids travel as they are: nothing
  // is discarded here, since the page adopts them. Expand waits on `busy`, so no id is pending.
  function snapshotSeed(): ComposeSeed {
    const tray = attachments.items.flatMap(({ id, fileName, size, contentType }) =>
      id === null ? [] : [{ id, fileName, size, contentType }])
    return {
      action: seed?.action ?? 'editAsNew',
      to, cc, bcc, subject,
      html: text !== null ? '' : editor ? (editor.isEmpty() ? '' : editor.getHTML()) : (editorHtml ?? ''),
      text, fromAddress, priority, draftRef,
      attachments: [...tray, ...(inlineAdopted ? [] : seedInline), ...insertedInline],
      inReplyTo: seed?.inReplyTo ?? null,
      references: seed?.references ?? [],
      nameHints: seed?.nameHints ?? {},
    }
  }

  // A phone is page-only: a window still open when the screen becomes one (rotation, split
  // screen) hands itself over exactly as Expand does, once no upload is in flight. Once per phone
  // spell: a page composer that refused it is asked again only after it has gone.
  const pageOpen = useMatch('/mail/compose') !== null
  const handOver = useRef(() => {})
  const handedOverTried = useRef(false)
  useEffect(() => { handOver.current = () => onExpand?.(snapshotSeed(), dirty) })
  useEffect(() => { if (!narrow || !pageOpen) handedOverTried.current = false }, [narrow, pageOpen])
  useEffect(() => {
    if (!windowed || !narrow || busy || handedOverTried.current) return
    handedOverTried.current = true
    handOver.current()
  }, [windowed, narrow, busy, pageOpen])

  const title = composeTitle(seed?.action, t)
  const liveTitle = subject.trim() || title
  useEffect(() => { onStatusChange?.({ title: liveTitle, draftRef }) }, [onStatusChange, liveTitle, draftRef])
  const saveLabel = t(saveDraftMutation.isPending ? 'header.saving' : 'header.saveDraft')
  const sendButton = (
    <button type="button" className="btn btn-primary compose-send" disabled={!canSend} onClick={submit}>
      <RocketIcon size={15} /> {t(send.isPending ? 'header.sending' : 'header.send')}
    </button>
  )
  const saveButton = (
    <button type="button" className="btn btn-ghost" disabled={!canSaveDraft} onClick={() => saveDraft()}>
      {saveLabel}
    </button>
  )
  const closeButton = (
    <button className="modal-close" aria-label={t('actions.close', { ns: 'common' })} disabled={busy && !dirty} onClick={close}>✕</button>
  )

  return (
    <div className={`compose-view${windowed ? ' quick-compose-form' : ''}`} data-testid="compose-view"
      {...dropZone.surface}>
      {windowed ? (
        /* eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-static-element-interactions --
           a click on the band's empty space minimises, as in Gmail: a pointer shortcut whose
           keyboard way is the Minimise button beside it. The band's own buttons keep their clicks. */
        <div className="compose-header quick-compose-header"
          onClick={e => { if (!(e.target as Element).closest('button')) onMinimise?.() }}>
          {minimised ? (
            <button type="button" className="modal-title quick-compose-title quick-compose-restore"
              title={t('window.restore')} onClick={onMinimise}>{liveTitle}</button>
          ) : (
            <>
              <span className="modal-title quick-compose-title">{liveTitle}</span>
              <button type="button" className="btn btn-ghost quick-compose-tool" aria-label={t('window.minimise')}
                title={t('window.minimise')} onClick={onMinimise}>
                <MinimiseIcon size={16} />
              </button>
            </>
          )}
          <button type="button" className="btn btn-ghost quick-compose-tool" aria-label={t('window.expand')}
            title={t('window.expand')} disabled={busy} onClick={() => onExpand?.(snapshotSeed(), dirty)}>
            <ExpandIcon size={16} />
          </button>
          {closeButton}
        </div>
      ) : (
        /* At 360px title + Send + Save draft + ✕ overflowed the band, which does not wrap, and pushed
           the ✕ off screen: the title goes (the subject says it) and Save draft moves into a menu. */
        <div className="compose-header">
          {!narrow && <span className="modal-title">{title}</span>}
          {sendButton}
          {narrow ? (
            <DropdownMenu
              ariaLabel={t('header.moreActions')}
              className="btn btn-ghost compose-header-more"
              trigger={<KebabIcon size={16} />}
              items={[{ label: saveLabel, disabled: !canSaveDraft, onSelect: () => saveDraft() }]}
            />
          ) : saveButton}
          {closeButton}
        </div>
      )}

      <ComposeFields folded={folded}
        onUnfold={() => { setFieldsFolded(false); if (to.length === 0) setRefocusTo(true) }}
        identity={identity} identityList={identityList} effectiveFrom={effectiveFrom} changeFrom={changeFrom}
        to={to} changeTo={changeTo} autoFocusTo={!seed || refocusTo} cc={cc} changeCc={changeCc}
        bcc={bcc} changeBcc={changeBcc} contacts={contacts} groupOptions={groupOptions}
        notifyEmptyGroup={notifyEmptyGroup} showCc={showCc} setShowCc={setShowCc} showBcc={showBcc}
        setShowBcc={setShowBcc} showPriority={showPriority} setShowPriority={setShowPriority}
        priority={priority} changePriority={changePriority} subject={subject} changeSubject={changeSubject} />

      <EditorToolbar editor={editor} active={active} plainText={plainText}
        switchLocked={inlineUploads > 0}
        onPickImages={routeFiles}
        onAddFiles={addFiles}
        folded={windowed}
        onTogglePlainText={() => (text === null ? toPlainText() : toHtml())} />
      {/* The fold keys on the caret entering the body, never on To or the subject being filled.
          `onFocus` because React's focusin bubbles out of Squire and the textarea alike. */}
      <div className="compose-body" {...dropZone.body}
        onFocus={() => { setFieldsFolded(true); setRefocusTo(false) }}>
        {text === null ? (
          <SquireEditor ref={setEditor} initialHtml={editorHtml} onChange={touchBody} onFormatChange={setActive} />
        ) : (
          <textarea className="compose-editor" data-testid="compose-text-editor" aria-label={t('fields.body')}
            value={text} onChange={e => { setText(e.target.value); touchBody() }} />
        )}
      </div>

      <AttachmentTray items={attachments.items} onRemove={removeFile} />

      {windowed && <div className="quick-compose-footer">{saveButton}{sendButton}</div>}

      {blockerProps && <LeaveBlocker {...blockerProps} />}

      {asking && (
        <LeaveDialog busy={busy} canSaveDraft={canSaveDraft} allValid={allValid} onKeepEditing={keepEditing}
          // The staged copies are scratch either way: a saved draft holds its own bytes in IMAP.
          onDiscard={leaveBehind}
          onSaveDraft={() => saveDraft(leaveBehind)} />
      )}

      {/* No ✕, like the leave guard: both of its answers are on its buttons. */}
      {confirmPlain && (
        <Modal role="alertdialog" title={t('plainText.title')} onEscape={() => setConfirmPlain(false)}>
          <p>{t('plainText.body')}</p>
          <div className="folder-pick-submit">
            <button type="button" className="btn btn-ghost" onClick={() => setConfirmPlain(false)}>
              {t('plainText.keepFormatting')}
            </button>
            <button type="button" className="btn btn-primary" onClick={switchToPlainText}>{t('plainText.confirm')}</button>
          </div>
        </Modal>
      )}

      {dropZone.dropTarget && (
        <div className="compose-drop-overlay">
          {t(dropZone.bodyInsert ? 'drop.image' : 'drop.files')}
        </div>
      )}
    </div>
  )
}
