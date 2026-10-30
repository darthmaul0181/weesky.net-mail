import { act, render } from '@testing-library/react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import SenderAvatar from './SenderAvatarView'
import { useSenderAvatarUrl } from './useSenderAvatarUrl'

vi.mock('./useSenderAvatarUrl', () => ({ useSenderAvatarUrl: vi.fn() }))

const hook = vi.mocked(useSenderAvatarUrl)

beforeEach(() => { hook.mockReset().mockReturnValue(null) })

function box(container: HTMLElement) {
  return container.querySelector('.message-row-avatar') as HTMLElement
}

describe('SenderAvatar', () => {
  it('shows the initials at rest, hidden from assistive technology', () => {
    const { container } = render(<SenderAvatar name="Alice Martin" address="a@x.be" color="mono" />)
    expect(box(container)).toHaveTextContent('AM')
    expect(box(container)).toHaveAttribute('aria-hidden', 'true')
    expect(container.querySelector('img')).toBeNull()
  })

  it('replaces the initials with the picture when one exists', () => {
    hook.mockReturnValue('blob:photo')
    const { container } = render(<SenderAvatar name="Alice Martin" address="a@x.be" color="sender" />)
    expect(box(container)).toHaveClass('is-photo')
    expect(container.querySelector('img')).toHaveAttribute('src', 'blob:photo')
    expect(container.querySelector('img')).toHaveAttribute('alt', '')
    expect(box(container)).not.toHaveTextContent('AM')
  })

  it('follows the colour setting and sets the hue only in sender mode', () => {
    const mono = render(<SenderAvatar name="Alice" address="a@x.be" color="mono" />)
    expect(box(mono.container)).toHaveClass('is-mono')
    expect(box(mono.container).style.getPropertyValue('--avatar-h')).toBe('')
    const sender = render(<SenderAvatar name="Alice" address="a@x.be" color="sender" />)
    expect(box(sender.container)).toHaveClass('is-sender')
    expect(box(sender.container).style.getPropertyValue('--avatar-h')).toMatch(/^\d+$/)
  })

  it('requests nothing until the box nears the list scroller', () => {
    const { container } = render(<div className="mail-list-scroll"><SenderAvatar address="a@x.be" color="mono" /></div>)
    expect(hook).toHaveBeenLastCalledWith('a@x.be', false)
    const observer = IntersectionObserver as unknown as { instances: Array<{ trigger(): void; options: IntersectionObserverInit }> }
    expect(observer.instances[0]!.options.rootMargin).toBe('1700px 0px')
    expect(observer.instances[0]!.options.root).toBe(container.firstChild)
    act(() => observer.instances[0]!.trigger())
    expect(hook).toHaveBeenLastCalledWith('a@x.be', true)
  })
})
