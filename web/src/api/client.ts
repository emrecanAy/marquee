const SESSION_KEY = 'marquee.session'

interface Session {
  token: string
  expiresAt: string
}

/** An error the API explained with a problem document. `code` is stable and safe to switch on. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    message: string,
  ) {
    super(message)
  }
}

let pendingSession: Promise<Session> | undefined

function storedSession(): Session | undefined {
  try {
    const raw = localStorage.getItem(SESSION_KEY)
    const session = raw ? (JSON.parse(raw) as Session) : undefined
    return session && new Date(session.expiresAt) > new Date() ? session : undefined
  } catch {
    return undefined
  }
}

/**
 * Every visitor shops as an anonymous guest. The session is created on first use and kept in
 * localStorage so a reload keeps the same held seats and tickets.
 */
async function getSession(): Promise<Session> {
  const existing = storedSession()
  if (existing) return existing

  // Parallel first requests must share one session, not create one each.
  pendingSession ??= fetch('/api/sessions', { method: 'POST' })
    .then(async (response) => {
      if (!response.ok) throw new ApiError(response.status, undefined, 'Could not start a session.')
      const session = (await response.json()) as Session
      localStorage.setItem(SESSION_KEY, JSON.stringify(session))
      return session
    })
    .finally(() => {
      pendingSession = undefined
    })
  return pendingSession
}

async function send(path: string, init: RequestInit): Promise<Response> {
  const { token } = await getSession()
  return fetch(`/api${path}`, {
    ...init,
    headers: { ...init.headers, Authorization: `Bearer ${token}` },
  })
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  let response = await send(path, init)
  if (response.status === 401) {
    // The stored session is no longer accepted (expired, or the server key changed). Start over once.
    localStorage.removeItem(SESSION_KEY)
    response = await send(path, init)
  }

  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as { detail?: string; code?: string } | null
    throw new ApiError(
      response.status,
      problem?.code,
      problem?.detail ?? (response.status === 429 ? 'Too many attempts. Wait a few seconds.' : 'Something went wrong.'),
    )
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

export function postJson<T>(path: string, body?: unknown, headers?: Record<string, string>): Promise<T> {
  return api<T>(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...headers },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
}
