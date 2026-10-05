import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import ToggleRow from './ToggleRow'
import { setupUser } from '../test-utils'

describe('ToggleRow', () => {
  it('names the control with the label and leaves the hint out of that name', () => {
    render(<ToggleRow id="t" label="Contacts (CardDAV)" hint="Sync with your phone"
      checked={false} onChange={() => {}} />)

    expect(screen.getByRole('checkbox', { name: 'Contacts (CardDAV)' })).not.toBeChecked()
  })

  it('reports the new value', async () => {
    const onChange = vi.fn()
    render(<ToggleRow id="t" label="Contacts (CardDAV)" hint="" checked={false} onChange={onChange} />)

    await setupUser().click(screen.getByRole('checkbox'))

    expect(onChange).toHaveBeenCalledWith(true)
  })
})
