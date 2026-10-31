import i18next from 'i18next'
import { describe, expect, it } from 'vitest'
import { ApiError } from '../../../api.js'
import { deliveryErrorMessage } from './deliveryReplyKeyErrors'

const t = i18next.getFixedT('en', 'admin')

describe('deliveryErrorMessage', () => {
  it('says the key is gone on a 404, even one carrying a mapped code', () => {
    // DeleteDeliveryReplyKey answers a 404 carrying `delivery_key_missing`, which is also a
    // mapped CODES entry for the 409 a locked toggle can hit — the 404 must win.
    const err = new ApiError('delivery_key_missing', 404, 'delivery_key_missing')
    expect(deliveryErrorMessage(err, t, 'fallback')).toBe('The key no longer exists.')
  })

  it('maps a 409 delivery_key_missing to the "generate a key" sentence', () => {
    const err = new ApiError('delivery_key_missing', 409, 'delivery_key_missing')
    expect(deliveryErrorMessage(err, t, 'fallback'))
      .toBe('Generate a key before enabling delivery-time replies.')
  })

  it('falls back on an unmapped error', () => {
    const err = new ApiError('Server error', 500, null)
    expect(deliveryErrorMessage(err, t, 'fallback')).toBe('fallback')
  })
})
