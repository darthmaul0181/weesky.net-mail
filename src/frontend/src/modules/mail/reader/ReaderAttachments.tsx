import { cloneElement, type Dispatch, type ReactNode, type SetStateAction } from 'react'
import { useTranslation } from 'react-i18next'
import { mailAttachmentUrl } from '../../../api.js'
import PaperclipIcon from '../../../icons/PaperclipIcon'
import ChevronUpIcon from '../../../icons/ChevronUpIcon'
import DropdownMenu from '../../../components/DropdownMenu'
import type { MailAttachmentInfo } from '../api/mailTypes'
import AttachmentViewerModal from './AttachmentViewerModal'
import { formatSize } from './formatSize'

interface Props {
  folderPath: string | null
  uid: number
  accountId: string
  attachments: MailAttachmentInfo[]
  imageAttachments: MailAttachmentInfo[]
  viewed: MailAttachmentInfo | null
  setViewed: Dispatch<SetStateAction<MailAttachmentInfo | null>>
  download: (part: string, fileName: string) => void
  /** Drawn between the chip list and the viewer launch, so a caller's own band (the phone foot
      actionbar) keeps its place in the column's DOM order. */
  children?: ReactNode
}

/** The non-inline attachment chips and the modal a "View" menu entry opens onto. */
export default function ReaderAttachments({
  folderPath, uid, accountId, attachments, imageAttachments, viewed, setViewed, download, children,
}: Props) {
  const { t } = useTranslation('mail')

  return (
    <>
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
                onClick={() => void download(attachment.part, attachment.fileName)}
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
                    { label: t('reader.download'), onSelect: () => void download(attachment.part, attachment.fileName) },
                    { label: t('reader.view'), onSelect: () => setViewed(attachment) },
                  ]}
                />
              </span>
            )
          })}
        </div>
      )}

      {children}

      {viewed && (
        <AttachmentViewerModal
          images={imageAttachments.map(a => ({
            part: a.part,
            src: mailAttachmentUrl(folderPath!, uid, a.part, accountId),
            fileName: a.fileName,
            size: a.size,
          }))}
          initialIndex={Math.max(0, imageAttachments.findIndex(a => a.part === viewed.part))}
          onDownload={image => void download(image.part, image.fileName)}
          onClose={() => setViewed(null)}
        />
      )}
    </>
  )
}
