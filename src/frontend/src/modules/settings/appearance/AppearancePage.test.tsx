import { describe, it, expect, beforeEach, vi } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { createTestQueryClient, setupUser } from '../../../test-utils'
import { ThemeProvider, PALETTE_IDS } from '../../../contexts/ThemeContext'
import AppearancePage from './AppearancePage'

const setPreference = vi.fn()
vi.mock('../../../contexts/LocaleContext', () => ({
  useLocale: () => ({ locale: 'en', preference: 'auto', setPreference, saving: false }),
}))

const mocks = vi.hoisted(() => ({ getPreferences: vi.fn(), setPreference: vi.fn() }))
vi.mock('../../../api.js', () => ({ api: mocks }))

function renderPage(customPalette = '') {
  mocks.getPreferences.mockResolvedValue({ 'ui.customPalette': customPalette })
  mocks.setPreference.mockResolvedValue(undefined)
  return render(
    <QueryClientProvider client={createTestQueryClient()}>
      <ThemeProvider><AppearancePage /></ThemeProvider>
    </QueryClientProvider>,
  )
}

let user: ReturnType<typeof setupUser>
beforeEach(() => { user = setupUser() })

describe('AppearancePage', () => {
  beforeEach(() => { localStorage.clear(); setPreference.mockClear() })

  // By role, not by label text: the loupe's own label names the palette too — deliberately, so
  // twelve of them are told apart by a screen reader — which makes getByLabelText ambiguous.
  it('reflects current preferences', () => {
    renderPage()
    expect(screen.getByLabelText('System')).toBeChecked()
    expect(screen.getByRole('radio', { name: /Night & coral/ })).toBeChecked()
  })

  it('changes the theme', () => {
    renderPage()
    fireEvent.click(screen.getByLabelText('Dark'))
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(localStorage.getItem('appearance_theme')).toBe('dark')
  })

  it.each([
    ['Sea breeze', 'classic'],
    ['Plum & gold', 'plum'],
  ])('changes the palette to %s', (label, id) => {
    renderPage()
    fireEvent.click(screen.getByLabelText(label))
    expect(document.documentElement.getAttribute('data-palette')).toBe(id)
    expect(localStorage.getItem('appearance_palette')).toBe(id)
  })

  // Selected by group rather than by a regex over every label, which had to grow with the list.
  it('offers every palette the app knows, in order', () => {
    renderPage()

    const radios = screen.getAllByRole('radio')
      .filter(r => (r as HTMLInputElement).name === 'palette') as HTMLInputElement[]

    expect(radios.map(r => r.value)).toEqual([...PALETTE_IDS])
  })

  // Each thumbnail declares the palette it advertises, which is the only thing standing between
  // twelve previews and twelve copies of the active one.
  it('previews each palette in its own colours', () => {
    const { container } = renderPage()

    const previews = Array.from(container.querySelectorAll('.palette-preview'))
    expect(previews.map(p => p.getAttribute('data-palette'))).toEqual([...PALETTE_IDS])
  })

  // The accent is what tells two palettes apart, and it reaches the running app mostly through
  // the compose button and the attachment chips. A preview without them was twelve near-greys.
  it('shows the accent surfaces the app actually carries', () => {
    const { container } = renderPage()

    const first = container.querySelector('.palette-preview')!
    expect(first.querySelector('.pp-compose')).not.toBeNull()
    expect(first.querySelector('.pp-chip')).not.toBeNull()
  })

  // The stored preference may be "system", which names no mode: the preview has to show the
  // mode the user is actually in, so it reads the resolved value.
  it('previews in the resolved theme, not the stored preference', () => {
    localStorage.setItem('appearance_theme', 'system')
    const original = window.matchMedia
    Object.defineProperty(window, 'matchMedia', {
      writable: true,
      value: vi.fn().mockImplementation((query: string) => ({
        matches: true,
        media: query,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      })),
    })

    try {
      const { container } = renderPage()

      Array.from(container.querySelectorAll('.palette-preview'))
        .forEach(p => expect(p.getAttribute('data-theme')).toBe('dark'))
    } finally {
      Object.defineProperty(window, 'matchMedia', { writable: true, value: original })
    }
  })

  // The label already names the palette; a screen reader has no use for a picture of colours.
  it('hides the thumbnails from assistive technology', () => {
    const { container } = renderPage()

    Array.from(container.querySelectorAll('.palette-preview'))
      .forEach(p => expect(p).toHaveAttribute('aria-hidden', 'true'))
  })

  it('offers Automatic, English and Français, and writes the chosen one', async () => {
    renderPage()

    expect(screen.getByRole('radio', { name: 'Automatic' })).toBeChecked()
    // Written in their own language: someone stranded in an English interface does not look
    // for "French".
    expect(screen.getByRole('radio', { name: 'English' })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: 'Français' })).toBeInTheDocument()

    await user.click(screen.getByRole('radio', { name: 'Français' }))

    expect(setPreference).toHaveBeenCalledWith('fr')
  })
})

