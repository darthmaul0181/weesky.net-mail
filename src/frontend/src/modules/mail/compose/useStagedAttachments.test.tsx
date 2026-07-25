import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { useStagedAttachments } from './useStagedAttachments'
import { uploadAttachment, api } from '../../../api.js'

vi.mock('../../../api.js', () => ({
  uploadAttachment: vi.fn(),
  api: { deleteAttachment: vi.fn().mockResolvedValue(null) },
}))

const file = new File(['abcd'], 'a.txt', { type: 'text/plain' })

describe('useStagedAttachments', () => {
  beforeEach(() => vi.clearAllMocks())

  it('uploads on add and stores the returned id', async () => {
    vi.mocked(uploadAttachment).mockResolvedValue({ id: 'id-1', fileName: 'a.txt', size: 4, contentType: 'text/plain' })
    const { result } = renderHook(() => useStagedAttachments())

    await act(async () => { result.current.addFiles([file]) })

    expect(result.current.items[0]).toMatchObject({ id: 'id-1', fileName: 'a.txt', progress: 1, error: null })
    expect(result.current.uploading).toBe(false)
    expect(result.current.ids).toEqual(['id-1'])
  })

  it('reports uploading while a file is in flight', async () => {
    let resolve!: (v: unknown) => void
    vi.mocked(uploadAttachment).mockReturnValue(new Promise(r => { resolve = r }))
    const { result } = renderHook(() => useStagedAttachments())

    act(() => { result.current.addFiles([file]) })
    expect(result.current.uploading).toBe(true)

    await act(async () => { resolve({ id: 'id-1', fileName: 'a.txt', size: 4, contentType: 'text/plain' }) })
    expect(result.current.uploading).toBe(false)
  })

  it('keeps the backend message on a refused file', async () => {
    vi.mocked(uploadAttachment).mockRejectedValue(new Error('The attachment exceeds the 25 MB limit'))
    const { result } = renderHook(() => useStagedAttachments())

    await act(async () => { result.current.addFiles([file]) })

    expect(result.current.items[0].error).toBe('The attachment exceeds the 25 MB limit')
    expect(result.current.ids).toEqual([])
  })

  it('remove deletes server-side and drops the row', async () => {
    vi.mocked(uploadAttachment).mockResolvedValue({ id: 'id-1', fileName: 'a.txt', size: 4, contentType: 'text/plain' })
    const { result } = renderHook(() => useStagedAttachments())
    await act(async () => { result.current.addFiles([file]) })

    await act(async () => { result.current.remove(result.current.items[0].key) })

    expect(api.deleteAttachment).toHaveBeenCalledWith('id-1')
    expect(result.current.items).toHaveLength(0)
  })

  it('discardAll deletes every staged id', async () => {
    vi.mocked(uploadAttachment).mockResolvedValue({ id: 'id-1', fileName: 'a.txt', size: 4, contentType: 'text/plain' })
    const { result } = renderHook(() => useStagedAttachments())
    await act(async () => { result.current.addFiles([file]) })

    act(() => { result.current.discardAll() })

    expect(api.deleteAttachment).toHaveBeenCalledWith('id-1')
  })

  it('remove while the upload is still in flight skips the DELETE', async () => {
    let resolve!: (v: unknown) => void
    vi.mocked(uploadAttachment).mockReturnValue(new Promise(r => { resolve = r }))
    const { result } = renderHook(() => useStagedAttachments())

    act(() => { result.current.addFiles([file]) })
    const key = result.current.items[0].key

    act(() => { result.current.remove(key) })
    expect(result.current.items).toHaveLength(0)
    expect(api.deleteAttachment).not.toHaveBeenCalled()

    await act(async () => { resolve({ id: 'id-1', fileName: 'a.txt', size: 4, contentType: 'text/plain' }) })
    expect(api.deleteAttachment).not.toHaveBeenCalled()
    expect(result.current.items).toHaveLength(0)
  })

  it('seeds already-staged items as completed uploads', () => {
    const { result } = renderHook(() =>
      useStagedAttachments([{ id: 'a1', fileName: 'doc.pdf', size: 9 }]))

    expect(result.current.items).toHaveLength(1)
    expect(result.current.items[0]).toMatchObject({ id: 'a1', fileName: 'doc.pdf', progress: 1, error: null })
    expect(result.current.ids).toEqual(['a1'])
    expect(result.current.uploading).toBe(false)
  })

  it('keeps each row on its own progress when two uploads interleave', async () => {
    let resolveA!: (v: unknown) => void
    let resolveB!: (v: unknown) => void
    const fileB = new File(['xyz'], 'b.txt', { type: 'text/plain' })
    let onProgressA!: (ratio: number) => void
    let onProgressB!: (ratio: number) => void

    const grabProgress = (opts: unknown) => (opts as { onProgress: (r: number) => void }).onProgress
    vi.mocked(uploadAttachment)
      .mockImplementationOnce((_f, opts) => { onProgressA = grabProgress(opts); return new Promise(r => { resolveA = r }) })
      .mockImplementationOnce((_f, opts) => { onProgressB = grabProgress(opts); return new Promise(r => { resolveB = r }) })

    const { result } = renderHook(() => useStagedAttachments())
    act(() => { result.current.addFiles([file, fileB]) })

    act(() => { onProgressA(0.3); onProgressB(0.1); onProgressA(0.6); onProgressB(0.9) })

    expect(result.current.items[0]).toMatchObject({ fileName: 'a.txt', progress: 0.6 })
    expect(result.current.items[1]).toMatchObject({ fileName: 'b.txt', progress: 0.9 })

    await act(async () => {
      resolveA({ id: 'id-a', fileName: 'a.txt', size: 4, contentType: 'text/plain' })
      resolveB({ id: 'id-b', fileName: 'b.txt', size: 3, contentType: 'text/plain' })
    })
    expect(result.current.items[0]).toMatchObject({ id: 'id-a', progress: 1 })
    expect(result.current.items[1]).toMatchObject({ id: 'id-b', progress: 1 })
  })
})
