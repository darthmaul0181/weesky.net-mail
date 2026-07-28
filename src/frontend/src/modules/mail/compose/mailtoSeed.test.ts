import { describe, it, expect } from 'vitest'
import { mailtoSeedFrom } from './mailtoSeed'

describe('mailtoSeedFrom', () => {
  it('answers null without a mailto parameter', () => {
    expect(mailtoSeedFrom('')).toBeNull()
    expect(mailtoSeedFrom('?folder=INBOX')).toBeNull()
  })

  it('answers null on anything that is not a mailto url', () => {
    expect(mailtoSeedFrom('?mailto=https%3A%2F%2Fexample.com')).toBeNull()
    expect(mailtoSeedFrom('?mailto=not%20a%20url')).toBeNull()
    // The scheme is the whole gate, and javascript: is what walks up to it.
    expect(mailtoSeedFrom('?mailto=javascript%3Aalert(1)')).toBeNull()
    expect(mailtoSeedFrom('?mailto=data%3Atext%2Fhtml%2C%3Cscript%3E')).toBeNull()
  })

  it('takes the recipient from the path', () => {
    const seed = mailtoSeedFrom('?mailto=mailto%3Aalice%40weesky.be')!

    expect(seed.to).toEqual(['alice@weesky.be'])
    expect(seed.action).toBe('editAsNew')
  })

  it('reads to, cc, bcc and subject', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent(
        'mailto:alice@weesky.be?cc=bob@weesky.be&bcc=carol@weesky.be&subject=Hello there'))!

    expect(seed.to).toEqual(['alice@weesky.be'])
    expect(seed.cc).toEqual(['bob@weesky.be'])
    expect(seed.bcc).toEqual(['carol@weesky.be'])
    expect(seed.subject).toBe('Hello there')
  })

  // RFC 6068 lets the recipient arrive as a to header instead of in the path, and an empty path
  // in front of it is the shape a link generator produces. Without this the header is read by
  // nobody: every other case here fills To from the path alone.
  it('reads a recipient the link carries as a to header', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent('mailto:?to=alice@weesky.be'))!

    expect(seed.to).toEqual(['alice@weesky.be'])
  })

  // An hfield is an RFC 3986 query component, where + is a literal, not the form encoding where it
  // means a space. Read the form way, a plus-addressed recipient becomes "alice tag@weesky.be",
  // fails the address gate and is dropped — the composer opens a recipient short, silently.
  it('keeps a plus in a to, cc or bcc header', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent(
        'mailto:?to=alice+tag@weesky.be&cc=bob+news@weesky.be&bcc=carol+list@weesky.be'))!

    expect(seed.to).toEqual(['alice+tag@weesky.be'])
    expect(seed.cc).toEqual(['bob+news@weesky.be'])
    expect(seed.bcc).toEqual(['carol+list@weesky.be'])
  })

  // An hfname is an RFC 5322 header name, so it is case-insensitive. Matched case-sensitively,
  // ?Subject= and ?CC= were dropped in silence and the composer opened visibly incomplete.
  it('reads a header whatever the case it is spelled in', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent(
        'mailto:?TO=alice@weesky.be&CC=bob@weesky.be&Bcc=carol@weesky.be'
        + '&Subject=Hello there&BODY=hi'))!

    expect(seed.to).toEqual(['alice@weesky.be'])
    expect(seed.cc).toEqual(['bob@weesky.be'])
    expect(seed.bcc).toEqual(['carol@weesky.be'])
    expect(seed.subject).toBe('Hello there')
    expect(seed.html).toBe('<div>hi</div>')
  })

  // Folding runs before the first-occurrence rule, so two spellings of one header are one field
  // and the leftmost wins — the same answer two identical spellings would give.
  it('keeps the leftmost of two spellings of the same header', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent('mailto:alice@weesky.be?subject=First&Subject=Second'))!

    expect(seed.subject).toBe('First')
  })

  it('keeps a plus in the subject and in the body', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent('mailto:alice@weesky.be?subject=A + B&body=1 + 1 = 2'))!

    expect(seed.subject).toBe('A + B')
    expect(seed.html).toBe('<div>1 + 1 = 2</div>')
  })

  // Decoding first would turn the escape into the separator it was escaped to stop being.
  it('splits an address list before decoding what it holds', () => {
    const seed = mailtoSeedFrom('?mailto=' + encodeURIComponent('mailto:a%2Cb@weesky.be'))!

    expect(seed.to).toEqual(['a,b@weesky.be'])
  })

  it('accepts several comma-separated recipients', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent('mailto:alice@weesky.be,bob@weesky.be'))!

    expect(seed.to).toEqual(['alice@weesky.be', 'bob@weesky.be'])
  })

  // The link comes from the operating system, so from the outside world.
  it('drops an address that is not one', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent('mailto:alice@weesky.be,rubbish'))!

    expect(seed.to).toEqual(['alice@weesky.be'])
  })

  // In every field, not only To: the same gate has to stand on the headers a link chooses freely.
  it('drops an address that is not one from cc and bcc too', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent(
        'mailto:alice@weesky.be?cc=rubbish,bob@weesky.be&bcc=also rubbish'))!

    expect(seed.cc).toEqual(['bob@weesky.be'])
    expect(seed.bcc).toEqual([])
  })

  it('escapes the body instead of trusting it as html', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent('mailto:alice@weesky.be?body=<img src=x onerror=alert(1)>'))!

    expect(seed.html).not.toContain('<img')
    expect(seed.html).toContain('&lt;img')
  })

  // The body is percent-encoded on its own here because an & inside it would otherwise end the
  // header. It also pins the order the escaping runs in: & escaped after < would answer
  // &amp;lt;d&amp;gt; and hand the editor an entity nobody wrote.
  it('escapes every html-significant character exactly once', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent(
        'mailto:alice@weesky.be?body=' + encodeURIComponent(`a & b "c" <d> 'e'`)))!

    expect(seed.html).toBe('<div>a &amp; b &quot;c&quot; &lt;d&gt; &#39;e&#39;</div>')
  })

  // %0A rather than a real line break: the URL parser strips tabs and newlines while parsing, so
  // a raw character here would be gone before the parser ever saw it and the test would prove
  // nothing.
  it('turns the body newlines into line breaks', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent('mailto:alice@weesky.be?body=one%0Atwo'))!

    expect(seed.html).toContain('one<br>two')
  })

  it('leaves the body empty when the link carries none', () => {
    expect(mailtoSeedFrom('?mailto=mailto%3Aalice%40weesky.be')!.html).toBe('')
  })

  // A mailto link may name any RFC 822 header. Honouring one past the five read here would let a
  // link choose the sender, or point an attach header at a file on the machine.
  it('ignores every header outside the five it reads', () => {
    const seed = mailtoSeedFrom(
      '?mailto=' + encodeURIComponent(
        'mailto:alice@weesky.be?from=evil@x.example&attach=/etc/passwd&in-reply-to=<m@x>'))!

    expect(seed.fromAddress).toBeNull()
    expect(seed.attachments).toEqual([])
    expect(seed.inReplyTo).toBeNull()
  })

  it('carries nothing a reply would carry', () => {
    const seed = mailtoSeedFrom('?mailto=mailto%3Aalice%40weesky.be')!

    expect(seed.attachments).toEqual([])
    expect(seed.inReplyTo).toBeNull()
    expect(seed.references).toEqual([])
    expect(seed.draftRef).toBeNull()
    expect(seed.fromAddress).toBeNull()
  })
})
