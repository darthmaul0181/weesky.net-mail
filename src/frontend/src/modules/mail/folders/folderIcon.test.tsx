import { describe, it, expect } from 'vitest'
import { folderIcon } from './folderIcon'
import ArchiveIcon from '../../../icons/ArchiveIcon'
import FolderIcon from '../../../icons/FolderIcon'
import InboxIcon from '../../../icons/InboxIcon'
import JunkIcon from '../../../icons/JunkIcon'
import PencilIcon from '../../../icons/PencilIcon'
import RocketIcon from '../../../icons/RocketIcon'
import TrashIcon from '../../../icons/TrashIcon'

describe('folderIcon', () => {
  it.each([
    ['inbox', InboxIcon],
    ['drafts', PencilIcon],
    ['sent', RocketIcon],
    ['archive', ArchiveIcon],
    ['junk', JunkIcon],
    ['trash', TrashIcon],
  ])('gives %s its own glyph', (role, expected) => {
    expect(folderIcon(role).type).toBe(expected)
  })

  it('gives every folder holding no role the same folder glyph', () => {
    expect(folderIcon(null).type).toBe(FolderIcon)
    expect(folderIcon(undefined).type).toBe(FolderIcon)
  })

  // Roles travel from the server through the role resolver; one this build has not heard of
  // must still draw something rather than leave a hole in the column.
  it('falls back to the folder glyph for a role it does not know', () => {
    expect(folderIcon('snoozed').type).toBe(FolderIcon)
  })

  it('gives no two roles the same glyph', () => {
    const roles = ['inbox', 'drafts', 'sent', 'archive', 'junk', 'trash']

    expect(new Set(roles.map(role => folderIcon(role).type)).size).toBe(roles.length)
  })

  // The tree draws at 16 alongside a 16px chevron; a caller may still ask for another size.
  it('draws at 16 unless asked otherwise', () => {
    expect(folderIcon('inbox').props).toMatchObject({ size: 16 })
    expect(folderIcon('inbox', 20).props).toMatchObject({ size: 20 })
  })
})
