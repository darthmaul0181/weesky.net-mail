import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import ComposeView from './ComposeView'
import { useIdentities } from '../queries'
import type { ComposeSeed } from './composeSeed'
import type { EditorHandle } from './SquireEditor'
import { settle } from '../../../test-utils'
import { confirmLeave } from '../../../lib/leaveGuard'

const mocks = vi.hoisted(() => ({
  sendMessage: vi.fn(),
  deleteAttachment: vi.fn(),
  uploadAttachment: vi.fn(),
  saveDraft: vi.fn(),
  deleteMessages: vi.fn(),
  getContacts: vi.fn(),
  getPreferences: vi.fn(),
  createContact: vi.fn(),
  deleteContact: vi.fn(),
  apiBase: 'https://api.test.example',
}))
// The editor's own state, shared with the stub below: Squire needs a real browser, so mounting
// it here would only re-test what SquireEditor.mount already covers.
const editorState = vi.hoisted(() => ({ html: '', commands: [] as string[], images: [] as string[] }))

vi.mock('../../../api.js', () => ({
  api: {
    sendMessage: mocks.sendMessage, deleteAttachment: mocks.deleteAttachment,
    saveDraft: mocks.saveDraft, deleteMessages: mocks.deleteMessages,
    getContacts: mocks.getContacts, getPreferences: mocks.getPreferences,
    createContact: mocks.createContact, deleteContact: mocks.deleteContact,
  },
  uploadAttachment: mocks.uploadAttachment,
  API_BASE: mocks.apiBase,
  stagedAttachmentUrl: (id: string) => `${mocks.apiBase}/api/Mail/Attachments/${id}/content`,
}))
vi.mock('../queries', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../queries')>()
  return { ...actual, useIdentities: vi.fn() }
})
// Mutable so a test can switch accounts under an open composer.
const auth = vi.hoisted(() => ({ activeAccountId: 'primary' }))

vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({
    activeAccount: { id: auth.activeAccountId },
    activeAccountId: auth.activeAccountId,
    identity: { displayName: 'Mick Weesky', email: 'mick@weesky.be' },
  }),
}))

beforeEach(() => { auth.activeAccountId = 'primary' })
vi.mock('./SquireEditor', async () => {
  const { forwardRef, useImperativeHandle } = await import('react')
  const Stub = forwardRef<EditorHandle, { onChange: () => void; initialHtml?: string }>(
    function SquireEditorStub({ onChange, initialHtml }, ref) {
      useImperativeHandle(ref, () => ({
        getHTML: () => editorState.html,
        isEmpty: () => editorState.html === '',
        focus: () => {},
        command: (name: string) => { editorState.commands.push(name) },
        setTextColour: () => {}, setHighlightColour: () => {},
        setFontFace: () => {}, setFontSize: () => {}, setAlignment: () => {}, makeLink: () => {},
        insertImage: (src: string) => { editorState.images.push(src) },
      }), [])
      return (
        <textarea
          data-testid="compose-editor"
          defaultValue={initialHtml}
          onChange={event => { editorState.html = event.target.value; onChange() }}
        />
      )
    })
  return { default: Stub }
})

function renderCompose(from = 'INBOX', seed?: ComposeSeed, search = '') {
  const onNotify = vi.fn()
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  const router = createMemoryRouter(
    [
      { path: '/mail', element: <span data-testid="mail-view">mail</span> },
      { path: '/mail/compose', element: <ComposeView onNotify={onNotify} /> },
    ],
    { initialEntries: ['/mail', { pathname: '/mail/compose', search, state: { from, seed } }], initialIndex: 1 },
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
  const title = await screen.findByText('Save this draft?')
  return title.closest('.modal') as HTMLElement
}

// items carries the type because a real drag exposes it before the drop, and the overlay's
// wording is decided from it.
function fileDragData(files: File[] = []) {
  return {
    dataTransfer: {
      types: ['Files'], files,
      items: files.map(file => ({ kind: 'file', type: file.type })),
    },
  }
}

const bruno = {
  id: 'b', firstName: 'Bruno', lastName: 'Mertens', nickname: null, isFavorite: false,
  addresses: ['bruno@x.be'],
}

const identityList = [
  { address: 'mick@weesky.be', displayName: 'Mick', isDefault: false, isPrimary: true, stale: false, labelIsCustom: false },
  { address: 'michel@weesky.be', displayName: 'Michel', isDefault: true, isPrimary: false, stale: false, labelIsCustom: true },
]

beforeEach(() => {
  vi.clearAllMocks()
  editorState.html = ''
  editorState.commands = []
  editorState.images = []
  mocks.uploadAttachment.mockResolvedValue({ id: 'att-1', size: 3 })
  mocks.deleteAttachment.mockResolvedValue(undefined)
  mocks.deleteMessages.mockResolvedValue(undefined)
  mocks.saveDraft.mockResolvedValue({ uid: 7, folderPath: 'Drafts' })
  mocks.getContacts.mockResolvedValue({ contacts: [bruno] })
  // Capture off by default: every test outside its own describe sends to addresses the book does
  // not hold, and would otherwise assert against a composer quietly creating contacts.
  mocks.getPreferences.mockResolvedValue({ 'contacts.captureRecipients': 'false' })
  // Default: identities still loading — every pre-existing test keeps the 2c1 plain From.
  vi.mocked(useIdentities).mockReturnValue({ data: undefined } as never)
})

// A draft belongs to the mailbox it was started in: its staged files live in that account's
// namespace, so a switch under an open composer must not redirect the send that consumes them.
describe('a composer opened under a connected account', () => {
  it('sends, stages and releases against the account it opened under, never the newly active one', async () => {
    auth.activeAccountId = 'linked-1'
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    mocks.uploadAttachment.mockResolvedValue({ id: 'id-1', fileName: 'a.txt', size: 4, contentType: 'text/plain' })
    renderCompose()

    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'staged here' } })
    await waitFor(() =>
      expect(mocks.uploadAttachment).not.toHaveBeenCalled())

    // The user switches mailboxes while the draft is open; the composer must not follow.
    auth.activeAccountId = 'primary'
    addRecipient('To', 'a@b.c')

    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalledWith(
      expect.objectContaining({ subject: 'staged here' }), { accountId: 'linked-1' }))
  })

  // The From list has to be the bound account's: an address only the newly active account owns
  // is refused by the backend on both Send and Save draft, and the draft becomes unusable.
  it('reads the From list from the account it opened under, not the newly active one', () => {
    auth.activeAccountId = 'linked-1'
    renderCompose()

    expect(vi.mocked(useIdentities)).toHaveBeenCalledWith('linked-1')

    auth.activeAccountId = 'primary'
    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'switched under me' } })

    expect(vi.mocked(useIdentities)).toHaveBeenLastCalledWith('linked-1')
  })
})

