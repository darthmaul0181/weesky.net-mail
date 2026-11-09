import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useEffect, type ReactElement } from 'react'
import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { createMemoryRouter, Outlet, RouterProvider } from 'react-router'
import { changeViewport, createTestQueryClient, mockViewport, resetViewport, settle, waitFor } from '../../../../test-utils'
import { useIdentities } from '../../queries'
import type { ComposeSeed } from '../composeSeed'
import { QuickComposeProvider, useQuickCompose, type QuickComposeApi } from './QuickComposeContext'
import ComposeView from '../ComposeView'
import { confirmLeave } from '../../../../lib/leaveGuard'

const mocks = vi.hoisted(() => ({
  sendMessage: vi.fn(),
  saveDraft: vi.fn(),
  deleteAttachment: vi.fn(),
  getContacts: vi.fn(),
  getContactGroups: vi.fn(),
  getPreferences: vi.fn(),
}))

vi.mock('../../../../api.js', () => ({
  api: {
    sendMessage: mocks.sendMessage, saveDraft: mocks.saveDraft, deleteAttachment: mocks.deleteAttachment,
    getContacts: mocks.getContacts, getContactGroups: mocks.getContactGroups, getPreferences: mocks.getPreferences,
  },
  uploadAttachment: vi.fn(),
  API_BASE: 'https://api.test.example',
  stagedAttachmentUrl: (id: string) => `https://api.test.example/api/Mail/Attachments/${id}/content`,
}))
vi.mock('../../queries', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../queries')>()
  return { ...actual, useIdentities: vi.fn() }
})
// A store the mocked useAuth subscribes to, so a switch re-renders every reader as the real context does.
const auth = vi.hoisted(() => {
  const listeners = new Set<() => void>()
  let activeAccountId = 'primary'
  return {
    get: () => activeAccountId,
    subscribe: (listener: () => void) => { listeners.add(listener); return () => { listeners.delete(listener) } },
    switchTo: (id: string) => { activeAccountId = id; listeners.forEach(listener => listener()) },
  }
})
vi.mock('../../../../contexts/AuthContext', async () => {
  const { useSyncExternalStore } = await import('react')
  return {
    useAuth: () => {
      const activeAccountId = useSyncExternalStore(auth.subscribe, auth.get)
      return {
        activeAccount: { id: activeAccountId }, activeAccountId,
        identity: { displayName: 'Mick Weesky', email: 'mick@weesky.be' },
      }
    },
  }
})
vi.mock('../SquireEditor', () => import('../squireEditorStub'))

let quick!: QuickComposeApi
function Probe() {
  const api = useQuickCompose()
  useEffect(() => { quick = api })
  return null
}

function renderShell(path = '/mail', page: ReactElement = <span data-testid="compose-page">page</span>) {
  const client = createTestQueryClient({ mutations: { retry: false } })
  client.setQueryData(['preferences'], { 'contacts.captureRecipients': 'false' })
  const router = createMemoryRouter([{
    path: '/',
    element: <QuickComposeProvider><Probe /><Outlet /></QuickComposeProvider>,
    children: [
      { path: 'mail', element: <span data-testid="mail-view">mail</span> },
      { path: 'mail/compose', element: page },
      { path: 'calendar', element: <span data-testid="calendar-view">calendar</span> },
    ],
  }], { initialEntries: [path] })
  const view = render(<QueryClientProvider client={client}><RouterProvider router={router} /></QueryClientProvider>)
  return { router, ...view }
}

async function openWindow(seed: ComposeSeed | null = null, origin = '/mail?folder=INBOX') {
  act(() => { quick.open(seed, origin) })
  const region = await screen.findByRole('region', { name: 'New message' })
  // The form is a lazy chunk: the region stands before it lands.
  await within(region).findByLabelText('Subject')
  return region
}

const typeSubject = (value: string) => fireEvent.change(screen.getByLabelText('Subject'), { target: { value } })

beforeEach(() => {
  vi.clearAllMocks()
  auth.switchTo('primary')
  mocks.getContacts.mockResolvedValue({ contacts: [] })
  mocks.getContactGroups.mockResolvedValue({ groups: [] })
  mocks.sendMessage.mockResolvedValue({ appendedToSent: true })
  mocks.saveDraft.mockResolvedValue({ uid: 7, folderPath: 'Drafts' })
  vi.mocked(useIdentities).mockReturnValue({ data: undefined } as never)
})

afterEach(() => resetViewport())

