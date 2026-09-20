import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from '../../../api.js'
import DeliveryRepliesSection from './DeliveryRepliesSection'

vi.mock('../../../api.js', () => ({ api: {
  adminGetDeliveryReplyKey: vi.fn(), adminGenerateDeliveryReplyKey: vi.fn(),
  adminSetDeliveryReplies: vi.fn(), adminDeleteDeliveryReplyKey: vi.fn(),
}, ApiError: class extends Error { status = 0 } }))

// The client is handed back for the one test that forces a refetch itself.
function mountExposingClient() {
  const addToast = vi.fn()
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><DeliveryRepliesSection addToast={addToast} /></QueryClientProvider>)
  return { addToast, client }
}

const mount = () => mountExposingClient().addToast

describe('DeliveryRepliesSection', () => {
  beforeEach(() => { vi.clearAllMocks() })

  it('without a key: generate is offered, the switch is disabled', async () => {
    vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: false, enabled: false })
    mount()
    expect(await screen.findByRole('button', { name: 'Generate a key' })).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'Apply replies at delivery' })).toBeDisabled()
  })

  it('generating shows the key once, with Copy', async () => {
    vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: false, enabled: false })
    vi.mocked(api.adminGenerateDeliveryReplyKey).mockResolvedValue({ key: 'abc-key' })
    const addToast = mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Generate a key' }))
    const dialog = await screen.findByRole('dialog', { name: 'Delivery key' })
    // toHaveTextContent cannot see an <input>'s value — it is not part of .textContent — so the
    // key (a read-only field, selectable by hand) is asserted through its displayed value instead.
    expect(dialog.querySelector('input')).toHaveValue('abc-key')
    Object.assign(navigator, { clipboard: { writeText: vi.fn().mockResolvedValue(undefined) } })
    await userEvent.click(screen.getByRole('button', { name: 'Copy' }))
    expect(navigator.clipboard.writeText).toHaveBeenCalledWith('abc-key')
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Key copied.'))
  })

  it('with a key and no call: the dates, the switch off, regenerate behind a confirmation', async () => {
    vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: true, enabled: false, createdAt: '2026-09-14T16:00:00Z' })
    vi.mocked(api.adminGenerateDeliveryReplyKey).mockResolvedValue({ key: 'new-key' })
    mount()
    expect(await screen.findByText('No call received since the key was created')).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: 'Apply replies at delivery' })).not.toBeChecked()
    await userEvent.click(screen.getByRole('button', { name: 'Regenerate' }))
    expect(screen.getByText('Regenerate the key?')).toBeInTheDocument()
    expect(api.adminGenerateDeliveryReplyKey).not.toHaveBeenCalled()
  })

  it('with a call received: the pill; the switch writes and toasts', async () => {
    vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: true, enabled: true, createdAt: '2026-09-14T16:00:00Z', lastCallAt: '2026-09-14T16:40:00Z' })
    vi.mocked(api.adminSetDeliveryReplies).mockResolvedValue(undefined)
    const addToast = mount()
    expect(await screen.findByText(/Last call received on/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('checkbox', { name: 'Apply replies at delivery' }))
    expect(api.adminSetDeliveryReplies).toHaveBeenCalledWith({ enabled: false })
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('Replies are now applied when the mail is opened.'))
  })

  it('deleting asks first, then toasts', async () => {
    vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: true, enabled: false, createdAt: '2026-09-14T16:00:00Z' })
    vi.mocked(api.adminDeleteDeliveryReplyKey).mockResolvedValue(undefined)
    const addToast = mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Delete' }))
    // .at(-1) needs es2022 lib, which this project's tsconfig does not set (SchedulingAccountSection's
    // own tests use the same indexed form for the identical reason).
    const deleteButtons = screen.getAllByRole('button', { name: 'Delete' })
    await userEvent.click(deleteButtons[deleteButtons.length - 1])
    await waitFor(() => expect(addToast).toHaveBeenCalledWith('The key was deleted.'))
  })

  // The confirm's own opener is the card's trash button, and the card becomes the empty branch
  // with the key: the section heading is what focus falls back to.
  it('hands focus to the section heading once the deleted key takes the trash button', async () => {
    let configured = true
    vi.mocked(api.adminGetDeliveryReplyKey).mockImplementation(async () =>
      (configured
        ? { configured: true, enabled: false, createdAt: '2026-09-14T16:00:00Z' }
        : { configured: false, enabled: false }))
    vi.mocked(api.adminDeleteDeliveryReplyKey).mockImplementation(async () => { configured = false })
    mount()
    await userEvent.click(await screen.findByRole('button', { name: 'Delete' }))

    const deleteButtons = screen.getAllByRole('button', { name: 'Delete' })
    await userEvent.click(deleteButtons[deleteButtons.length - 1])

    await waitFor(() => expect(screen.getByRole('button', { name: 'Generate a key' })).toBeInTheDocument())
    expect(screen.getByRole('heading', { name: "Guests' replies at delivery" })).toHaveFocus()
  })

  // The one case the card's own `cardRef` guard cannot reach, and therefore the one that reads the
  // confirm's `returnFocusRef`: the key goes while the confirm is open — focus is inside the
  // dialog, not inside the card, so the guard does nothing — and the ✕ then closes over an opener
  // that is no longer there. Nothing is replaced by the close itself.
  it('hands focus to the section heading when the ✕ closes over a vanished trash button', async () => {
    let resolveRefetch: (value: { configured: boolean, enabled: boolean }) => void = () => {}
    vi.mocked(api.adminGetDeliveryReplyKey)
      .mockResolvedValueOnce({ configured: true, enabled: false, createdAt: '2026-09-14T16:00:00Z' })
      .mockReturnValue(new Promise(resolve => { resolveRefetch = resolve }))
    const { client } = mountExposingClient()
    await userEvent.click(await screen.findByRole('button', { name: 'Delete' }))

    // Another admin deleted it: the refetch lands while the confirm stands.
    void client.invalidateQueries()
    await act(async () => resolveRefetch({ configured: false, enabled: false }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Generate a key' })).toBeInTheDocument())

    await userEvent.click(screen.getByRole('button', { name: 'Close' }))

    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
    expect(screen.getByRole('heading', { name: "Guests' replies at delivery" })).toHaveFocus()
  })

  describe('the switch look', () => {
    it('carries is-locked without a key', async () => {
      vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: false, enabled: false })
      mount()
      const toggle = await screen.findByRole('checkbox', { name: 'Apply replies at delivery' })
      expect(toggle.closest('.toggle-switch')).toHaveClass('is-locked')
    })

    it('is not locked once a key exists', async () => {
      vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: true, enabled: false, createdAt: '2026-09-14T16:00:00Z' })
      mount()
      const toggle = await screen.findByRole('checkbox', { name: 'Apply replies at delivery' })
      expect(toggle.closest('.toggle-switch')).not.toHaveClass('is-locked')
    })
  })

  // .field-h.is-setting's 260px fixed label column is what put the switch far right of the
  // label; the row was moved off that modifier and must not regain it.
  it('keeps the toggle row out of the .field-h fixed label column', async () => {
    vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: true, enabled: true, createdAt: '2026-09-14T16:00:00Z' })
    mount()
    const label = await screen.findByText('Apply replies at delivery')
    expect(label.closest('.dlv-toggle')).not.toHaveClass('field-h')
  })

  describe('focus after the key dialog closes', () => {
    it('returns to the section heading once the generate button that opened it is gone', async () => {
      // A deferred second answer, not mockResolvedValue: a real refetch takes a network round
      // trip, landing well after the dialog has mounted and captured the still-present Generate
      // button — an already-settled mock would fold both renders into one commit and never
      // exercise the case (an unrelated jsdom quirk then hands the dialog's mount effect a
      // sibling button as "previously focused", masking what this test means to cover).
      let resolveRefetch: (value: { configured: boolean, enabled: boolean, createdAt?: string }) => void = () => {}
      vi.mocked(api.adminGetDeliveryReplyKey)
        .mockResolvedValueOnce({ configured: false, enabled: false })
        .mockReturnValue(new Promise(resolve => { resolveRefetch = resolve }))
      vi.mocked(api.adminGenerateDeliveryReplyKey).mockResolvedValue({ key: 'abc-key' })
      mount()
      await userEvent.click(await screen.findByRole('button', { name: 'Generate a key' }))
      await screen.findByRole('dialog', { name: 'Delivery key' })
      expect(screen.getByRole('button', { name: 'Generate a key' })).toBeInTheDocument()

      // The refetch lands while the dialog is open, replacing the Generate button that
      // useLayer captured as the element to restore focus to.
      await act(async () => resolveRefetch({ configured: true, enabled: false, createdAt: '2026-09-14T16:00:00Z' }))
      await waitFor(() => expect(screen.queryByRole('button', { name: 'Generate a key' })).not.toBeInTheDocument())

      await userEvent.keyboard('{Escape}')

      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
      expect(screen.getByRole('heading', { name: "Guests' replies at delivery" })).toHaveFocus()
    })

    // The confirm is a layer of its own now, so it hands the focus back to the Regenerate button
    // it was opened from — still on screen, the key having stayed configured — and the key
    // dialog, mounting in that same commit, captures it and returns there in turn. It used to
    // land on the heading, the confirm leaving focus on <body> for the dialog to find.
    it('returns to the button the regenerate was asked from', async () => {
      vi.mocked(api.adminGetDeliveryReplyKey).mockResolvedValue({ configured: true, enabled: false, createdAt: '2026-09-14T16:00:00Z' })
      vi.mocked(api.adminGenerateDeliveryReplyKey).mockResolvedValue({ key: 'new-key' })
      mount()
      await userEvent.click(await screen.findByRole('button', { name: 'Regenerate' }))
      const confirmButtons = screen.getAllByRole('button', { name: 'Regenerate' })
      await userEvent.click(confirmButtons[confirmButtons.length - 1])
      await screen.findByRole('dialog', { name: 'Delivery key' })

      await userEvent.keyboard('{Escape}')

      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
      expect(screen.getByRole('button', { name: 'Regenerate' })).toHaveFocus()
    })
  })

  // The card's <div> used to be the same element whether configured or empty, so React never
  // remounted it on that flip and cardRef's callback never fired — a focused element inside it
  // (here the Regenerate button) was simply removed from under the cursor, dropping focus to
  // <body> instead of being handed back to the heading. A key on the card is the fix.
  it('moves focus to the heading when the card empties out from under a focused Regenerate button', async () => {
    let resolveRefetch: (value: { configured: boolean, enabled: boolean }) => void = () => {}
    vi.mocked(api.adminGetDeliveryReplyKey)
      .mockResolvedValueOnce({ configured: true, enabled: false, createdAt: '2026-09-14T16:00:00Z' })
      .mockReturnValue(new Promise(resolve => { resolveRefetch = resolve }))
    const { client } = mountExposingClient()

    const regenerateButton = await screen.findByRole('button', { name: 'Regenerate' })
    act(() => regenerateButton.focus())
    expect(regenerateButton).toHaveFocus()

    // A refetch — the same one a mutation's onSettled triggers, e.g. after a 404 on Regenerate
    // once another admin has already deleted the key — answers that the key is gone, while focus
    // is still on the button that is about to be removed.
    void client.invalidateQueries()
    await act(async () => resolveRefetch({ configured: false, enabled: false }))

    await waitFor(() => expect(screen.queryByRole('button', { name: 'Regenerate' })).not.toBeInTheDocument())
    expect(screen.getByRole('heading', { name: "Guests' replies at delivery" })).toHaveFocus()
  })
})
