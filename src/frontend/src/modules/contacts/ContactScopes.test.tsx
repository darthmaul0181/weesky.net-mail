import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { ComponentProps } from 'react'
import ContactScopes from './ContactScopes'
import { CONTACT_DRAG_MIME } from './dragContacts'

/** Imitates dataTransfer: the types are readable while hovering, the value only on drop. */
function dt() {
  return { types: [CONTACT_DRAG_MIME], getData: () => JSON.stringify({ ids: ['a'] }), dropEffect: '' }
}

type Props = ComponentProps<typeof ContactScopes>

/** The band's group half is the parent's business — the list, the four callbacks and the address
    test all arrive as props — so every case here declares only what it is about. */
function renderScopes(props: Partial<Props> = {}) {
  return render(
    <ContactScopes scope="all" total={42} favorites={5} onScope={vi.fn()} groups={[]}
      onCreateGroup={vi.fn()} onRenameGroup={vi.fn()} onDeleteGroup={vi.fn()}
      onWriteToGroup={vi.fn()} groupHasAddresses={() => true} {...props} />,
  )
}

describe('ContactScopes', () => {
  it('shows both scopes with their counts', () => {
    renderScopes()

    expect(screen.getByRole('button', { name: /all contacts/i })).toHaveTextContent('42')
    expect(screen.getByRole('button', { name: /favourites/i })).toHaveTextContent('5')
  })

  // `is-active` is the hook the navigation paint hangs on, and it must land on the active row
  // alone. Whether that paint is a fill rather than an accent bar is a CSS fact jsdom computes
  // nothing about — it is measured in the browser pass, Task 15.
  it('marks the active scope, and only the active one', () => {
    renderScopes({ scope: 'favorites' })

    expect(screen.getByRole('button', { name: /favourites/i })).toHaveClass('is-active')
    expect(screen.getByRole('button', { name: /all contacts/i })).not.toHaveClass('is-active')
  })

  // The class is invisible to a screen reader: without aria-current the active scope is announced
  // exactly like the other one. Same form as the mail folder tree's active row.
  it('announces the active scope to assistive technology', () => {
    renderScopes({ scope: 'favorites' })

    expect(screen.getByRole('button', { name: /favourites/i })).toHaveAttribute('aria-current', 'true')
    expect(screen.getByRole('button', { name: /all contacts/i })).not.toHaveAttribute('aria-current')
  })

  it('reports a scope change', async () => {
    const onScope = vi.fn()
    renderScopes({ onScope })

    await userEvent.click(screen.getByRole('button', { name: /favourites/i }))

    expect(onScope).toHaveBeenCalledWith('favorites')
  })

  // Zero is printed, not hidden: an absent count reads as a rendering fault next to a row that
  // has one.
  it('prints a zero count', () => {
    renderScopes({ total: 0, favorites: 0 })

    expect(screen.getByRole('button', { name: /all contacts/i })).toHaveTextContent('0')
  })

  // « Tous les contacts » n'est pas un groupe : il ne s'allume jamais et n'appelle rien.
  it('never lights up the all scope', () => {
    const onDropContacts = vi.fn()
    renderScopes({ total: 2, favorites: 0, onDropContacts })
    const target = screen.getByRole('button', { name: /all contacts/i })

    fireEvent.dragOver(target, { dataTransfer: dt() })
    expect(target).not.toHaveClass('drop-ready')

    fireEvent.drop(target, { dataTransfer: dt() })
    expect(onDropContacts).not.toHaveBeenCalled()
  })

  it('lights up favourites and hands the payload over on drop', () => {
    const onDropContacts = vi.fn()
    renderScopes({ total: 2, favorites: 0, onDropContacts })
    const target = screen.getByRole('button', { name: /favourites/i })

    fireEvent.dragOver(target, { dataTransfer: dt() })
    expect(target).toHaveClass('drop-ready')

    fireEvent.drop(target, { dataTransfer: dt() })
    expect(onDropContacts).toHaveBeenCalledWith('favorites', { ids: ['a'] })
  })

  // Le survol allume, le départ éteint : une cible restée allumée derrière le curseur ment.
  it('goes dark again when the drag leaves', () => {
    renderScopes({ total: 2, favorites: 0, onDropContacts: vi.fn() })
    const target = screen.getByRole('button', { name: /favourites/i })

    fireEvent.dragOver(target, { dataTransfer: dt() })
    fireEvent.dragLeave(target, { dataTransfer: dt() })

    expect(target).not.toHaveClass('drop-ready')
  })

  // Sans handler la bande n'est pas une cible : la classe ne doit pas s'allumer pour rien.
  it('is inert without a drop handler', () => {
    renderScopes({ total: 2, favorites: 0 })
    const target = screen.getByRole('button', { name: /favourites/i })

    fireEvent.dragOver(target, { dataTransfer: dt() })

    expect(target).not.toHaveClass('drop-ready')
  })
})

