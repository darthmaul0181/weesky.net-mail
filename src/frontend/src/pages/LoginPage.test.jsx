import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { api, markLoggedIn } from '../api.js'
import LoginPage from './LoginPage.jsx'

vi.mock('../api.js', () => ({
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

  it('shows an error message on failed login', async () => {
    api.login.mockRejectedValue(new Error('401'))
    render(<LoginPage onLogin={vi.fn()} />)
    await fillAndSubmit()
    await waitFor(() => expect(screen.getByText('Invalid credentials.')).toBeInTheDocument())
  })

  it('does not call onLogin on failed login', async () => {
    api.login.mockRejectedValue(new Error('401'))
    const onLogin = vi.fn()
    render(<LoginPage onLogin={onLogin} />)
    await fillAndSubmit()
    await waitFor(() => screen.getByText('Invalid credentials.'))
    expect(onLogin).not.toHaveBeenCalled()
  })
})
