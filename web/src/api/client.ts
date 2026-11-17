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

export interface Session {
  accessToken: string
  expiresAt: string
  sessionId: string
  signedIn: boolean
  mustChangePassword: boolean
}

/**
 * Every visitor has a session: a guest one to start with, an account's after signing in. The access
 * token lives only in this module's memory, never in localStorage, so a script injected into the page
 * has nothing to steal that outlives the tab. What keeps the session across reloads is an HttpOnly
 * cookie the page cannot read, sent only to /api/auth.
 */
let accessToken: string | null = null
const listeners = new Set<() => void>()

export function setSession(session: Session | null): void {
  accessToken = session?.accessToken ?? null
  listeners.forEach((listener) => listener())
}

/** Called whenever the session changes hands (sign-in, sign-out), so cached answers can be dropped. */
export function onSessionChange(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

// The auth endpoints refuse requests without it. A form on another site cannot send it, and a script on
// another site would need a CORS permission the API never gives; that is what protects the cookie.
const CLIENT_HEADER = { 'X-Requested-With': 'marquee' }

let resuming: Promise<void> | null = null

/**
 * Resumes the session in the cookie, or opens a guest one. Refresh tokens work once, so two tabs must
 * not refresh with the same cookie at the same moment: a Web Lock makes them take turns.
 */
export function resumeSession(): Promise<void> {
  resuming ??= withLock(async () => {
    const response = await fetch('/api/auth/session', { method: 'POST', headers: CLIENT_HEADER, credentials: 'same-origin' })
    if (!response.ok) throw await toError(response)
    setSession((await response.json()) as Session)
  }).finally(() => {
    resuming = null
  })
  return resuming
}

function withLock<T>(work: () => Promise<T>): Promise<T> {
  return typeof navigator !== 'undefined' && navigator.locks ? navigator.locks.request('marquee-session', work) : work()
}

async function send(path: string, init: RequestInit): Promise<Response> {
  if (!accessToken) await resumeSession()
  return fetch(`/api${path}`, {
    ...init,
    credentials: 'same-origin',
    headers: { ...init.headers, ...CLIENT_HEADER, Authorization: `Bearer ${accessToken}` },
  })
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  let response = await send(path, init)
  if (response.status === 401 && !path.startsWith('/auth/')) {
    // The access token ran out, or was ended by a password or role change: renew and try once more.
    await resumeSession()
    response = await send(path, init)
  }

  if (!response.ok) throw await toError(response)
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

export function postJson<T>(path: string, body?: unknown, headers?: Record<string, string>): Promise<T> {
  return api<T>(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...headers },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
}

export function putJson<T>(path: string, body: unknown): Promise<T> {
  return api<T>(path, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
}

/** Calls an /api/auth endpoint that answers with a session (or a two-step ticket) and adopts the session. */
export async function authenticate<T extends { session: Session | null }>(path: string, body: unknown): Promise<T> {
  const response = await fetch(`/api/auth${path}`, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', ...CLIENT_HEADER },
    body: JSON.stringify(body),
  })
  if (!response.ok) throw await toError(response)
  const result = (await response.json()) as T
  if (result.session) setSession(result.session)
  return result
}

/** Ends the session on the server too, then carries on as a fresh guest. */
export async function signOut(): Promise<void> {
  await fetch('/api/auth/sign-out', { method: 'POST', headers: CLIENT_HEADER, credentials: 'same-origin' }).catch(() => {})
  setSession(null)
  await resumeSession()
}

const MESSAGES: Record<string, string> = {
  invalid_credentials: 'The e-mail address or password is wrong.',
  too_many_attempts: 'Too many failed attempts. Try again in 15 minutes.',
  account_disabled: 'This account has been deactivated.',
  invalid_code: 'The code is wrong or has already been used.',
  ticket_expired: 'That took too long. Start again with your password.',
  weak_password: 'Use at least 12 characters that are not easy to guess. A short sentence makes a good password.',
  wrong_password: 'The current password is wrong.',
  same_password: 'Choose a password different from the current one.',
  email_in_use: 'An account with this e-mail address already exists. Sign in instead.',
  invalid_registration: 'Enter your name and a valid e-mail address.',
  cannot_change_self: 'Another administrator has to change your own role or access.',
  last_admin: 'There must be at least one active administrator.',
  hall_busy: 'The hall is not free then. Leave 20 minutes between showings.',
  starts_too_soon: 'A showtime starts at least an hour from now.',
}

async function toError(response: Response): Promise<ApiError> {
  const problem = (await response.json().catch(() => null)) as { detail?: string; code?: string } | null
  const message =
    (problem?.code && MESSAGES[problem.code]) ??
    problem?.detail ??
    (response.status === 429
      ? 'Too many attempts. Wait a moment.'
      : response.status === 403
        ? 'You do not have access to this.'
        : 'Something went wrong.')
  return new ApiError(response.status, problem?.code, message)
}
