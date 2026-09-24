import { describe, it, expect, afterEach } from 'vitest'
import { cleanup, renderHook } from '@testing-library/react'
import type { MailFolderNode, SpecialUse } from './api/mailTypes'
import { useRoleActions } from './useRoleActions'

afterEach(cleanup)

function node(path: string, specialUse?: SpecialUse): MailFolderNode {
  return { path, name: path, specialUse, selectable: true, subscribed: true, uidValidity: 1, children: [] }
}

const full: MailFolderNode[] = [
  node('INBOX', 'inbox'), node('Archive', 'archive'), node('Junk', 'junk'), node('Trash', 'trash'),
]

const run = (folders: MailFolderNode[] | undefined, role: SpecialUse | null) =>
  renderHook(() => useRoleActions(folders, role)).result.current

describe('useRoleActions', () => {
  it('offers every action from the inbox', () => {
    const actions = run(full, 'inbox')

    expect(actions.roles).toEqual({ archive: 'Archive', junk: 'Junk', trash: 'Trash' })
    expect(actions).toMatchObject({
      inTrash: false, archiveOff: false, junkOff: false, trashOff: false, deleteLabel: 'Delete',
    })
  })

  it('turns archive off inside the archive, saying it is already there', () => {
    const actions = run(full, 'archive')

    expect(actions).toMatchObject({ archiveOff: true, archiveReason: 'Already in the archive folder', junkOff: false })
  })

  it('turns junk off inside the junk, saying it is already there', () => {
    const actions = run(full, 'junk')

    expect(actions).toMatchObject({ junkOff: true, junkReason: 'Already in the junk folder', archiveOff: false })
  })

  it('deletes permanently inside the trash', () => {
    const actions = run(full, 'trash')

    expect(actions).toMatchObject({ inTrash: true, trashOff: false, deleteLabel: 'Delete permanently' })
  })

  it('turns each action off with its reason on a mailbox holding none of the three roles', () => {
    const actions = run([node('INBOX', 'inbox')], 'inbox')

    expect(actions.roles).toEqual({ archive: null, junk: null, trash: null })
    expect(actions).toMatchObject({
      archiveOff: true, archiveReason: 'Assign the archive folder in Settings → Folders',
      junkOff: true, junkReason: 'Assign the junk folder in Settings → Folders',
      trashOff: true, trashReason: 'Assign the trash folder in Settings → Folders',
    })
  })

  it('keeps the roles object while the folder list is the same', () => {
    const { result, rerender } = renderHook(({ role }) => useRoleActions(full, role),
      { initialProps: { role: 'inbox' as SpecialUse } })
    const first = result.current

    rerender({ role: 'inbox' })
    expect(result.current).toBe(first)
    rerender({ role: 'trash' })
    expect(result.current.roles).toBe(first.roles)
  })
})
