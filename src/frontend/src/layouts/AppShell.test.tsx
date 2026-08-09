import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

// The three ambient hooks are the shell's own business and none of them touches the tab bar.
vi.mock('../modules/mail/notify/useMailNotifications', () => ({ useMailNotifications: () => {} }))
vi.mock('../hooks/useTabTitle', () => ({ useTabTitle: () => {} }))
vi.mock('../hooks/useFaviconBadge', () => ({ useFaviconBadge: () => {} }))

const { default: AppShell } = await import('./AppShell')

function renderAt(path: string) {
  return render(<MemoryRouter initialEntries={[path]}><AppShell /></MemoryRouter>)
}

function hasTabBar(path: string) {
  return renderAt(path).container.querySelector('.app-bottom-nav') != null
}

describe('AppShell', () => {
  it('carries the tab bar on the ordinary routes', () => {
    expect(hasTabBar('/mail')).toBe(true)
    expect(hasTabBar('/contacts')).toBe(true)
    expect(hasTabBar('/contacts?id=b')).toBe(true)
    expect(hasTabBar('/settings/account')).toBe(true)
  })

  // A writing surface is full-screen and owns the bottom of the phone with its own action bar;
  // a tab bar under a software keyboard serves nobody. The contacts editor is the same shape as
  // the composer and takes the same rule.
  it('withholds it on every writing surface', () => {
    expect(hasTabBar('/mail/compose')).toBe(false)
    expect(hasTabBar('/contacts/new')).toBe(false)
    expect(hasTabBar('/contacts/9f1c-b2/edit')).toBe(false)
  })
})
