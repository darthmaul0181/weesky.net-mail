import { describe, it, expect } from 'vitest'
import { isConditionValid, isActionValid } from './ruleDraft'

describe('isConditionValid', () => {
  it('rejects an empty value on a text field', () => {
    expect(isConditionValid({ field: 'Subject', operator: 'Contains', value: '' })).toBe(false)
    expect(isConditionValid({ field: 'Subject', operator: 'Contains', value: '   ' })).toBe(false)
  })
  it('accepts a non-empty value on a text field', () => {
    expect(isConditionValid({ field: 'Subject', operator: 'Contains', value: 'x' })).toBe(true)
  })
  it('requires a header name for the Header field', () => {
    expect(isConditionValid({ field: 'Header', operator: 'Contains', value: 'x', headerName: '' })).toBe(false)
    expect(isConditionValid({ field: 'Header', operator: 'Contains', value: 'x', headerName: 'X-Spam' })).toBe(true)
  })
  it('treats Duplicate as always valid (seconds optional)', () => {
    expect(isConditionValid({ field: 'Duplicate', operator: 'Contains', value: '' })).toBe(true)
  })
})

describe('isActionValid', () => {
  it('requires an argument for FileInto/Redirect/Reject/SetFlag', () => {
    expect(isActionValid({ type: 'FileInto', argument: '' })).toBe(false)
    expect(isActionValid({ type: 'FileInto', argument: 'Inbox' })).toBe(true)
    expect(isActionValid({ type: 'Redirect', argument: '' })).toBe(false)
  })
  it('treats Discard and Keep as always valid', () => {
    expect(isActionValid({ type: 'Discard' })).toBe(true)
    expect(isActionValid({ type: 'Keep' })).toBe(true)
  })
})
