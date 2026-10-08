import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { RouteLocationNormalized } from 'vue-router'

import { authNavigationGuard } from './index'
import { useAuthStore } from '../stores/auth'
import type { AuthenticatedUser } from '../types/security'

vi.mock('../api/auth', () => ({
  getCurrentUser: vi.fn(),
  login: vi.fn(),
  logout: vi.fn(),
  changePassword: vi.fn(),
}))

describe('authNavigationGuard', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })

  it('redirects an unauthenticated user to login', async () => {
    const store = useAuthStore()
    store.initialized = true

    const result = await authNavigationGuard(createRoute({
      name: 'dashboard',
      fullPath: '/dashboard',
      requiresAuth: true,
    }))

    expect(result).toEqual({
      name: 'login',
      query: { redirect: '/dashboard' },
    })
  })

  it('redirects an authenticated user without permission to forbidden', async () => {
    const store = useAuthStore()
    store.initialized = true
    store.currentUser = createUser(['dashboard.view'])

    const result = await authNavigationGuard(createRoute({
      name: 'system',
      fullPath: '/system',
      requiresAuth: true,
      permissionsAny: ['security.users.view'],
    }))

    expect(result).toEqual({ name: 'forbidden' })
  })

  it('allows an authenticated user with a required permission', async () => {
    const store = useAuthStore()
    store.initialized = true
    store.currentUser = createUser(['security.users.view'])

    const result = await authNavigationGuard(createRoute({
      name: 'system',
      fullPath: '/system',
      requiresAuth: true,
      permissionsAny: ['security.users.view'],
    }))

    expect(result).toBe(true)
  })

  it('enforces finance route permissions', async () => {
    const store = useAuthStore()
    store.initialized = true
    store.currentUser = createUser(['receivables.view'])

    expect(await authNavigationGuard(createRoute({
      name: 'receivables',
      fullPath: '/finance/receivables',
      requiresAuth: true,
      permissionsAny: ['receivables.view'],
    }))).toBe(true)
    expect(await authNavigationGuard(createRoute({
      name: 'receipts',
      fullPath: '/finance/receipts',
      requiresAuth: true,
      permissionsAny: ['receipts.view'],
    }))).toEqual({ name: 'forbidden' })
    expect(await authNavigationGuard(createRoute({
      name: 'finance-dashboard',
      fullPath: '/finance/dashboard',
      requiresAuth: true,
      permissionsAny: ['finance.dashboard.view'],
    }))).toEqual({ name: 'forbidden' })
    store.currentUser = createUser(['finance.dashboard.view'])
    expect(await authNavigationGuard(createRoute({
      name: 'finance-dashboard',
      fullPath: '/finance/dashboard',
      requiresAuth: true,
      permissionsAny: ['finance.dashboard.view'],
    }))).toBe(true)
    expect(await authNavigationGuard(createRoute({
      name: 'payables',
      fullPath: '/finance/payables',
      requiresAuth: true,
      permissionsAny: ['payables.view'],
    }))).toEqual({ name: 'forbidden' })
    store.currentUser = createUser(['payments.view'])
    expect(await authNavigationGuard(createRoute({
      name: 'payments',
      fullPath: '/finance/payments',
      requiresAuth: true,
      permissionsAny: ['payments.view'],
    }))).toBe(true)
    expect(await authNavigationGuard(createRoute({
      name: 'shipments',
      fullPath: '/finance/shipments',
      requiresAuth: true,
      permissionsAny: ['shipments.view'],
    }))).toEqual({ name: 'forbidden' })
    store.currentUser = createUser(['shipments.view'])
    expect(await authNavigationGuard(createRoute({
      name: 'shipments',
      fullPath: '/finance/shipments',
      requiresAuth: true,
      permissionsAny: ['shipments.view'],
    }))).toBe(true)
  })
})

function createUser(grantedPermissions: string[]): AuthenticatedUser {
  return {
    id: 1,
    username: 'tester',
    displayName: 'Test User',
    securityVersion: 1,
    roles: [],
    permissions: grantedPermissions,
  }
}

function createRoute(input: {
  name: string
  fullPath: string
  requiresAuth: boolean
  permissionsAny?: string[]
}) {
  return {
    name: input.name,
    fullPath: input.fullPath,
    meta: {
      requiresAuth: input.requiresAuth,
      permissionsAny: input.permissionsAny,
    },
  } as unknown as RouteLocationNormalized
}
