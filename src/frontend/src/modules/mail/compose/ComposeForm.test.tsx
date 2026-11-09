import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useState } from 'react'
import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { createMemoryRouter, Outlet, RouterProvider } from 'react-router'
import ComposeForm from './ComposeForm'
import type { ComposeSeed } from './composeSeed'
import { editorState } from './squireEditorStub'
import { useIdentities } from '../queries'
import { createTestQueryClient, waitFor } from '../../../test-utils'

const mocks = vi.hoisted(() => ({
  deleteAttachment: vi.fn(),
  uploadAttachment: vi.fn(),
  saveDraft: vi.fn(),
  getContacts: vi.fn(),
  getContactGroups: vi.fn(),
}))

vi.mock('../../../api.js', () => ({
  api: {
    deleteAttachment: mocks.deleteAttachment, saveDraft: mocks.saveDraft,
    getContacts: mocks.getContacts, getContactGroups: mocks.getContactGroups,
  },
  uploadAttachment: mocks.uploadAttachment,
  API_BASE: 'https://api.test.example',
  stagedAttachmentUrl: (id: string) => `https://api.test.example/api/Mail/Attachments/${id}/content`,
}))
vi.mock('../queries', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../queries')>()
  return { ...actual, useIdentities: vi.fn() }
})
vi.mock('../../../contexts/AuthContext', () => ({
  useAuth: () => ({
    activeAccount: { id: 'primary' }, activeAccountId: 'primary',
    identity: { displayName: 'Mick Weesky', email: 'mick@weesky.be' },
  }),
}))
vi.mock('./SquireEditor', () => import('./squireEditorStub'))

const shared = { preferences: { 'contacts.captureRecipients': 'false' }, onNotify: vi.fn() }
const renderInRouter = (router: ReturnType<typeof createMemoryRouter>) => render(
  <QueryClientProvider client={createTestQueryClient({ mutations: { retry: false } })}>
    <RouterProvider router={router} />
  </QueryClientProvider>)

/** The window lives above the module outlet, as AppShell will mount it, so a route change keeps it. */
function renderWindow(seed: ComposeSeed | null = null) {
  const onLeave = vi.fn()
  const onExpand = vi.fn()
  const onMinimise = vi.fn()
  const router = createMemoryRouter([{
    path: '/',
    element: (
      <>
        <ComposeForm {...shared} seed={seed} frame="window"
          onLeave={onLeave} onExpand={onExpand} onMinimise={onMinimise} />
        <Outlet />
      </>
    ),
    children: [
      { path: 'calendar', element: <span data-testid="calendar-view">calendar</span> },
      { path: 'contacts', element: <span data-testid="contacts-view">contacts</span> },
    ],
  }], { initialEntries: ['/calendar'] })
  renderInRouter(router)
  return { router, onLeave, onExpand, onMinimise }
}

function addRecipient(label: string, value: string) {
  const input = screen.getByLabelText(label)
  fireEvent.change(input, { target: { value } })
  fireEvent.keyDown(input, { key: 'Enter' })
}

const typeSubject = (value: string) => fireEvent.change(screen.getByLabelText('Subject'), { target: { value } })
const header = () => screen.getByTestId('compose-view').querySelector('.compose-header') as HTMLElement

beforeEach(() => {
  vi.clearAllMocks()
  editorState.html = ''
  mocks.uploadAttachment.mockResolvedValue({ id: 'att-1', fileName: 'a.txt', size: 3, contentType: 'text/plain' })
  mocks.deleteAttachment.mockResolvedValue(undefined)
  mocks.saveDraft.mockResolvedValue({ uid: 7, folderPath: 'Drafts' })
  mocks.getContacts.mockResolvedValue({ contacts: [] })
  mocks.getContactGroups.mockResolvedValue({ groups: [] })
  vi.mocked(useIdentities).mockReturnValue({ data: undefined } as never)
})

