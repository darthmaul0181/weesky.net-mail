import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import TopBar from './TopBar'

describe('TopBar', () => {
  // The product name is fixed; the admin's application name only titles the tab and the installed app.
  it('names the product beside its logo', () => {
    render(<TopBar />)

    expect(screen.getByRole('banner')).toHaveTextContent('Scotty webmail')
    expect(screen.queryByAltText('weesky.net')).not.toBeInTheDocument()
  })
})
