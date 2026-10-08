import { render, screen, waitFor } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { api, markLoggedIn, ApiError } from '../api.js'
import { setupUser } from '../test-utils'
import LoginPage from './LoginPage'

// importOriginal keeps the real ApiError so rejections carry `.status`; only the network call
// is stubbed.
vi.mock('../api.js', async importOriginal => ({
  ...await importOriginal<typeof import('../api.js')>(),
  api: { login: vi.fn() },
  markLoggedIn: vi.fn(),
}))

beforeEach(() => vi.clearAllMocks())

async function fillAndSubmit(email = 'user@example.com', password = 'secret') {
  const user = setupUser()
  await user.type(screen.getByPlaceholderText('Email address'), email)
  await user.type(screen.getByPlaceholderText('Password'), password)
  await user.click(screen.getByRole('button', { name: 'Sign in' }))
  return user
}

describe('LoginPage', () => {
  // The visible design carries no <label> — the placeholder is what sights it — but an
  // accessible name cannot rely on the placeholder alone (gone once typed, unreliable in AT).
  it('renders email and password fields with accessible names, and a submit button', () => {
    render(<LoginPage onLogin={vi.fn()} />)
    expect(screen.getByLabelText('Email address')).toHaveAttribute('placeholder', 'Email address')
    expect(screen.getByLabelText('Password')).toHaveAttribute('placeholder', 'Password')
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('marks the session and calls onLogin after a successful login', async () => {
    vi.mocked(api.login).mockResolvedValue({ expiresIn: 3600 })
    const onLogin = vi.fn()
    render(<LoginPage onLogin={onLogin} />)
    await fillAndSubmit()
    await waitFor(() => expect(onLogin).toHaveBeenCalledOnce())
    expect(markLoggedIn).toHaveBeenCalledOnce()
  })

  it.each([
    ['a 401', new ApiError('unauthorized', 401, null), 'Invalid credentials.'],
    ['a 400', new ApiError('bad request', 400, null), 'Invalid credentials.'],
    ['a 429', new ApiError('rate limited', 429, null), 'Too many sign-in attempts. Wait a few minutes, then try again.'],
    ['a 500', new ApiError('server error', 500, null), 'Sign-in is unavailable right now. Check your connection and try again.'],
    ['a network failure', new TypeError('Failed to fetch'), 'Sign-in is unavailable right now. Check your connection and try again.'],
  ])('announces the matching message in an alert and does not sign in on %s', async (_, rejection, message) => {
    vi.mocked(api.login).mockRejectedValue(rejection)
    const onLogin = vi.fn()
    render(<LoginPage onLogin={onLogin} />)
    await fillAndSubmit()
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent(message))
    expect(onLogin).not.toHaveBeenCalled()
  })

  it('keeps the bundled background when there is no image of the day', () => {
    const { container } = render(<LoginPage onLogin={vi.fn()} />)

    expect(container.querySelector('.login-daily')).toBeNull()
    expect(container.querySelector('.daily-credit')).toBeNull()
  })

  it('shows the image of the day behind the card, with its credit', () => {
    const backdrop = { src: '/api/AppSettings/daily-image/v1?lang=en', title: 'Poulpe fiction', copyright: '© G. Barathieu' }
    const { container } = render(<LoginPage onLogin={vi.fn()} backdrop={backdrop} />)

    const photo = container.querySelector('img.login-daily')
    expect(photo).toHaveAttribute('src', backdrop.src)
    expect(photo).toHaveAttribute('alt', '')
    expect(screen.getByText('Poulpe fiction')).toBeInTheDocument()
    expect(screen.getByText('© G. Barathieu')).toBeInTheDocument()
  })
})
