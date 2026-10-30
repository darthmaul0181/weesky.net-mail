import { act, render } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useInView } from './useInView'

const FakeIntersectionObserver = IntersectionObserver as unknown as {
  instances: Array<{ trigger(isIntersecting?: boolean): void; options: IntersectionObserverInit }>
}

function Probe({ root }: { root?: string }) {
  const [ref, inView] = useInView<HTMLDivElement>('100px 0px', root)
  return <div ref={ref} data-testid="probe">{inView ? 'in' : 'out'}</div>
}

afterEach(() => { vi.unstubAllGlobals() })

describe('useInView', () => {
  it('observes against the scrolling ancestor the selector names', () => {
    const { getByTestId } = render(<div className="scroller"><Probe root=".scroller" /></div>)
    const options = FakeIntersectionObserver.instances[0]!.options
    expect(options.root).toBe(getByTestId('probe').parentElement)
    expect(options.rootMargin).toBe('100px 0px')
  })

  it('falls back to the viewport when no selector is given', () => {
    render(<Probe />)
    expect(FakeIntersectionObserver.instances[0]!.options.root).toBeNull()
  })

  it('turns true on the first hit and stops observing', () => {
    const { getByTestId } = render(<Probe />)
    act(() => FakeIntersectionObserver.instances[0]!.trigger(false))
    expect(getByTestId('probe')).toHaveTextContent('out')
    act(() => FakeIntersectionObserver.instances[0]!.trigger())
    expect(getByTestId('probe')).toHaveTextContent('in')
    expect(FakeIntersectionObserver.instances).toHaveLength(0)
  })

  it('disconnects when it unmounts', () => {
    const { unmount } = render(<Probe />)
    unmount()
    expect(FakeIntersectionObserver.instances).toHaveLength(0)
  })

  it('reads as in view where IntersectionObserver does not exist', () => {
    vi.stubGlobal('IntersectionObserver', undefined)
    const { getByTestId } = render(<Probe />)
    expect(getByTestId('probe')).toHaveTextContent('in')
  })
})
