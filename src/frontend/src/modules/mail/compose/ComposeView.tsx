import { useCallback, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import type { TFunction } from 'i18next'
import { useLocation, useNavigate } from 'react-router'
import { useAuth } from '../../../contexts/AuthContext'
import type { Toast, ToastAction } from '../../../hooks/useToasts'
import { useViewport } from '../../../hooks/useViewport'
import { useContactGroups, useContacts } from '../../contacts/queries'
import { groupOptionsOf } from '../../contacts/contactSearch'
import { capturable } from '../../contacts/captureModel'
import { useCaptureContacts } from '../../contacts/useCaptureContacts'
import { displayNameOf } from '../../contacts/contactName'
import { captureRecipientsOf, composeFormatOf, usePreferences, type Preferences } from '../../../hooks/usePreferences'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import { useAccountId, useDeleteMessages, useIdentities, useSaveDraft, useSendMessage } from '../queries'
import DropdownMenu from '../../../components/DropdownMenu'
import Modal from '../../../components/Modal'
import KebabIcon from '../../../icons/KebabIcon'
import RocketIcon from '../../../icons/RocketIcon'
import type { MailPriority } from '../api/mailTypes'
import AttachmentTray from './AttachmentTray'
import { htmlToText, losesFormatting, textToHtml } from './bodyFormat'
import EditorToolbar from './EditorToolbar'
import ComposeFields from './ComposeFields'
import { mailtoSeedFrom } from './mailtoSeed'
import { isValidAddress } from './RecipientsField'
import SquireEditor, { type ActiveFormats, type EditorHandle } from './SquireEditor'
import { applyComposeFormat, type ComposeAction, type ComposeSeed } from './composeSeed'
import LoadingBlock from '../../../components/LoadingBlock'
import { relativizeStagedUrls } from './stagedUrls'
import { useStagedAttachments } from './useStagedAttachments'
import { useComposeDropZone } from './useComposeDropZone'
import { useInlineUploads, type InsertedInline } from './useInlineUploads'
import { useLeaveGuard } from './useLeaveGuard'
import LeaveDialog from './LeaveDialog'

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

type ComposeState = { from?: string; seed?: ComposeSeed; backTo?: string } | null

/* `from` is a folder and only the mail module has one. `backTo` is a whole path, for a caller
   outside the module — the contact card's Write — so leaving returns to the card that opened it. */
function backTargetOf(state: ComposeState): string {
  return state?.backTo ?? (state?.from ? `/mail?folder=${encodeURIComponent(state.from)}` : '/mail')
}

interface Props {
  onNotify: (message: string, kind?: Toast['type'], action?: ToastAction) => void
}

// Replaces list+reader inside the mail module; `useLeaveGuard` owns every exit while something is
// unsaved. A reply/forward/draft arrives as `location.state.seed`, a new message carries none.
export default function ComposeView(props: Props) {
  const { t } = useTranslation('compose')
  const navigate = useNavigate()
  const location = useLocation()
  const { data: preferences, isError, isFetching, refetch } = usePreferences()
  // The form reads the default editor once, at mount: mounted before preferences are known, a cold
  // load (a mailto: link, a reload) would open in HTML whatever the account chose.
  if (preferences) return <ComposeForm {...props} preferences={preferences} />
  if (!isError || isFetching) return <div className="mail-full-pane"><LoadingBlock /></div>
  // Close as well as Retry: on a phone the composer hides every other way out of this route.
  return (
    <div className="mail-full-pane">
      <p>{t('general.loadFailed', { ns: 'settings' })}</p>
      <button type="button" className="btn btn-primary" onClick={() => void refetch()}>
        {t('list.retry', { ns: 'mail' })}
      </button>
      <button type="button" className="btn btn-ghost" onClick={() => void navigate(backTargetOf(location.state as ComposeState))}>
        {t('actions.close', { ns: 'common' })}
      </button>
    </div>
  )
}

function ComposeForm({ onNotify, preferences }: Props & { preferences: Preferences }) {
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

  const state = location.state as ComposeState
  // A mailto: arrives through the URL, not through the history state: the operating system opens
  // a cold address, with no React navigation behind it.
  const rawSeed = useMemo(
    () => state?.seed ?? mailtoSeedFrom(location.search), [state?.seed, location.search])
  const openedFormat = composeFormatOf(preferences)
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
  const [insertedInline, setInsertedInline] = useState<InsertedInline[]>([])
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

  // The non-empty clause only applies where no version is filed: emptying a draft is a change too.
  // An inserted inline image counts on its own rather than resting on Squire having fired `input`.
  const hasContent = draftRef !== null || to.length > 0 || cc.length > 0 || bcc.length > 0
    || subject !== '' || bodyTouched || attachments.items.length > 0 || insertedInline.length > 0
  const backTarget = backTargetOf(state)
  const { dirty, markDirty, markClean, leave, asking, keepEditing, leaveBehind } = useLeaveGuard(
    Boolean(seed) && seed?.action !== 'draft', hasContent, backTarget)
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
  const canSend = to.length > 0 && allValid && !busy
  const canSaveDraft = allValid && !busy

  // On a phone the keyboard leaves ~406px, so the fields fold while the caret is in the body.
  // `narrow &&` rather than a flag reset on rotation: growing past 639px brings them back unasked.
  const [fieldsFolded, setFieldsFolded] = useState(false)
  const [refocusTo, setRefocusTo] = useState(false)
  const folded = narrow && fieldsFolded

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
    // draft open re-staged copies of them, so nothing here is the only copy of anything.
    if (!dirty) { attachments.discardAll(); leave(); return }
    // Dirty: navigate anyway — the blocker turns it into the save-or-discard question.
    void navigate(backTarget)
  }

  return (
    <div className="compose-view" data-testid="compose-view" {...dropZone.surface}>
      {/* At 360px title + Send + Save draft + ✕ overflowed the band, which does not wrap, and pushed
          the ✕ off screen: the title goes (the subject says it) and Save draft moves into a menu. */}
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

      {asking && (
        <LeaveDialog busy={busy} canSaveDraft={canSaveDraft} allValid={allValid} onKeepEditing={keepEditing}
          onDiscard={() => {
            // The staged copies are scratch either way: a saved draft holds its own bytes in IMAP.
            attachments.discardAll()
            leaveBehind()
          }}
          onSaveDraft={() => saveDraft(() => {
            attachments.discardAll()
            leaveBehind()
          })} />
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
