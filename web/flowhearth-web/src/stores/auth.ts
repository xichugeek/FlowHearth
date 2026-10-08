import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import * as authApi from '../api/auth'
import { ApiError } from '../api/problem-details'
import { hasAnyPermission } from '../security/permissions'
import type { AuthenticatedUser } from '../types/security'
import { useSettingsStore } from './settings'

export const useAuthStore = defineStore('auth', () => {
  const settingsStore = useSettingsStore()
  const currentUser = ref<AuthenticatedUser | null>(null)
  const initialized = ref(false)
  const loading = ref(false)
  const isAuthenticated = computed(() => currentUser.value !== null)

  async function initialize() {
    if (initialized.value) return

    loading.value = true
    try {
      currentUser.value = await authApi.getCurrentUser()
      await restoreRuntimeSettings()
    } catch (error) {
      if (!(error instanceof ApiError) || error.status !== 401) {
        console.warn('Unable to restore the current session.', error)
      }
      currentUser.value = null
    } finally {
      initialized.value = true
      loading.value = false
    }
  }

  async function login(username: string, password: string) {
    loading.value = true
    try {
      currentUser.value = await authApi.login(username, password)
      initialized.value = true
      await restoreRuntimeSettings(true)
    } finally {
      loading.value = false
    }
  }

  async function logout() {
    loading.value = true
    try {
      await authApi.logout()
    } finally {
      currentUser.value = null
      initialized.value = true
      settingsStore.reset()
      loading.value = false
    }
  }

  async function changePassword(currentPassword: string, newPassword: string) {
    currentUser.value = await authApi.changePassword(currentPassword, newPassword)
  }

  function canAny(requiredPermissions?: readonly string[]) {
    return hasAnyPermission(
      currentUser.value?.permissions ?? [],
      requiredPermissions,
    )
  }

  async function restoreRuntimeSettings(force = false) {
    try {
      await settingsStore.initialize(force)
    } catch (error) {
      console.warn('Unable to load runtime settings; safe defaults are active.', error)
    }
  }

  return {
    currentUser,
    initialized,
    loading,
    isAuthenticated,
    initialize,
    login,
    logout,
    changePassword,
    canAny,
  }
})
