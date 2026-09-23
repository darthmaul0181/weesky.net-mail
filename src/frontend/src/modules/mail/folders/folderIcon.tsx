import type { ReactElement } from 'react'
import ArchiveIcon from '../../../icons/ArchiveIcon'
import FolderIcon from '../../../icons/FolderIcon'
import InboxIcon from '../../../icons/InboxIcon'
import JunkIcon from '../../../icons/JunkIcon'
import PencilIcon from '../../../icons/PencilIcon'
import RocketIcon from '../../../icons/RocketIcon'
import TrashIcon from '../../../icons/TrashIcon'

// Beside roleLabel so one folder never wears two icons. Hands back an element, not a component: a
// component resolved during render remounts its subtree on every pass.
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
