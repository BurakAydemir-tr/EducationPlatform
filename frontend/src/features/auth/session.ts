import { ApiError, readApiError } from '../../shared/api/apiError'
import { apiBaseUrl } from '../../shared/api/config'

export type Role = 'Admin' | 'Teacher' | 'Student'

export type LoginResponse = {
  accessToken: string
  refreshToken: string
  accessTokenExpiresAt: string
}

export type AuthSession = LoginResponse & {
  userId: string
  role: Role
}

const refreshStorageKey = 'education-platform.refresh-token'
const nameIdClaim = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'
const roleClaim = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'

let currentSession: AuthSession | null = null
let refreshInFlight: Promise<AuthSession> | null = null
const listeners = new Set<() => void>()

export function getSession(): AuthSession | null {
  return currentSession
}

export function subscribeSession(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

function notifySession(): void {
  listeners.forEach((listener) => listener())
}

export function getStoredRefreshToken(): string | null {
  try {
    return sessionStorage.getItem(refreshStorageKey)
  } catch {
    return null
  }
}

function parseIdentity(accessToken: string): { userId: string; role: Role } {
  const payload = accessToken.split('.')[1]
  if (!payload) throw new Error('Access token geçersiz.')

  const padded = payload.replace(/-/g, '+').replace(/_/g, '/')
  const claims = JSON.parse(atob(padded)) as Record<string, unknown>
  const userId = claims[nameIdClaim] ?? claims.nameid ?? claims.sub
  const rawRole = claims[roleClaim] ?? claims.role
  const roles = Array.isArray(rawRole) ? rawRole : [rawRole]
  const role = roles.find((value): value is Role =>
    value === 'Admin' || value === 'Teacher' || value === 'Student',
  )

  if (typeof userId !== 'string' || !role) throw new Error('Access token kimliği geçersiz.')
  return { userId, role }
}

export function saveSession(response: LoginResponse): AuthSession {
  const identity = parseIdentity(response.accessToken)
  const next = { ...response, ...identity }
  currentSession = next
  try {
    sessionStorage.setItem(refreshStorageKey, response.refreshToken)
  } catch {
    // The current tab can continue; restoration after reload will not be available.
  }
  notifySession()
  return next
}

export function clearSession(): void {
  currentSession = null
  try {
    sessionStorage.removeItem(refreshStorageKey)
  } catch {
    // The in-memory session is cleared even if browser storage is unavailable.
  }
  notifySession()
}

export function refreshSession(): Promise<AuthSession> {
  if (refreshInFlight) return refreshInFlight

  const refreshToken = currentSession?.refreshToken ?? getStoredRefreshToken()
  if (!refreshToken) {
    return Promise.reject(new ApiError(401, 'authentication_required', 'Oturum gerekli', 'Lütfen giriş yapın.'))
  }

  refreshInFlight = (async () => {
    const response = await fetch(`${apiBaseUrl}/auth/refresh`, {
      method: 'POST',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken }),
    })
    if (!response.ok) {
      const error = await readApiError(response)
      if (response.status === 401 || response.status === 403) clearSession()
      throw error
    }
    return saveSession((await response.json()) as LoginResponse)
  })().finally(() => {
    refreshInFlight = null
  })
  return refreshInFlight
}

export async function restoreSession(): Promise<AuthSession | null> {
  if (currentSession) return currentSession
  if (!getStoredRefreshToken()) return null
  try {
    return await refreshSession()
  } catch (error) {
    if (error instanceof ApiError && (error.status === 401 || error.status === 403)) return null
    throw error
  }
}

async function sendLogout(session: AuthSession): Promise<Response> {
  return fetch(`${apiBaseUrl}/auth/logout`, {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      Authorization: `Bearer ${session.accessToken}`,
    },
    body: JSON.stringify({ refreshToken: session.refreshToken }),
  })
}

export async function logoutSession(): Promise<void> {
  if (refreshInFlight) await refreshInFlight
  let session = currentSession
  if (!session) {
    clearSession()
    return
  }

  let response = await sendLogout(session)
  if (response.status === 401) {
    session = await refreshSession()
    response = await sendLogout(session)
  }
  if (!response.ok) {
    const error = await readApiError(response)
    if (response.status === 401 || response.status === 403) clearSession()
    throw error
  }
  clearSession()
}
