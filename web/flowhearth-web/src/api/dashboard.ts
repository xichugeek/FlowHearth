import type { DashboardSnapshot } from '../types/dashboard'
import { apiClient } from './client'

export async function getDashboard() {
  const response = await apiClient.get<DashboardSnapshot>('/dashboard')
  return response.data
}
