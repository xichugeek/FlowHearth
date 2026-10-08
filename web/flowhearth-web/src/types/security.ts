export interface AuthenticatedUser {
  id: number
  username: string
  displayName: string
  email?: string | null
  securityVersion: number
  roles: string[]
  permissions: string[]
}

export interface RoleReference {
  id: number
  code: string
  name: string
}

export interface PermissionReference {
  id: number
  code: string
  name: string
}

export interface UserDetails {
  id: number
  username: string
  displayName: string
  email?: string | null
  isActive: boolean
  lastLoginAtUtc?: string | null
  lockoutEndUtc?: string | null
  version: number
  roles: RoleReference[]
}

export interface RoleDetails {
  id: number
  code: string
  name: string
  description?: string | null
  isSystem: boolean
  isActive: boolean
  version: number
  permissions: PermissionReference[]
}

export interface PermissionDetails {
  id: number
  code: string
  name: string
  module: string
  description?: string | null
}

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  total: number
}

export interface CreateUserInput {
  username: string
  displayName: string
  email?: string | null
  password: string
  roleIds: number[]
}

export interface UpdateUserInput {
  displayName: string
  email?: string | null
  isActive: boolean
  roleIds: number[]
  version: number
}

export interface CreateRoleInput {
  code: string
  name: string
  description?: string | null
  permissionIds: number[]
}

export interface UpdateRoleInput {
  name: string
  description?: string | null
  isActive: boolean
  permissionIds: number[]
  version: number
}