describe('ComposeView', () => {
  it('shows the identity as plain-text From, focuses To and refuses to send with no recipient', () => {
    renderCompose()

    expect(screen.getByText('New message')).toBeInTheDocument()
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

  // The book is read here and handed to the fields, so this is the only place the wiring shows.
  // All three, not just To: a prop dropped from one field alone is what a To-only test misses.
  it('offers contact suggestions in To, Cc and Bcc alike', async () => {
    renderCompose()
    fireEvent.click(screen.getByRole('button', { name: 'Cc' }))
    fireEvent.click(screen.getByRole('button', { name: 'Bcc' }))

    for (const label of ['To', 'Cc', 'Bcc']) {
      const input = screen.getByLabelText(label)
      fireEvent.change(input, { target: { value: 'bru' } })

      const field = within(input.closest('.recipients-field') as HTMLElement)
      expect(await field.findByRole('option', { name: /bruno@x\.be/ })).toHaveTextContent('Bruno Mertens')

      // Emptied before the next field, so each assertion sees only its own dropdown.
      fireEvent.change(input, { target: { value: '' } })
    }
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
      priority: 'normal',
    }, { accountId: 'primary' }))
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
      expect.objectContaining({ fromAddress: 'michel@weesky.be' }), { accountId: 'primary' }))
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
      expect.objectContaining({ fromAddress: 'mick@weesky.be' }), { accountId: 'primary' }))
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
    expect(screen.queryByText('Save this draft?')).toBeNull()
  })

  // Switching mailbox is a state change, not a navigation, so the router blocker cannot see it.
  // Without the guard the identity menu carried the draft away in silence.
  it('asks before a mailbox switch takes an unsaved draft away', async () => {
    renderCompose()
    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'draft' } })

    let decision!: Promise<boolean>
    act(() => { decision = confirmLeave() })

    expect(await discardModal()).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Keep editing' }))

    await expect(decision).resolves.toBe(false)
    expect(screen.getByLabelText('Subject')).toHaveValue('draft')
  })

  it('lets a mailbox switch through when the composer has nothing to lose', async () => {
    renderCompose()

    await expect(confirmLeave()).resolves.toBe(true)
    expect(screen.queryByText('Save this draft?')).toBeNull()
  })
})

describe('the priority row', () => {
  const trigger = () => screen.getByRole('button', { name: 'Priority' })
  const pick = (label: string) => {
    fireEvent.click(trigger())
    fireEvent.click(screen.getByRole('menuitem', { name: label }))
  }
  const draftSeed: ComposeSeed = {
    action: 'draft', to: ['alice@ext.example'], cc: [], bcc: [],
    subject: 'Half written', html: '<p>later</p>', text: null, fromAddress: null, attachments: [],
    inReplyTo: null, references: [], draftRef: { folderPath: 'Drafts', uid: 41 },
    nameHints: {}, priority: 'normal',
  }

  it('reveals the priority row from the link beside Cc and Bcc', () => {
    renderCompose()
    expect(screen.queryByText('Priority', { selector: '.compose-priority-label' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByText('Priority'))

    expect(trigger()).toHaveTextContent('Normal')
  })

  it('sends the chosen priority', async () => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    renderCompose()

    fireEvent.click(screen.getByText('Priority'))
    pick('High')
    addRecipient('To', 'a@b.c')
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalledWith(
      expect.objectContaining({ priority: 'high' }), { accountId: 'primary' }))
  })

  it('sends normal when the row was never opened', async () => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    renderCompose()

    addRecipient('To', 'a@b.c')
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalledWith(
      expect.objectContaining({ priority: 'normal' }), { accountId: 'primary' }))
  })

  /** Folding the row away would take a live setting off the screen while it still rides the mail. */
  it('keeps the priority row open while the value is not normal', () => {
    renderCompose()

    fireEvent.click(screen.getByText('Priority'))
    pick('Low')

    expect(screen.queryByText('Priority', { selector: '.compose-link-btn' })).not.toBeInTheDocument()
    expect(trigger()).toHaveTextContent('Low')
  })

  // Back at Normal there is nothing left to keep on screen, so the row folds into its link again.
  it('folds the row away once the value returns to normal', () => {
    renderCompose()

    fireEvent.click(screen.getByText('Priority'))
    pick('High')
    pick('Normal')

    expect(screen.getByText('Priority', { selector: '.compose-link-btn' })).toBeInTheDocument()
  })

  /** Same rule as a From-only edit: on a resumed draft the priority is the only change there is,
      and leaving without the guard would silently drop it. */
  it('arms the leave guard on a priority-only change to a resumed draft', async () => {
    const { router } = renderCompose('INBOX', draftSeed)

    fireEvent.click(screen.getByText('Priority'))
    pick('High')
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(await discardModal()).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/mail/compose')
  })

  // The other half of the From rule: there is no message here to attach the setting to, so the
  // prompt would offer to file a draft holding nothing but a priority header.
  it('leaves an otherwise-empty composer clean', async () => {
    const { router } = renderCompose()

    fireEvent.click(screen.getByText('Priority'))
    pick('High')
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/mail'))
    expect(screen.queryByText('Save this draft?')).toBeNull()
  })

  // The whole point of storing it on the draft: the resumed composer has to open on it and save
  // it back, or the setting is lost on the first round trip.
  it('opens a resumed draft on its stored priority and saves it back', async () => {
    renderCompose('INBOX', { ...draftSeed, priority: 'high' })

    expect(trigger()).toHaveTextContent('High')
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }))

    await waitFor(() => expect(mocks.saveDraft).toHaveBeenCalledWith(
      expect.objectContaining({ priority: 'high' }), { accountId: 'primary' }))
  })
})

