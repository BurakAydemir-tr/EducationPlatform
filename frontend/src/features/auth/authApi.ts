import { apiRequest } from '../../shared/api/client'
import type { LoginResponse } from './session'

export function loginRequest(userName: string, password: string): Promise<LoginResponse> {
  return apiRequest<LoginResponse>('/auth/login', {
    method: 'POST',
    body: { userName, password },
    anonymous: true,
  })
}