describe('QuickComposeProvider', () => {
  it('open renders the window as a region named New message', async () => {
    renderShell()
    expect(screen.queryByRole('region')).toBeNull()

    const region = await openWindow()

    expect(within(region).getByLabelText('Subject')).toBeInTheDocument()
    expect(quick.state).toMatchObject({ seed: null, accountId: 'primary', origin: '/mail?folder=INBOX', minimised: false })
  })

  it('names the region by the subject once one is typed', async () => {
    renderShell()
    await openWindow()

    typeSubject('Lunch')

    expect(await screen.findByRole('region', { name: 'Lunch' })).toBeInTheDocument()
  })

  it('focuses To for a new message', async () => {
    renderShell()
    await openWindow()

    await waitFor(() => expect(screen.getByLabelText('To')).toHaveFocus())
  })

  it('focuses the body for a seeded message, plain text included', async () => {
    renderShell()
    act(() => {
      quick.open({
        action: 'reply', to: ['alice@x.be'], cc: [], bcc: [], subject: 'Re: hi', html: '', text: '> quoted',
        fromAddress: null, attachments: [], inReplyTo: null, references: [], priority: 'normal',
        draftRef: null, nameHints: {},
      }, '/mail')
    })

    const body = await screen.findByTestId('compose-text-editor')
    await waitFor(() => expect(body).toHaveFocus())
    expect(screen.getByRole('region', { name: 'Re: hi' })).toBeInTheDocument()
  })

  it('restore() brings the focus back into an open window', async () => {
    renderShell()
    await openWindow()
    const subject = screen.getByLabelText('Subject')
    subject.focus()
    act(() => { (document.activeElement as HTMLElement).blur() })

    act(() => { quick.restore() })

    expect(subject).toHaveFocus()
  })

  it('the window survives a route change', async () => {
    const { router } = renderShell()
    await openWindow()
    typeSubject('Kept')

    act(() => { void router.navigate('/calendar') })

    expect(await screen.findByTestId('calendar-view')).toBeInTheDocument()
    expect(screen.getByRole('region', { name: 'Kept' })).toBeInTheDocument()
    expect(screen.getByLabelText('Subject')).toHaveValue('Kept')
  })

  it('minimise then restore keeps the content', async () => {
    renderShell()
    await openWindow()
    typeSubject('Folded')

    fireEvent.click(screen.getByRole('button', { name: 'Minimise' }))

    expect(quick.state?.minimised).toBe(true)
    const restore = screen.getByRole('button', { name: 'Folded' })
    expect(restore).toHaveFocus()
    expect(screen.getByRole('region', { name: 'Folded' })).toHaveClass('is-minimised')

    fireEvent.click(restore)

    expect(quick.state?.minimised).toBe(false)
    expect(screen.getByLabelText('Subject')).toHaveValue('Folded')
    await waitFor(() => expect(screen.getByRole('region')).toContainElement(document.activeElement as HTMLElement))
  })

  it('a mailbox switch closes a clean window', async () => {
    renderShell()
    await openWindow()

    act(() => { auth.switchTo('acc-2') })

    await waitFor(() => expect(screen.queryByRole('region')).toBeNull())
    expect(quick.state).toBeNull()
  })

  it('closing a clean window from /calendar does not navigate', async () => {
    const { router } = renderShell('/calendar')
    await openWindow()

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(screen.queryByRole('region')).toBeNull()
    expect(router.state.location.pathname).toBe('/calendar')
  })

  it('expand navigates to the page with the hand-over', async () => {
    const { router } = renderShell('/calendar')
    await openWindow(null, '/contacts?id=c1')
    typeSubject('Bigger')

    fireEvent.click(screen.getByRole('button', { name: 'Expand' }))

    expect(await screen.findByTestId('compose-page')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/mail/compose')
    const state = router.state.location.state as { seed: ComposeSeed; backTo: string; handedOver: { dirty: boolean } }
    expect(state.handedOver).toEqual({ dirty: true })
    expect(state.backTo).toBe('/contacts?id=c1')
    expect(state.seed.subject).toBe('Bigger')
    expect(screen.queryByRole('region')).toBeNull()
  })

  // The page composer is not remounted by a /mail/compose → /mail/compose navigation on its own.
  it('expand over a clean page composer shows the window content on the page', async () => {
    renderShell('/mail/compose', <ComposeView onNotify={vi.fn()} />)
    await screen.findByLabelText('Subject')
    const region = await openWindow()
    fireEvent.change(within(region).getByLabelText('Subject'), { target: { value: 'Handed' } })

    fireEvent.click(within(region).getByRole('button', { name: 'Expand' }))

    await waitFor(() => expect(document.querySelector('.quick-compose')).toBeNull())
    expect(screen.getByLabelText('Subject')).toHaveValue('Handed')
  })

  it('expand over a dirty page composer kept by its owner leaves the window open', async () => {
    const { router } = renderShell('/mail/compose?mailto=' + encodeURIComponent('mailto:bob@x.be?subject=Page'),
      <ComposeView onNotify={vi.fn()} />)
    await screen.findByDisplayValue('Page')
    const region = await openWindow()
    fireEvent.change(within(region).getByLabelText('Subject'), { target: { value: 'Window' } })

    fireEvent.click(within(region).getByRole('button', { name: 'Expand' }))
    fireEvent.click(await screen.findByText('Keep editing'))

    await waitFor(() => expect(document.querySelectorAll('[role="alertdialog"]')).toHaveLength(0))
    expect(document.querySelector('.quick-compose')).not.toBeNull()
    expect(within(region).getByLabelText('Subject')).toHaveValue('Window')
    expect(screen.getByDisplayValue('Page')).toBeInTheDocument()
    expect(router.state.location.state).toBeNull()
  })

  it('expand over a dirty page composer its owner discards hands the window over', async () => {
    renderShell('/mail/compose?mailto=' + encodeURIComponent('mailto:bob@x.be?subject=Page'),
      <ComposeView onNotify={vi.fn()} />)
    await screen.findByDisplayValue('Page')
    const region = await openWindow()
    fireEvent.change(within(region).getByLabelText('Subject'), { target: { value: 'Window' } })

    fireEvent.click(within(region).getByRole('button', { name: 'Expand' }))
    fireEvent.click(await screen.findByText('Discard'))

    await waitFor(() => expect(document.querySelector('.quick-compose')).toBeNull())
    expect(screen.getByLabelText('Subject')).toHaveValue('Window')
  })

  it('hands itself over to the page when the screen becomes a phone', async () => {
    mockViewport('desktop')
    const { router } = renderShell('/calendar')
    await openWindow(null, '/mail?folder=INBOX')
    typeSubject('Rotated')

    await changeViewport('phone')

    expect(await screen.findByTestId('compose-page')).toBeInTheDocument()
    const state = router.state.location.state as { seed: ComposeSeed; backTo: string; handedOver: { dirty: boolean } }
    expect(state.handedOver).toEqual({ dirty: true })
    expect(state.backTo).toBe('/mail?folder=INBOX')
    expect(state.seed.subject).toBe('Rotated')
    expect(screen.queryByRole('region')).toBeNull()
    expect(mocks.deleteAttachment).not.toHaveBeenCalled()
  })

  it('a send toast shows inside the window host', async () => {
    renderShell()
    const region = await openWindow()
    const to = within(region).getByLabelText('To')
    fireEvent.change(to, { target: { value: 'alice@x.be' } })
    fireEvent.keyDown(to, { key: 'Enter' })

    fireEvent.click(within(region).getByRole('button', { name: 'Send' }))

    const toast = await screen.findByText('Message sent')
    expect(toast.closest('.quick-compose-toasts')).not.toBeNull()
    expect(screen.queryByRole('region')).toBeNull()
  })

  // Role queries over two whole composers cost seconds in jsdom: this case reads the DOM directly.
  it('a mailbox switch with a dirty window and a mailto page composer asks both', async () => {
    renderShell('/mail/compose?mailto=' + encodeURIComponent('mailto:bob@x.be?subject=Page'),
      <ComposeView onNotify={vi.fn()} />)
    await screen.findByDisplayValue('Page')
    act(() => { quick.open(null, '/mail') })
    const region = await waitFor(() => document.querySelector<HTMLElement>('.quick-compose')!)
    fireEvent.change(await within(region).findByLabelText('Subject'), { target: { value: 'Window' } })
    const questions = () => document.querySelectorAll('[role="alertdialog"]')

    let answer: boolean | undefined
    act(() => { void confirmLeave().then(ok => { answer = ok }) })

    fireEvent.click(await screen.findByText('Discard'))
    await waitFor(() => expect(questions()).toHaveLength(1))
    expect(answer).toBeUndefined()
    fireEvent.click(screen.getByText('Discard'))

    await waitFor(() => expect(answer).toBe(true))
    expect(questions()).toHaveLength(0)
  })

  // The window registered first, so it is asked first: its Keep editing ends the round.
  it('a mailbox switch kept by a dirty window never asks the mailto page composer', async () => {
    const { router } = renderShell('/calendar', <ComposeView onNotify={vi.fn()} />)
    const region = await openWindow()
    fireEvent.change(within(region).getByLabelText('Subject'), { target: { value: 'Window' } })
    act(() => { void router.navigate('/mail/compose?mailto=' + encodeURIComponent('mailto:bob@x.be?subject=Page')) })
    await screen.findByDisplayValue('Page')
    const questions = () => document.querySelectorAll('[role="alertdialog"]')

    let answer: boolean | undefined
    act(() => { void confirmLeave().then(ok => { answer = ok }) })

    await waitFor(() => expect(questions()).toHaveLength(1))
    expect(region).toContainElement(questions()[0] as HTMLElement)
    fireEvent.click(within(region).getByText('Keep editing'))

    await waitFor(() => expect(answer).toBe(false))
    await settle()
    expect(questions()).toHaveLength(0)
    expect(within(region).getByLabelText('Subject')).toHaveValue('Window')
  })

  it('replaces the form on every open, and knows the draft it shows once saved', async () => {
    renderShell()
    await openWindow()
    typeSubject('First')
    const first = quick.state!.instance

    fireEvent.click(screen.getByRole('button', { name: 'Save draft' }))
    await waitFor(() => expect(quick.showsDraft({ folderPath: 'Drafts', uid: 7 })).toBe(true))
    expect(quick.showsDraft({ folderPath: 'Drafts', uid: 8 })).toBe(false)

    act(() => { quick.open(null, '/mail') })

    expect(quick.state!.instance).toBe(first + 1)
    expect(await screen.findByLabelText('Subject')).toHaveValue('')
    expect(quick.showsDraft({ folderPath: 'Drafts', uid: 7 })).toBe(false)
  })
})
