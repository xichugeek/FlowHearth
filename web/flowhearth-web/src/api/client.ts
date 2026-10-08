import axios from 'axios'

import { toApiError } from './problem-details'

export const apiClient = axios.create({
  baseURL: '/api/v1',
  timeout: 15_000,
  withCredentials: true,
  withXSRFToken: true,
  xsrfCookieName: 'XSRF-TOKEN',
  xsrfHeaderName: 'X-XSRF-TOKEN',
  headers: {
    Accept: 'application/json',
  },
})

apiClient.interceptors.response.use(
  (response) => response,
  (error: unknown) => Promise.reject(toApiError(error)),
)