describe('the groups section', () => {
  const friends = { id: 'g1', name: 'Friends', memberIds: ['a', 'b'] }
  const family = { id: 'g2', name: 'Family', memberIds: [] }

  /** The row and its kebab both carry the group's name, so the row is found by the count its own
      accessible name ends on. */
  const groupRow = (name: string) => screen.getByRole('button', { name: new RegExp(`^${name}\\s*\\d+$`) })

  /** DropdownMenu mounts its rows only while open, so every assertion about them opens it first. */
  async function openMenu(name: string) {
    await userEvent.click(screen.getByRole('button', { name: `Actions for ${name}` }))
  }

  // Le « + » vit sur l'en-tête de la section, jamais dans `.column-actions` (décision 13) : le
  // premier groupe se crée depuis une section encore vide.
  it('offers the section and its + even with no group at all', async () => {
    const onCreateGroup = vi.fn()
    renderScopes({ onCreateGroup })

    expect(screen.getByText('Groups')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'New group' }))

    expect(onCreateGroup).toHaveBeenCalled()
  })

  it('draws one row per group with its member count', () => {
    renderScopes({ groups: [friends, family] })

    expect(groupRow('Friends')).toHaveTextContent('2')
    expect(groupRow('Family')).toHaveTextContent('0')
  })

  it('marks the open group and reports a change to it', async () => {
    const onScope = vi.fn()
    renderScopes({ scope: 'group:g1', groups: [friends, family], onScope })

    expect(groupRow('Friends')).toHaveClass('is-active')
    expect(groupRow('Family')).not.toHaveClass('is-active')

    await userEvent.click(groupRow('Family'))
    expect(onScope).toHaveBeenCalledWith('group:g2')
  })

  // La ligne est une cible par construction : `canDropIntoScope` n'accepte que ce qui n'est pas
  // « all », et le scope voyage entier jusqu'au parent.
  it('lights up a group row and hands the payload over on drop', () => {
    const onDropContacts = vi.fn()
    renderScopes({ groups: [friends], onDropContacts })
    const target = groupRow('Friends')

    fireEvent.dragOver(target, { dataTransfer: dt() })
    expect(target).toHaveClass('drop-ready')

    fireEvent.drop(target, { dataTransfer: dt() })
    expect(onDropContacts).toHaveBeenCalledWith('group:g1', { ids: ['a'] })
  })

  it('offers rename, write and delete on the row menu', async () => {
    const onRenameGroup = vi.fn()
    const onDeleteGroup = vi.fn()
    const onWriteToGroup = vi.fn()
    renderScopes({ groups: [friends], onRenameGroup, onDeleteGroup, onWriteToGroup })

    await openMenu('Friends')
    const menu = screen.getByRole('menu')
    await userEvent.click(within(menu).getByRole('menuitem', { name: 'Rename' }))
    expect(onRenameGroup).toHaveBeenCalledWith(friends)

    await openMenu('Friends')
    await userEvent.click(screen.getByRole('menuitem', { name: 'Write to group' }))
    expect(onWriteToGroup).toHaveBeenCalledWith(friends)

    await openMenu('Friends')
    await userEvent.click(screen.getByRole('menuitem', { name: 'Delete group' }))
    expect(onDeleteGroup).toHaveBeenCalledWith(friends)
  })

  // Un composeur sans destinataire n'est pas une réponse : l'entrée est refusée en amont, et
  // c'est le parent qui sait si le groupe offre une adresse.
  it('disables writing to a group that offers no address', async () => {
    renderScopes({ groups: [family], groupHasAddresses: () => false })

    await openMenu('Family')

    expect(screen.getByRole('menuitem', { name: 'Write to group' })).toBeDisabled()
    expect(screen.getByRole('menuitem', { name: 'Rename' })).toBeEnabled()
  })
})