// A reply/forward arrives as router state; the composer opens on it instead of on a blank form.
describe('a seeded ComposeView', () => {
  const seed: ComposeSeed = {
    action: 'reply',
    to: ['alice@ext.example'], cc: ['bob@ext.example'], bcc: [],
    subject: 'Re: Hello', html: '<div><br></div><blockquote><p>original</p></blockquote>', text: null,
    fromAddress: null,
    attachments: [
      { id: 'i1', fileName: 'logo.png', size: 3, contentType: 'image/png', contentId: 'logo@x' },
      { id: 'a1', fileName: 'doc.pdf', size: 9, contentType: 'application/pdf', contentId: null },
    ],
    inReplyTo: 'm@x', references: ['m@x'],
    draftRef: null, nameHints: {}, priority: 'normal',
  }

  it('prefills the form', () => {
    renderCompose('INBOX', seed)

    expect(screen.getByText('alice@ext.example')).toBeInTheDocument()
    expect(screen.getByLabelText('Cc')).toBeInTheDocument()
    expect(screen.getByText('bob@ext.example')).toBeInTheDocument()
    expect(screen.getByLabelText('Subject')).toHaveValue('Re: Hello')
    expect(screen.getByTestId('compose-editor')).toHaveValue(seed.html)
  })

  // The recipients are already filled in; the one thing left to do is write, so the editor takes
  // the focus the To field keeps on a blank composer.
  it('leaves the focus to the editor instead of the To field', () => {
    renderCompose('INBOX', seed)

    expect(screen.getByLabelText('To')).not.toHaveFocus()
  })

  // "New message" over a quoted reply describes the wrong thing; edit-as-new genuinely is one.
  it.each([
    ['reply', 'Reply'], ['replyAll', 'Reply'], ['forward', 'Forward'], ['editAsNew', 'New message'],
    ['draft', 'Draft'],
  ] as const)('titles a %s composer "%s"', (action, title) => {
    renderCompose('INBOX', { ...seed, action })

    expect(screen.getByText(title)).toBeInTheDocument()
  })

  it('seeds only regular attachments into the tray', () => {
    renderCompose('INBOX', seed)

    expect(screen.getByText('doc.pdf')).toBeInTheDocument()
    expect(screen.queryByText('logo.png')).toBeNull()
  })

  it('sends the threading and every staged id', async () => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    renderCompose('INBOX', seed)

    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalledWith(expect.objectContaining({
      inReplyTo: 'm@x', references: ['m@x'], attachmentIds: expect.arrayContaining(['i1', 'a1']),
    }), { accountId: 'primary' }))
  })

  // The composer displays staged images absolute, but MailSender swaps a staged URL for a cid by
  // matching the relative form only — and an absolute https URL would survive the outgoing
  // sanitiser and ship a link to our API in the recipient's mail.
  it('relativizes the staged image URLs before they go on the wire', async () => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    renderCompose('INBOX', seed)
    editorState.html = `<p>hi</p><img src="${mocks.apiBase}/api/Mail/Attachments/i1/content">`

    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalled())
    const { htmlBody } = mocks.sendMessage.mock.calls[0][0]
    expect(htmlBody).toContain('src="/api/Mail/Attachments/i1/content"')
    expect(htmlBody).not.toContain(mocks.apiBase)
  })

  // Only the quoted body is seeded here: the guard has to trip on it alone, not on a recipient
  // or a subject it happens to come with.
  it('a seeded composer is dirty from the start', async () => {
    const bodyOnly: ComposeSeed = {
      ...seed, to: [], cc: [], bcc: [], subject: '', attachments: [], inReplyTo: null, references: [],
    }
    const { router } = renderCompose('INBOX', bodyOnly)

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(await discardModal()).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/mail/compose')
  })

  it('opens prefilled from a mailto url carrying no seed', async () => {
    renderCompose('INBOX', undefined,
      '?mailto=' + encodeURIComponent('mailto:alice@weesky.be?subject=Hello'))

    expect(await screen.findByDisplayValue('Hello')).toBeInTheDocument()
    expect(screen.getByText('alice@weesky.be')).toBeInTheDocument()
  })

  // The parser escapes, but nothing else on this road does: the editor is handed the seed's html
  // verbatim, so this is where an escaping that never reached it would show.
  it('hands the editor the mailto body escaped', async () => {
    renderCompose('INBOX', undefined,
      '?mailto=' + encodeURIComponent('mailto:alice@weesky.be?body=<img src=x onerror=alert(1)>'))

    const editor = await screen.findByTestId('compose-editor')
    expect(editor).toHaveValue('<div>&lt;img src=x onerror=alert(1)&gt;</div>')
  })

  it('releases the inline resources too when the message is discarded', async () => {
    renderCompose('INBOX', seed)

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    const modal = await discardModal()
    fireEvent.click(within(modal).getByRole('button', { name: 'Discard' }))

    await waitFor(() => expect(mocks.deleteAttachment).toHaveBeenCalledWith('i1', { accountId: 'primary' }))
    expect(mocks.deleteAttachment).toHaveBeenCalledWith('a1', { accountId: 'primary' })
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

    await waitFor(() => expect(screen.queryByText('Save this draft?')).toBeNull())
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
    expect(mocks.deleteAttachment).toHaveBeenCalledWith('att-1', { accountId: 'primary' })
  })

  it('leaves straight away when the form is clean', async () => {
    const { router } = renderCompose()

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/mail'))
    expect(screen.queryByText('Save this draft?')).toBeNull()
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

describe('dropping files on the composer', () => {
  it('shows the overlay while a file drag hovers and hides it when the drag leaves', () => {
    renderCompose()
    const view = screen.getByTestId('compose-view')

    fireEvent.dragEnter(view, fileDragData())
    expect(screen.getByText('Drop files to attach')).toBeInTheDocument()

    // Nested enter/leave (child boundaries) must not blink the overlay off.
    fireEvent.dragEnter(view, fileDragData())
    fireEvent.dragLeave(view, fileDragData())
    expect(screen.getByText('Drop files to attach')).toBeInTheDocument()

    fireEvent.dragLeave(view, fileDragData())
    expect(screen.queryByText('Drop files to attach')).not.toBeInTheDocument()
  })

  it('ignores a drag that carries no files', () => {
    renderCompose()

    fireEvent.dragEnter(screen.getByTestId('compose-view'),
      { dataTransfer: { types: ['text/plain'], files: [], items: [] } })

    expect(screen.queryByText('Drop files to attach')).not.toBeInTheDocument()
  })

  // The overlay is a promise about the drop to come: over the body it may only say "into the
  // message" for a file routeFiles will actually put there.
  it('words the overlay from what the drag carries, not from where it hovers', () => {
    renderCompose()
    const body = screen.getByTestId('compose-editor')

    fireEvent.dragEnter(body, fileDragData([new File(['x'], 'a.png', { type: 'image/png' })]))
    expect(screen.getByText('Drop image into the message')).toBeInTheDocument()

    fireEvent.dragLeave(body, fileDragData())
    fireEvent.dragEnter(body, fileDragData([new File(['x'], 'r.pdf', { type: 'application/pdf' })]))
    expect(screen.getByText('Drop files to attach')).toBeInTheDocument()
  })

  it('promises no insertion over a plain-text body, which has nowhere to show an image', () => {
    renderCompose()
    fireEvent.click(screen.getByRole('button', { name: 'Plain text' }))

    fireEvent.dragEnter(screen.getByTestId('compose-text-editor'),
      fileDragData([new File(['x'], 'a.png', { type: 'image/png' })]))

    expect(screen.getByText('Drop files to attach')).toBeInTheDocument()
  })

  it('stages the dropped files and dirties the composer', async () => {
    const { router } = renderCompose()
    const file = new File(['x'], 'photo.png', { type: 'image/png' })

    // fireEvent returns false when a handler called preventDefault — pins that a drop never
    // falls through to the browser's own "open this file" navigation.
    const dropped = fireEvent.drop(screen.getByTestId('compose-view'), fileDragData([file]))
    expect(dropped).toBe(false)

    await screen.findByText('photo.png')
    expect(screen.queryByText('Drop files to attach')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(await discardModal()).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/mail/compose')
  })
})

// Drafts turn `dirty` from "the form is non-empty" into "changed since open or the last save",
// and give the leave dialog a third answer.
describe('drafts in the composer', () => {
  const draftSeed: ComposeSeed = {
    action: 'draft',
    to: ['alice@ext.example'], cc: [], bcc: [],
    subject: 'Half written', html: '<p>later</p>', text: null,
    fromAddress: null, attachments: [],
    inReplyTo: null, references: [],
    draftRef: { folderPath: 'Drafts', uid: 41 }, nameHints: {}, priority: 'normal',
  }
  const withParts: ComposeSeed = {
    ...draftSeed,
    attachments: [
      { id: 'i1', fileName: 'logo.png', size: 3, contentType: 'image/png', contentId: 'logo@x' },
      { id: 'a1', fileName: 'doc.pdf', size: 9, contentType: 'application/pdf', contentId: null },
    ],
  }
  // Nothing else on this seed is content the composer could add: the subject is all there is.
  const subjectOnly: ComposeSeed = { ...draftSeed, to: [], subject: 'Only this', html: '' }
  const headerSave = () => screen.getByRole('button', { name: 'Save draft' })

  it('saves a draft and replaces it on the next save', async () => {
    const { onNotify } = renderCompose()

    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'Notes' } })
    fireEvent.click(headerSave())

    await waitFor(() => expect(onNotify).toHaveBeenCalledWith('Draft saved'))
    expect(mocks.saveDraft.mock.calls[0][0]).toMatchObject({ subject: 'Notes' })
    expect(mocks.saveDraft.mock.calls[0][0].replaceUid).toBeUndefined()

    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'Notes and more' } })
    fireEvent.click(headerSave())

    await waitFor(() => expect(mocks.saveDraft).toHaveBeenCalledTimes(2))
    expect(mocks.saveDraft.mock.calls[1][0]).toMatchObject({ subject: 'Notes and more', replaceUid: 7 })
  })

  // The old rule was "the form is non-empty", which a resumed draft satisfies without a single
  // edit — and which no From pick could ever satisfy on its own.
  it('a draft seed opens clean and a From-only change dirties it', async () => {
    vi.mocked(useIdentities).mockReturnValue({ data: identityList } as never)
    const untouched = renderCompose('INBOX', draftSeed)

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    await waitFor(() => expect(untouched.router.state.location.pathname).toBe('/mail'))
    expect(screen.queryByText('Save this draft?')).toBeNull()
    cleanup()

    const { router } = renderCompose('INBOX', draftSeed)
    fireEvent.click(screen.getByRole('button', { name: 'From identity' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Mick Weesky (mick@weesky.be)' }))
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(await discardModal()).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/mail/compose')
  })

  // Removing a part is the one edit that shrinks the form, so the non-empty clause can never see
  // it: only the callback marking the composer changed can.
  it('removing an attachment dirties a resumed draft', async () => {
    const { router } = renderCompose('INBOX', withParts)

    fireEvent.click(screen.getByRole('button', { name: 'Remove doc.pdf' }))
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(await discardModal()).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/mail/compose')
  })

  // Clearing a filed draft is a change worth keeping; the emptied form must not read as nothing
  // to lose, or the clearing is dropped and the stored version survives it.
  it('emptying a resumed draft still asks before leaving', async () => {
    const { router } = renderCompose('INBOX', subjectOnly)

    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: '' } })
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(await discardModal()).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/mail/compose')
  })

  it('the leave dialog offers Save draft / Discard / Keep editing', async () => {
    const kept = renderCompose('INBOX', withParts)

    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'Notes' } })
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    let modal = await discardModal()
    for (const name of ['Keep editing', 'Discard', 'Save draft']) {
      expect(within(modal).getByRole('button', { name })).toBeInTheDocument()
    }

    fireEvent.click(within(modal).getByRole('button', { name: 'Keep editing' }))
    await waitFor(() => expect(screen.queryByText('Save this draft?')).toBeNull())
    expect(kept.router.state.location.pathname).toBe('/mail/compose')
    expect(mocks.deleteAttachment).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    modal = await discardModal()
    fireEvent.click(within(modal).getByRole('button', { name: 'Save draft' }))

    await waitFor(() => expect(mocks.saveDraft).toHaveBeenCalled())
    await waitFor(() => expect(kept.router.state.location.pathname).toBe('/mail'))
    // The saved draft holds its own bytes in IMAP, so the staged copies go on this path too.
    expect(mocks.deleteAttachment).toHaveBeenCalledWith('i1', { accountId: 'primary' })
    expect(mocks.deleteAttachment).toHaveBeenCalledWith('a1', { accountId: 'primary' })
    cleanup()
    mocks.deleteAttachment.mockClear()

    const discarded = renderCompose('INBOX', withParts)
    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'Notes' } })
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    modal = await discardModal()
    fireEvent.click(within(modal).getByRole('button', { name: 'Discard' }))

    await waitFor(() => expect(discarded.router.state.location.pathname).toBe('/mail'))
    expect(mocks.deleteAttachment).toHaveBeenCalledWith('i1', { accountId: 'primary' })
    expect(mocks.deleteAttachment).toHaveBeenCalledWith('a1', { accountId: 'primary' })
  })

  // Discard deletes the staged ids the save in flight may still be uploading from.
  it('locks Discard while the draft is being saved', async () => {
    mocks.saveDraft.mockReturnValue(new Promise(() => {}))
    renderCompose('INBOX', withParts)

    fireEvent.change(screen.getByLabelText('Subject'), { target: { value: 'Notes' } })
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    const modal = await discardModal()
    fireEvent.click(within(modal).getByRole('button', { name: 'Save draft' }))

    await waitFor(() => expect(within(modal).getByRole('button', { name: 'Discard' })).toBeDisabled())
  })

  // Send and Save both consume the staged ids and file a message; whichever lost would leave a
  // draft behind that the winner's cleanup never knew about.
  it('locks Send and Save draft against each other while either is in flight', async () => {
    mocks.saveDraft.mockReturnValue(new Promise(() => {}))
    renderCompose('INBOX', draftSeed)

    fireEvent.click(headerSave())

    await waitFor(() => expect(sendButton()).toBeDisabled())
    expect(screen.getByRole('button', { name: 'Saving…' })).toBeDisabled()
  })

  // The composer is clean, but the tray still holds staged copies nothing else will release.
  it('releases the staged copies when a clean composer is closed', async () => {
    const { router } = renderCompose('INBOX', withParts)

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/mail'))
    expect(mocks.deleteAttachment).toHaveBeenCalledWith('i1', { accountId: 'primary' })
    expect(mocks.deleteAttachment).toHaveBeenCalledWith('a1', { accountId: 'primary' })
  })

  // The sent copy makes the stored draft a duplicate of a message that already left.
  it('sending a resumed draft deletes it', async () => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    const { router } = renderCompose('INBOX', draftSeed)

    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.deleteMessages).toHaveBeenCalledWith('Drafts', [41], { accountId: 'primary' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/mail'))
  })

  // The composer is already unmounted when the DELETE answers, so the toast has to come from the
  // mutation's own callback. Staying silent leaves a re-sendable draft of a message that went out.
  it('says so when the draft of a sent message could not be removed', async () => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    mocks.deleteMessages.mockRejectedValue(new Error('IMAP is down'))
    const { onNotify, router } = renderCompose('INBOX', draftSeed)

    fireEvent.click(sendButton())

    await waitFor(() => expect(onNotify).toHaveBeenCalledWith(
      'Message sent — the draft could not be removed', 'error'))
    expect(router.state.location.pathname).toBe('/mail')
  })

  it('says why the leave dialog locks Save draft on an invalid address', async () => {
    renderCompose('INBOX', draftSeed)

    addRecipient('To', 'nope')
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    const save = within(await discardModal()).getByRole('button', { name: 'Save draft' })

    expect(save).toBeDisabled()
    expect(save).toHaveAttribute('title', 'Fix the invalid address first')
  })

  it('saving a resumed draft targets its own uid', async () => {
    mocks.saveDraft.mockResolvedValue({ uid: 42, folderPath: 'Drafts' })
    renderCompose('INBOX', draftSeed)

    fireEvent.click(headerSave())

    await waitFor(() => expect(mocks.saveDraft).toHaveBeenCalled())
    expect(mocks.saveDraft.mock.calls[0][0]).toMatchObject({ replaceUid: 41 })
  })
})

