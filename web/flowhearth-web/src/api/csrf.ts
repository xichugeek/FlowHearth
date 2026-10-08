import { apiClient } from './client'

let requestToken: string | null = null

interface CsrfResponse {
  token: string
}

export async function getCsrfHeaders(forceRefresh = false) {
  if (forceRefresh || !requestToken) {
    const response = await apiClient.get<CsrfResponse>('/auth/csrf')
    requestToken = response.data.token
  }

  return { 'X-XSRF-TOKEN': requestToken }
}

export function clearCsrfToken() {
  requestToken = null
}
