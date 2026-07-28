import { describe, it, expect } from 'vitest'
import { escapeHtml } from './escapeHtml'

describe('escapeHtml', () => {
  it('escapes the five characters that would read as markup', () => {
    expect(escapeHtml(`a & b "c" <d> 'e'`)).toBe('a &amp; b &quot;c&quot; &lt;d&gt; &#39;e&#39;')
  })

  // The ampersand replaced after the others would escape their own output a second time.
  it('escapes each character exactly once', () => {
    expect(escapeHtml('<img src=x>')).toBe('&lt;img src=x&gt;')
    expect(escapeHtml('&amp;')).toBe('&amp;amp;')
  })

  it('leaves text carrying none of them alone', () => {
    expect(escapeHtml('alice+tag@weesky.be')).toBe('alice+tag@weesky.be')
    expect(escapeHtml('')).toBe('')
  })
})
