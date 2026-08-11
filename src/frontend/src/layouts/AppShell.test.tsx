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

  // An open message owns the phone screen, and "Mail" is the tab it would navigate to: the bar
  // costs 57px to point at where the reader already is, while the ← is the real way out. Keyed on
  // the uid rather than on the viewport, which the bar's own stylesheet already answers — above
  // 639px it is not drawn whatever this decides. An open contact is the same arrangement one
  // module over, so it takes the same rule: on a phone its card replaces the list and draws its
  // own `.actionbar` at the foot.
  it('withholds it while an open item owns the screen', () => {
    expect(hasTabBar('/mail?folder=INBOX&uid=42')).toBe(false)
    expect(hasTabBar('/mail?folder=INBOX')).toBe(true)
    expect(hasTabBar('/contacts?id=b')).toBe(false)
    expect(hasTabBar('/contacts?scope=favorites')).toBe(true)
  })
})
