import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ContactCard from './ContactCard'
import type { Contact, ContactDetail } from './contactTypes'

vi.mock('../../api.js', () => ({
  api: { getContact: vi.fn(), getContactPhoto: vi.fn() },
  ApiError: class extends Error {},
}))
vi.mock('../../hooks/useAccountId', () => ({ useAccountId: () => 'primary' }))

const { api } = await import('../../api.js') as unknown as {
  api: Record<'getContact' | 'getContactPhoto', ReturnType<typeof vi.fn>>
}

function contact(fields: Partial<Contact> & { id: string }): Contact {
  return {
    firstName: null, lastName: null, nickname: null, isFavorite: false, addresses: [], ...fields,
  }
}

function detail(fields: Partial<ContactDetail> = {}): ContactDetail {
  return {
    id: 'b', firstName: 'Bruno', lastName: 'Mertens', nickname: 'bru', displayName: 'Bruno Mertens',
    isFavorite: false, hasPhoto: false,
    addresses: [
      { position: 0, address: 'bruno@x.be', type: 'INTERNET', pref: 101, params: '', groupName: '' },
      { position: 1, address: 'b.mertens@wk.be', type: 'INTERNET', pref: 101, params: '', groupName: '' },
    ],
    phones: [], postalAddresses: [], ...fields,
  }
}

const bruno = contact({
  id: 'b', firstName: 'Bruno', lastName: 'Mertens', nickname: 'bru',
  addresses: ['bruno@x.be', 'b.mertens@wk.be'],
})

beforeEach(() => {
  vi.clearAllMocks()
  api.getContact.mockResolvedValue(detail())
  api.getContactPhoto.mockResolvedValue(new Blob(['x'], { type: 'image/jpeg' }))
  // jsdom n'implémente pas l'API des URL objet ; la carte s'en sert pour l'avatar.
  URL.createObjectURL = vi.fn(() => 'blob:photo')
  URL.revokeObjectURL = vi.fn()
})

function setup(overrides: Partial<Parameters<typeof ContactCard>[0]> = {}) {
  const props = {
    contact: bruno, onEdit: vi.fn(), onDelete: vi.fn(), onToggleFavorite: vi.fn(), ...overrides,
  }
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return {
    ...props,
    ...render(<QueryClientProvider client={client}><ContactCard {...props} /></QueryClientProvider>),
  }
}

