import { screen, waitFor, fireEvent } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { pickOption, setupUser } from '../../../test-utils'
import { AddEditUserModal } from './AddEditUserModal'
import { MOCK_DOMAINS, MOCK_USERS, render } from './adminTestFixtures'
import type { AdminDomain, AdminUser } from './adminTypes'

const mocks = vi.hoisted(() => ({
  adminCreateUser: vi.fn(),
  adminUpdateUser: vi.fn(),
  adminGetUserQuota: vi.fn(),
}))

vi.mock('../../../api.js', () => ({ api: mocks, clearSession: vi.fn() }))

const EDIT_USER: AdminUser = { id: 1, userName: 'alice', domainId: 'WSY', domainName: 'weesky.be', fullName: 'Alice Smith', quotaMb: 1024, active: true, admin: false, lastLogins: [] }

let user: ReturnType<typeof setupUser>
beforeEach(() => {
  vi.clearAllMocks()
  mocks.adminGetUserQuota.mockRejectedValue(new Error('unavailable'))
  user = setupUser()
})

function renderCreate(props: Partial<Parameters<typeof AddEditUserModal>[0]> = {}) {
  return render(
    <AddEditUserModal user={null} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} {...props} />
  )
}

function renderEdit(props: Partial<Parameters<typeof AddEditUserModal>[0]> = {}) {
  return render(
    <AddEditUserModal user={EDIT_USER} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} {...props} />
  )
}

async function fillCredentials() {
  await user.type(screen.getAllByRole('textbox')[0]!, 'alice')
  await user.type(screen.getByLabelText('Password'), 'pw')
}

describe('AddEditUserModal — create mode', () => {
  it('submit button is disabled when username is empty', () => {
    renderCreate()
    expect(screen.getByRole('button', { name: 'Create account' })).toBeDisabled()
  })

  it('submit button is disabled when username is filled but password is empty', async () => {
    renderCreate()
    await user.type(screen.getAllByRole('textbox')[0]!, 'alice')
    expect(screen.getByRole('button', { name: 'Create account' })).toBeDisabled()
  })

  it('calls adminCreateUser with correct payload on submit', async () => {
    mocks.adminCreateUser.mockResolvedValue({})
    const onSave = vi.fn()
    renderCreate({ onSave })
    await user.type(screen.getAllByRole('textbox')[0]!, 'alice')
    await user.type(screen.getByLabelText('Password'), 'secret')
    await user.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() =>
      expect(mocks.adminCreateUser).toHaveBeenCalledWith(
        expect.objectContaining({ userName: 'alice', domainId: 'WSY', password: 'secret' })
      )
    )
    await waitFor(() => expect(onSave).toHaveBeenCalledOnce())
  })

  // Abandoning the POST via Escape would skip onSave() and leave the created user invisible
  // until a reload.
  it('blocks Escape while the create request is in flight', async () => {
    let resolveCreate: (value: unknown) => void = () => {}
    mocks.adminCreateUser.mockReturnValue(new Promise(resolve => { resolveCreate = resolve }))
    const onClose = vi.fn()
    renderCreate({ onClose })
    await user.type(screen.getAllByRole('textbox')[0]!, 'alice')
    await user.type(screen.getByLabelText('Password'), 'secret')
    await user.click(screen.getByRole('button', { name: 'Create account' }))

    await user.keyboard('{Escape}')
    expect(onClose).not.toHaveBeenCalled()

    resolveCreate({})
    await waitFor(() => expect(screen.getByRole('button', { name: 'Create account' })).not.toBeDisabled())
    await user.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalledTimes(1)
  })
})

describe('AddEditUserModal — edit mode', () => {
  it('submit button is enabled without filling password', () => {
    renderEdit()
    expect(screen.getByRole('button', { name: 'Save changes' })).not.toBeDisabled()
  })

  it('password placeholder says "leave blank to keep"', () => {
    renderEdit()
    expect(screen.getByLabelText('Password')).toHaveAttribute('placeholder', 'leave blank to keep')
  })

  it('calls adminUpdateUser with null password when password field is left empty', async () => {
    mocks.adminUpdateUser.mockResolvedValue({})
    renderEdit()
    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() =>
      expect(mocks.adminUpdateUser).toHaveBeenCalledWith(
        1,
        expect.objectContaining({ password: null })
      )
    )
  })

  it('calls adminUpdateUser with new password when one is provided', async () => {
    mocks.adminUpdateUser.mockResolvedValue({})
    renderEdit()
    await user.type(screen.getByLabelText('Password'), 'newpass')
    await user.click(screen.getByRole('button', { name: 'Save changes' }))
    await waitFor(() =>
      expect(mocks.adminUpdateUser).toHaveBeenCalledWith(
        1,
        expect.objectContaining({ password: 'newpass' })
      )
    )
  })

  // The server omits `at` for a service the account never logged into.
  it('names a service never logged into without inventing a time', () => {
    const neverLoggedIn: AdminUser = { ...MOCK_USERS[0]!, lastLogins: [{ service: 'imap' }] }
    renderEdit({ user: neverLoggedIn })
    expect(screen.getByText('IMAP')).toBeInTheDocument()
    expect(screen.queryByText(/NaN/)).not.toBeInTheDocument()
  })
})