describe('capturing new recipients', () => {
  const created = {
    id: 'c1', firstName: 'Alice', lastName: 'Dupont', nickname: null,
    isFavorite: false, addresses: ['alice@x.be'],
  }
  const hintedSeed: ComposeSeed = {
    action: 'reply',
    to: ['alice@x.be'], cc: [], bcc: [],
    subject: 'Re: Hello', html: '<p>later</p>', text: null,
    fromAddress: null, attachments: [],
    inReplyTo: null, references: [],
    draftRef: null, nameHints: { 'alice@x.be': 'Alice Dupont' }, priority: 'normal',
  }

  beforeEach(() => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    mocks.getPreferences.mockResolvedValue({ 'contacts.captureRecipients': 'true' })
    mocks.createContact.mockResolvedValue(created)
    mocks.deleteContact.mockResolvedValue(undefined)
  })

  // The capture reads the book and the preferences at send time, so sending on the mounting render
  // would skip it on the loading guard and every "creates nothing" case below would pass blind.
  // Two boundaries because the two queries answer one notification batch apart.
  async function openCompose(seed?: ComposeSeed) {
    const view = renderCompose('INBOX', seed)
    await settle()
    await settle()
    return view
  }

  it('creates a contact for a recipient the book does not hold', async () => {
    const { onNotify } = await openCompose()

    addRecipient('To', 'alice@x.be')
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.createContact).toHaveBeenCalledWith(
      expect.objectContaining({ addresses: ['alice@x.be'], source: 'captured' })))
    await waitFor(() => expect(onNotify).toHaveBeenCalledWith(
      'Alice Dupont added to contacts', 'success', expect.objectContaining({ label: 'Undo' })))
  })

  it('creates nothing for a recipient already in the book', async () => {
    await openCompose()

    addRecipient('To', 'bruno@x.be')
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalled())
    expect(mocks.createContact).not.toHaveBeenCalled()
  })

  it('creates nothing when the preference is off', async () => {
    mocks.getPreferences.mockResolvedValue({ 'contacts.captureRecipients': 'false' })
    await openCompose()

    addRecipient('To', 'alice@x.be')
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalled())
    expect(mocks.createContact).not.toHaveBeenCalled()
  })

  // Not knowing what the book holds means duplicating all of it. The send has already succeeded,
  // so a missed capture costs nothing and a wrong one is on screen forever.
  it('creates nothing while the book is still loading', async () => {
    mocks.getContacts.mockReturnValue(new Promise(() => {}))
    await openCompose()

    addRecipient('To', 'alice@x.be')
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalled())
    expect(mocks.createContact).not.toHaveBeenCalled()
  })

  it('names the contact from the seed name hints', async () => {
    await openCompose(hintedSeed)

    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.createContact).toHaveBeenCalledWith(
      expect.objectContaining({ firstName: 'Alice', lastName: 'Dupont' })))
  })

  type Notify = ReturnType<typeof renderCompose>['onNotify']
  const undoAction = (onNotify: Notify) => {
    const calls = onNotify.mock.calls
    return calls[calls.length - 1][2] as { onClick: () => void }
  }
  const undoOffered = (onNotify: Notify) => waitFor(() => expect(onNotify).toHaveBeenCalledWith(
    expect.any(String), 'success', expect.objectContaining({ label: 'Undo' })))

  it('offers an undo that deletes exactly what it created', async () => {
    const { onNotify } = await openCompose()

    addRecipient('To', 'alice@x.be')
    fireEvent.click(sendButton())

    await undoOffered(onNotify)
    undoAction(onNotify).onClick()

    await waitFor(() => expect(mocks.deleteContact).toHaveBeenCalledWith('c1'))
    expect(mocks.deleteContact).toHaveBeenCalledTimes(1)
  })

  // Undoing an id the create never returned deletes nothing and hides that the other one survived.
  it('undoes the contact that was created and not the one that was refused', async () => {
    mocks.createContact
      .mockResolvedValueOnce(created)
      .mockRejectedValueOnce(new Error('at the ceiling'))
    const { onNotify } = await openCompose()

    addRecipient('To', 'alice@x.be')
    addRecipient('To', 'carla@x.be')
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.createContact).toHaveBeenCalledTimes(2))
    await undoOffered(onNotify)
    undoAction(onNotify).onClick()

    await waitFor(() => expect(mocks.deleteContact).toHaveBeenCalledWith('c1'))
    expect(mocks.deleteContact).toHaveBeenCalledTimes(1)
  })

  it('counts the contacts in one toast and undoes all of them at once', async () => {
    const carla = { ...created, id: 'c2', firstName: 'Carla', addresses: ['carla@x.be'] }
    mocks.createContact.mockResolvedValueOnce(created).mockResolvedValueOnce(carla)
    const { onNotify } = await openCompose()

    addRecipient('To', 'alice@x.be')
    addRecipient('To', 'carla@x.be')
    fireEvent.click(sendButton())

    await waitFor(() => expect(onNotify).toHaveBeenCalledWith(
      '2 contacts added', 'success', expect.objectContaining({ label: 'Undo' })))
    undoAction(onNotify).onClick()

    await waitFor(() => expect(mocks.deleteContact).toHaveBeenCalledTimes(2))
    expect(mocks.deleteContact.mock.calls.flat()).toEqual(['c1', 'c2'])
  })

  // The undo was explicitly asked for, so unlike a refused capture its failure has to be spoken.
  it('says so when the undo cannot be carried out', async () => {
    mocks.deleteContact.mockRejectedValue(new Error('Address book is down'))
    const { onNotify } = await openCompose()

    addRecipient('To', 'alice@x.be')
    fireEvent.click(sendButton())

    await undoOffered(onNotify)
    undoAction(onNotify).onClick()

    await waitFor(() => expect(onNotify).toHaveBeenCalledWith('Could not undo', 'error'))
  })

  // The message already left; a refused address book must not turn that into an error on screen.
  // The whole log, not an exclusion per shape: the two-argument error toast every other failure
  // here raises can never match a three-argument expectation.
  it('says nothing when a capture is refused', async () => {
    mocks.createContact.mockRejectedValue(new Error('at the ceiling'))
    const { onNotify } = await openCompose()

    addRecipient('To', 'alice@x.be')
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.createContact).toHaveBeenCalled())
    await settle()
    expect(onNotify.mock.calls).toEqual([['Message sent']])
  })
})

