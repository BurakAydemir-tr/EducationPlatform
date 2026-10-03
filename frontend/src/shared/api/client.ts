import { readApiError } from './apiError'
import { apiBaseUrl } from './config'
import { clearSession, getSession, refreshSession } from '../../features/auth/session'

type ApiRequestOptions = {
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'
  body?: unknown
  anonymous?: boolean
}

export async function apiRequest<T>(path: string, options: ApiRequestOptions = {}): Promise<T> {
  const { method = 'GET', body, anonymous = false } = options
  const jsonBody = body === undefined ? undefined : JSON.stringify(body)

  const send = (accessToken?: string) => fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      Accept: 'application/json',
      ...(jsonBody === undefined ? {} : { 'Content-Type': 'application/json' }),
      ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}),
    },
    body: jsonBody,
  })

  const initialToken = anonymous ? undefined : getSession()?.accessToken
  let response = await send(initialToken)

  if (response.status === 401 && !anonymous) {
    // A different request may already have rotated the token while this response was in flight.
    const latestToken = getSession()?.accessToken
    const accessToken = latestToken && latestToken !== initialToken
      ? latestToken
      : (await refreshSession()).accessToken
    response = await send(accessToken)
  }

  if (!response.ok) {
    const error = await readApiError(response)
    if (response.status === 401 && !anonymous) clearSession()
    throw error
  }
  if (response.status === 204) return undefined as T
  const text = await response.text()
  return text ? JSON.parse(text) as T : undefined as T
}
