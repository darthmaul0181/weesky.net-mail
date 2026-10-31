import { describe, expect, it } from 'vitest'
import type { TFunction } from 'i18next'
import { myAnswerOf } from './myAnswer'

const t = ((key: string) => key) as unknown as TFunction<'calendar'>

describe('myAnswerOf', () => {
  it('words the four answers, whatever the case', () => {
    expect(myAnswerOf('ACCEPTED', t)).toBe('myAnswer.accepted')
    expect(myAnswerOf('tentative', t)).toBe('myAnswer.tentative')
    expect(myAnswerOf('DECLINED', t)).toBe('myAnswer.declined')
    expect(myAnswerOf('NEEDS-ACTION', t)).toBe('myAnswer.pending')
  })

  it('says nothing for an absent or unknown value', () => {
    expect(myAnswerOf(undefined, t)).toBeNull()
    expect(myAnswerOf('DELEGATED', t)).toBeNull()
  })
})
