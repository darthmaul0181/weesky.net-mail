import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { api, markLoggedIn } from '../api.js'

export default function LoginPage({ onLogin }) {
  const { t } = useTranslation('auth')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState(null)
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      await api.login(email, password)
      markLoggedIn()
      onLogin()
    } catch (err) {
      if (err?.status === 429) setError(t('login.tooManyAttempts'))
      else if (err?.status === 401 || err?.status === 400) setError(t('login.invalidCredentials'))
      else setError(t('login.unavailable'))
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="page-center">
      <div className="card">
        {error && (
          <div className="alert alert-error" role="alert">
            {error}
          </div>
        )}

        <form onSubmit={handleSubmit}>
          <div className="field">
            <label className="visually-hidden" htmlFor="email">{t('login.email')}</label>
            <input
              id="email"
              type="email"
              autoComplete="email"
              placeholder={t('login.email')}
              value={email}
              onChange={e => setEmail(e.target.value)}
              required
            />
          </div>

          <div className="field">
            <label className="visually-hidden" htmlFor="password">{t('login.password')}</label>
            <input
              id="password"
              type="password"
              autoComplete="current-password"
              placeholder={t('login.password')}
              value={password}
              onChange={e => setPassword(e.target.value)}
              required
            />
          </div>

          <button className="btn btn-primary" type="submit" disabled={loading}>
            {loading ? <span className="spinner" /> : t('login.submit')}
          </button>
        </form>
      </div>
    </div>
  )
}
