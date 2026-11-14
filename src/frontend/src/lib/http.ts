import type { StagedAttachmentInfo } from '../modules/mail/api/mailTypes'
import { readStored, removeStored, writeStored } from './safeStorage'
import { anySignal, withTimeout } from './withTimeout'
import { configuredApiBase } from './runtimeConfig'

/** The API origin. Exported so the composer can undo an absolute staged URL before sending. */
export const API_BASE: string = configuredApiBase
const SESSION_KEY = 'sessionActive'

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

export interface RequestOptions {
  /** A connected account's id; 'primary', null or absent all mean the session's own mailbox. */
  accountId?: string | null
  signal?: AbortSignal
  /** A read's wait for the response headers; writes have no deadline, as one cut may have landed. */
  timeoutMs?: number
  /** Lets a write outlive the page: a move sent while the tab closes. */
  keepalive?: boolean
}

/** A frozen backend otherwise spins forever. The body is not timed: a big download may be slow. */
const READ_TIMEOUT_MS = 30_000
/** The backend builds a whole file (an IMAP part, an export, a message source) before its first byte. */
const LARGE_READ_TIMEOUT_MS = 120_000
export const largeRead = (options?: RequestOptions): RequestOptions =>
  ({ ...options, timeoutMs: options?.timeoutMs ?? LARGE_READ_TIMEOUT_MS })

export interface UploadOptions extends RequestOptions {
  onProgress?: (fraction: number) => void
  inline?: boolean
}

let unauthorizedHandler: (() => void) | null = null

export function markLoggedIn(): void {
  writeStored(SESSION_KEY, '1')
}

export function clearSession(): void {
  removeStored(SESSION_KEY)
}

export function hasSession(): boolean {
  return readStored(SESSION_KEY) === '1'
}

export function setUnauthorizedHandler(fn: (() => void) | null): void {
  unauthorizedHandler = fn
}

/** An HTTP failure that keeps its status. The backend's stable ResultEnveloppe message
 * ("credentials_unavailable", "Message not found") is `code`, so callers branch without matching
 * prose. Extends Error, so `rejects.toThrow(message)` still holds. */
export class ApiError extends Error {
  status: number
  code: string | null

  constructor(message: string, status: number, code: string | null) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.code = code
  }
}

/** A rejection handler turning a 404 into `null`: the resource has no image, which is an answer. */
export function nullOn404(error: unknown): null {
  if (error instanceof ApiError && error.status === 404) return null
  throw error
}

interface ErrorEnvelope { message: string; code: string | null }

/** Our envelope carries `message` (`Message` from an older writer); ASP.NET's ProblemDetails, `title`. */
interface ErrorBody { message?: unknown; Message?: unknown; title?: unknown }

// Shared by readError (fetch) and uploadAttachment (XHR) — same envelope shape, different
// transport for the raw text.
function parseErrorEnvelope(text: string, fallbackMessage: string): ErrorEnvelope {
  if (!text) return { message: fallbackMessage, code: null }
  try {
    const parsed: unknown = JSON.parse(text)
    const body: ErrorBody = typeof parsed === 'object' && parsed !== null ? parsed : {}
    const message = body.message ?? body.Message
    if (typeof message === 'string') return { message, code: message }

    // A model-validation refusal answers ProblemDetails, not our envelope. Its title is the
    // only readable line in it, and it carries no stable code — falling through to the raw
    // text printed the whole JSON blob on screen.
    return { message: typeof body.title === 'string' ? body.title : fallbackMessage, code: null }
  } catch {
    return { message: text, code: null }
  }
}

async function readError(res: Response): Promise<ErrorEnvelope> {
  // Defensive: some callers stub a response with no body reader at all.
  if (typeof res.text !== 'function') return { message: res.statusText ?? '', code: null }

  const text = await res.text().catch(() => '')
  return parseErrorEnvelope(text, res.statusText ?? '')
}

// 'primary' means the same as no id at all — the backend defaults there — so only a genuinely
// connected account changes what travels over the wire. The one place this is decided.
export function carriesAccount(accountId: string | null | undefined): accountId is string {
  return Boolean(accountId) && accountId !== 'primary'
}

function send(path: string, init: RequestInit, { signal, timeoutMs, keepalive }: RequestOptions): Promise<Response> {
  if (init.method !== 'GET') return fetch(`${API_BASE}${path}`, { ...init, signal, keepalive })
  return withTimeout(deadline => fetch(`${API_BASE}${path}`, {
    ...init, signal: signal ? anySignal([signal, deadline]) : deadline,
  }), timeoutMs ?? READ_TIMEOUT_MS)
}

