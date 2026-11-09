import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import FloatingAction from './FloatingAction'
import { QuickComposeContext, type QuickComposeApi } from '../modules/mail/compose/quick/QuickComposeContext'

const inert: QuickComposeApi = {
  state: null, open: () => {}, restore: () => {}, close: () => {}, showsDraft: () => false,
}

function renderWith(api: QuickComposeApi) {
  return render(
    <QuickComposeContext.Provider value={api}>
      <FloatingAction label="Add contact" onClick={() => {}}>+</FloatingAction>
    </QuickComposeContext.Provider>)
}

describe('FloatingAction', () => {
  it('draws its button while no quick compose window exists', () => {
    renderWith(inert)

    expect(screen.getByRole('button', { name: 'Add contact' })).toBeInTheDocument()
  })

  // Contacts' Add and Calendar's + exist only here below 1024px: the stylesheet lifts them instead.
  it('keeps its button while a quick compose window exists', () => {
    renderWith({ ...inert, state: { seed: null, accountId: 'primary', origin: '/mail', minimised: true, instance: 1 } })

    expect(screen.getByRole('button', { name: 'Add contact' })).toBeInTheDocument()
  })
})