describe('AddEditUserModal — field changes', () => {
  const TWO_DOMAINS: AdminDomain[] = [
    { id: 'WSY', name: 'weesky.be', aliasCount: 0 },
    { id: 'EXM', name: 'example.com', aliasCount: 0 },
  ]

  it('changing the domain select updates domainId in the payload', async () => {
    mocks.adminCreateUser.mockResolvedValue({})
    renderCreate({ domains: TWO_DOMAINS })
    await fillCredentials()
    await pickOption(screen.getByRole('combobox'), 'example.com')
    await user.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() =>
      expect(mocks.adminCreateUser).toHaveBeenCalledWith(
        expect.objectContaining({ domainId: 'EXM' })
      )
    )
  })

  it('sends the typed full name in the payload', async () => {
    mocks.adminCreateUser.mockResolvedValue({})
    renderCreate()
    await fillCredentials()
    await user.type(screen.getByLabelText('Full name'), 'Alice Smith')
    await user.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() =>
      expect(mocks.adminCreateUser).toHaveBeenCalledWith(
        expect.objectContaining({ fullName: 'Alice Smith' })
      )
    )
  })

  it('changing the range slider updates the quota number input', () => {
    renderCreate()
    fireEvent.change(screen.getByRole('slider'), { target: { value: '2048' } })
    expect(screen.getByRole('spinbutton')).toHaveValue(2048)
  })
})

describe('AddEditUserModal — toggles and quota', () => {
  it.each([
    ['unchecking active sets active:false', 'Active', { active: false }],
    ['checking admin sets admin:true', 'Administrator', { admin: true }],
  ])('%s in the payload', async (_name, checkbox, payload) => {
    mocks.adminCreateUser.mockResolvedValue({})
    renderCreate()
    await fillCredentials()
    await user.click(screen.getByRole('checkbox', { name: checkbox }))
    await user.click(screen.getByRole('button', { name: 'Create account' }))
    await waitFor(() =>
      expect(mocks.adminCreateUser).toHaveBeenCalledWith(expect.objectContaining(payload))
    )
  })

  it('changing the quota number input updates the slider value', () => {
    renderCreate()
    fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '512' } })
    expect(screen.getByRole('slider')).toHaveValue('512')
  })
})

describe('AddEditUserModal — accessible field names', () => {
  it('every field is reachable through its label', () => {
    renderCreate()
    expect(screen.getByLabelText('Username')).toBeInTheDocument()
    expect(screen.getByLabelText('Domain')).toBeInTheDocument()
    expect(screen.getByLabelText('Password')).toBeInTheDocument()
    expect(screen.getByLabelText('Full name')).toBeInTheDocument()
    expect(screen.getByRole('spinbutton', { name: 'Quota (MB)' })).toBeInTheDocument()
    expect(screen.getByRole('slider', { name: 'Quota (MB)' })).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'Active' })).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'Administrator' })).toBeInTheDocument()
  })

  it('every field is reachable through its label in edit mode, and the username stays disabled', () => {
    renderEdit()
    const username = screen.getByLabelText('Username')
    expect(username).toBeInTheDocument()
    expect(username).toBeDisabled()
  })

  // Server prose never reaches the screen; the local fallback does — see apiErrorMessage.
  it('the error banner announces the local fallback as an alert', async () => {
    mocks.adminCreateUser.mockRejectedValue(new Error('Duplicate user'))
    renderCreate()
    await user.type(screen.getByLabelText('Username'), 'alice')
    await user.type(screen.getByLabelText('Password'), 'pw')
    await user.click(screen.getByRole('button', { name: 'Create account' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('An error occurred')
  })

  it('two instances mounted at once do not collide on id', () => {
    render(
      <>
        <AddEditUserModal user={null} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} />
        <AddEditUserModal user={null} domains={MOCK_DOMAINS} onSave={vi.fn()} onClose={vi.fn()} />
      </>
    )
    expect(screen.getAllByLabelText('Username')).toHaveLength(2)
  })
})
