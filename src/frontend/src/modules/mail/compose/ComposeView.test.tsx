import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import ComposeView from './ComposeView'
import { useIdentities } from '../queries'
import type { EditorHandle } from './SquireEditor'

const mocks = vi.hoisted(() => ({
  sendMessage: vi.fn(),
  deleteAttachment: vi.fn(),
  uploadAttachment: vi.fn(),
}))
// The editor's own state, shared with the stub below: Squire needs a real browser, so mounting
// it here would only re-test what SquireEditor.mount already covers.
const editorState = vi.hoisted(() => ({ html: '', commands: [] as string[] }))

vi.mock('../../../api.js', () => ({
  api: { sendMessage: mocks.sendMessage, deleteAttachment: mocks.deleteAttachment },
  uploadAttachment: mocks.uploadAttachment,
}))
vi.mock('../queries', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../queries')>()
  return { ...actual, useIdentities: vi.fn() }
})
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({
    activeAccount: { id: 'primary' },
    identity: { displayName: 'Mick Weesky', email: 'mick@weesky.be' },
  }),
}))
vi.mock('./SquireEditor', async () => {
  const { forwardRef, useImperativeHandle } = await import('react')
  const Stub = forwardRef<EditorHandle, { onChange: () => void }>(
    function SquireEditorStub({ onChange }, ref) {
      useImperativeHandle(ref, () => ({
        getHTML: () => editorState.html,
        isEmpty: () => editorState.html === '',
        focus: () => {},
        command: (name: string) => { editorState.commands.push(name) },
        setTextColour: () => {}, setHighlightColour: () => {},
        setFontFace: () => {}, setFontSize: () => {}, setAlignment: () => {}, makeLink: () => {},
      }), [])
      return (
        <textarea
          data-testid="compose-editor"
          onChange={event => { editorState.html = event.target.value; onChange() }}
        />
      )
    })
  return { default: Stub }
})

function renderCompose(from = 'INBOX') {
  const onNotify = vi.fn()
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const router = createMemoryRouter(
    [
      { path: '/mail', element: <span data-testid="mail-view">mail</span> },
      { path: '/mail/compose', element: <ComposeView onNotify={onNotify} /> },
    ],
    { initialEntries: ['/mail', { pathname: '/mail/compose', state: { from } }], initialIndex: 1 },
  )
  render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>)
  return { onNotify, router }
}

function addRecipient(label: string, value: string) {
  const input = screen.getByLabelText(label)
  fireEvent.change(input, { target: { value } })
  fireEvent.keyDown(input, { key: 'Enter' })
}

const sendButton = () => screen.getByRole('button', { name: 'Send' })
const attach = (name = 'a.txt') =>
  fireEvent.change(screen.getByTestId('attachment-input'),
    { target: { files: [new File(['x'], name, { type: 'text/plain' })] } })

async function discardModal() {
  const title = await screen.findByText('Discard this message?')
  return title.closest('.modal') as HTMLElement
}

const identityList = [
  { address: 'mick@weesky.be', displayName: 'Mick', isDefault: false, isPrimary: true, stale: false, labelIsCustom: false },
  { address: 'michel@weesky.be', displayName: 'Michel', isDefault: true, isPrimary: false, stale: false, labelIsCustom: true },
]

beforeEach(() => {
  vi.clearAllMocks()
  editorState.html = ''
  editorState.commands = []
  mocks.uploadAttachment.mockResolvedValue({ id: 'att-1', size: 3 })
  mocks.deleteAttachment.mockResolvedValue(undefined)
  // Default: identities still loading — every pre-existing test keeps the 2c1 plain From.
  vi.mocked(useIdentities).mockReturnValue({ data: undefined } as never)
})