describe('ComposeForm in a window', () => {
  it('draws the title, Minimise, Expand and ✕ in the header, and Save draft then Send in the footer', () => {
    renderWindow()

    expect(within(header()).getAllByRole('button').map(b => b.getAttribute('aria-label')))
      .toEqual(['Minimise', 'Expand', 'Close'])
    expect(within(header()).getByText('New message')).toBeInTheDocument()
    const footer = screen.getByTestId('compose-view').querySelector('.quick-compose-footer') as HTMLElement
    expect(within(footer).getAllByRole('button').map(b => b.textContent?.trim())).toEqual(['Save draft', 'Send'])
  })

  it('names itself by the subject once one is typed', () => {
    renderWindow()
    typeSubject('Lunch on Friday')

    expect(within(header()).getByText('Lunch on Friday')).toBeInTheDocument()
  })

  it('folds the toolbar behind ⋯ whatever the viewport', () => {
    renderWindow()

    expect(screen.getByRole('button', { name: 'More formatting tools' })).toBeInTheDocument()
  })

  it('minimises from its button and from the header’s empty space, never from a header button', () => {
    const { onMinimise, onLeave } = renderWindow()

    fireEvent.click(screen.getByRole('button', { name: 'Minimise' }))
    fireEvent.click(header())
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(onMinimise).toHaveBeenCalledTimes(2)
    expect(onLeave).toHaveBeenCalledTimes(1)
  })

  it('closing a clean window calls onLeave and does not navigate', () => {
    const { router, onLeave } = renderWindow()

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(onLeave).toHaveBeenCalledTimes(1)
    expect(router.state.location.pathname).toBe('/calendar')
    expect(screen.queryByText('Save this draft?')).toBeNull()
  })

  it('closing a dirty window asks first', async () => {
    const { router, onLeave } = renderWindow()
    typeSubject('Unsaved')

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(await screen.findByText('Save this draft?')).toBeInTheDocument()
    expect(onLeave).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Discard' }))

    expect(onLeave).toHaveBeenCalledTimes(1)
    expect(router.state.location.pathname).toBe('/calendar')
  })

  it('keeps editing a dirty window when the question is declined', async () => {
    const { onLeave } = renderWindow()
    typeSubject('Unsaved')

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Keep editing' }))

    expect(screen.queryByText('Save this draft?')).toBeNull()
    expect(onLeave).not.toHaveBeenCalled()
    expect(screen.getByLabelText('Subject')).toHaveValue('Unsaved')
  })

  it('navigating away from a dirty window is not blocked', async () => {
    const { router } = renderWindow()
    typeSubject('Unsaved')

    act(() => { void router.navigate('/contacts') })

    expect(await screen.findByTestId('contacts-view')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/contacts')
    expect(screen.queryByText('Save this draft?')).toBeNull()
    expect(screen.getByLabelText('Subject')).toHaveValue('Unsaved')
  })

  it('expand hands over everything', async () => {
    const { onExpand } = renderWindow()
    addRecipient('To', 'alice@x.be')
    fireEvent.click(screen.getByRole('button', { name: 'Cc' }))
    addRecipient('Cc', 'bob@x.be')
    typeSubject('Hello')
    fireEvent.change(screen.getByTestId('compose-editor'), { target: { value: '<p>body</p>' } })
    fireEvent.change(screen.getByTestId('attachment-input'),
      { target: { files: [new File(['x'], 'a.txt', { type: 'text/plain' })] } })
    await screen.findByRole('button', { name: 'Remove a.txt' })
    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }))
    await waitFor(() => expect(mocks.saveDraft).toHaveBeenCalled())
    await waitFor(() => expect(screen.getByRole('button', { name: 'Expand' })).toBeEnabled())

    fireEvent.click(screen.getByRole('button', { name: 'Expand' }))

    expect(onExpand).toHaveBeenCalledTimes(1)
    const [handed, dirty] = onExpand.mock.calls[0]! as [ComposeSeed, boolean]
    // Saved just before: the page opens it clean, as the window was.
    expect(dirty).toBe(false)
    expect(handed).toMatchObject({
      action: 'editAsNew', to: ['alice@x.be'], cc: ['bob@x.be'], bcc: [], subject: 'Hello',
      html: '<p>body</p>', text: null, priority: 'normal',
      draftRef: { folderPath: 'Drafts', uid: 7 },
    })
    expect(handed.attachments).toEqual([
      { id: 'att-1', fileName: 'a.txt', size: 3, contentType: 'text/plain' }])
    expect(mocks.deleteAttachment).not.toHaveBeenCalled()
  })

  it('expand keeps plain text', () => {
    const { onExpand } = renderWindow()
    fireEvent.click(screen.getByRole('button', { name: 'Plain text' }))
    fireEvent.change(screen.getByTestId('compose-text-editor'), { target: { value: 'just text' } })

    fireEvent.click(screen.getByRole('button', { name: 'Expand' }))

    const [handed, dirty] = onExpand.mock.calls[0]! as [ComposeSeed, boolean]
    expect(dirty).toBe(true)
    expect(handed.text).toBe('just text')
    expect(handed.html).toBe('')
  })

  it('expand carries the body’s inline images with their contentId, and the original threading', () => {
    const logo = { id: 'img-1', fileName: 'logo.png', size: 3, contentType: 'image/png', contentId: 'logo@x' }
    const { onExpand } = renderWindow({
      action: 'reply', to: ['alice@x.be'], cc: [], bcc: [], subject: 'Re: hi', html: '<p>quoted</p>',
      text: null, fromAddress: null, attachments: [logo], inReplyTo: '<m1@x>', references: ['<m0@x>', '<m1@x>'],
      priority: 'normal', draftRef: null, nameHints: { 'alice@x.be': 'Alice' },
    })
    editorState.html = '<p>quoted</p>'

    fireEvent.click(screen.getByRole('button', { name: 'Expand' }))

    expect(onExpand.mock.calls[0]![0]).toMatchObject({
      action: 'reply', attachments: [logo], inReplyTo: '<m1@x>', references: ['<m0@x>', '<m1@x>'],
      nameHints: { 'alice@x.be': 'Alice' }, html: '<p>quoted</p>',
    })
  })

  // A router consults one blocker, the last registered: a window's must not exist, or a page
  // composer mounted before it would stop asking.
  it('leaves a dirty page composer blocking when a window mounts after it', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    let showWindow!: (open: boolean) => void
    function Shell() {
      const [open, setOpen] = useState(false)
      showWindow = setOpen
      return <><Outlet />{open && <ComposeForm {...shared} seed={null} frame="window" onLeave={vi.fn()} />}</>
    }
    const router = createMemoryRouter([{
      path: '/', element: <Shell />,
      children: [
        { path: 'mail', element: <span data-testid="mail-view">mail</span> },
        { path: 'mail/compose', element: <ComposeForm {...shared} seed={null} frame="page"
          onLeave={() => void router.navigate('/mail')} /> },
      ],
    }], { initialEntries: ['/mail', '/mail/compose'], initialIndex: 1 })
    renderInRouter(router)
    typeSubject('Unsaved on the page')
    act(() => { showWindow(true) })
    expect(screen.getAllByTestId('compose-view')).toHaveLength(2)

    act(() => { void router.navigate('/mail') })

    expect(await screen.findByText('Save this draft?')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/mail/compose')
    expect(warn.mock.calls.flat().join(' ')).not.toContain('one blocker')
    warn.mockRestore()
  })
})
