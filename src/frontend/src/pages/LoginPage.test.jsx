import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { api, markLoggedIn, ApiError } from '../api.js'
import LoginPage from './LoginPage.jsx'

// importOriginal keeps the real ApiError so rejections carry `.status`; only the network call
// is stubbed.
vi.mock('../api.js', async importOriginal => ({
  ...(await importOriginal()),
  api: { login: vi.fn() },
  markLoggedIn: vi.fn(),
}))

beforeEach(() => vi.clearAllMocks())

async function fillAndSubmit(email = 'user@example.com', password = 'secret') {
  const user = userEvent.setup()
  await user.type(screen.getByPlaceholderText('Email address'), email)
  await user.type(screen.getByPlaceholderText('Password'), password)
  await user.click(screen.getByRole('button', { name: 'Sign in' }))
  return user
}

describe('LoginPage', () => {
  it('renders email, password and submit button', () => {
    render(<LoginPage onLogin={vi.fn()} />)
    expect(screen.getByPlaceholderText('Email address')).toBeInTheDocument()
    expect(screen.getByPlaceholderText('Password')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('calls onLogin after successful login', async () => {
    api.login.mockResolvedValue({})
    const onLogin = vi.fn()
    render(<LoginPage onLogin={onLogin} />)
    await fillAndSubmit()
    await waitFor(() => expect(onLogin).toHaveBeenCalledOnce())
  })

  it('calls markLoggedIn after successful login', async () => {
    api.login.mockResolvedValue({})
    render(<LoginPage onLogin={vi.fn()} />)
    await fillAndSubmit()
    await waitFor(() => expect(markLoggedIn).toHaveBeenCalledOnce())
  })

  it('shows an invalid-credentials message on a 401', async () => {
    api.login.mockRejectedValue(new ApiError('unauthorized', 401))
    render(<LoginPage onLogin={vi.fn()} />)
    await fillAndSubmit()
    await waitFor(() => expect(screen.getByText('Invalid credentials.')).toBeInTheDocument())
  })

  it('shows an invalid-credentials message on a 400', async () => {
    api.login.mockRejectedValue(new ApiError('bad request', 400))
    render(<LoginPage onLogin={vi.fn()} />)
    await fillAndSubmit()
    await waitFor(() => expect(screen.getByText('Invalid credentials.')).toBeInTheDocument())
  })

  it('shows a too-many-attempts message on a 429', async () => {
    api.login.mockRejectedValue(new ApiError('rate limited', 429))
    render(<LoginPage onLogin={vi.fn()} />)
    await fillAndSubmit()
    await waitFor(() =>
      expect(
        screen.getByText('Too many sign-in attempts. Wait a few minutes, then try again.')
      ).toBeInTheDocument()
    )
  })

  it('shows an unavailable message on a 500', async () => {
    api.login.mockRejectedValue(new ApiError('server error', 500))
    render(<LoginPage onLogin={vi.fn()} />)
    await fillAndSubmit()
    await waitFor(() =>
      expect(
        screen.getByText('Sign-in is unavailable right now. Check your connection and try again.')
      ).toBeInTheDocument()
    )
  })

  it('shows an unavailable message on a network failure', async () => {
    api.login.mockRejectedValue(new TypeError('Failed to fetch'))
    render(<LoginPage onLogin={vi.fn()} />)
    await fillAndSubmit()
    await waitFor(() =>
      expect(
        screen.getByText('Sign-in is unavailable right now. Check your connection and try again.')
      ).toBeInTheDocument()
    )
  })

  it('shows the error banner with role="alert"', async () => {
    api.login.mockRejectedValue(new ApiError('unauthorized', 401))
    render(<LoginPage onLogin={vi.fn()} />)
    await fillAndSubmit()
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Invalid credentials.'))
  })

  it('does not call onLogin on failed login', async () => {
    api.login.mockRejectedValue(new ApiError('unauthorized', 401))
    const onLogin = vi.fn()
    render(<LoginPage onLogin={onLogin} />)
    await fillAndSubmit()
    await waitFor(() => screen.getByText('Invalid credentials.'))
    expect(onLogin).not.toHaveBeenCalled()
  })
})