describe('AppearancePage — the enlarged preview', () => {
  beforeEach(() => localStorage.clear())

  const open = (name: string) => {
    const rendered = renderPage()
    fireEvent.click(screen.getByRole('button', { name: `Enlarge the ${name} preview` }))
    return rendered
  }

  // The whole reason the loupe sits outside the <label>: inside one, a click on it activates the
  // label's control, so asking to look at a palette would have applied it.
  it('does not select the palette it enlarges', () => {
    const { container } = open('Sea breeze')

    expect(document.documentElement.getAttribute('data-palette')).toBe('night')
    expect(localStorage.getItem('appearance_palette')).toBeNull()
    expect(screen.getByRole('radio', { name: /Night & coral/ })).toBeChecked()
    expect(container.querySelector('.modal-title')?.textContent).toContain('Sea breeze')
  })

  // A thumbnail can only ever show the mode in use, and a palette is chosen once for both.
  it('shows the enlarged palette in both modes', () => {
    const { container } = open('Ink')

    const large = Array.from(container.querySelectorAll('.palette-preview.is-large'))
    expect(large.map(p => p.getAttribute('data-theme'))).toEqual(['light', 'dark'])
    large.forEach(p => expect(p.getAttribute('data-palette')).toBe('ink'))
  })

  it('closes on the ✕', () => {
    const { container } = open('Azure')

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(container.querySelector('.palette-zoom-modal')).toBeNull()
  })

  it('closes on Escape', () => {
    const { container } = open('Azure')

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(container.querySelector('.palette-zoom-modal')).toBeNull()
  })

  it('offers a loupe per palette', () => {
    renderPage()

    expect(screen.getAllByRole('button', { name: /^Enlarge the .* preview$/ }))
      .toHaveLength(PALETTE_IDS.length)
  })
})