describe('ContactCard', () => {
  it('heads the card with the display name', () => {
    setup()

    expect(screen.getByRole('heading', { name: 'Bruno Mertens' })).toBeInTheDocument()
  })

  it('lists every address in order', () => {
    setup()

    const addresses = screen.getAllByTestId('card-address').map(node => node.textContent)
    expect(addresses?.[0]).toContain('bruno@x.be')
    expect(addresses?.[1]).toContain('b.mertens@wk.be')
  })

  // Position 0 is the primary by definition, so the card has to say which one it is: it is the
  // address a reply or a new message will use.
  it('marks the first address as the primary', () => {
    setup()

    expect(screen.getAllByTestId('card-address')[0]).toHaveTextContent(/primary/i)
    expect(screen.getAllByTestId('card-address')[1]).not.toHaveTextContent(/primary/i)
  })

  it('shows the nickname', () => {
    setup()

    expect(screen.getByText('bru')).toBeInTheDocument()
  })

  // Ce que la liste ne transporte pas : la fiche va le chercher, sans quoi une carte importée
  // n'affiche que son nom et son adresse alors que le serveur en détient bien plus.
  it('shows the phone numbers the detail carries', async () => {
    api.getContact.mockResolvedValue(detail({
      phones: [{ position: 0, number: '+32 492 80 90 00', type: 'CELL', pref: 101, params: '', groupName: '' }],
    }))
    setup()

    expect(await screen.findByText('+32 492 80 90 00')).toBeInTheDocument()
  })

  it('shows the birthday in the interface language', async () => {
    api.getContact.mockResolvedValue(detail({ birthday: '19930621T115900Z' }))
    setup()

    expect(await screen.findByText('June 21, 1993')).toBeInTheDocument()
  })

  it('shows the organisation and the job title', async () => {
    api.getContact.mockResolvedValue(detail({ organization: 'Acme', jobTitle: 'Plombier' }))
    setup()

    expect(await screen.findByText('Acme')).toBeInTheDocument()
    expect(await screen.findByText('Plombier')).toBeInTheDocument()
  })

  it('shows the postal address on one line per component that exists', async () => {
    api.getContact.mockResolvedValue(detail({
      postalAddresses: [{
        position: 0, type: 'HOME', pref: 101, params: '', groupName: '', poBox: null,
        extended: null, street: 'Rue Haute 1', locality: 'Bruxelles', region: null,
        postalCode: '1000', country: 'Belgique',
      }],
    }))
    setup()

    const postal = await screen.findByTestId('card-postal')
    expect(postal).toHaveTextContent('Rue Haute 1')
    expect(postal).toHaveTextContent('1000')
    expect(postal).toHaveTextContent('Bruxelles')
    expect(postal).toHaveTextContent('Belgique')
  })

  it('shows the photo the card carries', async () => {
    api.getContact.mockResolvedValue(detail({ hasPhoto: true }))
    setup()

    expect(await screen.findByTestId('card-photo')).toBeInTheDocument()
    expect(api.getContactPhoto).toHaveBeenCalledWith('b')
  })

  it('asks for no photo when the card carries none', async () => {
    setup()

    expect(await screen.findByText('bruno@x.be')).toBeInTheDocument()
    expect(screen.queryByTestId('card-photo')).not.toBeInTheDocument()
    expect(api.getContactPhoto).not.toHaveBeenCalled()
  })

  // Le détail arrive après coup : la fiche doit peindre tout de suite avec ce que la liste sait,
  // sinon chaque sélection passe par un vide.
  it("paints the list's name and addresses before the detail lands", () => {
    api.getContact.mockReturnValue(new Promise(() => {}))
    setup()

    expect(screen.getByRole('heading', { name: 'Bruno Mertens' })).toBeInTheDocument()
    expect(screen.getByText('bruno@x.be')).toBeInTheDocument()
  })

  // A field that does not exist renders nothing at all — an empty labelled row reads as data lost.
  it('renders no nickname row when there is none', () => {
    setup({ contact: contact({ id: 'n', firstName: 'Alice', addresses: ['a@x.be'] }) })

    expect(screen.queryByText(/nickname/i)).not.toBeInTheDocument()
  })

  it('renders no address section when the contact carries none', () => {
    setup({ contact: contact({ id: 'n', firstName: 'Alice' }) })

    expect(screen.queryByTestId('card-address')).not.toBeInTheDocument()
  })

  // The tile's own arrangement, one surface up: the star and the pencil on the row, the
  // destructive one a click deeper.
  it('offers the favourite toggle and edit in the head, delete behind the kebab', async () => {
    const props = setup()

    await userEvent.click(screen.getByRole('button', { name: /^edit$/i }))
    await userEvent.click(screen.getByRole('button', { name: /add to favourites/i }))
    await userEvent.click(screen.getByRole('button', { name: /contact actions/i }))
    await userEvent.click(screen.getByRole('menuitem', { name: /^delete$/i }))

    expect(props.onEdit).toHaveBeenCalledWith('b')
    expect(props.onToggleFavorite).toHaveBeenCalledWith(bruno)
    expect(props.onDelete).toHaveBeenCalledWith(bruno)
  })

  // The phone's shape: three named cells in a band of their own, no kebab — a last cell that only
  // ever opens a one-entry menu would spend a third of the screen saying nothing.
  it('draws the three actions as a bottom band when the caller asks for one', () => {
    const { container } = setup({ bottomActions: true })

    const bar = container.querySelector('.actionbar')
    expect(bar).not.toBeNull()
    expect(bar!.querySelectorAll('.actionbar-item')).toHaveLength(3)
    expect(container.querySelector('.contact-card-actions')).toBeNull()
    expect(screen.queryByRole('button', { name: /contact actions/i })).not.toBeInTheDocument()
  })

  it('keeps the actions in the head when it is not asked for a band', () => {
    const { container } = setup()

    expect(container.querySelector('.contact-card-head .contact-card-actions')).not.toBeNull()
    expect(container.querySelector('.actionbar')).toBeNull()
  })

  it('names the action to come on the favourite toggle', () => {
    setup({ contact: { ...bruno, isFavorite: true } })

    expect(screen.getByRole('button', { name: /remove from favourites/i })).toBeInTheDocument()
  })

  it('invites a pick when nothing is selected', () => {
    setup({ contact: null })

    expect(screen.getByText(/select a contact/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^edit$/i })).not.toBeInTheDocument()
  })
})
