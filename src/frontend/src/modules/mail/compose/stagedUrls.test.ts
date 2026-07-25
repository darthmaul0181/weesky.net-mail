import { describe, expect, it } from 'vitest'
import { API_BASE, stagedAttachmentUrl } from '../../../api.js'
import { absolutizeStagedUrls, relativizeStagedUrls } from './stagedUrls'

const html = (src: string) => `<p>hi</p><img src="${src}"><img src="/api/Mail/Attachments/other/content">`

describe('staged URLs in a quoted body', () => {
  it('absolutizes only the srcs whose id was staged with the quote', () => {
    const result = absolutizeStagedUrls(html('/api/Mail/Attachments/i1/content'), ['i1'])

    expect(result).toContain(stagedAttachmentUrl('i1'))
    expect(result).toContain('src="/api/Mail/Attachments/other/content"')
  })

  it('relativizes every staged URL back, whatever its id', () => {
    const result = relativizeStagedUrls(html(stagedAttachmentUrl('i1')))

    expect(result).toContain('src="/api/Mail/Attachments/i1/content"')
    expect(result).not.toContain(API_BASE)
  })

  it('leaves a body carrying no staged URL alone', () => {
    expect(relativizeStagedUrls('<p>hi</p>')).toBe('<p>hi</p>')
    expect(absolutizeStagedUrls('<p>hi</p>', ['i1'])).toBe('<p>hi</p>')
  })
})
