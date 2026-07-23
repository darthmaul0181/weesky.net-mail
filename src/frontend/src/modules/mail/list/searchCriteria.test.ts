import { describe, it, expect } from 'vitest'
import { criteriaFromForm, daysSinceYearStart, isEmptyCriteria, labelOf } from './searchCriteria'
import type { AdvancedForm } from './searchCriteria'

const blankForm: AdvancedForm = {
  from: '', to: '', subject: '', text: '',
  sinceDays: null, unread: false, flagged: false, hasAttachment: false, allFolders: false,
}

describe('isEmptyCriteria', () => {
  it('is true when only folderPath and allFolders are set', () => {
    expect(isEmptyCriteria({ folderPath: 'INBOX', allFolders: true })).toBe(true)
  })
  it('is false for any text field, date or flag', () => {
    expect(isEmptyCriteria({ folderPath: 'INBOX', allFolders: false, quick: 'x' })).toBe(false)
    expect(isEmptyCriteria({ folderPath: 'INBOX', allFolders: false, sinceDays: 7 })).toBe(false)
    expect(isEmptyCriteria({ folderPath: 'INBOX', allFolders: false, hasAttachment: true })).toBe(false)
  })
  it('ignores whitespace-only text', () => {
    expect(isEmptyCriteria({ folderPath: 'INBOX', allFolders: false, quick: '  ' })).toBe(true)
  })
})

describe('labelOf', () => {
  it('prefers the quick text', () => {
    expect(labelOf({ folderPath: 'F', allFolders: false, quick: 'facture', subject: 'x' })).toBe('facture')
  })
  it('falls back through subject, text, from, to', () => {
    expect(labelOf({ folderPath: 'F', allFolders: false, text: 'body' })).toBe('body')
    expect(labelOf({ folderPath: 'F', allFolders: false, from: 'alice' })).toBe('alice')
  })
  it('is null for a checkbox-only search', () => {
    expect(labelOf({ folderPath: 'F', allFolders: false, unread: true })).toBeNull()
  })
})

describe('criteriaFromForm', () => {
  it('trims fields and drops the empty ones', () => {
    const criteria = criteriaFromForm('INBOX', { ...blankForm, from: ' alice ', unread: true })
    expect(criteria).toEqual({ folderPath: 'INBOX', allFolders: false, from: 'alice', unread: true })
  })
  it('returns null when nothing is filled', () => {
    expect(criteriaFromForm('INBOX', blankForm)).toBeNull()
    expect(criteriaFromForm('INBOX', { ...blankForm, allFolders: true })).toBeNull()
  })
  it('carries scope and date', () => {
    const criteria = criteriaFromForm('INBOX', { ...blankForm, subject: 'x', sinceDays: 30, allFolders: true })
    expect(criteria).toEqual({ folderPath: 'INBOX', allFolders: true, subject: 'x', sinceDays: 30 })
  })
})

describe('daysSinceYearStart', () => {
  it('counts days since January 1st, minimum 1', () => {
    expect(daysSinceYearStart(new Date(2026, 0, 1))).toBe(1)
    // 2026 is not a leap year: 31+28+31+30+31+30 = 181 days through June, +23 = 204.
    expect(daysSinceYearStart(new Date(2026, 6, 23))).toBe(204)
  })

  // Computed via Date.UTC on local Y/M/D, so a DST transition between Jan 1 and `now`
  // must not shave off an hour and swallow a day — this must hold under any host TZ.
  it('is timezone-independent', () => {
    const now = new Date(2026, 6, 23)
    const calendarDayOfYear = Math.round(
      (Date.UTC(now.getFullYear(), now.getMonth(), now.getDate())
        - Date.UTC(now.getFullYear(), 0, 1)) / 86_400_000,
    ) + 1
    expect(daysSinceYearStart(now)).toBe(calendarDayOfYear)
  })
})
