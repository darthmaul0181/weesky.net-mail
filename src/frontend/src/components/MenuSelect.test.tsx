import { render, screen } from '@testing-library/react'
import { describe, it, expect, vi } from 'vitest'
import { useState } from 'react'
import MenuSelect, { type SelectOption } from './MenuSelect'
import Modal from './Modal'
import { setupUser } from '../test-utils'

const OPTIONS: SelectOption[] = [
  { value: 'none', label: 'None' },
  { value: 'starttls', label: 'STARTTLS' },
  { value: 'ssl', label: 'SSL/TLS' },
  { value: 'smtp', label: 'SMTP', disabled: true },
  { value: 'sieve', label: 'Sieve' },
]

function Harness({ onChange = vi.fn() }: { onChange?: (v: string) => void }) {
  const [value, setValue] = useState('starttls')
  return (
    <>
      <label htmlFor="sec">Security</label>
      <MenuSelect id="sec" value={value} options={OPTIONS}
        onChange={v => { setValue(v); onChange(v) }} />
      <button type="button">After</button>
    </>
  )
}

const box = () => screen.getByRole('combobox', { name: 'Security' })

describe('MenuSelect', () => {
  it('is named by its label and shows the chosen option', () => {
    render(<Harness />)
    expect(box()).toHaveTextContent('STARTTLS')
    expect(box()).toHaveAttribute('aria-expanded', 'false')
  })

  it('opens on the chosen option, marked selected, and a click chooses another', async () => {
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)
    await setupUser().click(box())
    expect(screen.getByRole('option', { name: 'STARTTLS' })).toHaveAttribute('aria-selected', 'true')
    expect(box()).toHaveAttribute('aria-activedescendant', screen.getByRole('option', { name: 'STARTTLS' }).id)
    await setupUser().click(screen.getByRole('option', { name: 'SSL/TLS' }))
    expect(onChange).toHaveBeenCalledWith('ssl')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect(box()).toHaveFocus()
  })

  it('walks with the arrows over a disabled option and chooses with Enter', async () => {
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)
    box().focus()
    await setupUser().keyboard('{ArrowDown}{ArrowDown}{ArrowDown}{Enter}')
    expect(onChange).toHaveBeenCalledWith('sieve')
    expect(box()).toHaveTextContent('Sieve')
  })

  it('jumps by typed letters and cycles on a repeated one', async () => {
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)
    box().focus()
    await setupUser().keyboard('s')
    expect(box()).toHaveAttribute('aria-activedescendant', screen.getByRole('option', { name: 'SSL/TLS' }).id)
    await setupUser().keyboard('s{Enter}')
    expect(onChange).toHaveBeenCalledWith('sieve')
  })

  it('closes on Escape without choosing, and Tab chooses the active option', async () => {
    const onChange = vi.fn()
    render(<Harness onChange={onChange} />)
    box().focus()
    await setupUser().keyboard('{ArrowDown}{Home}{Escape}')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect(onChange).not.toHaveBeenCalled()
    await setupUser().keyboard('{ArrowDown}{End}{Tab}')
    expect(onChange).toHaveBeenCalledWith('sieve')
    expect(screen.getByRole('button', { name: 'After' })).toHaveFocus()
  })

  it('does not open when disabled', async () => {
    render(<MenuSelect ariaLabel="Unit" value="a" options={[{ value: 'a', label: 'A' }]} onChange={vi.fn()} disabled />)
    await setupUser().click(screen.getByRole('combobox', { name: 'Unit' }))
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  // Escape belongs to the topmost layer: the open list, never the dialog it stands in.
  it('closes its list on Escape and leaves the dialog around it open', async () => {
    const onClose = vi.fn()
    render(<Modal title="Settings" onClose={onClose}><Harness /></Modal>)
    await setupUser().click(box())
    await setupUser().keyboard('{Escape}')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect(onClose).not.toHaveBeenCalled()
    expect(box()).toHaveFocus()
  })
})
