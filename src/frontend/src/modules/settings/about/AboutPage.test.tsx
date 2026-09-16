import type { ReactNode } from 'react'
import { render, screen } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import AboutPage from './AboutPage'
import { api } from '../../../api.js'
import { BUILT_AT, WEB_COMMIT, WEB_VERSION } from '../../../lib/appVersion'

vi.mock('../../../api.js', async importOriginal => ({
  ...await importOriginal<typeof import('../../../api.js')>(),
  api: { getVersion: vi.fn() },
}))

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const wrapper = ({ children }: { children: ReactNode }) =>
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  return render(<AboutPage />, { wrapper })
}

beforeEach(() => {
  vi.mocked(api.getVersion).mockResolvedValue({ version: '1.0.0-dev', commit: 'f4e5d6c' })
})

describe('AboutPage', () => {
  it('names the product and its web version', () => {
    renderPage()

    expect(screen.getByRole('heading', { level: 1, name: 'About' })).toBeInTheDocument()
    // The name is drawn in two weights, so it is two nodes: match the line rather than a word.
    expect(screen.getByText((_, element) =>
      element?.tagName === 'P' && element.textContent === 'Scotty webmail')).toBeInTheDocument()
    expect(screen.getByText(`Version ${WEB_VERSION}`)).toBeInTheDocument()
    expect(screen.getByText(`Web app ${WEB_VERSION} (${WEB_COMMIT})`)).toBeInTheDocument()
  })

  it('shows the server version the API reports', async () => {
    renderPage()

    expect(await screen.findByText('Server 1.0.0-dev (f4e5d6c)')).toBeInTheDocument()
  })

  it('omits the parentheses when the server build carried no commit', async () => {
    vi.mocked(api.getVersion).mockResolvedValue({ version: '1.0.0', commit: null })
    renderPage()

    expect(await screen.findByText('Server 1.0.0')).toBeInTheDocument()
  })

  // The page is where someone looks while something is broken: the web half must still read.
  it('says the server is unavailable rather than hiding the web version', async () => {
    vi.mocked(api.getVersion).mockRejectedValue(new Error('down'))
    renderPage()

    expect(await screen.findByText('Server unavailable')).toBeInTheDocument()
    expect(screen.getByText(`Web app ${WEB_VERSION} (${WEB_COMMIT})`)).toBeInTheDocument()
  })

  it('dates the web build in the interface language', () => {
    renderPage()

    const expected = new Intl.DateTimeFormat('en', { dateStyle: 'long' }).format(new Date(BUILT_AT))
    expect(screen.getByText(expected)).toBeInTheDocument()
  })

  // The line keeps its place while the answer is in flight: it used to appear from nothing and
  // push the actions down under whoever was reaching for them.
  it('holds the server line while the answer is in flight', () => {
    renderPage()

    expect(screen.getByText('Server …')).toBeInTheDocument()
  })

  it('opens the third-party licences in a new tab', () => {
    renderPage()

    const link = screen.getByRole('link', { name: 'Third-party components' })
    expect(link).toHaveAttribute('href', '/third-party-licenses.html')
    expect(link).toHaveAttribute('target', '_blank')
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')
  })

  it('offers the source, which the licence obliges a modified deployment to', () => {
    renderPage()

    const link = screen.getByRole('link', { name: 'Source code' })
    expect(link).toHaveAttribute('href', 'https://github.com/darthmaul0181/weesky.net-mail')
    expect(link).toHaveAttribute('target', '_blank')
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')
  })

  it('carries the copyright for the year of the build', () => {
    renderPage()

    const year = new Date(BUILT_AT).getFullYear()
    expect(screen.getByText(`© ${year} darthmaul0181 — AGPL-3.0`)).toBeInTheDocument()
  })
})
