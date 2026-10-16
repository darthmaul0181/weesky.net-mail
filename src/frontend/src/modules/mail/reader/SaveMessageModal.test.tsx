import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import type { ReactNode } from 'react'
import { createTestQueryClient, setupUser } from '../../../test-utils'
import SaveMessageModal from './SaveMessageModal'

// The format row is a what's-new target, which reads the novelties.
vi.mock('../../../api.js', () => ({
  api: { getWhatsNew: () => new Promise(() => {}), getAppSettings: () => new Promise(() => {}) },
}))

function wrapper({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={createTestQueryClient()}>
      <MemoryRouter>{children}</MemoryRouter>
    </QueryClientProvider>
  )
}

describe('SaveMessageModal', () => {
  it('saves as EML unless MSG is chosen, then closes', async () => {
    const user = setupUser()
    const onSave = vi.fn().mockResolvedValue(undefined)
    const onClose = vi.fn()
    render(<SaveMessageModal onSave={onSave} onClose={onClose} />, { wrapper })

    expect(screen.getByRole('radio', { name: 'EML' })).toBeChecked()
    await user.click(screen.getByRole('radio', { name: 'MSG' }))
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(onSave).toHaveBeenCalledWith('msg')
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('describes each format apart from its name', () => {
    render(<SaveMessageModal onSave={vi.fn()} onClose={vi.fn()} />, { wrapper })

    expect(screen.getByRole('radio', { name: 'MSG' }))
      .toHaveAccessibleDescription('For Outlook. A signed message loses its signature there.')
  })

  it('keeps the dialog open on a refusal and says so', async () => {
    const user = setupUser()
    const onClose = vi.fn()
    render(<SaveMessageModal onSave={vi.fn().mockRejectedValue(new Error('boom'))} onClose={onClose} />, { wrapper })

    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not save the message')
    expect(onClose).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
  })

  it('holds the dialog while the file is prepared', async () => {
    const user = setupUser()
    render(<SaveMessageModal onSave={() => new Promise<void>(() => {})} onClose={vi.fn()} />, { wrapper })

    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled()
  })
})
