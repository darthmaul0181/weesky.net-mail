import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import GeneralPage from './GeneralPage'

const mocks = vi.hoisted(() => ({ getPreferences: vi.fn(), setPreference: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

function renderPage(preferences = { 'mail.pageSize': '30', 'mail.showPreview': 'true' }) {
  mocks.getPreferences.mockResolvedValue(preferences)
  mocks.setPreference.mockResolvedValue(undefined)
  return render(<GeneralPage />, { wrapper })
}

describe('GeneralPage', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows the stored page size, not a value of its own', async () => {
    renderPage({ 'mail.pageSize': '100', 'mail.showPreview': 'true' })

    expect(await screen.findByLabelText('Messages per page')).toHaveValue('100')
  })

  it('offers the five steps and All', async () => {
    renderPage()

    const options = Array.from((await screen.findByLabelText('Messages per page')).querySelectorAll('option'))
    expect(options.map(o => o.value)).toEqual(['10', '20', '30', '50', '100', 'all'])
    expect(options.map(o => o.textContent)).toEqual(['10', '20', '30', '50', '100', 'All'])
  })

  it('shows All as the selection when it is stored', async () => {
    renderPage({ 'mail.pageSize': 'all', 'mail.showPreview': 'true' })

    expect(await screen.findByLabelText('Messages per page')).toHaveValue('all')
  })

  it('saves All as the string the backend accepts', async () => {
    renderPage()

    fireEvent.change(await screen.findByLabelText('Messages per page'), { target: { value: 'all' } })

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.pageSize', 'all'))
  })

  it('saves a new page size', async () => {
    renderPage()

    fireEvent.change(await screen.findByLabelText('Messages per page'), { target: { value: '50' } })

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.pageSize', '50'))
  })

  it('shows the preview toggle on when it is on', async () => {
    renderPage()

    expect(await screen.findByLabelText('Preview in the message list')).toBeChecked()
  })

  it('shows it off when it is off', async () => {
    renderPage({ 'mail.pageSize': '30', 'mail.showPreview': 'false' })

    expect(await screen.findByLabelText('Preview in the message list')).not.toBeChecked()
  })

  it('saves the toggle as a string the backend accepts', async () => {
    renderPage()

    fireEvent.click(await screen.findByLabelText('Preview in the message list'))

    await waitFor(() =>
      expect(mocks.setPreference).toHaveBeenCalledWith('mail.showPreview', 'false'))
  })

  // A boolean shown as a switch, like every other boolean in the app.
  it('uses the house toggle switch', async () => {
    const { container } = renderPage()
    await screen.findByLabelText('Preview in the message list')

    expect(container.querySelector('.toggle-switch')).toBeTruthy()
  })

  it('surfaces a failure to save instead of pretending', async () => {
    renderPage()
    mocks.setPreference.mockRejectedValue(new Error('Refused by the server'))

    fireEvent.change(await screen.findByLabelText('Messages per page'), { target: { value: '10' } })

    expect(await screen.findByText('Refused by the server')).toBeInTheDocument()
  })

  it('reports a load failure rather than showing empty controls', async () => {
    mocks.getPreferences.mockRejectedValue(new Error('nope'))
    render(<GeneralPage />, { wrapper })

    expect(await screen.findByText('Could not load the settings.')).toBeInTheDocument()
  })

  // .field-h was drawn for the admin dialogs: a 110px label column and a control on flex:1.
  // A settings page is the opposite shape — sentence-length labels in a wide column — so the
  // rows carry the modifier that widens the label and lets the control size to its content.
  it('lays its rows out as settings rows, not dialog rows', async () => {
    const { container } = renderPage()
    await screen.findByLabelText('Messages per page')

    const rows = container.querySelectorAll('.field-h')
    expect(rows).toHaveLength(2)
    rows.forEach(row => expect(row).toHaveClass('is-setting'))
  })
})