describe('ComposeView', () => {
  it('shows the identity as plain-text From, focuses To and refuses to send with no recipient', () => {
    renderCompose()

    expect(screen.getByText('Mick Weesky (mick@weesky.be)')).toBeInTheDocument()
    expect(screen.queryByRole('textbox', { name: 'From' })).toBeNull()
    expect(screen.getByLabelText('To')).toHaveFocus()
    expect(screen.getByLabelText('Subject')).toBeInTheDocument()
    expect(sendButton()).toBeDisabled()
  })

  it('hides Cc and Bcc behind their links until each is clicked', () => {
    renderCompose()

    expect(screen.queryByLabelText('Cc')).toBeNull()
    expect(screen.queryByLabelText('Bcc')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Cc' }))
    expect(screen.getByLabelText('Cc')).toBeInTheDocument()
    expect(screen.queryByLabelText('Bcc')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Bcc' }))
    expect(screen.getByLabelText('Bcc')).toBeInTheDocument()
  })

  it('enables Send on a valid recipient and disables it again on an invalid one', () => {
    renderCompose()

    addRecipient('To', 'a@b.c')
    expect(sendButton()).toBeEnabled()

    addRecipient('To', 'nope')
    expect(sendButton()).toBeDisabled()
  })

  it('posts the composed message, toasts, and returns to the folder it came from', async () => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    const { onNotify, router } = renderCompose('Projects')

    addRecipient('To', 'a@b.c')
    fireEvent.click(screen.getByRole('button', { name: 'Cc' }))
    addRecipient('Cc', 'c@b.c')
    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'Hello' } })
    fireEvent.change(screen.getByTestId('compose-editor'), { target: { value: '<p>Hi</p>' } })
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalledWith({
      to: ['a@b.c'], cc: ['c@b.c'], bcc: [], subject: 'Hello', htmlBody: '<p>Hi</p>', attachmentIds: [],
    }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/mail'))
    expect(router.state.location.search).toBe('?folder=Projects')
    expect(onNotify).toHaveBeenCalledWith('Message sent')
  })

  // The mail left, only the Sent copy did not: a success the reader has to be told about, not a
  // failure — the message is gone either way.
  it('softens the toast when no Sent copy could be filed', async () => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: false })
    const { onNotify, router } = renderCompose()

    addRecipient('To', 'a@b.c')
    fireEvent.click(sendButton())

    await waitFor(() => expect(router.state.location.pathname).toBe('/mail'))
    expect(onNotify).toHaveBeenCalledWith('Message sent — no Sent copy could be filed')
  })

  it('keeps the view and its staged files when the send fails', async () => {
    mocks.sendMessage.mockRejectedValue(new Error('Bad gateway'))
    const { onNotify, router } = renderCompose()

    attach()
    await screen.findByText('a.txt')
    addRecipient('To', 'a@b.c')
    fireEvent.click(sendButton())

    await waitFor(() => expect(onNotify).toHaveBeenCalledWith('Bad gateway', 'error'))
    expect(router.state.location.pathname).toBe('/mail/compose')
    expect(screen.getByTestId('compose-view')).toBeInTheDocument()
    expect(screen.getByText('a.txt')).toBeInTheDocument()
    expect(mocks.deleteAttachment).not.toHaveBeenCalled()
  })

  // Sending before the upload lands would post an attachmentIds list short of that file.
  it('keeps Send disabled while an upload is in flight', async () => {
    mocks.uploadAttachment.mockReturnValue(new Promise(() => {}))
    renderCompose()

    addRecipient('To', 'a@b.c')
    expect(sendButton()).toBeEnabled()

    attach()
    await waitFor(() => expect(sendButton()).toBeDisabled())
  })

  // A second click while the request is in flight would double-post the same message.
  it('keeps Send disabled and labelled "Sending…" while the request is in flight', async () => {
    mocks.sendMessage.mockReturnValue(new Promise(() => {}))
    renderCompose()

    addRecipient('To', 'a@b.c')
    fireEvent.click(sendButton())

    await waitFor(() => expect(screen.getByRole('button', { name: 'Sending…' })).toBeDisabled())
  })

  it('drives the editor from the toolbar before anything is typed', () => {
    renderCompose()

    fireEvent.click(screen.getByRole('button', { name: 'Bold' }))

    expect(editorState.commands).toContain('bold')
  })

  it('keeps the 2c1 plain From while identities are still loading', () => {
    renderCompose()

    expect(screen.getByText('Mick Weesky (mick@weesky.be)')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'From identity' })).toBeNull()
  })

  it('preselects the default identity and sends its address', async () => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    vi.mocked(useIdentities).mockReturnValue({ data: identityList } as never)
    renderCompose()

    expect(screen.getByRole('button', { name: 'From identity' })).toHaveTextContent('Michel (michel@weesky.be)')
    addRecipient('To', 'a@b.c')
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalledWith(
      expect.objectContaining({ fromAddress: 'michel@weesky.be' })))
  })

  // ComposeView is the only owner of the resolution now, so the address on the trigger and the
  // one in the payload have to stay the same thing after a pick, not just at the default.
  it('sends the identity picked in the menu, not the default it replaced', async () => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    vi.mocked(useIdentities).mockReturnValue({ data: identityList } as never)
    renderCompose()

    fireEvent.click(screen.getByRole('button', { name: 'From identity' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Mick Weesky (mick@weesky.be)' }))
    expect(screen.getByRole('button', { name: 'From identity' })).toHaveTextContent('Mick Weesky (mick@weesky.be)')

    addRecipient('To', 'a@b.c')
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalledWith(
      expect.objectContaining({ fromAddress: 'mick@weesky.be' })))
  })

  // A poll can mark the pick stale under an open composer. The payload still carries it — the
  // send is refused by name — so the From line has to keep saying which address that is.
  it('keeps naming a chosen identity that goes stale under the composer', () => {
    vi.mocked(useIdentities).mockReturnValue({ data: identityList } as never)
    renderCompose()

    fireEvent.click(screen.getByRole('button', { name: 'From identity' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Mick Weesky (mick@weesky.be)' }))

    vi.mocked(useIdentities).mockReturnValue(
      { data: [{ ...identityList[0], stale: true }, identityList[1]] } as never)
    // Any state change; the refetched list lands on the next render.
    fireEvent.click(screen.getByRole('button', { name: 'Cc' }))

    expect(screen.getByRole('button', { name: 'From identity' })).toHaveTextContent('Mick Weesky (mick@weesky.be)')
    expect(screen.getByText('unavailable')).toBeInTheDocument()
  })

  // The guard exists for content the user would lose: a From choice is part of the message once
  // there is one, and nothing to discard when there is not.
  it('changing the identity still dirties a composer that has content', async () => {
    vi.mocked(useIdentities).mockReturnValue({ data: identityList } as never)
    renderCompose()

    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'draft' } })
    fireEvent.click(screen.getByRole('button', { name: 'From identity' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Mick Weesky (mick@weesky.be)' }))
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(await discardModal()).toBeInTheDocument()
  })

  it('changing the identity leaves an otherwise-empty composer clean', async () => {
    vi.mocked(useIdentities).mockReturnValue({ data: identityList } as never)
    const { router } = renderCompose()

    fireEvent.click(screen.getByRole('button', { name: 'From identity' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Mick Weesky (mick@weesky.be)' }))
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/mail'))
    expect(screen.queryByText('Discard this message?')).toBeNull()
  })
})

// No drafts yet: leaving loses the message, so every way out asks the same question — the ✕, a
// folder click (a plain navigation out of /mail/compose) and the browser's Back button.
describe('leaving a dirty composer', () => {
  it('asks before the ✕ leaves, and stays put on Keep editing', async () => {
    const { router } = renderCompose()

    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'draft' } })
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    const modal = await discardModal()
    fireEvent.click(within(modal).getByRole('button', { name: 'Keep editing' }))

    await waitFor(() => expect(screen.queryByText('Discard this message?')).toBeNull())
    expect(router.state.location.pathname).toBe('/mail/compose')
    expect(screen.getByTestId('compose-view')).toBeInTheDocument()
  })

  it('asks on the browser Back button too', async () => {
    const { router } = renderCompose()

    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'draft' } })
    router.navigate(-1)

    expect(await discardModal()).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/mail/compose')
  })

  it('discards the staged attachments and leaves when Discard is confirmed', async () => {
    const { router } = renderCompose()

    attach()
    await screen.findByText('a.txt')
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    const modal = await discardModal()
    fireEvent.click(within(modal).getByRole('button', { name: 'Discard' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/mail'))
    expect(mocks.deleteAttachment).toHaveBeenCalledWith('att-1')
  })

  it('leaves straight away when the form is clean', async () => {
    const { router } = renderCompose()

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/mail'))
    expect(screen.queryByText('Discard this message?')).toBeNull()
  })

  // The guard must block a folder navigation once the form is dirty. The onChange callbacks mark
  // the ref synchronously so this holds even for a navigation fired in the same gesture that
  // dirtied the form — a window jsdom cannot reproduce, since act() flushes the mirroring effect
  // before this assertion runs, so this pins the post-condition rather than the race itself.
  it('blocks a navigation once the subject has been edited', () => {
    const { router } = renderCompose()

    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'draft' } })
    act(() => { router.navigate('/mail') })

    const blocked = [...router.state.blockers.values()].some(blocker => blocker.state === 'blocked')
    expect(blocked).toBe(true)
  })

  // Closing the tab is the one exit the router never sees, so the browser has to ask instead.
  it('warns the browser before the tab closes on a dirty form, and not on a clean one', () => {
    renderCompose()

    const clean = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(clean)
    expect(clean.defaultPrevented).toBe(false)

    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'draft' } })
    const dirtyEvent = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(dirtyEvent)
    expect(dirtyEvent.defaultPrevented).toBe(true)
  })
})