/** `T` is the DTO the route answers; a route that can answer 204 names `| null` in it. */
export async function request<T>(
  method: HttpMethod, path: string, body?: unknown, options: RequestOptions = {},
): Promise<T> {
  // FormData carries its own multipart boundary; naming a content type here breaks the parse on
  // the server side.
  const isForm = typeof FormData !== 'undefined' && body instanceof FormData
  const headers: Record<string, string> = {}
  if (body && !isForm) headers['Content-Type'] = 'application/json'
  if (carriesAccount(options.accountId)) headers['X-Account-Id'] = options.accountId

  const res = await send(path, {
    method,
    headers,
    credentials: 'include',
    body: body ? (isForm ? body : JSON.stringify(body)) : undefined,
  }, options)

  if (res.status === 401) {
    const { code } = await readError(res)
    clearSession()
    unauthorizedHandler?.()
    throw new ApiError('Unauthorized', 401, code)
  }

  // A 204 has no body: the routes that answer one declare `| null` in T, which this stands for.
  if (res.status === 204) return null as T

  if (!res.ok) {
    const { message, code } = await readError(res)
    throw new ApiError(message || res.statusText, res.status, code)
  }

  // The wire contract: the body is the DTO the route declares, and nothing checks it here.
  const payload: unknown = await res.json()
  return payload as T
}

/** Fetches a binary response (attachments), since request() always parses JSON. */
export async function requestBlob(
  path: string, options: RequestOptions = {},
): Promise<{ blob: Blob; fileName: string }> {
  const headers: Record<string, string> = {}
  if (carriesAccount(options.accountId)) headers['X-Account-Id'] = options.accountId

  const res = await send(path, { method: 'GET', headers, credentials: 'include' }, largeRead(options))

  if (res.status === 401) {
    clearSession()
    unauthorizedHandler?.()
    throw new ApiError('Unauthorized', 401, null)
  }

  if (!res.ok) {
    const { message, code } = await readError(res)
    throw new ApiError(message || res.statusText, res.status, code)
  }

  const disposition = res.headers?.get?.('content-disposition') ?? ''
  // filename* first: the plain one is ASP.NET's ASCII fallback, every accent an underscore. Only
  // the starred one is percent-encoded; decoding the plain one throws on a literal "100%".
  const star = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1]
  const plain = /filename="?([^";]+)"?/i.exec(disposition)?.[1]

  return { blob: await res.blob(), fileName: star ? decodeURIComponent(star) : plain ?? 'attachment' }
}


/** The attachment download URL, encoded in one place. A subresource fetch cannot carry a header,
 * so a connected account rides along as `?account=`. */
export function mailAttachmentUrl(folder: string, uid: number, part: string, accountId?: string | null): string {
  const account = carriesAccount(accountId) ? `&account=${encodeURIComponent(accountId)}` : ''
  return `/api/Mail/Messages/Attachment?folder=${encodeURIComponent(folder)}&uid=${uid}&part=${encodeURIComponent(part)}${account}`
}

/** A staged attachment's content URL, the src of the composer's inline images. Absolute, unlike
 * mailAttachmentUrl, or an <img> resolves it against the SPA's origin. Staged files are namespaced
 * by account, so a connected account carries `?account=` or its inline images 404. */
export function stagedAttachmentUrl(id: string, accountId?: string | null): string {
  const account = carriesAccount(accountId) ? `?account=${encodeURIComponent(accountId)}` : ''
  return `${API_BASE}/api/Mail/Attachments/${id}/content${account}`
}

/** Uploads one outgoing attachment. XHR, not fetch: only XHR exposes upload progress, and a
 * 25 MB file without a bar reads as a hang. */
export function uploadAttachment(
  file: File, { onProgress, signal, accountId, inline }: UploadOptions = {},
): Promise<StagedAttachmentInfo> {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', `${API_BASE}/api/Mail/Attachments`)
    xhr.withCredentials = true
    if (carriesAccount(accountId)) xhr.setRequestHeader('X-Account-Id', accountId)
    xhr.upload.onprogress = (event) => {
      if (event.lengthComputable) onProgress?.(event.loaded / event.total)
    }

    const rejectAborted = () => { detachAbort(); reject(new ApiError('Aborted', 0, null)) }
    const onAbort = () => { xhr.abort(); rejectAborted() }
    const detachAbort = () => signal?.removeEventListener('abort', onAbort)

    xhr.onload = () => {
      detachAbort()
      if (xhr.status === 401) {
        const { code } = parseErrorEnvelope(xhr.responseText, xhr.statusText)
        clearSession()
        unauthorizedHandler?.()
        reject(new ApiError('Unauthorized', 401, code))
        return
      }
      if (xhr.status >= 200 && xhr.status < 300) {
        // The wire contract: the upload route answers a StagedAttachmentInfo, unchecked here. A 2xx
        // that is not JSON (a proxy's page) must still settle the promise.
        try {
          const staged: unknown = JSON.parse(xhr.responseText)
          resolve(staged as StagedAttachmentInfo)
        } catch {
          reject(new ApiError('Invalid response', xhr.status, null))
        }
        return
      }
      const { message, code } = parseErrorEnvelope(xhr.responseText, xhr.statusText)
      reject(new ApiError(message || xhr.statusText, xhr.status, code))
    }
    xhr.onerror = () => { detachAbort(); reject(new ApiError('Network error', 0, null)) }
    xhr.onabort = rejectAborted

    // fetch rejects synchronously for a pre-aborted signal; XHR needs the same check up front.
    if (signal?.aborted) {
      rejectAborted()
      return
    }

    signal?.addEventListener('abort', onAbort)
    const form = new FormData()
    form.append('file', file)
    if (inline) form.append('inline', 'true')
    xhr.send(form)
  })
}