describe('AppearancePage — my palette', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.clearAllMocks()
    document.getElementById('custom-palette')?.remove()
  })

  const slider = (name: string) => screen.getByRole<HTMLInputElement>('slider', { name })

  it('offers to create one when the account has none', async () => {
    renderPage()
    expect(await screen.findByRole('button', { name: 'Create my palette' })).toBeInTheDocument()
    expect(screen.queryByRole('radio', { name: 'My palette' })).toBeNull()
  })

  it('starts the editor from the palette in use', async () => {
    localStorage.setItem('appearance_palette', 'forest')
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Create my palette' }))

    expect(slider('Structure').value).toBe('159')
    expect(slider('Accent').value).toBe('71')
    expect(screen.getByRole('radio', { name: 'Subtle' })).toBeChecked()
  })

  it('saves the draft and selects it', async () => {
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Create my palette' }))
    fireEvent.change(slider('Accent'), { target: { value: '200' } })
    await user.click(screen.getByRole('radio', { name: 'Vivid' }))
    await user.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(mocks.setPreference).toHaveBeenCalledWith('ui.customPalette', '265,vivid,200,structure'))
    await waitFor(() => expect(document.documentElement.getAttribute('data-palette')).toBe('custom'))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('keeps the editor open and the palette unchanged when saving fails', async () => {
    renderPage()
    mocks.setPreference.mockRejectedValue(new Error('offline'))
    await user.click(await screen.findByRole('button', { name: 'Create my palette' }))
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Could not save your palette.')).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(document.documentElement.getAttribute('data-palette')).toBe('night')
  })

  it('reopens the editor on the saved palette', async () => {
    renderPage('100,neutral,300')
    await user.click(await screen.findByRole('button', { name: 'Edit my palette' }))

    expect(slider('Structure').value).toBe('100')
    expect(screen.getByRole('radio', { name: 'Neutral' })).toBeChecked()
  })

  // The preview shows the draft; the grid keeps showing what is saved.
  it('previews the draft, not the saved palette', async () => {
    renderPage('100,neutral,300')
    await user.click(await screen.findByRole('button', { name: 'Edit my palette' }))
    fireEvent.change(slider('Structure'), { target: { value: '10' } })

    const draft = screen.getByRole('dialog').querySelector<HTMLElement>('.palette-preview')!
    expect(draft.style.getPropertyValue('--topbar-bg')).not.toBe('')
    const card = document.querySelector<HTMLElement>('.palette-card .palette-preview[data-palette="custom"]')!
    expect(card.style.getPropertyValue('--topbar-bg')).toBe('')
  })

  it('warns when the accent sits on the error red', async () => {
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Create my palette' }))
    expect(screen.queryByText(/close to the red used for errors/)).toBeNull()

    fireEvent.change(slider('Accent'), { target: { value: '27' } })
    expect(screen.getByText(/close to the red used for errors/)).toBeInTheDocument()
  })

  // The ✕, Escape and the backdrop are locked while the write is in flight; Cancel is the fourth way out.
  it('locks Cancel while the save is in flight', async () => {
    renderPage()
    mocks.setPreference.mockReturnValue(new Promise(() => {}))
    await user.click(await screen.findByRole('button', { name: 'Create my palette' }))
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(screen.getByRole('button', { name: 'Cancel' })).toBeDisabled()
  })

  // Unknown is not "none": offering Create here would overwrite a palette the account may own.
  it('withholds the ninth card until the preferences are known', async () => {
    mocks.getPreferences.mockReturnValue(new Promise(() => {}))
    render(
      <QueryClientProvider client={createTestQueryClient()}>
        <ThemeProvider><AppearancePage /></ThemeProvider>
      </QueryClientProvider>,
    )
    await screen.findByRole('radio', { name: /Night & coral/ })

    expect(screen.queryByRole('button', { name: 'Create my palette' })).toBeNull()
    expect(screen.queryByRole('radio', { name: 'My palette' })).toBeNull()
  })

  // Selecting `custom` with no stylesheet declared would leave every role token undefined.
  it('declares the palette before selecting it', async () => {
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Create my palette' }))
    await user.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(document.documentElement.getAttribute('data-palette')).toBe('custom'))
    expect(document.getElementById('custom-palette')).not.toBeNull()
  })

  it('opens on the structure slider, which speaks in degrees', async () => {
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Create my palette' }))

    expect(slider('Structure')).toHaveFocus()
    expect(slider('Structure')).toHaveAttribute('aria-valuetext', '265°')
  })

  // A slot that appears and disappears resizes the dialog, which re-centres under the dragging pointer.
  it('keeps the warning slot mounted and announces it politely', async () => {
    const { container } = renderPage()
    await user.click(await screen.findByRole('button', { name: 'Create my palette' }))
    const slot = container.ownerDocument.querySelector('.custom-palette-warning')

    expect(slot).toHaveAttribute('aria-live', 'polite')
    expect(slot).toHaveTextContent('')
    fireEvent.change(slider('Accent'), { target: { value: '27' } })
    expect(container.ownerDocument.querySelector('.custom-palette-warning')).toBe(slot)
    expect(slot).toHaveTextContent(/close to the red used for errors/)
  })

  it('saves accent buttons when asked, and previews them in light mode only', async () => {
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Create my palette' }))
    expect(screen.getByRole('radiogroup', { name: 'Buttons in light mode' })).toBeInTheDocument()
    await user.click(screen.getByRole('radio', { name: 'Accent' }))

    const [light, dark] = screen.getByRole('dialog').querySelectorAll<HTMLElement>('.palette-preview')
    expect(light!.style.getPropertyValue('--action-primary')).toBe(light!.style.getPropertyValue('--accent-unread'))
    expect(dark!.style.getPropertyValue('--action-primary')).toBe(dark!.style.getPropertyValue('--accent-unread'))

    await user.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(mocks.setPreference).toHaveBeenCalledWith('ui.customPalette', '265,muted,35,accent'))
  })

  it('starts from Slate with accent buttons, as Slate wears them', async () => {
    localStorage.setItem('appearance_palette', 'slate')
    renderPage()
    await user.click(await screen.findByRole('button', { name: 'Create my palette' }))

    expect(screen.getByRole('radio', { name: 'Accent' })).toBeChecked()
  })

  it('reopens on the saved buttons choice', async () => {
    renderPage('100,neutral,300,accent')
    await user.click(await screen.findByRole('button', { name: 'Edit my palette' }))

    expect(screen.getByRole('radio', { name: 'Accent' })).toBeChecked()
  })

  it('selects the saved palette like any other', async () => {
    renderPage('100,neutral,300')
    await user.click(await screen.findByRole('radio', { name: 'My palette' }))
    expect(localStorage.getItem('appearance_palette')).toBe('custom')
  })
})
