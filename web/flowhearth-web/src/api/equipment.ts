import type {
  EquipmentComponentDetails,
  EquipmentComponentInput,
  EquipmentDetails,
  EquipmentInput,
  EquipmentListParameters,
  EquipmentPage,
  EquipmentParameterDetails,
  EquipmentParameterInput,
  EquipmentVersionDetails,
  EquipmentVersionInput,
  UpdateEquipmentComponentInput,
  UpdateEquipmentInput,
  UpdateEquipmentParameterInput,
  UpdateEquipmentVersionInput,
} from '../types/equipment'
import { apiClient } from './client'
import { getCsrfHeaders } from './csrf'

export async function listEquipment(parameters: EquipmentListParameters) {
  return (await apiClient.get<EquipmentPage>('/equipment', { params: parameters })).data
}
export async function getEquipment(id: number) {
  return (await apiClient.get<EquipmentDetails>(`/equipment/${id}`)).data
}
export async function createEquipment(input: EquipmentInput) {
  return (await apiClient.post<EquipmentDetails>('/equipment', input, { headers: await getCsrfHeaders() })).data
}
export async function updateEquipment(id: number, input: UpdateEquipmentInput) {
  return (await apiClient.put<EquipmentDetails>(`/equipment/${id}`, input, { headers: await getCsrfHeaders() })).data
}
export async function setEquipmentArchived(id: number, archived: boolean, version: number) {
  const action = archived ? 'archive' : 'restore'
  return (await apiClient.post<EquipmentDetails>(`/equipment/${id}/${action}`, { version }, { headers: await getCsrfHeaders() })).data
}
export async function createComponent(equipmentId: number, input: EquipmentComponentInput) {
  return (await apiClient.post<EquipmentComponentDetails>(`/equipment/${equipmentId}/components`, input, { headers: await getCsrfHeaders() })).data
}
export async function updateComponent(equipmentId: number, componentId: number, input: UpdateEquipmentComponentInput) {
  return (await apiClient.put<EquipmentComponentDetails>(`/equipment/${equipmentId}/components/${componentId}`, input, { headers: await getCsrfHeaders() })).data
}
export async function deleteComponent(equipmentId: number, componentId: number, version: number) {
  await apiClient.delete(`/equipment/${equipmentId}/components/${componentId}`, { params: { version }, headers: await getCsrfHeaders() })
}
export async function createParameter(equipmentId: number, input: EquipmentParameterInput) {
  return (await apiClient.post<EquipmentParameterDetails>(`/equipment/${equipmentId}/parameters`, input, { headers: await getCsrfHeaders() })).data
}
export async function updateParameter(equipmentId: number, parameterId: number, input: UpdateEquipmentParameterInput) {
  return (await apiClient.put<EquipmentParameterDetails>(`/equipment/${equipmentId}/parameters/${parameterId}`, input, { headers: await getCsrfHeaders() })).data
}
export async function deleteParameter(equipmentId: number, parameterId: number, version: number) {
  await apiClient.delete(`/equipment/${equipmentId}/parameters/${parameterId}`, { params: { version }, headers: await getCsrfHeaders() })
}
export async function createVersion(equipmentId: number, input: EquipmentVersionInput) {
  return (await apiClient.post<EquipmentVersionDetails>(`/equipment/${equipmentId}/versions`, input, { headers: await getCsrfHeaders() })).data
}
export async function updateVersion(equipmentId: number, versionId: number, input: UpdateEquipmentVersionInput) {
  return (await apiClient.put<EquipmentVersionDetails>(`/equipment/${equipmentId}/versions/${versionId}`, input, { headers: await getCsrfHeaders() })).data
}
export async function deleteVersion(equipmentId: number, versionId: number, version: number) {
  await apiClient.delete(`/equipment/${equipmentId}/versions/${versionId}`, { params: { version }, headers: await getCsrfHeaders() })
}