describe('plain text in the composer', () => {
  const plainToggle = () => screen.getByRole('button', { name: 'Plain text' })
  const textArea = () => screen.getByTestId('compose-text-editor') as HTMLTextAreaElement

  const draftSeed: ComposeSeed = {
    action: 'draft', to: [], cc: [], bcc: [], subject: '', html: '', text: null,
    fromAddress: null, attachments: [], inReplyTo: null, references: [], priority: 'normal',
    draftRef: { folderPath: 'Drafts', uid: 3 }, nameHints: {},
  }

  it('swaps the editor for a textarea and folds the toolbar away', () => {
    editorState.html = '<div>one</div><div>two</div>'
    renderCompose()

    fireEvent.click(plainToggle())

    expect(textArea().value).toBe('one\ntwo')
    expect(screen.queryByTestId('compose-editor')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Bold' })).toBeNull()
    expect(plainToggle()).toHaveAttribute('aria-pressed', 'true')
  })

  it('sends the textarea as textBody with no html beside it', async () => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    editorState.html = '<div>draft</div>'
    renderCompose()
    addRecipient('To', 'alice@x.be')

    fireEvent.click(plainToggle())
    fireEvent.change(textArea(), { target: { value: 'just text' } })
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalled())
    const payload = mocks.sendMessage.mock.calls[0][0]
    expect(payload.textBody).toBe('just text')
    expect(payload.htmlBody).toBe('')
  })

  it('carries the text back into the editor on the way out', () => {
    renderCompose()

    fireEvent.click(plainToggle())
    fireEvent.change(textArea(), { target: { value: 'a\nb' } })
    fireEvent.click(plainToggle())

    expect(screen.getByTestId('compose-editor')).toHaveValue('<div>a<br>b</div>')
    expect(screen.queryByTestId('compose-text-editor')).toBeNull()
  })

  it('opens a text draft in text mode', () => {
    renderCompose('INBOX', { ...draftSeed, text: 'stored text' })

    expect(textArea().value).toBe('stored text')
    expect(plainToggle()).toHaveAttribute('aria-pressed', 'true')
  })

  it('sends an HTML message with no textBody at all', async () => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    editorState.html = '<div>rich</div>'
    renderCompose()
    addRecipient('To', 'alice@x.be')

    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalled())
    expect(mocks.sendMessage.mock.calls[0][0].textBody).toBeUndefined()
  })
})

