import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter, Routes, Route, createMemoryRouter, RouterProvider } from 'react-router-dom'
import { expectNoAxeViolations } from './a11y-test'
import { resetViewport } from './test-utils'
import LoginPage from './pages/LoginPage.jsx'
import MailLayout from './modules/mail/MailLayout'
import type { MailFolderNode } from './modules/mail/api/mailTypes'
import ContactsLayout from './modules/contacts/ContactsLayout'
import type { Contact, ContactDetail } from './modules/contacts/contactTypes'
import CalendarLayout from './modules/calendar/CalendarLayout'
import EventEditor, { EDITOR_TITLE_ID } from './modules/calendar/EventEditor'
import type { EventFormState } from './modules/calendar/eventForm'
import { calendarOf, renderInCalendar, TZ } from './modules/calendar/calendarTestHarness'
import Modal from './components/Modal'
import AdminPage from './modules/settings/admin/AdminPage.jsx'
import GeneralPage from './modules/settings/general/GeneralPage'
import DeleteConfirmModal from './components/DeleteConfirmModal.jsx'

// Every surface's own test file mocks the API at the network layer; vi.mock('./api.js', ...) can
// only be declared once per file, so this is the union of what all eight need.
const mocks = vi.hoisted(() => ({
  getMailFolders: vi.fn(), getMailMessages: vi.fn(), getMailMessage: vi.fn(),
  moveMessages: vi.fn(), deleteMessages: vi.fn(), searchMessages: vi.fn(),
  setMessageFlags: vi.fn(), openDraft: vi.fn(), getIdentities: vi.fn(), emptyFolder: vi.fn(),
  getPreferences: vi.fn(), setPreference: vi.fn(),
  getContacts: vi.fn(), getContact: vi.fn(), createContact: vi.fn(), updateContact: vi.fn(),
  deleteContact: vi.fn(), setContactFavorite: vi.fn(), deleteContacts: vi.fn(),
  setContactsFavorite: vi.fn(), importContacts: vi.fn(), exportContacts: vi.fn(),
  getContactGroups: vi.fn(), createContactGroup: vi.fn(), renameContactGroup: vi.fn(),
  deleteContactGroup: vi.fn(), addContactGroupMembers: vi.fn(), removeContactGroupMembers: vi.fn(),
  getCalendars: vi.fn(), createCalendar: vi.fn(), updateCalendar: vi.fn(),
  setCalendarVisible: vi.fn(), deleteCalendar: vi.fn(), exportCalendar: vi.fn(),
  importCalendar: vi.fn(), importCalendarAsNew: vi.fn(), getOccurrences: vi.fn(),
  searchEvents: vi.fn(), getEvent: vi.fn(), createEvent: vi.fn(), updateEvent: vi.fn(),
  deleteEvent: vi.fn(),
  changeFullName: vi.fn(), adminGetUsers: vi.fn(), adminCreateUser: vi.fn(),
  adminUpdateUser: vi.fn(), adminDeleteUser: vi.fn(), adminGetDomains: vi.fn(),
  adminCreateDomain: vi.fn(), adminUpdateDomain: vi.fn(), adminDeleteDomain: vi.fn(),
  adminGetUserQuota: vi.fn(), adminGetVirtualDomains: vi.fn(),
  adminAddVirtualDomainOwner: vi.fn(), adminRemoveVirtualDomainOwner: vi.fn(),
  adminGetExternalDomains: vi.fn(), getAppSettings: vi.fn(), setAppSetting: vi.fn(),
  login: vi.fn(), markLoggedIn: vi.fn(),
  playNewMailSound: vi.fn(), desktopPermission: vi.fn(), requestDesktopPermission: vi.fn(),
}))

vi.mock('./api.js', async () => ({
  ...(await vi.importActual<object>('./api.js')),
  api: mocks,
  markLoggedIn: mocks.markLoggedIn,
  clearSession: vi.fn(),
  setIsAdmin: vi.fn(),
  requestBlob: vi.fn(),
  mailAttachmentUrl: vi.fn(),
}))

// useAccountId (real, unmocked) reads activeAccountId off this, which is enough for the contacts
// and calendar surfaces too — neither imports AuthContext directly.
vi.mock('./contexts/AuthContext', () => ({
  PRIMARY_ACCOUNT_ID: 'primary',
  useAuth: () => ({
    activeAccount: { id: 'primary', authMode: 'Password' },
    activeAccountId: 'primary',
    accountsLoading: false,
  }),
}))
vi.mock('./contexts/ThemeContext', () => ({ useTheme: () => ({ isDark: false }) }))
vi.mock('./modules/mail/notify/channels', () => ({
  playNewMailSound: mocks.playNewMailSound,
  desktopPermission: mocks.desktopPermission,
  requestDesktopPermission: mocks.requestDesktopPermission,
}))

afterEach(resetViewport)
// clearAllMocks only clears call history, not implementations. resetAllMocks would be the real
// guarantee, but it also wipes test-setup.js's window.matchMedia vi.fn() (built once per file,
// never re-armed per test), which breaks every surface calling useViewport — see task-1-report.md.
beforeEach(() => vi.clearAllMocks())

function queryClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false } } })
}

describe('accessibility sweep', () => {
  it('LoginPage', async () => {
    const { container } = render(<LoginPage onLogin={vi.fn()} />)
    await expectNoAxeViolations(container)
  })

  // List and reader both on screen: the default desktop reading-pane arrangement puts them side
  // by side, which is what the brief asks this surface to be swept while showing.
  it('MailLayout — list and reader both on screen', async () => {
    function folderNode(partial: Partial<MailFolderNode>): MailFolderNode {
      return {
        path: 'X', name: 'X', specialUse: null, selectable: true, subscribed: true,
        total: 0, unread: 0, uidValidity: 1, uidNext: null, highestModSeq: null, children: [],
        ...partial,
      }
    }
    mocks.getMailFolders.mockResolvedValue([
      folderNode({ path: 'INBOX', name: 'INBOX', specialUse: 'inbox' }),
    ])
    mocks.getMailMessages.mockResolvedValue({
      folderPath: 'INBOX', uidValidity: 1, total: 1, page: 0, pageSize: 30,
      messages: [{
        uid: 7, subject: 'Hello', fromName: 'A', fromAddress: 'a@b.c',
        date: '2026-07-18T09:00:00Z', seen: true, flagged: false, answered: false,
        hasAttachments: false, size: 1, preview: '',
      }],
    })
    mocks.getMailMessage.mockResolvedValue({
      uid: 7, folderPath: 'INBOX', uidValidity: 1, subject: 'Hello', fromName: 'A',
      fromAddress: 'a@b.c', to: [], cc: [], date: '2026-07-18T09:00:00Z',
      htmlBody: '<p>Hi</p>', textBody: 'Hi', blockedImageCount: 0, attachments: [],
    })
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', 'mail.readingPane': 'right' })
    mocks.getIdentities.mockResolvedValue({ identities: [] })

    const { container } = render(
      <QueryClientProvider client={queryClient()}>
        <MemoryRouter initialEntries={['/mail?folder=INBOX&uid=7']}><MailLayout /></MemoryRouter>
      </QueryClientProvider>,
    )

    const reader = container.querySelector('.mail-reader') as HTMLElement
    await within(reader).findByText('Hello')
    expect(container.querySelector('.mail-list')).not.toBeNull()
    // nested-interactive: .message-row is a role="button" wrapping its own checkbox, star and
    // action buttons — Task 3's mail-list ARIA-grid rewrite removes this waiver.
    // iframes:false: the reader's real <iframe> defeats axe's cross-frame scan in jsdom — this
    // surface only, since it is the only one of the eight with a real iframe.
    await expectNoAxeViolations(container, {
      extraRules: { 'nested-interactive': { enabled: false } },
      iframes: false,
    })
  })

  it('ContactsLayout', async () => {
    function contactOf(fields: Partial<Contact> & { id: string }): Contact {
      return {
        firstName: null, lastName: null, nickname: null, isFavorite: false, addresses: [], ...fields,
      }
    }
    function detailOf(row: Contact): ContactDetail {
      return {
        id: row.id, isFavorite: row.isFavorite, hasPhoto: false,
        ...(row.firstName != null ? { firstName: row.firstName } : {}),
        addresses: row.addresses.map((address, position) => (
          { position, address, type: 'INTERNET', pref: position === 0 ? 1 : 101, params: '', groupName: '' })),
        phones: [], postalAddresses: [],
      }
    }
    const rows = [
      contactOf({ id: 'a', firstName: 'Alice', addresses: ['alice@x.be'] }),
      contactOf({ id: 'b', firstName: 'Bruno', addresses: ['bruno@x.be'] }),
    ]
    mocks.getContacts.mockResolvedValue({ contacts: rows })
    mocks.getContact.mockImplementation((id: string) => {
      const row = rows.find(one => one.id === id)
      return row ? Promise.resolve(detailOf(row)) : Promise.reject(new Error('not found'))
    })
    mocks.getContactGroups.mockResolvedValue({ groups: [] })

    const { container } = render(
      <QueryClientProvider client={queryClient()}>
        <MemoryRouter initialEntries={['/contacts']}>
          <Routes><Route path="/contacts" element={<ContactsLayout />} /></Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    )

    await screen.findByText('Alice')
    // nested-interactive: .contact-tile is a role="button" wrapping its own checkbox, star and
    // icon buttons — Task 4's contacts-list ARIA-grid rewrite removes this waiver.
    await expectNoAxeViolations(container, {
      extraRules: { 'nested-interactive': { enabled: false } },
    })
  })

  it('CalendarLayout — month view', async () => {
    const browserTz = Intl.DateTimeFormat().resolvedOptions().timeZone
    mocks.getCalendars.mockResolvedValue({ calendars: [calendarOf('a', '#3b82c4', 'Personal')] })
    mocks.getOccurrences.mockResolvedValue({
      occurrences: [{
        eventId: 'e1', calendarId: 'a', uid: 'e1', instanceId: '', isOverride: false,
        isAllDay: false, isFloating: false, transparency: 'OPAQUE', hasAlarm: false,
        summary: 'Stand-up', startUtc: '2026-09-16T07:00:00Z', endUtc: '2026-09-16T08:00:00Z',
      }],
    })
    mocks.searchEvents.mockResolvedValue({ occurrences: [] })
    mocks.getEvent.mockResolvedValue({
      id: 'e1', calendarId: 'a', uid: 'u1', icsHash: 'h1',
      fields: {
        calendarId: 'a', summary: 'Stand-up', isAllDay: false,
        start: '2026-09-16T09:00:00', end: '2026-09-16T09:30:00', timeZone: browserTz,
        reminderMinutesBefore: [], availability: 'Busy', visibility: 'Default',
      },
      attendees: [], repeatIsExact: true, foreignAlarms: [], canInvite: true,
    })
    mocks.getContacts.mockResolvedValue({ contacts: [] })

    const routes = [
      { path: '/calendar', element: <CalendarLayout /> },
      { path: '/calendar/new', element: <CalendarLayout /> },
      { path: '/calendar/:id/edit', element: <CalendarLayout /> },
    ]
    const router = createMemoryRouter(routes, {
      initialEntries: ['/calendar?view=month&date=2026-09-16'],
    })
    const { container } = render(
      <QueryClientProvider client={queryClient()}><RouterProvider router={router} /></QueryClientProvider>,
    )

    await screen.findByText('Stand-up')
    await expectNoAxeViolations(container)
  })

  // EventEditor draws no dialog shell of its own — CalendarLayout wraps it in Modal with
  // header={false} and labelledBy={EDITOR_TITLE_ID}, which is reproduced here rather than the
  // bare renderInCalendar harness EventEditor.test.tsx uses, since "a modal" is the brief's surface.
  it('EventEditor — a modal', async () => {
    mocks.getContacts.mockResolvedValue({ contacts: [] })
    const calendars = [calendarOf('a', '#3b82c4', 'Personal'), calendarOf('b', '#c4783b', 'Work')]
    const initial: EventFormState = {
      calendarId: 'a', title: 'Dentist', isAllDay: false,
      startDate: '2026-09-14', startTime: '09:00', endDate: '2026-09-14', endTime: '10:00',
      timeZone: TZ, repeat: { kind: 'never' }, reminders: [15],
      location: 'Rue Haute 12', description: 'Bring the card',
      availability: 'Busy', visibility: 'Default', url: '',
      keepRepeat: false, foreignAlarms: [], attendees: [], canInvite: true,
    }

    // Wrapped in the real Modal — a violation here could come from Modal's chrome, not EventEditor.
    const { container } = renderInCalendar(
      <QueryClientProvider client={queryClient()}>
        <Modal header={false} labelledBy={EDITOR_TITLE_ID} onClose={vi.fn()}>
          <EventEditor detail={null} occurrence={null} initial={initial} calendars={calendars}
            saving={false} error={null} onReload={null} fullScreen={false}
            onSave={vi.fn()} onDelete={vi.fn()} onClose={vi.fn()} />
        </Modal>
      </QueryClientProvider>,
      { calendars },
    )

    await screen.findByLabelText('Title')
    await expectNoAxeViolations(container)
  })

  it('AdminPage', async () => {
    mocks.adminGetUsers.mockResolvedValue([
      { id: 1, userName: 'alice', domainName: 'weesky.be', domainId: 'WSY', fullName: 'Alice Smith',
        quotaMb: 1024, active: true, admin: false },
    ])
    mocks.adminGetDomains.mockResolvedValue([{ id: 'WSY', name: 'weesky.be' }])
    mocks.adminGetVirtualDomains.mockResolvedValue([])
    mocks.adminGetExternalDomains.mockResolvedValue([])
    mocks.adminGetUserQuota.mockRejectedValue(new Error('unavailable'))
    mocks.getAppSettings.mockResolvedValue({
      'app.installable': 'true', 'app.name': 'Weesky Mail', 'app.shortName': 'Weesky',
    })
    mocks.setAppSetting.mockResolvedValue(undefined)

    const { container } = render(
      <QueryClientProvider client={queryClient()}><AdminPage /></QueryClientProvider>,
    )

    await screen.findByText('alice@weesky.be')
    await expectNoAxeViolations(container)
  })

  it('GeneralPage', async () => {
    mocks.getPreferences.mockResolvedValue({ 'mail.pageSize': '30', 'mail.showPreview': 'true' })
    mocks.setPreference.mockResolvedValue(undefined)
    mocks.desktopPermission.mockReturnValue('default')

    const client = queryClient()
    const { container } = render(<GeneralPage />, {
      wrapper: ({ children }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>,
    })

    await screen.findByLabelText('Messages per page')
    await expectNoAxeViolations(container)
  })

  it('DeleteConfirmModal', async () => {
    const { container } = render(
      <DeleteConfirmModal entityLabel="alice@weesky.be" onConfirm={vi.fn()} onClose={vi.fn()} />,
    )
    await expectNoAxeViolations(container)
  })
})
