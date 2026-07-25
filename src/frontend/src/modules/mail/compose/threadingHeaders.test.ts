import { describe, expect, it } from 'vitest'
import { threadingHeaders } from './threadingHeaders'

describe('threadingHeaders', () => {
  it('extends the references chain with the message id', () => {
    expect(threadingHeaders({ messageId: 'b@x', references: ['a@x'] }))
      .toEqual({ inReplyTo: 'b@x', references: ['a@x', 'b@x'] })
  })

  it('does not duplicate an id already in the chain', () => {
    expect(threadingHeaders({ messageId: 'a@x', references: ['a@x'] }))
      .toEqual({ inReplyTo: 'a@x', references: ['a@x'] })
  })

  it('leaves the chain alone when the original has no id', () => {
    expect(threadingHeaders({ messageId: null, references: ['a@x'] }))
      .toEqual({ inReplyTo: null, references: ['a@x'] })
  })
})
