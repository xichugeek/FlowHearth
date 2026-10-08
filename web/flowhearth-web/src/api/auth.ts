import type { AuthenticatedUser } from '../types/security'
import { apiClient } from './client'
import { clearCsrfToken, getCsrfHeaders } from './csrf'

export async function getCurrentUser() {
  const response = await apiClient.get<AuthenticatedUser>('/auth/me')
  return response.data
}

export async function login(username: string, password: string) {
  const headers = await getCsrfHeaders(true)
  const response = await apiClient.post<AuthenticatedUser>(
    '/auth/login',
    { username, password },
    { headers },
  )
  await getCsrfHeaders(true)
  return response.data
}

export async function logout() {
  const headers = await getCsrfHeaders(true)
  await apiClient.post('/auth/logout', undefined, { headers })
  clearCsrfToken()
}

export async function changePassword(
  currentPassword: string,
  newPassword: string,
) {
  const headers = await getCsrfHeaders()
  const response = await apiClient.post<AuthenticatedUser>(
    '/auth/change-password',
    { currentPassword, newPassword },
    { headers },
  )
  await getCsrfHeaders(true)
  return response.data
}
