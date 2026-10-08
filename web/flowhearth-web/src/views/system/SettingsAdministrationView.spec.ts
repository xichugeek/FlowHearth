import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { getSettingsAdministration } from '../../api/settings'
import { useAuthStore } from '../../stores/auth'
import SettingsAdministrationView from './SettingsAdministrationView.vue'

vi.mock('../../api/settings', () => ({
  getSettingsAdministration: vi.fn(),
  getRuntimeSettings: vi.fn(),
  createLookupItem: vi.fn(),
  updateLookupItem: vi.fn(),
  updateSystemSetting: vi.fn(),
}))

describe('SettingsAdministrationView', () => {
  beforeEach(() => {
    vi.mocked(getSettingsAdministration).mockResolvedValue({
      dictionaries: [
        {
          code: 'customer.industry',
          name: '客户所属行业',
          description: '客户行业建议值',
          items: [],
        },
      ],
      settings: [
        {
          key: 'ui.default_page_size',
          name: '默认每页数量',
          value: '20',
          valueType: 'WholeNumber',
          description: '业务列表默认值',
          isPublic: true,
          version: 1,
          updatedAtUtc: '2026-08-31T00:00:00Z',
        },
      ],
    })
  })

  it('renders read-only administration for settings viewers', async () => {
    const wrapper = mountView(['settings.view'])
    await flushPromises()

    expect(getSettingsAdministration).toHaveBeenCalledOnce()
    expect(wrapper.text()).toContain('客户所属行业')
    expect(wrapper.text()).not.toContain('CONTROLLED CONFIGURATION')
    expect(wrapper.text()).not.toContain('新增字典项')
  })

  it('shows dictionary mutation control only to managers', async () => {
    const wrapper = mountView(['settings.view', 'settings.manage'])
    await flushPromises()

    expect(wrapper.text()).toContain('新增字典项')
  })
})

function mountView(grantedPermissions: string[]) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const authStore = useAuthStore()
  authStore.initialized = true
  authStore.currentUser = {
    id: 1,
    username: 'tester',
    displayName: '测试用户',
    securityVersion: 1,
    roles: [],
    permissions: grantedPermissions,
  }

  return mount(SettingsAdministrationView, {
    global: {
      plugins: [pinia],
      directives: { loading: () => undefined },
      stubs: {
        'el-alert': { template: '<div />' },
        'el-button': { template: '<button><slot /></button>' },
        'el-card': { template: '<section><slot name="header" /><slot /></section>' },
        'el-dialog': { template: '<div />' },
        'el-tab-pane': { template: '<div><slot /></div>' },
        'el-tabs': { template: '<div><slot /></div>' },
        'el-table': { template: '<div />' },
      },
    },
  })
}
