import { cloneElement, useEffect, useMemo, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { mailAttachmentUrl, requestBlob } from '../../../api.js'
import { downloadBlob } from '../../../lib/downloadBlob'
import { useAuth } from '../../../contexts/AuthContext'
import { useTheme } from '../../../contexts/ThemeContext'
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
import { formatReaderDate } from './formatReaderDate'
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

const NO_ARCHIVE = 'Assign the archive folder in Settings → Folders'
const NO_JUNK = 'Assign the junk folder in Settings → Folders'

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
  const { data, isLoading, isError } = useMessage(folderPath, uid)
  const { isDark } = useTheme()
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
          aria-label="Back to the message list"
          onClick={onBack}
        >
          <ArrowLeftIcon size={16} />
        </button>
      )}
      <p className="mail-empty">{message}</p>
    </div>
  )

  if (uid === null) return <p className="mail-empty">Select a message</p>
  if (isLoading) return fallback('Loading message…')
  if (isError || !data) return fallback('Could not load this message.')

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

  async function download(part: string, fileName: string) {
    setDownloadError(null)
    try {
      const result = await requestBlob(mailAttachmentUrl(folderPath!, uid!, part, accountId))
      downloadBlob(result.blob, result.fileName || fileName)
    } catch (error) {
      setDownloadError(error instanceof Error ? error.message : 'Could not download the attachment')
    }
  }

  // Delete outside the trash is a move to it — the trash is the undo, so nothing to confirm.
  const inTrash = folderRole === 'trash'
  const deleteLabel = inTrash ? 'Delete permanently' : 'Delete'
  const deleteDisabled = !inTrash && !roles.trash
  const archiveOff = !roles.archive || folderRole === 'archive'
  const archiveReason = folderRole === 'archive' ? 'Already in the archive folder' : NO_ARCHIVE
  const junkOff = !roles.junk || folderRole === 'junk'
  const junkReason = folderRole === 'junk' ? 'Already in the junk folder' : NO_JUNK

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
      onNotify?.(error instanceof Error ? error.message : 'Could not prepare the message')
    }
  }

  const actions: MenuEntry[] = [
    {
      label: 'Archive', icon: <ArchiveIcon size={18} />,
      onSelect: () => moveTo(roles.archive, false),
      disabled: archiveOff, title: archiveOff ? archiveReason : undefined,
    },
    {
      label: 'Report as junk', icon: <JunkIcon size={18} />,
      onSelect: () => moveTo(roles.junk, false),
      disabled: junkOff, title: junkOff ? junkReason : undefined,
    },
    { label: 'Move to…', icon: <FolderMoveIcon size={18} />, onSelect: () => setPicker({ mode: 'move' }) },
    { label: 'Copy to…', icon: <CopyIcon size={18} />, onSelect: () => setPicker({ mode: 'copy' }) },
    { label: 'Edit as new', icon: <PencilIcon size={18} />, onSelect: () => void openCompose('editAsNew') },
  ]

  // Only for an approved sender, and only while nothing else is already showing the images: with
  // the global setting or the book doing it, revoking changes nothing on screen, and an entry
  // whose effect is invisible misleads.
  if (senderApproved && !alwaysShow && !contactTrusted) {
    actions.push('separator', {
      label: "Block sender's images",
      icon: <ImageOffIcon size={18} />,
      onSelect: () => setTrust.mutate({ address: senderAddress, trusted: false }),
    })
  }

  // Its own group: this is neither a flag nor a move but a look at the bytes. A link rather
  // than a button so middle-click and Ctrl+click open the tab the entry promises.
  actions.push('separator', {
    label: 'View source',
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
                aria-label="Back to the message list"
                onClick={onBack}
              >
                <ArrowLeftIcon size={16} />
              </button>
            )}
            {data.subject || '(no subject)'}
            {data.priority !== 'normal' && (
              <Tooltip
                placement="bottom-left"
                content={data.priority === 'high'
                  ? 'The sender marked this message X-Priority: 1 (Highest)'
                  : 'The sender marked this message X-Priority: 5 (Lowest)'}
              >
                <span className={`reader-priority is-${data.priority}`}>
                  {data.priority === 'high' ? 'High priority' : 'Low priority'}
                </span>
              </Tooltip>
            )}
          </h1>
          <div className="reader-meta">
            <div className="reader-from">
              <AddressLabel sender name={data.fromName} address={data.fromAddress} />
              <AuthBadge authentication={data.authentication} />
              <span className="reader-date">({formatReaderDate(data.date)})</span>
              <button
                type="button"
                className={`details-toggle${detailsOpen ? ' is-open' : ''}`}
                aria-expanded={detailsOpen}
                aria-label={detailsOpen ? 'Hide details' : 'Show details'}
                onClick={() => setDetailsOpen(open => !open)}
              >
                <ChevronRightIcon size={12} />
              </button>
              {/* Unsubscribing acts on the sender, not on this message — hence here, not in the
                  actions zone. */}
              {unsubscribe && (
                <a className="unsub-btn" href={unsubscribe} target="_blank" rel="noopener noreferrer">
                  <ExternalLinkIcon />
                  Unsubscribe
                </a>
              )}
            </div>
            {detailsOpen ? (
              <ReaderDetails message={data} />
            ) : (
              <>
                {data.to.length > 0 && (
                  <div className="reader-recipients">To: <AddressList addresses={data.to} /></div>
                )}
                {data.cc.length > 0 && (
                  <div className="reader-recipients">Cc: <AddressList addresses={data.cc} /></div>
                )}
              </>
            )}
            {!!preferences && showSpamScoreOf(preferences) && <SpamGauge spamScore={data.spamScore} />}
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

      {data.blockedImageCount > 0 && !showImages && (
        <div className="reader-blocked-images">
          <span>
            {data.blockedImageCount} remote image{data.blockedImageCount > 1 ? 's were' : ' was'} blocked.
            Loading them tells the sender you opened this message.
          </span>
          {/* The chevron can only ever grant: an approved sender has no banner to hang it from. */}
          <span className="banner-split">
            <button type="button" className="btn" onClick={() => setImagesShown(true)}>Show images</button>
            <DropdownMenu
              ariaLabel="More image options"
              className="banner-split-more"
              trigger={<ChevronDownIcon size={13} />}
              items={[{
                label: 'Always show images from this sender',
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
          title="Message body"
          srcDoc={renderBodyDocument(body, { dark: inverted })}
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
                  ariaLabel={`More actions for ${attachment.fileName}`}
                  className="attachment-split-more"
                  trigger={<ChevronUpIcon size={13} />}
                  items={[
                    { label: 'Download', onSelect: () => download(attachment.part, attachment.fileName) },
                    { label: 'View', onSelect: () => setViewed(attachment) },
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
          entityLabel={data.subject || '(no subject)'}
          onConfirm={expunge}
          onClose={() => setConfirmDelete(false)}
          loading={deleteMessages.isPending}
        />
      )}
    </article>
  )
}