describe('what a plain-text switch costs', () => {
  const plainToggle = () => screen.getByRole('button', { name: 'Plain text' })
  const textArea = () => screen.getByTestId('compose-text-editor') as HTMLTextAreaElement

  const draftSeed: ComposeSeed = {
    action: 'draft', to: [], cc: [], bcc: [], subject: '', html: '', text: null,
    fromAddress: null, attachments: [], inReplyTo: null, references: [], priority: 'normal',
    draftRef: { folderPath: 'Drafts', uid: 3 }, nameHints: {},
  }

  it('asks before a switch that would cost formatting', () => {
    editorState.html = '<div><b>bold</b></div>'
    renderCompose()

    fireEvent.click(plainToggle())

    expect(screen.getByText('Switch to plain text?')).toBeInTheDocument()
    expect(screen.queryByTestId('compose-text-editor')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Switch' }))
    expect(textArea().value).toBe('bold')
  })

  it('keeps the editor when the switch is declined', () => {
    editorState.html = '<div><b>bold</b></div>'
    renderCompose()

    fireEvent.click(plainToggle())
    fireEvent.click(screen.getByRole('button', { name: 'Keep formatting' }))

    expect(screen.queryByTestId('compose-text-editor')).toBeNull()
    expect(screen.getByTestId('compose-editor')).toBeInTheDocument()
  })

  it('says nothing when there is nothing to lose', () => {
    editorState.html = '<div>plain enough</div>'
    renderCompose()

    fireEvent.click(plainToggle())

    expect(screen.queryByText('Switch to plain text?')).toBeNull()
    expect(textArea().value).toBe('plain enough')
  })

  it('moves an inline image into the tray on the switch', () => {
    const seed: ComposeSeed = {
      ...draftSeed,
      html: '<div><img src="https://api.test.example/api/Mail/Attachments/i1/content"></div>',
      attachments: [
        { id: 'i1', fileName: 'logo.png', size: 3, contentType: 'image/png', contentId: 'logo@mail' },
      ],
    }
    editorState.html = seed.html
    renderCompose('INBOX', seed)

    fireEvent.click(plainToggle())
    fireEvent.click(screen.getByRole('button', { name: 'Switch' }))

    expect(screen.getByText('logo.png')).toBeInTheDocument()
  })

  // The adopted id now rides the payload as a tray id. Sent twice, the backend packs the file
  // twice and the recipient gets two copies of it.
  it('sends an adopted image once, not twice', async () => {
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    const seed: ComposeSeed = {
      ...draftSeed,
      html: '<div><img src="https://api.test.example/api/Mail/Attachments/i1/content"></div>',
      attachments: [
        { id: 'i1', fileName: 'logo.png', size: 3, contentType: 'image/png', contentId: 'logo@mail' },
      ],
    }
    editorState.html = seed.html
    renderCompose('INBOX', seed)
    addRecipient('To', 'alice@x.be')

    fireEvent.click(plainToggle())
    fireEvent.click(screen.getByRole('button', { name: 'Switch' }))
    fireEvent.click(sendButton())

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalled())
    expect(mocks.sendMessage.mock.calls[0][0].attachmentIds).toEqual(['i1'])
  })
})

