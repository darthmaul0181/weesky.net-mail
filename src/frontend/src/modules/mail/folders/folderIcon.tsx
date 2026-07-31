import type { ReactElement } from 'react'
import ArchiveIcon from '../../../icons/ArchiveIcon'
import FolderIcon from '../../../icons/FolderIcon'
import InboxIcon from '../../../icons/InboxIcon'
import JunkIcon from '../../../icons/JunkIcon'
import PencilIcon from '../../../icons/PencilIcon'
import RocketIcon from '../../../icons/RocketIcon'
import TrashIcon from '../../../icons/TrashIcon'

/**
 * One table from role to glyph, sitting beside roleLabel for the same reason: a second consumer
 * reads this mapping rather than inventing its own, so one folder can never wear two icons.
 *
 * It hands back an element rather than a component, because a component resolved during a render
 * remounts its subtree on every pass — the row would rebuild its glyph each time it redraws.
 */
const BY_ROLE: Record<string, (size: number) => ReactElement> = {
  inbox: size => <InboxIcon size={size} />,
  drafts: size => <PencilIcon size={size} />,
  // The rocket the composer's New message and Send buttons carry: one mark for sending, everywhere.
  sent: size => <RocketIcon size={size} />,
  archive: size => <ArchiveIcon size={size} />,
  junk: size => <JunkIcon size={size} />,
  trash: size => <TrashIcon size={size} />,
}

/** A folder holding no role — or one this build does not know — carries the plain folder. */
export function folderIcon(role: string | null | undefined, size = 16): ReactElement {
  const draw = (role && BY_ROLE[role]) || ((s: number) => <FolderIcon size={s} />)
  return draw(size)
}
