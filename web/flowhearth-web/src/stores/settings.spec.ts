import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as settingsApi from '../api/settings'
import { lookupDictionaryCodes } from '../types/settings'
import type { RuntimeSettings } from '../types/settings'
import { useSettingsStore } from './settings'

vi.mock('../api/settings', () => ({
  getRuntimeSettings: vi.fn(),
}))

const configuredRuntime: RuntimeSettings = {
  defaultPageSize: 50,
  businessTimeZone: 'UTC',
  numberPrefixes: {
    customer: 'CX',
    opportunity: 'OX',
    project: 'PX',
    equipment: 'EX',
    service: 'SX',
  },
  lookups: {
    [lookupDictionaryCodes.customerIndustry]: [
      {
        id: 1,
        dictionaryCode: lookupDictionaryCodes.customerIndustry,
        value: '半导体',
        label: '半导体行业',
        description: null,
        sortOrder: 10,
        isActive: true,
        version: 1,
        createdAtUtc: '2026-08-31T00:00:00Z',
        updatedAtUtc: '2026-08-31T00:00:00Z',
      },
    ],
  },
}

describe('settings store', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    vi.mocked(settingsApi.getRuntimeSettings).mockResolvedValue(configuredRuntime)
  })

  it('loads effective settings once and exposes lookup values', async () => {
    const store = useSettingsStore()

    await store.initialize()
    await store.initialize()

    expect(settingsApi.getRuntimeSettings).toHaveBeenCalledTimes(1)
    expect(store.defaultPageSize).toBe(50)
    expect(store.runtime.numberPrefixes.customer).toBe('CX')
    expect(store.lookupItems(lookupDictionaryCodes.customerIndustry)[0]?.label)
      .toBe('半导体行业')
  })

  it('force refreshes and reset returns safe defaults', async () => {
    const store = useSettingsStore()

    await store.initialize()
    await store.initialize(true)
    store.reset()

    expect(settingsApi.getRuntimeSettings).toHaveBeenCalledTimes(2)
    expect(store.defaultPageSize).toBe(10)
    expect(store.runtime.businessTimeZone).toBe('Asia/Shanghai')
    expect(store.lookupItems(lookupDictionaryCodes.customerIndustry)).toEqual([])
    expect(store.initialized).toBe(false)
  })
})
