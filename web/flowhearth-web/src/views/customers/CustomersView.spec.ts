import { flushPromises, mount } from '@vue/test-utils'
import { ElCascader, ElSelect } from 'element-plus'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { getCustomer, listCustomers } from '../../api/customers'
import FollowUpDialog from '../../components/customers/FollowUpDialog.vue'
import { useAuthStore } from '../../stores/auth'
import CustomersView from './CustomersView.vue'

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
}))

vi.mock('../../api/customers', () => ({
  listCustomers: vi.fn(),
  getCustomer: vi.fn(),
  createCustomer: vi.fn(),
  updateCustomer: vi.fn(),
  setCustomerArchived: vi.fn(),
  createContact: vi.fn(),
  updateContact: vi.fn(),
  deleteContact: vi.fn(),
  createFollowUp: vi.fn(),
}))

describe('CustomersView', () => {
  beforeEach(() => {
    vi.mocked(listCustomers).mockResolvedValue({
      items: [
        {
          id: 1,
          code: 'CU-2026-0001',
          name: '测试客户',
          phone: '027-88888888',
          status: 'Active',
          level: 'A',
          isArchived: false,
          nextFollowUpAtUtc: null,
          contactCount: 2,
          version: 1,
          updatedAtUtc: '2026-09-01T00:00:00Z',
          primaryContactName: '张工',
          primaryContactMethod: '13800000000',
        },
      ],
      page: 1,
      pageSize: 10,
      total: 1,
    })
    vi.mocked(getCustomer).mockResolvedValue({
      id: 1,
      code: 'CU-2026-0001',
      name: '测试客户',
      shortName: null,
      industry: '工业自动化',
      status: 'Dormant',
      level: 'Unrated',
      phone: '027-88888888',
      email: null,
      website: null,
      provinceCode: '42',
      cityCode: '4201',
      districtCode: '420106',
      address: '测试地址',
      notes: null,
      isArchived: false,
      archivedAtUtc: null,
      version: 1,
      createdAtUtc: '2026-09-01T00:00:00Z',
      updatedAtUtc: '2026-09-01T00:00:00Z',
      contacts: [],
      followUps: [],
    })
  })

  it('renders the selected contact separately from the contact method', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const authStore = useAuthStore()
    authStore.initialized = true
    authStore.currentUser = {
      id: 1,
      username: 'viewer',
      displayName: '只读用户',
      securityVersion: 1,
      roles: [],
      permissions: ['customers.view'],
    }

    const wrapper = mount(CustomersView, {
      global: {
        plugins: [pinia],
        directives: { loading: () => undefined },
        stubs: {
          CustomerDetailDrawer: true,
          CustomerFormDialog: true,
          ContactFormDialog: true,
          FollowUpDialog: true,
        },
      },
    })
    await flushPromises()

    expect(listCustomers).toHaveBeenCalledOnce()
    expect(listCustomers).toHaveBeenCalledWith(expect.objectContaining({
      page: 1,
      pageSize: 10,
      archive: 'active',
      status: undefined,
      level: undefined,
    }))
    expect(wrapper.text()).toContain('联系人')
    expect(wrapper.text()).toContain('张工')
    expect(wrapper.text()).toContain('2 位联系人')
    expect(wrapper.text()).toContain('联系方式')
    expect(wrapper.text()).toContain('13800000000')
    expect(wrapper.text()).toContain('合作中')
    expect(wrapper.text()).toContain('A级 · 重点')
  })

  it('keeps classification filters with the primary action and applies them', async () => {
    const pinia = createPinia()
    setActivePinia(pinia)
    const authStore = useAuthStore()
    authStore.initialized = true
    authStore.currentUser = {
      id: 1,
      username: 'manager',
      displayName: '客户管理员',
      securityVersion: 1,
      roles: [],
      permissions: ['customers.view', 'customers.manage'],
    }

    const wrapper = mount(CustomersView, {
      global: {
        plugins: [pinia],
        directives: { loading: () => undefined },
        stubs: {
          CustomerDetailDrawer: true,
          CustomerFormDialog: true,
          ContactFormDialog: true,
          FollowUpDialog: true,
        },
      },
    })
    await flushPromises()

    const filterRow = wrapper.get('.customer-filter-row')
    expect(filterRow.text()).toContain('新建客户')
    const selects = filterRow.findAllComponents(ElSelect)
    expect(selects).toHaveLength(3)
    selects[1]!.vm.$emit('update:modelValue', 'Prospect')
    selects[1]!.vm.$emit('change', 'Prospect')
    selects[2]!.vm.$emit('update:modelValue', 'B')
    selects[2]!.vm.$emit('change', 'B')
    const region = filterRow.getComponent(ElCascader)
    region.vm.$emit('update:modelValue', ['42', '4201', '420106'])
    region.vm.$emit('change', ['42', '4201', '420106'])
    await flushPromises()

    expect(listCustomers).toHaveBeenLastCalledWith(expect.objectContaining({
      page: 1,
      pageSize: 10,
      archive: 'active',
      status: 'Prospect',
      level: 'B',
      provinceCode: '42',
      cityCode: '4201',
      districtCode: '420106',
    }))

    const followUpButton = wrapper.findAll('button')
      .find((button) => button.text() === '跟进')
    if (!followUpButton) throw new Error('Follow-up action was not rendered.')
    await followUpButton.trigger('click')
    await flushPromises()

    expect(getCustomer).toHaveBeenCalledWith(1)
    const followUpDialog = wrapper.getComponent(FollowUpDialog)
    expect(followUpDialog.props('modelValue')).toBe(true)
    expect(followUpDialog.props('customerStatus')).toBe('Dormant')
  })
})
