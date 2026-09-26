import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, renderHook, waitFor } from '@testing-library/react'
import { ApiError, api, uploadAttachment } from '../../../api.js'
import { useInlineUploads } from './useInlineUploads'
import type { EditorHandle } from './SquireEditor'

vi.mock('../../../api.js', async (importOriginal) => ({
  ...await importOriginal<typeof import('../../../api.js')>(),
  uploadAttachment: vi.fn(),
  api: { deleteAttachment: vi.fn(() => Promise.resolve(null)) },
  stagedAttachmentUrl: (id: string) => `url:${id}`,
}))

type Pending = { name: string; signal: AbortSignal; resolve: () => void; reject: (e: unknown) => void }
let pending: Pending[]

beforeEach(() => {
  pending = []
  vi.mocked(api.deleteAttachment).mockClear()
  vi.mocked(uploadAttachment).mockReset()
  vi.mocked(uploadAttachment).mockImplementation((file, options = {}) => new Promise((resolve, reject) => {
    const signal = options.signal!
    if (signal.aborted) { reject(new ApiError('Aborted', 0, null)); return }
    signal.addEventListener('abort', () => reject(new ApiError('Aborted', 0, null)))
    pending.push({
      name: file.name, signal, reject,
      resolve: () => resolve({ id: file.name, fileName: file.name, size: 1, contentType: 'image/png' }),
    })
  }))
})

function setup() {
  const images: string[] = []
  const editor = { insertImage: (src: string) => { images.push(src) } } as unknown as EditorHandle
  const args = {
    accountId: 'primary', plainText: false, editor, addFiles: vi.fn(), addInline: vi.fn(),
    setInsertedInline: vi.fn(), markDirty: vi.fn(), onNotify: vi.fn(),
  }
  const view = renderHook(() => useInlineUploads(args))
  return { ...view, args, images }
}
const files = (...names: string[]) => names.map(n => new File(['x'], n, { type: 'image/png' }))
const upload = (name: string) => pending.find(p => p.name === name)!

describe('useInlineUploads', () => {
  it('uploads a few at a time and inserts in paste order', async () => {
    const { result, images } = setup()
    act(() => result.current.routeFiles(files('a', 'b', 'c', 'd')))
    expect(pending.map(p => p.name)).toEqual(['a', 'b', 'c'])
    expect(result.current.inlineUploads).toBe(4)

    await act(async () => { upload('c').resolve() })
    expect(pending.map(p => p.name)).toEqual(['a', 'b', 'c', 'd'])
    await act(async () => { upload('d').resolve(); upload('b').resolve() })
    expect(images).toEqual([])

    await act(async () => { upload('a').resolve() })
    expect(images).toEqual(['url:a', 'url:b', 'url:c', 'url:d'])
    expect(result.current.inlineUploads).toBe(0)
  })

  it('shares the limit between two pastes', async () => {
    const { result } = setup()
    act(() => result.current.routeFiles(files('a', 'b')))
    act(() => result.current.routeFiles(files('c', 'd')))
    expect(pending.map(p => p.name)).toEqual(['a', 'b', 'c'])
    expect(result.current.inlineUploads).toBe(4)
  })

  it('keeps paste order across pastes whose uploads finish out of order', async () => {
    const { result, images } = setup()
    act(() => result.current.routeFiles(files('a')))
    act(() => result.current.routeFiles(files('b')))
    await act(async () => { upload('b').resolve() })
    expect(images).toEqual([])

    await act(async () => { upload('a').resolve() })
    expect(images).toEqual(['url:a', 'url:b'])
    expect(result.current.inlineUploads).toBe(0)
  })

  it('does not let a failed image of an earlier paste block a later one', async () => {
    const { result, images, args } = setup()
    act(() => result.current.routeFiles(files('a')))
    act(() => result.current.routeFiles(files('b')))
    await act(async () => { upload('b').resolve(); upload('a').reject(new Error('Too large')) })

    expect(images).toEqual(['url:b'])
    expect(args.onNotify).toHaveBeenCalledOnce()
    expect(result.current.inlineUploads).toBe(0)
  })

  it('toasts a failed image and still inserts the others', async () => {
    const { result, images, args } = setup()
    act(() => result.current.routeFiles(files('a', 'b', 'c')))
    await act(async () => { upload('b').reject(new Error('Too large')); upload('a').resolve(); upload('c').resolve() })

    expect(images).toEqual(['url:a', 'url:c'])
    expect(args.onNotify).toHaveBeenCalledExactlyOnceWith('Could not insert the image', 'error')
    expect(result.current.inlineUploads).toBe(0)
  })

  it('aborts in-flight and queued uploads on unmount, touching nothing after', async () => {
    const { result, unmount, images, args } = setup()
    act(() => result.current.routeFiles(files('a', 'b', 'c', 'd', 'e')))
    await act(async () => { upload('b').resolve() })
    expect(pending.map(p => p.name)).toEqual(['a', 'b', 'c', 'd'])
    unmount()

    await waitFor(() => expect(pending.every(p => p.signal.aborted)).toBe(true))
    await act(async () => { await Promise.resolve() })
    expect(pending).toHaveLength(4)
    expect(images).toEqual([])
    expect(args.onNotify).not.toHaveBeenCalled()
    expect(args.addInline).not.toHaveBeenCalled()
    expect(args.setInsertedInline).not.toHaveBeenCalled()
    expect(api.deleteAttachment).toHaveBeenCalledExactlyOnceWith('b', { accountId: 'primary' })
  })
})
