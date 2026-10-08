import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import * as settingsApi from '../api/settings'
import type { LookupItem, RuntimeSettings } from '../types/settings'

const defaults: RuntimeSettings = {
  defaultPageSize: 10,
  businessTimeZone: 'Asia/Shanghai',
  numberPrefixes: {
    customer: 'CU',
    opportunity: 'OP',
    project: 'TN',
    equipment: 'EQ',
    service: 'SR',
  },
  lookups: {},
}

export const useSettingsStore = defineStore('settings', () => {
  const runtime = ref<RuntimeSettings>(defaults)
  const initialized = ref(false)
  const loading = ref(false)
  const defaultPageSize = computed(() => runtime.value.defaultPageSize)

  async function initialize(force = false) {
    if (initialized.value && !force) return
    loading.value = true
    try {
      runtime.value = await settingsApi.getRuntimeSettings()
      initialized.value = true
    } finally {
      loading.value = false
    }
  }

  function lookupItems(code: string): LookupItem[] {
    return runtime.value.lookups[code] ?? []
  }

  function reset() {
    runtime.value = defaults
    initialized.value = false
  }

  return {
    runtime,
    initialized,
    loading,
    defaultPageSize,
    initialize,
    lookupItems,
    reset,
  }
})
