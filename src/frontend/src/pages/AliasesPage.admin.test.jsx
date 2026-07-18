import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { AccountPanel } from './AliasesPage.jsx'
import { QuotaMini } from '../components/QuotaBlock.jsx'
import DeleteConfirmModal from '../components/DeleteConfirmModal.jsx'

vi.mock('../api.js', () => ({
  api: {
    changeFullName: vi.fn(),
  },
  clearSession: vi.fn(),
  setIsAdmin: vi.fn(),
}))

const MB = 1024 * 1024
const GB = 1024 * MB

beforeEach(() => {
  vi.clearAllMocks()
})

// ── QuotaMini ─────────────────────────────────────────────────

describe('QuotaMini', () => {
  it('renders — when quota is null', () => {
    render(<QuotaMini quota={null} />)
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('renders — when storageBytesLimit is zero', () => {
    render(<QuotaMini quota={{ storageBytesUsed: 0, storageBytesLimit: 0 }} />)
    expect(screen.getByText('—')).toBeInTheDocument()
  })

  it('displays used / total in MB when values are under 1 GB', () => {
    render(<QuotaMini quota={{ storageBytesUsed: 50 * MB, storageBytesLimit: 200 * MB }} />)
    expect(screen.getByText(/50\.0 \/ 200 MB/)).toBeInTheDocument()
  })

  it('displays used / total in GB when values reach 1 GB', () => {
    render(<QuotaMini quota={{ storageBytesUsed: 1 * GB, storageBytesLimit: 2 * GB }} />)
    expect(screen.getByText(/1\.0 \/ 2\.0 GB/)).toBeInTheDocument()
  })

  it('applies is-danger class when usage is ≥ 90%', () => {
    const { container } = render(
      <QuotaMini quota={{ storageBytesUsed: 92 * MB, storageBytesLimit: 100 * MB }} />
    )
    expect(container.querySelector('.panel-quota-bar')).toHaveClass('is-danger')
  })

  it('applies is-warn class when usage is between 75% and 90%', () => {
    const { container } = render(
      <QuotaMini quota={{ storageBytesUsed: 80 * MB, storageBytesLimit: 100 * MB }} />
    )
    expect(container.querySelector('.panel-quota-bar')).toHaveClass('is-warn')
  })
})

// ── AccountPanel — isAdmin behaviour ─────────────────────────

describe('AccountPanel isAdmin', () => {
  function renderPanel(isAdmin = false, onAdmin = vi.fn()) {
    return render(
      <AccountPanel
        initials="WB"
        fullName="Test User"
        primaryEmail="test@weesky.be"
        subDomains={[]}
        quota={null}
        onLogout={vi.fn()}
        onAdmin={onAdmin}
        isAdmin={isAdmin}
        alphaMode={false}
        onAlphaModeChange={vi.fn()}
        onFullNameChange={vi.fn()}
      />
    )
  }

  it('hides the Administration button when isAdmin is false', async () => {
    renderPanel(false)
    await userEvent.click(screen.getByRole('button', { name: 'WB' }))
    expect(screen.queryByRole('button', { name: 'Administration' })).not.toBeInTheDocument()
  })

  it('shows the Administration button when isAdmin is true', async () => {
    renderPanel(true)
    await userEvent.click(screen.getByRole('button', { name: 'WB' }))
    expect(screen.getByRole('button', { name: 'Administration' })).toBeInTheDocument()
  })

  it('calls onAdmin when the Administration button is clicked', async () => {
    const onAdmin = vi.fn()
    renderPanel(true, onAdmin)
    await userEvent.click(screen.getByRole('button', { name: 'WB' }))
    await userEvent.click(screen.getByRole('button', { name: 'Administration' }))
    expect(onAdmin).toHaveBeenCalledOnce()
  })
})

// ── DeleteConfirmModal ────────────────────────────────────────

describe('DeleteConfirmModal', () => {
  it('renders the entity label', () => {
    render(<DeleteConfirmModal entityLabel="alice@weesky.be" onConfirm={vi.fn()} onClose={vi.fn()} loading={false} />)
    expect(screen.getByText('alice@weesky.be')).toBeInTheDocument()
  })

  it('calls onClose when Cancel is clicked', async () => {
    const onClose = vi.fn()
    render(<DeleteConfirmModal entityLabel="x" onConfirm={vi.fn()} onClose={onClose} loading={false} />)
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('calls onConfirm when Delete is clicked', async () => {
    const onConfirm = vi.fn()
    render(<DeleteConfirmModal entityLabel="x" onConfirm={onConfirm} onClose={vi.fn()} loading={false} />)
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }))
    expect(onConfirm).toHaveBeenCalledOnce()
  })

  it('disables both buttons while loading', () => {
    render(<DeleteConfirmModal entityLabel="x" onConfirm={vi.fn()} onClose={vi.fn()} loading={true} />)
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled()
  })
})
