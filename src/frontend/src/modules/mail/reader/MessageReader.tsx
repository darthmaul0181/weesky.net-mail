import { cloneElement, useEffect, useMemo, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { mailAttachmentUrl, requestBlob } from '../../../api.js'
import { downloadBlob } from '../../../lib/downloadBlob'
import { useAuth } from '../../../contexts/AuthContext'
import { useTheme } from '../../../contexts/ThemeContext'
import { useViewport } from '../../../hooks/useViewport'
import PaperclipIcon from '../../../icons/PaperclipIcon'
import ChevronRightIcon from '../../../icons/ChevronRightIcon'
import ChevronUpIcon from '../../../icons/ChevronUpIcon'
import ArrowLeftIcon from '../../../icons/ArrowLeftIcon'
import ExternalLinkIcon from '../../../icons/ExternalLinkIcon'
import ArchiveIcon from '../../../icons/ArchiveIcon'
import JunkIcon from '../../../icons/JunkIcon'
import FolderMoveIcon from '../../../icons/FolderMoveIcon'
import CopyIcon from '../../../icons/CopyIcon'
import PencilIcon from '../../../icons/PencilIcon.jsx'
import ChevronDownIcon from '../../../icons/ChevronDownIcon'
import Tooltip from '../../../components/Tooltip'
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
import DeleteConfirmModal from '../../../components/DeleteConfirmModal.jsx'
import MoveMessagesModal from '../MoveMessagesModal'
import AttachmentViewerModal from './AttachmentViewerModal'
import {
  alwaysShowImagesOf, showSpamScoreOf, trustContactsOf, usePreferences,
} from '../../../hooks/usePreferences'
import { useContacts } from '../../contacts/queries'
import { buildComposeSeed, type ComposeAction } from '../compose/composeSeed'
import { formatReaderDate, formatReaderDateShort } from './formatReaderDate'
import { canonicalAddress } from '../../../lib/canonicalAddress'
import AddressLabel, { AddressList } from './AddressLabel'
import AuthBadge from './AuthBadge'
import SpamGauge from './SpamGauge'
import ReaderActions from './ReaderActions'
import ReaderDetails from './ReaderDetails'
import { isWebUnsubscribe } from './unsubscribeLink'
import { formatSize } from './formatSize'
import { darkenColours } from './darkenColours'
import { renderBodyDocument, revealBlockedImages, sanitizeBody } from './sanitizeBody'
import { substituteInlineImages } from './inlineImages'
import { isImageType } from './mediaType'
import { bodyInlineParts, useInlineImages } from './useInlineImages'
import { findCachedSummary, useMarkSeenOnOpen } from './useMarkSeenOnOpen'

interface Props {
  folderPath: string | null
  uid: number | null
  /** The open folder's own role: inside the trash, deleting expunges instead of moving. */
  folderRole?: SpecialUse | null
  onBack?: () => void
  onNotify?: (message: string) => void
  onDeparted?: (uid: number) => void
}

export default function MessageReader(
  { folderPath, uid, folderRole, onBack, onNotify, onDeparted }: Props) {
  const { t } = useTranslation('mail')
  const { data, isLoading, isError } = useMessage(folderPath, uid)
  const { isDark } = useTheme()
  const viewportNarrow = useViewport() === 'phone'
  // Frozen per open message rather than tracking the viewport live: a changed srcDoc is a fresh
  // iframe document load, and `narrow` crosses the 639px boundary on a phone rotation — reloading
  // the body and throwing a reader midway through a long message back to the top. Adjusted during
  // render rather than in an effect, per React's own pattern for resetting state when a prop
  // changes: an effect fires after commit, so the message that just opened would paint one frame
  // with the PREVIOUS message's padding before the effect corrected it. Calling setState here
  // instead discards this render's output and replays the component immediately with the new
  // state already in place — the freeze takes effect in the same render that opens the message,
  // with no extra reload and no visible frame in between.
  const readerKey = `${folderPath ?? ''}:${uid ?? ''}`
  const [frozenNarrow, setFrozenNarrow] = useState(() => ({ key: readerKey, value: viewportNarrow }))
  // The guarded write means a committed render always has frozenNarrow.key === readerKey — the
  // mismatched render it corrects is discarded before commit — so reading `.value` needs no
  // fallback for the case that never reaches the screen.
  if (frozenNarrow.key !== readerKey) setFrozenNarrow({ key: readerKey, value: viewportNarrow })
  const narrow = frozenNarrow.value
  const { data: preferences } = usePreferences()
  const { data: folders } = useFolders()
  const [imagesShown, setImagesShown] = useState(false)
  const [originalColours, setOriginalColours] = useState(false)
  const [downloadError, setDownloadError] = useState<string | null>(null)
  const [detailsOpen, setDetailsOpen] = useState(false)
  const [picker, setPicker] = useState<{ mode: 'move' | 'copy' } | null>(null)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const [viewed, setViewed] = useState<MailAttachmentInfo | null>(null)
  const accountId = useAccountId()
  const queryClient = useQueryClient()
  const setFlags = useSetFlags(onNotify)
  const moveMessages = useMoveMessages(onNotify)
  const deleteMessages = useDeleteMessages(onNotify)
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

  // Consent is per message and never carried to the next one. So is the colour choice: a mail
  // that recolours badly says nothing about the next one.
  useEffect(() => {
    setImagesShown(false)
    setOriginalColours(false)
    setDownloadError(null)
    setDetailsOpen(false)
    setPicker(null)
    setConfirmDelete(false)
    setViewed(null)
  }, [folderPath, uid])

  // Escape mirrors the ← button; both exist only in the no-split mode, where the reader has
  // replaced the list and needs a way back. An open modal owns Escape, so the reader stays put
  // rather than backing out from under the picker, the confirm, or the attachment viewer.
  useEffect(() => {
    if (!onBack || picker || confirmDelete || viewed) return

    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onBack() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onBack, picker, confirmDelete, viewed])

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

  // Recomputed on every re-render of THIS component — driven by its own useSetFlags settling
  // or by useMessage refetching, never by the menu opening (that toggles state inside
  // DropdownMenu). No summary (deep link): read and unstarred, since opening just marked it read.
  const summary = findCachedSummary(queryClient, accountId, folderPath!, uid!)
  const seen = summary?.seen ?? true
  const flagged = summary?.flagged ?? false

  // Two ways a part is not an attachment to offer: the server calls it inline, or the body
  // displays it — a cid-referenced image often arrives with an attachment disposition.
  const attachments = data.attachments.filter(
    attachment => !attachment.isInline && !displayedParts.has(attachment.part))
  // One list for the split chips and the viewer's navigation — the two can never disagree.
  const imageAttachments = attachments.filter(a => isImageType(a.contentType))
  const unsubscribe = isWebUnsubscribe(data.unsubscribeUrl) ? data.unsubscribeUrl : null
  const spamOn = !!preferences && showSpamScoreOf(preferences)
  // A phone header spends four of its lines on metadata before the body starts. Two of them are
  // recovered here: the date shrinks to its locale's short form and joins the recipients line,
  // and the gauge moves behind the chevron. Both stay in full inside the details grid.
  const compactDate = viewportNarrow
    ? <span className="reader-date">{formatReaderDateShort(data.date)}</span> : null

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
    moveMessages.mutate({ folderPath: folderPath!, uids: [uid!], targetFolderPath: target, copy })
    if (!copy) onDeparted?.(uid!)  // A copy departs nothing; the row stays.
  }

  function onDelete() {
    if (inTrash) setConfirmDelete(true)
    else moveTo(roles.trash, false)
  }

  function expunge() {
    deleteMessages.mutate({ folderPath: folderPath!, uids: [uid!] })
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
      navigate('/mail/compose', { state: { from: folderPath, seed } })
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

  return (
    <article>
      <header className="reader-header">
        <div className="reader-stack">
          <h1 className="reader-subject">
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
            {/* Its own element so the phone block can ellipsise the SUBJECT rather than the h1:
                a box with element children that clips is a real defect everywhere else in this
                app, and probes/mobile-layout.html only forgives an ellipsised leaf. */}
            <span className="reader-subject-text">{data.subject || t('list.noSubject')}</span>
            {data.priority !== 'normal' && (
              <Tooltip
                placement="bottom-left"
                content={t(data.priority === 'high' ? 'reader.priorityHigh' : 'reader.priorityLow')}
              >
                <span className={`reader-priority is-${data.priority}`}>
                  {t(data.priority === 'high' ? 'list.highPriority' : 'list.lowPriority')}
                </span>
              </Tooltip>
            )}
          </h1>
          <div className="reader-meta">
            <div className="reader-from">
              <AddressLabel sender name={data.fromName} address={data.fromAddress} />
              <AuthBadge authentication={data.authentication} />
              {!viewportNarrow && <span className="reader-date">({formatReaderDate(data.date)})</span>}
              <button
                type="button"
                className={`details-toggle${detailsOpen ? ' is-open' : ''}`}
                aria-expanded={detailsOpen}
                aria-label={t(detailsOpen ? 'reader.hideDetails' : 'reader.showDetails')}
                onClick={() => setDetailsOpen(open => !open)}
              >
                <ChevronRightIcon size={12} />
              </button>
              {/* Unsubscribing acts on the sender, not on this message — hence here, not in the
                  actions zone. Not at all on a phone: at 360px the sender line has 316px and a
                  mailing list's name spends most of it, so the pill never shared the line and
                  always cost a whole one — 52px with its gutter, for a control the details grid
                  already lists a chevron away. An icon-only pill saves nothing, 44px not fitting
                  any better than 141. */}
              {!viewportNarrow && unsubscribe && (
                <a className="unsub-btn" href={unsubscribe} target="_blank" rel="noopener noreferrer">
                  <ExternalLinkIcon />
                  {t('reader.unsubscribe')}
                </a>
              )}
            </div>
            {detailsOpen ? (
              <ReaderDetails
                message={data}
                showSubject={viewportNarrow}
                showSpamScore={viewportNarrow && spamOn}
              />
            ) : (
              <>
                {(data.to.length > 0 || compactDate) && (
                  <div className="reader-recipients reader-to-row">
                    {data.to.length > 0 && (
                      <span className="reader-to">{t('reader.details.to')} <AddressList addresses={data.to} /></span>
                    )}
                    {compactDate}
                  </div>
                )}
                {data.cc.length > 0 && (
                  <div className="reader-recipients">{t('reader.details.cc')} <AddressList addresses={data.cc} /></div>
                )}
              </>
            )}
            {spamOn && !viewportNarrow && <SpamGauge spamScore={data.spamScore} />}
          </div>
        </div>
        <ReaderActions
          showColourToggle={isDark && !!data.htmlBody}
          originalColours={originalColours}
          onToggleColours={() => setOriginalColours(v => !v)}
          seen={seen}
          flagged={flagged}
          onToggleSeen={() => setFlags.mutate({ folderPath: folderPath!, uids: [uid!], flag: 'seen', value: !seen })}
          onToggleFlagged={() =>
            setFlags.mutate({ folderPath: folderPath!, uids: [uid!], flag: 'flagged', value: !flagged })}
          deleteLabel={deleteLabel}
          deleteDisabled={deleteDisabled}
          onDelete={onDelete}
          actions={actions}
          onReply={() => void openCompose('reply')}
          onReplyAll={() => void openCompose('replyAll')}
          onForward={() => void openCompose('forward')}
          preparing={prepare.isPending}
        />
      </header>

      {/* The backend hit one of its sanitiser ceilings. Nothing here can restore the rest — the
          cut happens before the body is ever parsed — so this states the fact and points at the
          one view that still shows the whole thing. Silence would be worse than the cut: a
          message ending mid-sentence reads as the sender's mistake. */}
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

      {data.htmlBody ? (
        // Three independent barriers: the backend sanitised this, DOMPurify sanitised it again
        // with a different parser, and this iframe can neither run scripts nor reach our
        // origin. Message HTML is never rendered into the page itself.
        //
        // The two popup permissions are what make links work at all. A fully empty sandbox
        // withholds every capability including navigation, so target="_blank" anchors — which
        // is what the sanitiser rewrites every link into — silently do nothing on click. The
        // escape clause matters as much as the popup one: without it the opened tab inherits
        // this sandbox and the destination site loads scriptless and broken. Neither grants
        // allow-scripts or allow-same-origin, so the message body itself stays inert.
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

      {attachments.length > 0 && (
        <div className="reader-attachments">
          {attachments.map(attachment => {
            // Built without a key: the non-image branch clones one on (no wrapping element,
            // byte-identical DOM to the old unconditional loop), the image branch keys the
            // wrapping <span> instead, since the chip is no longer the array's direct child.
            const chip = (
              <button
                type="button"
                className="attachment-chip"
                onClick={() => download(attachment.part, attachment.fileName)}
              >
                <PaperclipIcon size={14} />
                {attachment.fileName}
                <span className="attachment-chip-size">{formatSize(attachment.size)}</span>
              </button>
            )
            if (!imageAttachments.includes(attachment)) {
              return cloneElement(chip, { key: attachment.part })
            }
            return (
              <span key={attachment.part} className="attachment-split">
                {chip}
                <DropdownMenu
                  direction="up"
                  ariaLabel={t('reader.moreActionsFor', { name: attachment.fileName })}
                  className="attachment-split-more"
                  trigger={<ChevronUpIcon size={13} />}
                  items={[
                    { label: t('reader.download'), onSelect: () => download(attachment.part, attachment.fileName) },
                    { label: t('reader.view'), onSelect: () => setViewed(attachment) },
                  ]}
                />
              </span>
            )
          })}
        </div>
      )}

      {viewed && (
        <AttachmentViewerModal
          images={imageAttachments.map(a => ({
            part: a.part,
            src: mailAttachmentUrl(folderPath!, uid!, a.part, accountId),
            fileName: a.fileName,
            size: a.size,
          }))}
          initialIndex={Math.max(0, imageAttachments.findIndex(a => a.part === viewed.part))}
          onDownload={image => download(image.part, image.fileName)}
          onClose={() => setViewed(null)}
        />
      )}

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
        />
      )}
    </article>
  )
}
