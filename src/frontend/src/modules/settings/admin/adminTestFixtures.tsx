import { render as rtlRender, screen, within } from '@testing-library/react'
import type { ReactElement } from 'react'
import { createTestQueryClient, withQueryClient } from '../../../test-utils'
import type { AdminDomain, AdminUser, VirtualDomain } from './adminTypes'

// Every tab and dialog reads and writes through TanStack Query: each render gets a fresh cache,
// held for the life of that render so a rerender keeps it.
export function render(ui: ReactElement, client = createTestQueryClient()) {
  return rtlRender(ui, { wrapper: withQueryClient(client) })
}

/** The confirm button of the open delete dialog, never the row's own trash button. */
export const confirmButton = () => within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Delete' })

export const MB = 1024 * 1024

export const MOCK_DOMAINS: AdminDomain[] = [{ id: 'WSY', name: 'weesky.be', aliasCount: 0 }]
export const EDIT_DOMAIN: AdminDomain = { id: 'WSY', name: 'weesky.be', aliasCount: 0 }
export const MOCK_USERS: AdminUser[] = [
  { id: 1, userName: 'alice', domainName: 'weesky.be', domainId: 'WSY', fullName: 'Alice Smith', quotaMb: 1024, active: true, admin: false, lastLogins: [] },
]
export const BOB: AdminUser =
  { id: 2, userName: 'bob', domainName: 'weesky.be', domainId: 'WSY', fullName: 'Bob Jones', quotaMb: 1024, active: true, admin: false, lastLogins: [] }
export const MOCK_VIRTUAL_DOMAINS: VirtualDomain[] = [
  { domainId: 'EXT', domainName: 'extra.com', owners: [{ ownerId: 1, ownerEmail: 'alice@weesky.be' }] },
  { domainId: 'ORF', domainName: 'orphan.net', owners: [] },
]
