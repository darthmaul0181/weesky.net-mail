import { describe, expect, it, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { createTestQueryClient, withQueryClient } from '../../test-utils'
import WhatsNewDot from './WhatsNewDot'

vi.mock('../../api.js', () => ({ api: { markWhatsNew: vi.fn().mockResolvedValue(null) } }))

function renderDot(props: { anchor?: string; unlisted?: boolean }, listed = false) {
  const client = createTestQueryClient()
  client.setQueryData(['appSettings'], { 'app.dailyImage': 'true' })
  client.setQueryData(['whatsNew'], {
    baseline: '0.0.0',
    items: listed ? [{ id: 'daily-image', firstShownAt: new Date().toISOString(), listedAt: new Date().toISOString() }] : [],
  })
  return render(<WhatsNewDot {...props} />, { wrapper: withQueryClient(client) })
}

beforeEach(() => vi.clearAllMocks())

describe('WhatsNewDot', () => {
  it('marks an anchor on the path of an active novelty', () => {
    renderDot({ anchor: 'settings:appearance' })
    expect(screen.getByRole('img', { name: 'New' })).toBeInTheDocument()
  })

  it('stays out of an anchor no novelty passes through', () => {
    renderDot({ anchor: 'settings:folders' })
    expect(screen.queryByRole('img')).toBeNull()
  })

  it('marks the list while a novelty is unlisted, and only then', () => {
    renderDot({ unlisted: true })
    expect(screen.getByRole('img', { name: 'New' })).toBeInTheDocument()
  })

  it('leaves the list unmarked once the list showed the novelty', () => {
    renderDot({ unlisted: true }, true)
    expect(screen.queryByRole('img')).toBeNull()
  })

  it('carries no visible text, so a row label keeps its textContent', () => {
    renderDot({ anchor: 'settings:appearance' })
    expect(screen.getByRole('img', { name: 'New' })).toHaveTextContent('')
  })
})
