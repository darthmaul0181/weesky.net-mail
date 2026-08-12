import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ContactScopes from './ContactScopes'
import { CONTACT_DRAG_MIME } from './dragContacts'

/** Imitates dataTransfer: the types are readable while hovering, the value only on drop. */
function dt() {
  return { types: [CONTACT_DRAG_MIME], getData: () => JSON.stringify({ ids: ['a'] }), dropEffect: '' }
}

describe('ContactScopes', () => {
  it('shows both scopes with their counts', () => {
    render(<ContactScopes scope="all" total={42} favorites={5} onScope={vi.fn()} />)

    expect(screen.getByRole('button', { name: /all contacts/i })).toHaveTextContent('42')
    expect(screen.getByRole('button', { name: /favourites/i })).toHaveTextContent('5')
  })

  // `is-active` is the hook the navigation paint hangs on, and it must land on the active row
  // alone. Whether that paint is a fill rather than an accent bar is a CSS fact jsdom computes
  // nothing about — it is measured in the browser pass, Task 15.
  it('marks the active scope, and only the active one', () => {
    render(<ContactScopes scope="favorites" total={42} favorites={5} onScope={vi.fn()} />)

    expect(screen.getByRole('button', { name: /favourites/i })).toHaveClass('is-active')
    expect(screen.getByRole('button', { name: /all contacts/i })).not.toHaveClass('is-active')
  })

  // The class is invisible to a screen reader: without aria-current the active scope is announced
  // exactly like the other one. Same form as the mail folder tree's active row.
  it('announces the active scope to assistive technology', () => {
    render(<ContactScopes scope="favorites" total={42} favorites={5} onScope={vi.fn()} />)

    expect(screen.getByRole('button', { name: /favourites/i })).toHaveAttribute('aria-current', 'true')
    expect(screen.getByRole('button', { name: /all contacts/i })).not.toHaveAttribute('aria-current')
  })

  it('reports a scope change', async () => {
    const onScope = vi.fn()
    render(<ContactScopes scope="all" total={42} favorites={5} onScope={onScope} />)

    await userEvent.click(screen.getByRole('button', { name: /favourites/i }))

    expect(onScope).toHaveBeenCalledWith('favorites')
  })

  // Zero is printed, not hidden: an absent count reads as a rendering fault next to a row that
  // has one.
  it('prints a zero count', () => {
    render(<ContactScopes scope="all" total={0} favorites={0} onScope={vi.fn()} />)

    expect(screen.getByRole('button', { name: /all contacts/i })).toHaveTextContent('0')
  })

  // « Tous les contacts » n'est pas un groupe : il ne s'allume jamais et n'appelle rien.
  it('never lights up the all scope', () => {
    const onDropContacts = vi.fn()
    render(<ContactScopes scope="all" total={2} favorites={0} onScope={vi.fn()}
      onDropContacts={onDropContacts} />)
    const target = screen.getByRole('button', { name: /all contacts/i })

    fireEvent.dragOver(target, { dataTransfer: dt() })
    expect(target).not.toHaveClass('drop-ready')

    fireEvent.drop(target, { dataTransfer: dt() })
    expect(onDropContacts).not.toHaveBeenCalled()
  })

  it('lights up favourites and hands the payload over on drop', () => {
    const onDropContacts = vi.fn()
    render(<ContactScopes scope="all" total={2} favorites={0} onScope={vi.fn()}
      onDropContacts={onDropContacts} />)
    const target = screen.getByRole('button', { name: /favourites/i })

    fireEvent.dragOver(target, { dataTransfer: dt() })
    expect(target).toHaveClass('drop-ready')

    fireEvent.drop(target, { dataTransfer: dt() })
    expect(onDropContacts).toHaveBeenCalledWith('favorites', { ids: ['a'] })
  })

  // Le survol allume, le départ éteint : une cible restée allumée derrière le curseur ment.
  it('goes dark again when the drag leaves', () => {
    render(<ContactScopes scope="all" total={2} favorites={0} onScope={vi.fn()}
      onDropContacts={vi.fn()} />)
    const target = screen.getByRole('button', { name: /favourites/i })

    fireEvent.dragOver(target, { dataTransfer: dt() })
    fireEvent.dragLeave(target, { dataTransfer: dt() })

    expect(target).not.toHaveClass('drop-ready')
  })

  // Sans handler la bande n'est pas une cible : la classe ne doit pas s'allumer pour rien.
  it('is inert without a drop handler', () => {
    render(<ContactScopes scope="all" total={2} favorites={0} onScope={vi.fn()} />)
    const target = screen.getByRole('button', { name: /favourites/i })

    fireEvent.dragOver(target, { dataTransfer: dt() })

    expect(target).not.toHaveClass('drop-ready')
  })
})
