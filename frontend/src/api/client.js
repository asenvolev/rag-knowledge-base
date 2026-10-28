// Thin API client for the .NET backend.
// Empty = same origin (production: the UI is served by the API). Dev sets it in .env.development.
const BASE_URL = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '')
const API_KEY = import.meta.env.VITE_API_KEY || ''

export class ApiError extends Error {
  constructor(message, status) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

function authHeaders() {
  return API_KEY ? { 'X-Api-Key': API_KEY } : {}
}

/** Pulls a human-readable message out of ASP.NET ProblemDetails / validation errors. */
function messageFromBody(body, status) {
  if (!body) return `Request failed (HTTP ${status})`
  if (typeof body === 'string') return body
  if (body.errors) {
    const first = Object.values(body.errors).flat()[0]
    if (first) return first
  }
  return body.detail || body.title || `Request failed (HTTP ${status})`
}

async function request(path, { method = 'GET', body, timeoutMs = 60_000, signal } = {}) {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(new DOMException('Timeout', 'TimeoutError')), timeoutMs)
  signal?.addEventListener('abort', () => controller.abort(signal.reason), { once: true })

  let res
  try {
    res = await fetch(`${BASE_URL}${path}`, {
      method,
      headers: {
        ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...authHeaders(),
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
      signal: controller.signal,
    })
  } catch (err) {
    if (err?.name === 'AbortError' || err?.name === 'TimeoutError') {
      throw new ApiError(signal?.aborted ? 'Request cancelled.' : 'The server took too long to respond.', 0)
    }
    throw new ApiError(`Cannot reach the API at ${BASE_URL || window.location.host}. Is the backend running?`, 0)
  } finally {
    clearTimeout(timer)
  }

  if (res.status === 204) return null

  const text = await res.text()
  let data = null
  try { data = text ? JSON.parse(text) : null } catch { data = text }

  if (!res.ok) {
    if (res.status === 429) throw new ApiError('Too many requests — please wait a moment and try again.', 429)
    throw new ApiError(messageFromBody(data, res.status), res.status)
  }
  return data
}

export const api = {
  baseUrl: BASE_URL,

  listDocuments: () => request('/api/documents'),

  deleteDocument: (id) => request(`/api/documents/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  /** @param {{message: string, documentId?: string|null, history?: {role: string, content: string}[]}} payload */
  chat: (payload, signal) => request('/api/documents/chat', { method: 'POST', body: payload, timeoutMs: 120_000, signal }),

  /**
   * Upload with progress. fetch() can't report upload progress, so this uses XHR.
   * @param {File} file
   * @param {(pct: number) => void} onProgress
   */
  uploadDocument(file, onProgress) {
    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest()
      const form = new FormData()
      form.append('file', file)

      xhr.open('POST', `${BASE_URL}/api/documents/upload`)
      xhr.timeout = 10 * 60_000 // big PDFs = many embedding batches
      Object.entries(authHeaders()).forEach(([k, v]) => xhr.setRequestHeader(k, v))

      xhr.upload.onprogress = (e) => {
        if (e.lengthComputable) onProgress?.(Math.round((e.loaded / e.total) * 100))
      }
      xhr.onload = () => {
        let data = null
        try { data = xhr.responseText ? JSON.parse(xhr.responseText) : null } catch { data = xhr.responseText }
        if (xhr.status >= 200 && xhr.status < 300) resolve(data)
        else if (xhr.status === 429) reject(new ApiError('Upload limit reached — try again in a few minutes.', 429))
        else reject(new ApiError(messageFromBody(data, xhr.status), xhr.status))
      }
      xhr.onerror = () => reject(new ApiError(`Cannot reach the API at ${BASE_URL || window.location.host}. Is the backend running?`, 0))
      xhr.ontimeout = () => reject(new ApiError('Upload timed out.', 0))
      xhr.send(form)
    })
  },
}
