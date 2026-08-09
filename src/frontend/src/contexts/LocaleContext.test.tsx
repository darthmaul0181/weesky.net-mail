import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import i18next from 'i18next'
import { useTranslation } from 'react-i18next'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider, useLocale } from './LocaleContext'
import { LANGUAGE_MIRROR_KEY } from '../lib/locale'
import { loadLocale } from '../lib/i18n'

// Stateful, not a fixed answer: useSetPreference's onSuccess invalidates and refetches, so a
// static double that always answers 'fr' would make a refetch overwrite the optimistic write
// before any assertion could see it — exactly the failure a real backend, which persists what
// setPreference writes, would not have.
const state = vi.hoisted(() => ({ language: 'fr' }))

const mocks = vi.hoisted(() => ({
  getPreferences: vi.fn(),
  setPreference: vi.fn(),
}))

vi.mock('../api.js', () => ({ api: mocks }))

vi.mock('./AuthContext', () => ({ useAuth: () => ({ isLoggedIn: true }) }))

// The default behaviour is the real loadLocale — only the reload test below overrides it, so the
// other cases in this file keep exercising the genuine dynamic-import path.
vi.mock('../lib/i18n', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../lib/i18n')>()
  return { ...actual, loadLocale: vi.fn(actual.loadLocale) }
})

function Probe() {
  const { locale, setPreference } = useLocale()
  const { t } = useTranslation('mail')
  return (
    <>
      <span data-testid="locale">{locale}</span>
      <span data-testid="inbox">{t('folders.roles.inbox')}</span>
      <button onClick={() => setPreference('en')}>to english</button>
    </>
  )
}

function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <LocaleProvider><Probe /></LocaleProvider>
    </QueryClientProvider>,
  )
}

describe('LocaleProvider', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.clearAllMocks()
    state.language = 'fr'
    mocks.getPreferences.mockImplementation(async () => ({ 'ui.language': state.language }))
    mocks.setPreference.mockImplementation(async (key: string, value: string) => {
      if (key === 'ui.language') state.language = value
    })
  })
  afterEach(async () => { await i18next.changeLanguage('en') })

  it('applies the stored preference, mirrors it and stamps documentElement.lang', async () => {
    mount()

    await waitFor(() => expect(screen.getByTestId('locale')).toHaveTextContent('fr'))
    expect(screen.getByTestId('inbox')).toHaveTextContent('Boîte de réception')
    expect(localStorage.getItem(LANGUAGE_MIRROR_KEY)).toBe('fr')
    expect(document.documentElement.lang).toBe('fr')
  })

  it('switches language without a reload when the preference changes', async () => {
    mount()
    await waitFor(() => expect(screen.getByTestId('inbox')).toHaveTextContent('Boîte de réception'))

    await userEvent.click(screen.getByRole('button', { name: 'to english' }))

    await waitFor(() => expect(screen.getByTestId('inbox')).toHaveTextContent('Inbox'))
    expect(document.documentElement.lang).toBe('en')
    expect(localStorage.getItem(LANGUAGE_MIRROR_KEY)).toBe('en')
    expect(mocks.setPreference).toHaveBeenCalledWith('ui.language', 'en')
  })

  // The optimistic write in useSetPreference's onMutate must not read as success: a refused save
  // has to leave the interface exactly where the server actually holds it, mirror included, or a
  // failed request would strand the user on a language the account never chose.
  it('rolls back the interface and the mirror when the save is refused', async () => {
    mocks.setPreference.mockRejectedValueOnce(new Error('network'))
    mount()
    await waitFor(() => expect(screen.getByTestId('inbox')).toHaveTextContent('Boîte de réception'))

    await userEvent.click(screen.getByRole('button', { name: 'to english' }))

    await waitFor(() => expect(mocks.setPreference).toHaveBeenCalledWith('ui.language', 'en'))
    await waitFor(() => expect(screen.getByTestId('inbox')).toHaveTextContent('Boîte de réception'))
    expect(document.documentElement.lang).toBe('fr')
    expect(localStorage.getItem(LANGUAGE_MIRROR_KEY)).toBe('fr')
    expect(state.language).toBe('fr')
  })

  // A deploy rotating the hashed catalogue chunks out from under an open tab makes the dynamic
  // import reject. Swallowing it used to leave the radio flipped and the interface silently in
  // the old language; a reload is the recovery, since a fresh index.html has the current hashes.
  it('reloads the page when the catalogue import fails', async () => {
    const reload = vi.fn()
    vi.stubGlobal('location', { ...window.location, reload })
    mount()
    await waitFor(() => expect(screen.getByTestId('inbox')).toHaveTextContent('Boîte de réception'))

    // Only the switch triggered by the click is made to fail — the initial mount's own
    // resolution must succeed normally, or the failure would be indistinguishable from one.
    vi.mocked(loadLocale).mockRejectedValueOnce(new Error('chunk load error'))
    await userEvent.click(screen.getByRole('button', { name: 'to english' }))

    await waitFor(() => expect(reload).toHaveBeenCalled())
    vi.unstubAllGlobals()
  })
})
