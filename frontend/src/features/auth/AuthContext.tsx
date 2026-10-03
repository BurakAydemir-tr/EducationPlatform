import { createContext, useContext, useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { loginRequest } from './authApi'
import {
  getSession,
  getStoredRefreshToken,
  logoutSession,
  restoreSession,
  saveSession,
  subscribeSession,
} from './session'
import type { Role } from './session'

type AuthState =
  | { status: 'restoring' }
  | { status: 'anonymous' }
  | { status: 'error' }
  | { status: 'authenticated'; userId: string; role: Role }

type AuthContextValue = {
  state: AuthState
  login: (userName: string, password: string) => Promise<Role>
  logout: () => Promise<void>
  retryRestore: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>(() => {
    const session = getSession()
    if (session) return { status: 'authenticated', userId: session.userId, role: session.role }
    return getStoredRefreshToken() ? { status: 'restoring' } : { status: 'anonymous' }
  })

  useEffect(() => {
    const unsubscribe = subscribeSession(() => {
      const session = getSession()
      if (session) setState({ status: 'authenticated', userId: session.userId, role: session.role })
      else if (!getStoredRefreshToken()) setState({ status: 'anonymous' })
    })

    if (getStoredRefreshToken()) {
      void restoreSession()
        .then((session) => {
          if (!session) setState({ status: 'anonymous' })
        })
        .catch(() => setState({ status: 'error' }))
    }
    return unsubscribe
  }, [])

  async function retryRestore(): Promise<void> {
    setState({ status: 'restoring' })
    try {
      const session = await restoreSession()
      if (!session) setState({ status: 'anonymous' })
    } catch {
      setState({ status: 'error' })
    }
  }

  async function login(userName: string, password: string): Promise<Role> {
    const response = await loginRequest(userName, password)
    return saveSession(response).role
  }

  return (
    <AuthContext.Provider value={{ state, login, logout: logoutSession, retryRestore }}>
      {children}
    </AuthContext.Provider>
  )
}

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext)
  if (!value) throw new Error('AuthProvider bulunamadı.')
  return value
}
