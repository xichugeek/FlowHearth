import { apiClient } from './client'
import type {
  CreateLookupItemInput,
  LookupItem,
  RuntimeSettings,
  SettingsAdministrationSnapshot,
  SystemSetting,
  UpdateLookupItemInput,
  UpdateSystemSettingInput,
} from '../types/settings'

export async function getRuntimeSettings() {
  const response = await apiClient.get<RuntimeSettings>('/settings/runtime')
  return response.data
}

export async function getSettingsAdministration() {
  const response = await apiClient.get<SettingsAdministrationSnapshot>('/settings')
  return response.data
}

export async function createLookupItem(input: CreateLookupItemInput) {
  const response = await apiClient.post<LookupItem>('/settings/lookup-items', input)
  return response.data
}

export async function updateLookupItem(id: number, input: UpdateLookupItemInput) {
  const response = await apiClient.put<LookupItem>(`/settings/lookup-items/${id}`, input)
  return response.data
}

export async function updateSystemSetting(
  key: string,
  input: UpdateSystemSettingInput,
) {
  const response = await apiClient.put<SystemSetting>(
    `/settings/system/${encodeURIComponent(key)}`,
    input,
  )
  return response.data
}
