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

  // Staged files are namespaced by account on the backend. An upload filed under the primary
  // while the send reads the connected account loses the attachment with no error anywhere.
  it('stages and releases under the account it was given', async () => {
    vi.mocked(uploadAttachment).mockResolvedValue({ id: 'id-1', fileName: 'a.txt', size: 4, contentType: 'text/plain' })
    const { result } = renderHook(() => useStagedAttachments('linked-1'))

    await act(async () => { result.current.addFiles([file]) })

    expect(vi.mocked(uploadAttachment).mock.calls[0][1]).toMatchObject({ accountId: 'linked-1' })

    act(() => { result.current.remove(result.current.items[0].key) })
    expect(api.deleteAttachment).toHaveBeenCalledWith('id-1', { accountId: 'linked-1' })
  })

  // A file is released from the mailbox that holds it, not from whatever the hook is rendered
  // under later: releasing A's ids against B leaves A's files to the TTL sweeper, in silence.
  it('releases a staged file under the account it was staged with, not the current one', async () => {
    vi.mocked(uploadAttachment).mockResolvedValue({ id: 'id-1', fileName: 'a.txt', size: 4, contentType: 'text/plain' })
    const { result, rerender } = renderHook(
      ({ account }) => useStagedAttachments(account, [], ['inline-1']),
      { initialProps: { account: 'linked-1' } })

    await act(async () => { result.current.addFiles([file]) })
    rerender({ account: 'primary' })

    act(() => { result.current.discardAll() })

    expect(api.deleteAttachment).toHaveBeenCalledWith('id-1', { accountId: 'linked-1' })
    expect(api.deleteAttachment).toHaveBeenCalledWith('inline-1', { accountId: 'linked-1' })
    expect(api.deleteAttachment).not.toHaveBeenCalledWith('id-1', { accountId: 'primary' })
  })

  it('uploads on add and stores the returned id', async () => {
    vi.mocked(uploadAttachment).mockResolvedValue({ id: 'id-1', fileName: 'a.txt', size: 4, contentType: 'text/plain' })
    const { result } = renderHook(() => useStagedAttachments('primary'))

    await act(async () => { result.current.addFiles([file]) })

    expect(result.current.items[0]).toMatchObject({ id: 'id-1', fileName: 'a.txt', progress: 1, error: null })
    expect(result.current.uploading).toBe(false)
    expect(result.current.ids).toEqual(['id-1'])
  })

  it('reports uploading while a file is in flight', async () => {
    let resolve!: (v: unknown) => void
    vi.mocked(uploadAttachment).mockReturnValue(new Promise(r => { resolve = r }))
    const { result } = renderHook(() => useStagedAttachments('primary'))

    act(() => { result.current.addFiles([file]) })
    expect(result.current.uploading).toBe(true)

    await act(async () => { resolve({ id: 'id-1', fileName: 'a.txt', size: 4, contentType: 'text/plain' }) })
    expect(result.current.uploading).toBe(false)
  })

  it('keeps the backend message on a refused file', async () => {
    vi.mocked(uploadAttachment).mockRejectedValue(new Error('The attachment exceeds the 25 MB limit'))
    const { result } = renderHook(() => useStagedAttachments('primary'))

    await act(async () => { result.current.addFiles([file]) })

    expect(result.current.items[0].error).toBe('The attachment exceeds the 25 MB limit')
    expect(result.current.ids).toEqual([])
  })

  it('remove deletes server-side and drops the row', async () => {
    vi.mocked(uploadAttachment).mockResolvedValue({ id: 'id-1', fileName: 'a.txt', size: 4, contentType: 'text/plain' })
    const { result } = renderHook(() => useStagedAttachments('primary'))
    await act(async () => { result.current.addFiles([file]) })

    await act(async () => { result.current.remove(result.current.items[0].key) })

    expect(api.deleteAttachment).toHaveBeenCalledWith('id-1', { accountId: 'primary' })
    expect(result.current.items).toHaveLength(0)
  })

  it('discardAll deletes every staged id', async () => {
    vi.mocked(uploadAttachment).mockResolvedValue({ id: 'id-1', fileName: 'a.txt', size: 4, contentType: 'text/plain' })
    const { result } = renderHook(() => useStagedAttachments('primary'))
    await act(async () => { result.current.addFiles([file]) })

    act(() => { result.current.discardAll() })

    expect(api.deleteAttachment).toHaveBeenCalledWith('id-1', { accountId: 'primary' })
  })

  // The inline parts live in the body rather than the tray, so nothing else would release them.
  it('discardAll deletes the inline ids too', () => {
    const { result } = renderHook(() => useStagedAttachments('primary', [], ['i1']))

    act(() => { result.current.discardAll() })

    expect(api.deleteAttachment).toHaveBeenCalledWith('i1', { accountId: 'primary' })
  })

  it('remove while the upload is still in flight skips the DELETE', async () => {
    let resolve!: (v: unknown) => void
    vi.mocked(uploadAttachment).mockReturnValue(new Promise(r => { resolve = r }))
    const { result } = renderHook(() => useStagedAttachments('primary'))

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
      useStagedAttachments('primary', [{ id: 'a1', fileName: 'doc.pdf', size: 9 }]))

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

    const { result } = renderHook(() => useStagedAttachments('primary'))
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

  it('moves the inline ids into the tray, once', () => {
    const { result } = renderHook(() => useStagedAttachments('primary', [], ['inline-1']))

    act(() => { result.current.adoptInline([{ id: 'inline-1', fileName: 'logo.png', size: 3 }]) })

    expect(result.current.items).toHaveLength(1)
    expect(result.current.items[0]).toMatchObject({ id: 'inline-1', fileName: 'logo.png', size: 3 })
    expect(result.current.ids).toEqual(['inline-1'])

    act(() => { result.current.adoptInline([{ id: 'inline-1', fileName: 'logo.png', size: 3 }]) })
    expect(result.current.items).toHaveLength(1)
  })

  // Adopted means owned by the tray: discarding must release it once, not twice.
  it('releases an adopted id a single time', () => {
    const { result } = renderHook(() => useStagedAttachments('primary', [], ['inline-1']))

    act(() => { result.current.adoptInline([{ id: 'inline-1', fileName: 'logo.png', size: 3 }]) })
    act(() => { result.current.discardAll() })

    expect(api.deleteAttachment).toHaveBeenCalledTimes(1)
  })
})
