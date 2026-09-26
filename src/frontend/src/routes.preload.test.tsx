import { waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { markLoggedIn } from './api.js'

const chunkLoaded = vi.fn()
vi.mock('./modules/mail/MailLayout', () => {
  chunkLoaded()
  return { default: () => null }
})

markLoggedIn()
await import('./routes')

describe('mail chunk preload', () => {
  it('starts downloading the mail chunk at module evaluation when a session exists', async () => {
    await waitFor(() => { expect(chunkLoaded).toHaveBeenCalledTimes(1) })
  })
})
