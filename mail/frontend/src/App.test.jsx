import { render, screen, act } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { hasToken, setUnauthorizedHandler } from './api.js'
import App from './App.jsx'

vi.mock('./api.js', () => ({
  hasToken: vi.fn(),
  setUnauthorizedHandler: vi.fn(),
}))

vi.mock('./pages/LoginPage.jsx', () => ({
  default: ({ onLogin }) => <button onClick={onLogin}>login-page</button>,
}))

vi.mock('./pages/AliasesPage.jsx', () => ({
  default: ({ onLogout }) => <button onClick={onLogout}>aliases-page</button>,
}))

beforeEach(() => vi.clearAllMocks())

describe('App', () => {
  it('renders LoginPage when no token', () => {
    hasToken.mockReturnValue(false)
    render(<App />)
    expect(screen.getByText('login-page')).toBeInTheDocument()
  })

  it('renders AliasesPage when token exists', () => {
    hasToken.mockReturnValue(true)
    render(<App />)
    expect(screen.getByText('aliases-page')).toBeInTheDocument()
  })

  it('registers the unauthorized handler on mount', () => {
    hasToken.mockReturnValue(false)
    render(<App />)
    expect(setUnauthorizedHandler).toHaveBeenCalledWith(expect.any(Function))
  })

  it('switches to LoginPage when the unauthorized handler fires', async () => {
    hasToken.mockReturnValue(true)
    let capturedHandler
    setUnauthorizedHandler.mockImplementation(fn => { capturedHandler = fn })
    render(<App />)
    await act(async () => capturedHandler())
    expect(screen.getByText('login-page')).toBeInTheDocument()
  })

  it('switches to AliasesPage after successful login', async () => {
    hasToken.mockReturnValue(false)
    render(<App />)
    await userEvent.click(screen.getByText('login-page'))
    expect(screen.getByText('aliases-page')).toBeInTheDocument()
  })

  it('switches to LoginPage after logout', async () => {
    hasToken.mockReturnValue(true)
    render(<App />)
    await userEvent.click(screen.getByText('aliases-page'))
    expect(screen.getByText('login-page')).toBeInTheDocument()
  })
})
