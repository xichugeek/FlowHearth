import type { GlobalSearchResult } from '../types/search'
import { apiClient } from './client'

export async function globalSearch(query: string, limit = 20) {
  const response = await apiClient.get<GlobalSearchResult[]>('/search', {
    params: { q: query, limit },
  })
  return response.data
}
