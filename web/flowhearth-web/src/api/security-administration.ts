import type {
  CreateRoleInput,
  CreateUserInput,
  PagedResult,
  PermissionDetails,
  RoleDetails,
  UpdateRoleInput,
  UpdateUserInput,
  UserDetails,
} from '../types/security'
import { apiClient } from './client'
import { getCsrfHeaders } from './csrf'

export async function listUsers(page = 1, pageSize = 10, search = '') {
  const response = await apiClient.get<PagedResult<UserDetails>>('/users', {
    params: { page, pageSize, search: search || undefined },
  })
  return response.data
}

export async function createUser(input: CreateUserInput) {
  const response = await apiClient.post<UserDetails>('/users', input, {
    headers: await getCsrfHeaders(),
  })
  return response.data
}

export async function updateUser(userId: number, input: UpdateUserInput) {
  const response = await apiClient.put<UserDetails>(`/users/${userId}`, input, {
    headers: await getCsrfHeaders(),
  })
  return response.data
}

export async function resetUserPassword(userId: number, newPassword: string) {
  await apiClient.put(
    `/users/${userId}/password`,
    { newPassword },
    { headers: await getCsrfHeaders() },
  )
}

export async function listRoles() {
  const response = await apiClient.get<RoleDetails[]>('/roles')
  return response.data
}

export async function listPermissions() {
  const response = await apiClient.get<PermissionDetails[]>('/roles/permissions')
  return response.data
}

export async function createRole(input: CreateRoleInput) {
  const response = await apiClient.post<RoleDetails>('/roles', input, {
    headers: await getCsrfHeaders(),
  })
  return response.data
}

export async function updateRole(roleId: number, input: UpdateRoleInput) {
  const response = await apiClient.put<RoleDetails>(`/roles/${roleId}`, input, {
    headers: await getCsrfHeaders(),
  })
  return response.data
}
