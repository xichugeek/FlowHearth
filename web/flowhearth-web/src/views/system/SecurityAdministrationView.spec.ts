import { flushPromises, mount } from '@vue/test-utils'
import { ElDialog, ElInput, ElPagination, ElTable } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as securityApi from '../../api/security-administration'
import { useAuthStore } from '../../stores/auth'
import SecurityAdministrationView from './SecurityAdministrationView.vue'

vi.mock('../../api/security-administration', () => ({
  listUsers: vi.fn(),
  listRoles: vi.fn(),
  listPermissions: vi.fn(),
  createUser: vi.fn(),
  updateUser: vi.fn(),
  resetUserPassword: vi.fn(),
  createRole: vi.fn(),
  updateRole: vi.fn(),
}))

const managerPermissions = [
  'security.users.view', 'security.users.manage',
  'security.roles.view', 'security.roles.manage',
]

describe('SecurityAdministrationView toolbar and tables', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(securityApi.listUsers).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 10 })
    vi.mocked(securityApi.listRoles).mockResolvedValue([])
    vi.mocked(securityApi.listPermissions).mockResolvedValue([])
  })

  it('groups user actions and preserves query, Enter, clear and page reset behavior', async () => {
    const wrapper = mountView(managerPermissions)
    try {
      await flushPromises()
      const actions = wrapper.findAll('.security-toolbar-actions button')
      expect(actions.map(button => button.text())).toEqual(['查询', '新建用户'])
      const input = wrapper.get('input[placeholder="搜索用户名、姓名或邮箱"]')
      wrapper.findComponent(ElPagination).vm.$emit('update:current-page', 3)
      await input.setValue('  示例  ')
      await actions[0]!.trigger('click')
      await flushPromises()
      expect(securityApi.listUsers).toHaveBeenLastCalledWith(1, 10, '示例')

      await input.setValue('preview')
      await input.trigger('keyup.enter')
      await flushPromises()
      expect(securityApi.listUsers).toHaveBeenLastCalledWith(1, 10, 'preview')

      const search = wrapper.findComponent(ElInput)
      search.vm.$emit('update:modelValue', '')
      search.vm.$emit('clear')
      await flushPromises()
      expect(securityApi.listUsers).toHaveBeenLastCalledWith(1, 10, '')
      expect(wrapper.findComponent(ElPagination).props('currentPage')).toBe(1)
    } finally { wrapper.unmount() }
  })

  it('uses bordered striped tables and opens creation without writing a user', async () => {
    const wrapper = mountView(managerPermissions)
    try {
      await flushPromises()
      const tables = wrapper.findAllComponents({ name: 'ElTable' })
      expect(tables).toHaveLength(2)
      for (const table of tables) {
        expect(table.classes()).toContain('security-data-table')
        expect(table.props('border')).toBe(true)
        expect(table.props('stripe')).toBe(true)
      }
      await wrapper.findAll('.security-toolbar-actions button')[1]!.trigger('click')
      expect(wrapper.findAllComponents(ElDialog)[0]!.props('modelValue')).toBe(true)
      expect(securityApi.createUser).not.toHaveBeenCalled()
    } finally { wrapper.unmount() }
  })

  it('preserves read-only and role-visibility permission gates', async () => {
    const viewer = mountView(['security.users.view'])
    const managerWithoutRoles = mountView(['security.users.view', 'security.users.manage'])
    try {
      await flushPromises()
      expect(viewer.findAll('.security-toolbar-actions button').map(button => button.text())).toEqual(['查询'])
      expect(viewer.findAllComponents(ElTable)).toHaveLength(1)
      expect(securityApi.listRoles).not.toHaveBeenCalled()
      expect(managerWithoutRoles.findAll('.security-toolbar-actions button')[1]!.attributes('disabled')).toBeDefined()
    } finally {
      viewer.unmount()
      managerWithoutRoles.unmount()
    }
  })
})

function mountView(grantedPermissions: string[]) {
  const pinia = createPinia()
  setActivePinia(pinia)
  const auth = useAuthStore()
  auth.initialized = true
  auth.currentUser = {
    id: 1, username: 'preview', displayName: '布局验收',
    securityVersion: 1, roles: [], permissions: grantedPermissions,
  }
  return mount(SecurityAdministrationView, {
    global: {
      plugins: [pinia],
      directives: { loading: () => undefined },
      stubs: { ElDialog: true },
    },
  })
}
