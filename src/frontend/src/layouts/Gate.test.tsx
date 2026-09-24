import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router'
import Gate from './Gate'

vi.mock('../contexts/AuthContext', () => ({ useAuth: () => ({}) }))

function renderGate(allow: () => boolean | 'wait') {
  return render(
    <MemoryRouter initialEntries={['/settings/rules']}>
      <Routes>
        <Route element={<Gate allow={allow} redirect="/settings/general" />}>
          <Route path="/settings/rules" element={<span>Rules page</span>} />
        </Route>
        <Route path="/settings/general" element={<span>General page</span>} />
      </Routes>
    </MemoryRouter>
  )
}

describe('Gate', () => {
  it('renders nothing while the predicate is still deciding', () => {
    renderGate(() => 'wait')
    expect(screen.queryByText('Rules page')).not.toBeInTheDocument()
    expect(screen.queryByText('General page')).not.toBeInTheDocument()
  })

  it('redirects when the predicate refuses', () => {
    renderGate(() => false)
    expect(screen.getByText('General page')).toBeInTheDocument()
  })

  it('renders the outlet when the predicate allows', () => {
    renderGate(() => true)
    expect(screen.getByText('Rules page')).toBeInTheDocument()
  })
})
