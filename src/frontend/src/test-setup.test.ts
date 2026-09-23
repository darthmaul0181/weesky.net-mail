import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { createElement } from 'react'

describe('test setup', () => {
  it('renders into an empty document', () => {
    render(createElement('p', null, 'cleanup probe'))
    expect(screen.getAllByText('cleanup probe')).toHaveLength(1)
  })

  it('removes the previous test\'s DOM before the next one', () => {
    expect(document.body).toBeEmptyDOMElement()
  })
})