describe('inline images', () => {
  function imageFile(name = 'shot.png') {
    return new File(['xx'], name, { type: 'image/png' })
  }

  it('uploads a pasted image inline and inserts it at the caret', async () => {
    mocks.uploadAttachment.mockResolvedValue({ id: 'i1', fileName: 'shot.png', size: 2 })
    renderCompose()

    fireEvent.paste(screen.getByTestId('compose-editor'), {
      clipboardData: { files: [imageFile()] },
    })

    await waitFor(() => expect(editorState.images).toEqual([
      'https://api.test.example/api/Mail/Attachments/i1/content',
    ]))
    expect(mocks.uploadAttachment).toHaveBeenCalledWith(
      expect.any(File), expect.objectContaining({ inline: true }))
    // The body is where it lives; the tray must not also list it.
    expect(screen.queryByText('shot.png')).not.toBeInTheDocument()
  })

  it('inserts an image dropped on the body and attaches one dropped on the composer', async () => {
    mocks.uploadAttachment.mockResolvedValue({ id: 'i2', fileName: 'shot.png', size: 2 })
    renderCompose()

    fireEvent.drop(screen.getByTestId('compose-editor'), {
      dataTransfer: { files: [imageFile()], types: ['Files'] },
    })
    await waitFor(() => expect(editorState.images).toHaveLength(1))

    fireEvent.drop(screen.getByTestId('compose-view'), {
      dataTransfer: { files: [new File(['x'], 'report.pdf', { type: 'application/pdf' })], types: ['Files'] },
    })
    expect(await screen.findByText('report.pdf')).toBeInTheDocument()
    expect(editorState.images).toHaveLength(1)
  })

  it('attaches a non-image dropped on the body rather than refusing it', async () => {
    renderCompose()

    fireEvent.drop(screen.getByTestId('compose-editor'), {
      dataTransfer: { files: [new File(['x'], 'report.pdf', { type: 'application/pdf' })], types: ['Files'] },
    })

    expect(await screen.findByText('report.pdf')).toBeInTheDocument()
    expect(editorState.images).toHaveLength(0)
  })

  it('sends an inserted image as an inline id', async () => {
    mocks.uploadAttachment.mockResolvedValue({ id: 'i3', fileName: 'shot.png', size: 2 })
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    renderCompose()
    addRecipient('To', 'her@example.com')

    fireEvent.paste(screen.getByTestId('compose-editor'), {
      clipboardData: { files: [imageFile()] },
    })
    await waitFor(() => expect(editorState.images).toHaveLength(1))
    fireEvent.click(screen.getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalled())
    expect(mocks.sendMessage.mock.calls[0][0].attachmentIds).toContain('i3')
  })

  it('moves an inserted image to the tray when the composer switches to plain text', async () => {
    mocks.uploadAttachment.mockResolvedValue({ id: 'i4', fileName: 'shot.png', size: 2 })
    renderCompose()

    fireEvent.paste(screen.getByTestId('compose-editor'), {
      clipboardData: { files: [imageFile()] },
    })
    await waitFor(() => expect(editorState.images).toHaveLength(1))
    fireEvent.click(screen.getByRole('button', { name: 'Plain text' }))

    // Exactly one row: the hook holds the inserted id once, and a tray listing it twice would
    // send the file twice.
    expect(await screen.findByText('shot.png')).toBeInTheDocument()
  })
})

