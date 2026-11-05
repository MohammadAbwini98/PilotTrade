// status and body are set when the server answered with a refusal the page can act on (400 or 409).
export class ApiError extends Error {
  constructor(message: string, readonly status?: number, readonly body?: unknown) { super(message) }
}
export class NotFound extends ApiError {}

// A read is tried once more after a network failure; a write never is, so a POST cannot reach the server twice.
export const RETRY_DELAY_MS = 500

// What the last request learned about the link to the backend. A 404 still means the server answered.
export type LinkState = 'unknown' | 'connected' | 'refused' | 'server-error' | 'unreachable'

export type ApiClient = {
  get: <T>(path: string) => Promise<T>
  post: <T>(path: string, body: unknown) => Promise<T>
}

type Fetch = (input: string, init: RequestInit) => Promise<Response>

// The owner token is read from memory on every call and sent only as a header, never in a URL or browser storage.
export function createApiClient(getToken: () => string, onLink: (state: LinkState) => void = () => {}, fetchImpl: Fetch = (i, init) => fetch(i, init)): ApiClient {
  const request = async <T,>(path: string, init: RequestInit = {}): Promise<T> => {
    const headers = new Headers(init.headers)
    headers.set('X-PilotTrade-Owner-Token', getToken())
    const isRead = (init.method ?? 'GET') === 'GET'
    let r: Response
    try { r = await fetchImpl(path, { ...init, headers }) }
    catch {
      if (!isRead) { onLink('unreachable'); throw new ApiError('The server could not be reached. The request was not repeated.') }
      await new Promise(resolve => setTimeout(resolve, RETRY_DELAY_MS))
      try { r = await fetchImpl(path, { ...init, headers }) }
      catch { onLink('unreachable'); throw new ApiError('The server could not be reached.') }
    }
    if (r.status === 403) { onLink('refused'); throw new ApiError('Refused: check the owner token.') }
    if (r.status === 404) { onLink('connected'); throw new NotFound('Not found.') }
    if (r.status === 400 || r.status === 409) {
      // The server answered and refused the request itself; the link is fine.
      onLink('connected')
      const body = await r.json().catch(() => null) as { error?: string; reason?: string } | null
      throw new ApiError(body?.reason ?? `Request refused (${r.status}${body?.error ? `: ${body.error}` : ''}).`, r.status, body)
    }
    if (!r.ok) {
      onLink('server-error')
      const body = await r.json().catch(() => null) as { error?: string } | null
      throw new ApiError(`Request failed (${r.status}${body?.error ? `: ${body.error}` : ''}).`)
    }
    onLink('connected')
    return await r.json() as T
  }

  return {
    get: path => request(path),
    // Every write first fetches a CSRF token; the server also checks the Origin.
    post: async (path, body) => {
      const { csrfToken } = await request<{ csrfToken: string }>('/api/security/csrf-token')
      return await request(path, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-PilotTrade-CSRF': csrfToken }, body: JSON.stringify(body) })
    },
  }
}
