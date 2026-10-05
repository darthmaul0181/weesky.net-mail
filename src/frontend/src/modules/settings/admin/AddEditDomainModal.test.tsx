import { screen, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setupUser } from '../../../test-utils'
import { AddEditDomainModal } from './AddEditDomainModal'
import { EDIT_DOMAIN, render } from './adminTestFixtures'

const mocks = vi.hoisted(() => ({
  adminCreateDomain: vi.fn(),
  adminUpdateDomain: vi.fn(),
}))

vi.mock('../../../api.js', () => ({ api: mocks, clearSession: vi.fn() }))

let user: ReturnType<typeof setupUser>
beforeEach(() => {
  vi.clearAllMocks()
  user = setupUser()
})

function renderCreate(props: Partial<Parameters<typeof AddEditDomainModal>[0]> = {}) {
  return render(
    <AddEditDomainModal domain={null} onSave={vi.fn()} onClose={vi.fn()} {...props} />
  )
}

const idInput = () => screen.getByLabelText('ID (3 chars max)')
const nameInput = () => screen.getByLabelText('Domain name')

async function fillDomain(id: string, name: string) {
  await user.type(idInput(), id)
  await user.type(nameInput(), name)
}

describe('AddEditDomainModal — create mode', () => {
  it('renders id and name fields', () => {
    renderCreate()
    expect(screen.getAllByRole('textbox')).toHaveLength(2)
  })

  it('submit button is disabled when both fields are empty', () => {
    renderCreate()
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
  })

  it.each([
    ['id', idInput, 'TST'],
    ['name', nameInput, 'test.com'],
  ])('submit button is disabled when only the %s is filled', async (_which, input, value) => {
    renderCreate()
    await user.type(input(), value)
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
  })

  it('submit button is enabled when both fields are filled with a valid domain', async () => {
    renderCreate()
    await fillDomain('TST', 'test.com')
    expect(screen.getByRole('button', { name: 'Create domain' })).not.toBeDisabled()
  })

  it('submit button is disabled when domain name is syntactically invalid', async () => {
    renderCreate()
    await fillDomain('TST', 'notadomain')
    expect(screen.getByRole('button', { name: 'Create domain' })).toBeDisabled()
  })

  it.each([
    ['gets', 'invalid', 'notadomain', true],
    ['has no', 'valid', 'test.com', false],
    ['has no', 'empty', '', false],
  ])('name input %s is-error class when the domain name is %s', async (_verb, _state, value, error) => {
    renderCreate()
    if (value) await user.type(nameInput(), value)
    expect(nameInput().classList.contains('is-error')).toBe(error)
  })

  it('calls adminCreateDomain with correct payload on submit', async () => {
    mocks.adminCreateDomain.mockResolvedValue({})
    const onSave = vi.fn()
    renderCreate({ onSave })
    await fillDomain('TST', 'test.com')
    await user.click(screen.getByRole('button', { name: 'Create domain' }))
    await waitFor(() =>
      expect(mocks.adminCreateDomain).toHaveBeenCalledWith(
        expect.objectContaining({ id: 'TST', name: 'test.com' })
      )
    )
    await waitFor(() => expect(onSave).toHaveBeenCalledOnce())
  })

  it('blocks Escape while the create request is in flight', async () => {
    let resolveCreate: (value: unknown) => void = () => {}
    mocks.adminCreateDomain.mockReturnValue(new Promise(resolve => { resolveCreate = resolve }))
    const onClose = vi.fn()
    renderCreate({ onClose })
    await fillDomain('TST', 'test.com')
    await user.click(screen.getByRole('button', { name: 'Create domain' }))

    await user.keyboard('{Escape}')
    expect(onClose).not.toHaveBeenCalled()

    resolveCreate({})
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create domain' })).not.toBeDisabled())
    await user.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalledTimes(1)
  })
})

describe('AddEditDomainModal — edit mode', () => {
  it('calls adminUpdateDomain on submit', async () => {
    mocks.adminUpdateDomain.mockResolvedValue({})
    render(<AddEditDomainModal domain={EDIT_DOMAIN} onSave={vi.fn()} onClose={vi.fn()} />)
    await user.clear(nameInput())
    await user.type(nameInput(), 'new.weesky.be')
    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() =>
      expect(mocks.adminUpdateDomain).toHaveBeenCalledWith('WSY', expect.objectContaining({ name: 'new.weesky.be' }))
    )
  })

  // Server prose never reaches the screen; the local fallback does — see apiErrorMessage.
  it('shows the local fallback when update API fails', async () => {
    mocks.adminUpdateDomain.mockRejectedValue(new Error('Not found'))
    render(<AddEditDomainModal domain={EDIT_DOMAIN} onSave={vi.fn()} onClose={vi.fn()} />)
    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() => expect(screen.getByText('An error occurred')).toBeInTheDocument())
  })
})

describe('AddEditDomainModal — accessible field names', () => {
  it('every field is reachable through its label', () => {
    renderCreate()
    expect(idInput()).toBeInTheDocument()
    expect(nameInput()).toBeInTheDocument()
  })

  it('every field is reachable through its label in edit mode, and the id stays disabled', () => {
    render(<AddEditDomainModal domain={EDIT_DOMAIN} onSave={vi.fn()} onClose={vi.fn()} />)
    expect(idInput()).toBeInTheDocument()
    expect(idInput()).toBeDisabled()
  })

  // Server prose never reaches the screen; the local fallback does — see apiErrorMessage.
  it('the error banner announces the local fallback as an alert', async () => {
    mocks.adminCreateDomain.mockRejectedValue(new Error('Invalid ID'))
    renderCreate()
    await fillDomain('TST', 'test.com')
    await user.click(screen.getByRole('button', { name: 'Create domain' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('An error occurred')
  })
})
