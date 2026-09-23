import { useEffect, useMemo, useState, type RefObject } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router'
import { mailAttachmentUrl, requestBlob } from '../../../api.js'
import { downloadBlob } from '../../../lib/downloadBlob'
import { useAuth } from '../../../contexts/AuthContext'
import { useTheme } from '../../../contexts/ThemeContext'
import { useViewport } from '../../../hooks/useViewport'
import ArrowLeftIcon from '../../../icons/ArrowLeftIcon'
import ArchiveIcon from '../../../icons/ArchiveIcon'
import JunkIcon from '../../../icons/JunkIcon'
import FolderMoveIcon from '../../../icons/FolderMoveIcon'
import CopyIcon from '../../../icons/CopyIcon'
import PencilIcon from '../../../icons/PencilIcon'
import ChevronDownIcon from '../../../icons/ChevronDownIcon'
import ImageOffIcon from '../../../icons/ImageOffIcon'
import CodeIcon from '../../../icons/CodeIcon'
import { apiErrorMessage } from '../../../lib/apiErrorMessage'
import {
  useAccountId, useAliases, useDeleteMessages, useFolders, useIdentities, useMessage,
  useMoveMessages, usePrepareQuote, useSetFlags, useTrustSender, useTrustedSenders,
} from '../queries'
import { rolePathsOf } from '../folders/folderNodes'
import DropdownMenu, { type MenuEntry } from '../../../components/DropdownMenu'
import type { MailAttachmentInfo, SpecialUse } from '../api/mailTypes'
import DeleteConfirmModal from '../../../components/DeleteConfirmModal'
import MoveMessagesModal from '../MoveMessagesModal'
import {
  alwaysShowImagesOf, showSpamScoreOf, trustContactsOf, usePreferences,
} from '../../../hooks/usePreferences'
import { useContacts } from '../../contacts/queries'
import { buildComposeSeed, type ComposeAction } from '../compose/composeSeed'
import { canonicalAddress } from '../../../lib/canonicalAddress'
import { hasOpenLayer } from '../../../lib/layerStack'
import ReaderActions from './ReaderActions'
import ReaderHeader from './ReaderHeader'
import ReaderAttachments from './ReaderAttachments'
import { isWebUnsubscribe } from './unsubscribeLink'
import { darkenColours } from './darkenColours'
import { renderBodyDocument, revealBlockedImages, sanitizeBody } from './sanitizeBody'
import { substituteInlineImages } from './inlineImages'
import { isCalendarType, isImageType } from './mediaType'
import InvitationCard from './InvitationCard'
import { bodyInlineParts, useInlineImages } from './useInlineImages'
import { useCachedSummaryFlags, useMarkSeenOnOpen } from './useMarkSeenOnOpen'

interface Props {
  folderPath: string | null
  uid: number | null
  /** The open folder's own role: inside the trash, deleting expunges instead of moving. */
  folderRole?: SpecialUse | null
  onBack?: () => void
  onNotify?: (message: string) => void
  onDeparted?: (uid: number) => void
  /** The list's exit, so a message deleted from here leaves its row the way the row's own icon
      would. Absent means "remove it now" — the reader itself animates nothing. */
  depart?: (uids: number[], fire: () => void) => void
  /** Draw the actions across the foot of the column instead of inside the header. The caller's
      call, not this component's: only the layout knows whether the reader owns the screen. */
  bottomActions?: boolean
  /** The list column, where focus goes when an expunge takes the reader's own Delete with it. */
  regionRef?: RefObject<HTMLElement | null>
}

