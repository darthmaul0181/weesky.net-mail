import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, useLocation, useNavigate } from 'react-router'
import { createTestQueryClient, setupUser, withQueryClient } from '../../test-utils'
import WhatsNewTarget from './WhatsNewTarget'

const mocks = vi.hoisted(() => ({ markWhatsNew: vi.fn(), getWhatsNew: vi.fn() }))
vi.mock('../../api.js', () => ({ api: mocks }))

type FakeObserver = { trigger: (isIntersecting?: boolean) => void }
const observers = () => (IntersectionObserver as unknown as { instances: FakeObserver[] }).instances

// jsdom has no scrollIntoView: installed per test and taken away again, whatever jsdom had there.
const scrollIntoView = vi.fn()
const had = Object.getOwnPropertyDescriptor(Element.prototype, 'scrollIntoView')
beforeEach(() => {
  vi.clearAllMocks()
  mocks.markWhatsNew.mockResolvedValue(null)
  Object.defineProperty(Element.prototype, 'scrollIntoView', { value: scrollIntoView, configurable: true, writable: true })
})
afterEach(() => {
  if (had) Object.defineProperty(Element.prototype, 'scrollIntoView', had)
  else delete (Element.prototype as { scrollIntoView?: unknown }).scrollIntoView
})

/** Show me, as the list sends it: the same page again, carrying the novelty's id. */
function Leader() {
  const navigate = useNavigate()
  const { state } = useLocation() as { state: unknown }
  return (
    <>
      <button type="button" onClick={() => void navigate('/settings/appearance', { state: { whatsNew: 'daily-image' } })}>Show me</button>
      <button type="button" onClick={() => void navigate(-1)}>Back</button>
      <button type="button" onClick={() => void navigate(1)}>Forward</button>
      <output>{JSON.stringify(state)}</output>
    </>
  )
}

function renderTarget(items: object[] = [], state?: { whatsNew: string }) {
  const client = createTestQueryClient()
  client.setQueryData(['appSettings'], { 'app.dailyImage': 'true' })
  client.setQueryData(['whatsNew'], { baseline: '0.0.0', items })
  mocks.getWhatsNew.mockResolvedValue({ baseline: '0.0.0', items: [{ id: 'daily-image', seenAt: 'x' }] })
  return render(
    <MemoryRouter initialEntries={[{ pathname: '/settings/appearance', state }]}>
      <Leader />
      <WhatsNewTarget id="appearance:daily-image" className="account-section"><p>Block</p></WhatsNewTarget>
    </MemoryRouter>,
    { wrapper: withQueryClient(client) },
  )
}

const SEEN = [{ id: 'daily-image', seenAt: '2026-10-01T00:00:00Z' }]

describe('WhatsNewTarget', () => {
  it('frames the block and marks it seen once half of it is on screen', async () => {
    renderTarget()
    expect(screen.getByText('New')).toBeInTheDocument()
    expect(observers()[0]).toMatchObject({ options: { threshold: 0.5 } })
    expect(mocks.markWhatsNew).not.toHaveBeenCalled()
    act(() => observers()[0]!.trigger(false))
    expect(mocks.markWhatsNew).not.toHaveBeenCalled()
    act(() => observers()[0]!.trigger())
    await waitFor(() => expect(mocks.markWhatsNew).toHaveBeenCalledWith(['daily-image'], 'seen'))
  })

  it('keeps the frame for the rest of the visit after the novelty is seen', async () => {
    renderTarget()
    act(() => observers()[0]!.trigger())
    await waitFor(() => expect(mocks.getWhatsNew).toHaveBeenCalled())
    expect(screen.getByText('New')).toBeInTheDocument()
    expect(observers()).toHaveLength(0)
  })

  it('draws nothing around a block already seen', () => {
    renderTarget(SEEN)
    expect(screen.queryByText('New')).toBeNull()
    expect(screen.getByText('Block')).toBeInTheDocument()
    expect(observers()).toHaveLength(0)
  })

  // The frame is the block's own box: it wears the block's class, so the outline hugs the
  // section's 560px rather than the whole content column.
  it('wears the class of the block it frames', () => {
    const { container } = renderTarget()
    expect(container.querySelector('.whats-new-target')).toHaveClass('account-section', 'is-new')
    expect(container.querySelector('.whats-new-target')!.firstElementChild).toHaveTextContent('Block')
  })

  it('scrolls to the block when Show me led here', () => {
    renderTarget([], { whatsNew: 'daily-image' })
    expect(scrollIntoView).toHaveBeenCalledWith({ block: 'center', behavior: 'smooth' })
  })

  it('scrolls without animation when motion is to be reduced', () => {
    // Once: the suite-wide stub answers every other query, and a restore would empty it.
    vi.mocked(window.matchMedia).mockImplementationOnce(query => ({
      matches: query.includes('reduce'), media: query, addEventListener: vi.fn(), removeEventListener: vi.fn(),
    }) as unknown as MediaQueryList)
    renderTarget([], { whatsNew: 'daily-image' })
    expect(scrollIntoView).toHaveBeenCalledWith({ block: 'center', behavior: 'auto' })
  })

  it('stays put when the page was reached on its own', () => {
    renderTarget()
    expect(scrollIntoView).not.toHaveBeenCalled()
  })

  // The history's Show me leads to a novelty already seen: no frame, but still the scroll.
  it('scrolls to a block already seen, without framing it', () => {
    renderTarget(SEEN, { whatsNew: 'daily-image' })
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
    expect(screen.queryByText('New')).toBeNull()
  })

  // The state lives in the history entry: left there, a reload or a Back onto it would centre again.
  it('spends the Show me once it has scrolled', async () => {
    renderTarget(SEEN, { whatsNew: 'daily-image' })
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('null'))
  })

  it('does not scroll again when Back and Forward return to a Show me', async () => {
    const user = setupUser()
    renderTarget(SEEN)
    await user.click(screen.getByRole('button', { name: 'Show me' }))
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
    await user.click(screen.getByRole('button', { name: 'Back' }))
    await user.click(screen.getByRole('button', { name: 'Forward' }))
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
  })

  it('marks a novelty seen once even when the server keeps refusing the stamp', async () => {
    mocks.markWhatsNew.mockImplementation(() => new Promise((_, reject) => setTimeout(() => reject(new Error('offline')), 5)))
    renderTarget()
    mocks.getWhatsNew.mockResolvedValue({ baseline: '0.0.0', items: [] })
    act(() => observers()[0]!.trigger())
    await waitFor(() => expect(mocks.getWhatsNew).toHaveBeenCalled())
    await act(() => new Promise(resolve => setTimeout(resolve, 100)))
    act(() => observers().forEach(observer => observer.trigger()))
    await act(() => new Promise(resolve => setTimeout(resolve, 100)))
    expect(mocks.markWhatsNew).toHaveBeenCalledTimes(1)
  })

  it('scrolls again on a second Show me from the same page', async () => {
    const user = setupUser()
    renderTarget(SEEN)
    await user.click(screen.getByRole('button', { name: 'Show me' }))
    expect(scrollIntoView).toHaveBeenCalledTimes(1)
    await user.click(screen.getByRole('button', { name: 'Show me' }))
    expect(scrollIntoView).toHaveBeenCalledTimes(2)
  })
})
