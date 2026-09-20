import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, it, expect } from 'vitest'
import ChangePasswordSection from './ChangePasswordSection'

describe('ChangePasswordSection', () => {
  it('announces a validation error assertively', async () => {
    render(<ChangePasswordSection />)

    await userEvent.click(screen.getByRole('button', { name: 'Change password' }))

    expect(screen.getByRole('alert')).toHaveTextContent('Current password is required.')
  })
})