// Four lifetime bugs, none of which turns anything red on screen: a staged id that reaches no
// payload, a second artefact from one paste, and two windows where an upload in flight is
// invisible to everything that consumes the id it will produce.
describe('inline images, staged-id lifetime', () => {
  function imageFile(name = 'shot.png') {
    return new File(['xx'], name, { type: 'image/png' })
  }
  const pasteImage = () => fireEvent.paste(screen.getByTestId('compose-editor'),
    { clipboardData: { files: [imageFile()] } })
  const plainToggle = () => screen.getByRole('button', { name: 'Plain text' })

  // inlineAdopted speaks for the seed. Zeroing the inserted half with it left the body pointing
  // at a cid the send packed no part for: a broken image, with nothing on screen to say so.
  it('sends an image inserted after a plain-text round trip', async () => {
    mocks.uploadAttachment.mockResolvedValue({ id: 'round-trip', fileName: 'shot.png', size: 2 })
    mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
    renderCompose()
    addRecipient('To', 'her@example.com')

    fireEvent.click(plainToggle())
    fireEvent.click(plainToggle())
    pasteImage()
    await waitFor(() => expect(editorState.images).toHaveLength(1))

    fireEvent.click(sendButton())
    await waitFor(() => expect(mocks.sendMessage).toHaveBeenCalled())
    expect(mocks.sendMessage.mock.calls[0][0].attachmentIds).toContain('round-trip')
  })

  // Squire's _onPaste never reads defaultPrevented: it bails only on an image with no text/plain
  // and no rtf+html. Explorer and Word both send those, so only stopping the event keeps its
  // handler — here, any listener on the editor element — from inserting a second artefact.
  it('keeps the paste from reaching the editor s own handler', async () => {
    mocks.uploadAttachment.mockResolvedValue({ id: 'i5', fileName: 'shot.png', size: 2 })
    renderCompose()
    const squirePaste = vi.fn()
    screen.getByTestId('compose-editor').addEventListener('paste', squirePaste)

    pasteImage()

    await waitFor(() => expect(editorState.images).toHaveLength(1))
    expect(squirePaste).not.toHaveBeenCalled()
  })

  // The upload is not in the tray, so attachments.uploading is blind to it. Sent in that window
  // the message leaves without the image and without its id, and the staged bytes are released
  // by nobody — the leave unmounts the hook the upload was going to report to.
  it('locks Send and Discard while an inline upload is in flight', async () => {
    let resolve!: (v: unknown) => void
    mocks.uploadAttachment.mockReturnValue(new Promise(r => { resolve = r }))
    renderCompose()
    addRecipient('To', 'her@example.com')
    expect(sendButton()).toBeEnabled()

    pasteImage()
    await waitFor(() => expect(sendButton()).toBeDisabled())

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(within(await discardModal()).getByRole('button', { name: 'Discard' })).toBeDisabled()

    await act(async () => { resolve({ id: 'i6', fileName: 'shot.png', size: 2 }) })
    expect(sendButton()).toBeEnabled()
  })

  // Switched mid-upload, adoptInline finds both refs empty and returns, inlineAdopted lands, and
  // the id arriving after it belongs to no tray row and no payload — the image silently vanishes.
  it('locks the plain-text switch while an inline upload is in flight', async () => {
    let resolve!: (v: unknown) => void
    mocks.uploadAttachment.mockReturnValue(new Promise(r => { resolve = r }))
    renderCompose()

    pasteImage()
    await waitFor(() => expect(plainToggle()).toBeDisabled())

    await act(async () => { resolve({ id: 'i7', fileName: 'shot.png', size: 2 }) })
    expect(plainToggle()).toBeEnabled()

    fireEvent.click(plainToggle())
    expect(await screen.findByText('shot.png')).toBeInTheDocument()
  })

  // A resumed draft is the case that bites: it opens clean while every other clause of `dirty` is
  // already true, so dirtying up front made a refused upload alone ask to re-save an untouched
  // draft. An empty composer hides the bug — nothing there for `changed` to combine with.
  it('leaves a resumed draft clean when an inline upload is refused', async () => {
    mocks.uploadAttachment.mockRejectedValue(new Error('Too large'))
    const seed: ComposeSeed = {
      action: 'draft', to: [], cc: [], bcc: [], subject: '', html: '', text: null,
      fromAddress: null, attachments: [], inReplyTo: null, references: [], priority: 'normal',
      draftRef: { folderPath: 'Drafts', uid: 7 }, nameHints: {},
    }
    const { onNotify, router } = renderCompose('INBOX', seed)

    pasteImage()
    await waitFor(() => expect(onNotify).toHaveBeenCalledWith('Too large', 'error'))

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(screen.queryByText('Save this draft?')).toBeNull()
    expect(router.state.location.pathname).toBe('/mail')
  })
})