export default function MessageReader(
  { folderPath, uid, folderRole, onBack, onNotify, onDeparted, depart, bottomActions,
    regionRef }: Props) {
  const { t } = useTranslation('mail')
  const { data, isLoading, isError } = useMessage(folderPath, uid)
  const { isDark } = useTheme()
  const viewportNarrow = useViewport() === 'phone'
  // Frozen per open message: a changed srcDoc reloads the iframe, so a phone rotation crossing 639px
  // would throw the reader back to the top. Reset during render, not in an effect, so the message
  // just opened never paints one frame with the previous one's padding.
  const readerKey = `${folderPath ?? ''}:${uid ?? ''}`
  const [frozenNarrow, setFrozenNarrow] = useState(() => ({ key: readerKey, value: viewportNarrow }))
  const { data: preferences } = usePreferences()
  const { data: folders } = useFolders()
  const [imagesShown, setImagesShown] = useState(false)
  const [originalColours, setOriginalColours] = useState(false)
  const [downloadError, setDownloadError] = useState<string | null>(null)
  const [detailsOpen, setDetailsOpen] = useState(false)
  const [picker, setPicker] = useState<{ mode: 'move' | 'copy' } | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [viewed, setViewed] = useState<MailAttachmentInfo | null>(null)
  // The guarded write means a committed render always has frozenNarrow.key === readerKey — the
  // mismatched render it corrects is discarded before commit — so no committed render pairs a
  // message with the previous one's consent, colour choice or open dialog either.
  if (frozenNarrow.key !== readerKey) {
    setFrozenNarrow({ key: readerKey, value: viewportNarrow })
    setImagesShown(false)
    setOriginalColours(false)
    setDownloadError(null)
    setDetailsOpen(false)
    setPicker(null)
    setConfirmDelete(false)
    setViewed(null)
  }
  const narrow = frozenNarrow.value
  const accountId = useAccountId()
  const setFlags = useSetFlags(onNotify)
  const moveMessages = useMoveMessages(onNotify)
  const deleteMessages = useDeleteMessages(onNotify)
  const leave = depart ?? ((_uids: number[], fire: () => void) => fire())
  const roles = useMemo(() => rolePathsOf(folders ?? []), [folders])
  const navigate = useNavigate()
  const { identity, activeAccount } = useAuth()
  const { data: identityList } = useIdentities()
  const { data: aliases } = useAliases()
  const prepare = usePrepareQuote()
  const { data: trustedSenders } = useTrustedSenders()
  const setTrust = useTrustSender(onNotify)
  const trustContacts = !!preferences && trustContactsOf(preferences)
  const { data: contacts } = useContacts(trustContacts)

  useMarkSeenOnOpen(folderPath, uid, Boolean(data))
  const { seen, flagged } = useCachedSummaryFlags(folderPath, uid)

  // Escape mirrors the ← button; both exist only in the no-split mode, where the reader has
  // replaced the list and needs a way back. Any open layer owns Escape — the reader's own
  // dialogs and anything else on the screen alike — so the reader stays put under it.
  useEffect(() => {
    if (!onBack) return

    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !event.defaultPrevented && !hasOpenLayer()) onBack()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onBack])

  // The body is computed above the early returns because useInlineImages hangs off it and hooks
  // cannot be conditional; before the detail lands it works on an empty document.
  const inverted = isDark && !originalColours
  const senderAddress = canonicalAddress(data?.fromAddress)
  // Two booleans, not one: senderApproved is the explicit list and is what the revoke entry acts
  // on, while contactTrusted is computed and has nothing to revoke.
  const senderApproved = senderAddress !== '' && trustedSenders?.has(senderAddress) === true
  const contactTrusted = trustContacts && senderAddress !== ''
    && (contacts ?? []).some(c => c.addresses.some(a => canonicalAddress(a) === senderAddress))
  const alwaysShow = !!preferences && alwaysShowImagesOf(preferences)
  const showImages = imagesShown || alwaysShow || senderApproved || contactTrusted
  // Recolour before sanitising, so everything darkenColours writes faces the same pass as the
  // rest — the same reason revealBlockedImages runs on this side of it.
  const sanitized = useMemo(() => {
    const html = data?.htmlBody ?? ''
    const revealed = showImages ? revealBlockedImages(html) : html
    return sanitizeBody(inverted ? darkenColours(revealed) : revealed)
  }, [data?.htmlBody, showImages, inverted])
  // Inlined after sanitising, unlike the reveal: these data URIs are built from our own API's
  // bytes rather than from message markup, so they are not what the pass exists to police.
  const inlineImages = useInlineImages(folderPath, uid, data?.attachments, sanitized)
  const body = useMemo(
    () => substituteInlineImages(sanitized, inlineImages), [sanitized, inlineImages])
  // Derived from the body rather than from what the fetch returned, so a chip cannot appear and
  // then vanish once the bytes land.
  const displayedParts = useMemo(
    () => new Set(bodyInlineParts(data?.attachments, sanitized).map(p => p.part)),
    [data?.attachments, sanitized])

  const fallback = (message: string) => (
    <div className="reader-fallback">
      {onBack && (
        <button
          type="button"
          className="reader-back"
          aria-label={t('reader.back')}
          onClick={onBack}
        >
          <ArrowLeftIcon size={16} />
        </button>
      )}
      <p className="mail-empty">{message}</p>
    </div>
  )

  if (uid === null) return <p className="mail-empty">{t('reader.selectMessage')}</p>
  if (isLoading) return fallback(t('reader.loading'))
  if (isError || !data) return fallback(t('reader.loadFailed'))

  // Not offered as attachments: parts the server calls inline, parts the body displays (a cid image
  // often carries an attachment disposition), and the calendar file the card shows unless unreadable.
  const cardShown = !!data.invitation && !data.invitation.unreadable
  const attachments = data.attachments.filter(
    attachment => !attachment.isInline && !displayedParts.has(attachment.part)
      && !(cardShown && isCalendarType(attachment.contentType, attachment.fileName)))
  // One list for the split chips and the viewer's navigation — the two can never disagree.
  const imageAttachments = attachments.filter(a => isImageType(a.contentType))
  const unsubscribe = isWebUnsubscribe(data.unsubscribeUrl) ? data.unsubscribeUrl : null
  const spamOn = !!preferences && showSpamScoreOf(preferences)

  async function download(part: string, fileName: string) {
    setDownloadError(null)
    try {
      const result = await requestBlob(mailAttachmentUrl(folderPath!, uid!, part, accountId))
      downloadBlob(result.blob, result.fileName || fileName)
    } catch (error) {
      setDownloadError(apiErrorMessage(error, t('reader.downloadFailed')))
    }
  }

  // Delete outside the trash is a move to it — the trash is the undo, so nothing to confirm.
  const inTrash = folderRole === 'trash'
  const deleteLabel = inTrash
    ? t('actions.deletePermanently') : t('actions.delete', { ns: 'common' })
  const deleteDisabled = !inTrash && !roles.trash
  const archiveOff = !roles.archive || folderRole === 'archive'
  const archiveReason = t(folderRole === 'archive' ? 'actions.alreadyArchived' : 'actions.noArchiveFolder')
  const junkOff = !roles.junk || folderRole === 'junk'
  const junkReason = t(folderRole === 'junk' ? 'actions.alreadyJunk' : 'actions.noJunkFolder')

  function moveTo(target: string | null, copy: boolean) {
    if (!target) return
    const fire = () =>
      moveMessages.mutate({ folderPath: folderPath!, uids: [uid!], targetFolderPath: target, copy })
    // A copy departs nothing; the row stays, so there is nothing to play out.
    if (copy) { fire(); return }
    leave([uid!], fire)
    onDeparted?.(uid!)
  }

  function onDelete() {
    if (inTrash) setConfirmDelete(true)
    else moveTo(roles.trash, false)
  }

  function expunge() {
    leave([uid!], () => deleteMessages.mutate({ folderPath: folderPath!, uids: [uid!] }))
    setConfirmDelete(false)
    onDeparted?.(uid!)
  }

  async function openCompose(action: ComposeAction) {
    try {
      const purpose = action === 'editAsNew' ? 'editAsNew' : action === 'forward' ? 'forward' : 'reply'
      const prepared = await prepare.mutateAsync({ folder: folderPath!, uid: uid!, purpose })
      // Both mailboxes: the active account's address is the one a reply-all keeps mailing back,
      // and the primary's is in no alias list, so neither is covered by the other.
      const seed = buildComposeSeed(
        action, data!, prepared, identityList ?? [], aliases ?? [],
        [activeAccount?.email ?? null, identity?.email ?? null], accountId)
      void navigate('/mail/compose', { state: { from: folderPath, seed } })
    } catch (error) {
      onNotify?.(apiErrorMessage(error, t('reader.prepareFailed')))
    }
  }

  const actions: MenuEntry[] = [
    {
      label: t('toolbar.archive'), icon: <ArchiveIcon size={18} />,
      onSelect: () => moveTo(roles.archive, false),
      disabled: archiveOff, title: archiveOff ? archiveReason : undefined,
    },
    {
      label: t('toolbar.junk'), icon: <JunkIcon size={18} />,
      onSelect: () => moveTo(roles.junk, false),
      disabled: junkOff, title: junkOff ? junkReason : undefined,
    },
    { label: t('toolbar.moveTo'), icon: <FolderMoveIcon size={18} />, onSelect: () => setPicker({ mode: 'move' }) },
    { label: t('toolbar.copyTo'), icon: <CopyIcon size={18} />, onSelect: () => setPicker({ mode: 'copy' }) },
    { label: t('reader.editAsNew'), icon: <PencilIcon size={18} />, onSelect: () => void openCompose('editAsNew') },
  ]

  // Only for an approved sender, and only while nothing else is already showing the images: with
  // the global setting or the book doing it, revoking changes nothing on screen, and an entry
  // whose effect is invisible misleads.
  if (senderApproved && !alwaysShow && !contactTrusted) {
    actions.push('separator', {
      label: t('reader.blockSenderImages'),
      icon: <ImageOffIcon size={18} />,
      onSelect: () => setTrust.mutate({ address: senderAddress, trusted: false }),
    })
  }

  // Its own group: this is neither a flag nor a move but a look at the bytes. A link rather
  // than a button so middle-click and Ctrl+click open the tab the entry promises.
  actions.push('separator', {
    label: t('reader.viewSource'),
    icon: <CodeIcon size={18} />,
    href: `/mail/source?folder=${encodeURIComponent(folderPath!)}&uid=${uid}`,
  })

  const readerActions = (
    <ReaderActions
      bar={bottomActions}
      showColourToggle={isDark && !!data.htmlBody}
      originalColours={originalColours}
      onToggleColours={() => setOriginalColours(v => !v)}
      seen={seen}
      flagged={flagged}
      onToggleSeen={() => setFlags.mutate({ folderPath: folderPath!, uids: [uid], flag: 'seen', value: !seen })}
      onToggleFlagged={() =>
        setFlags.mutate({ folderPath: folderPath!, uids: [uid], flag: 'flagged', value: !flagged })}
      deleteLabel={deleteLabel}
      deleteDisabled={deleteDisabled}
      onDelete={onDelete}
      actions={actions}
      onReply={() => void openCompose('reply')}
      onReplyAll={() => void openCompose('replyAll')}
      onForward={() => void openCompose('forward')}
      preparing={prepare.isPending}
    />
  )

  return (
    <article>
      <ReaderHeader
        data={data}
        onBack={onBack}
        viewportNarrow={viewportNarrow}
        detailsOpen={detailsOpen}
        onToggleDetails={() => setDetailsOpen(open => !open)}
        unsubscribe={unsubscribe}
        spamOn={spamOn}
        bottomActions={bottomActions}
        readerActions={readerActions}
      />

      {/* The backend hit a sanitiser ceiling before parsing, so nothing restores the rest: say so and
          point at the full view, or a message ending mid-sentence reads as the sender's mistake. */}
      {data.truncated && (
        <div className="reader-truncated">
          {t('reader.truncated')}
        </div>
      )}

      {data.blockedImageCount > 0 && !showImages && (
        <div className="reader-blocked-images">
          <span>{t('reader.blockedImages', { count: data.blockedImageCount })}</span>
          {/* The chevron can only ever grant: an approved sender has no banner to hang it from. */}
          <span className="banner-split">
            <button type="button" className="btn" onClick={() => setImagesShown(true)}>{t('reader.showImages')}</button>
            <DropdownMenu
              ariaLabel={t('reader.moreImageOptions')}
              className="banner-split-more"
              trigger={<ChevronDownIcon size={13} />}
              items={[{
                label: t('reader.alwaysShowSender'),
                // A malformed message can carry images and no parsable sender; posting an empty
                // address would just earn a 400 nobody surfaces.
                disabled: senderAddress === '',
                onSelect: () => setTrust.mutate({ address: senderAddress, trusted: true }),
              }]}
            />
          </span>
        </div>
      )}

      {/* Keyed on the message: a card carries the answer it was given in its own state, and a
          second message must not open on the first one's. */}
      {data.invitation && (
        <InvitationCard
          key={`${folderPath}:${uid}`}
          invitation={data.invitation}
          folderPath={folderPath!}
          uid={uid}
          onTrashed={() => { leave([uid], () => {}); onDeparted?.(uid) }}
        />
      )}

      {data.htmlBody ? (
        // Third barrier after the backend and DOMPurify: never allow-scripts or allow-same-origin.
        // The popup permissions are what make the sanitiser's target="_blank" links open at all, and
        // the escape clause keeps the opened tab out of this sandbox. Never render bodies in the page.
        <iframe
          className="reader-body"
          sandbox="allow-popups allow-popups-to-escape-sandbox"
          title={t('reader.bodyTitle')}
          srcDoc={renderBodyDocument(body, { dark: inverted, narrow })}
        />
      ) : (
        <div className="reader-text">{data.textBody}</div>
      )}

      {downloadError && <div className="reader-blocked-images">{downloadError}</div>}

      <ReaderAttachments
        folderPath={folderPath}
        uid={uid}
        accountId={accountId}
        attachments={attachments}
        imageAttachments={imageAttachments}
        viewed={viewed}
        setViewed={setViewed}
        download={(part, fileName) => void download(part, fileName)}
      >
        {/* Last band of the column, so it sits on the screen's own edge — under the attachments,
            which belong to the message, not to the actions taken on it. */}
        {bottomActions && <div className="actionbar">{readerActions}</div>}
      </ReaderAttachments>

      {picker && (
        <MoveMessagesModal
          mode={picker.mode}
          folders={folders ?? []}
          currentFolderPath={folderPath!}
          onPick={target => { moveTo(target, picker.mode === 'copy'); setPicker(null) }}
          onClose={() => setPicker(null)}
        />
      )}

      {/* Only inside the trash: everywhere else deleting is a move, and the trash is the undo. */}
      {confirmDelete && (
        <DeleteConfirmModal
          entityLabel={data.subject || t('list.noSubject')}
          onConfirm={expunge}
          onClose={() => setConfirmDelete(false)}
          loading={deleteMessages.isPending}
          returnFocusRef={regionRef}
        />
      )}
    </article>
  )
}
