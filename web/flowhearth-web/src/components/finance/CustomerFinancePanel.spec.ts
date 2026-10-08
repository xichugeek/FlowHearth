import { createPinia, setActivePinia } from 'pinia'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { getCustomerFinance, listCustomerProjectFinance } from '../../api/finance'
import { useAuthStore } from '../../stores/auth'
import CustomerFinancePanel from './CustomerFinancePanel.vue'

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))
vi.mock('../../api/finance', () => ({
  getCustomerFinance: vi.fn(),
  listCustomerProjectFinance: vi.fn(),
}))

const summary = {
  customerId: 7,
  customerCode: 'CU-2026-0007',
  customerName: '测试客户',
  projectCount: 2,
  activeProjectCount: 1,
  contractAmount: 200,
  receiptAmount: 100,
  receivedAllocatedAmount: 80,
  unallocatedReceiptAmount: 20,
  receivableAmount: 150,
  receivableOutstandingAmount: 70,
  receivableOverdueAmount: 20,
  receivableNotDueAmount: 50,
  purchaseAmount: 120,
  payableAmount: 90,
  paidAllocatedAmount: 60,
  payableOutstandingAmount: 30,
  payableOverdueAmount: 10,
  payableNotDueAmount: 20,
  shipmentCount: 2,
  receivedShipmentCount: 1,
  lastShipmentDate: '2026-09-01',
  estimatedGrossProfit: 80,
  estimatedGrossMargin: 40,
  warnings: [],
}

describe('CustomerFinancePanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.mocked(getCustomerFinance).mockClear()
    vi.mocked(listCustomerProjectFinance).mockClear()
    vi.mocked(getCustomerFinance).mockResolvedValue(summary)
    vi.mocked(listCustomerProjectFinance).mockResolvedValue({
      items: [], page: 1, pageSize: 10, total: 0,
    })
  })

  it('renders cash, allocation, gross margin and overdue as separate facts', async () => {
    const auth = useAuthStore()
    auth.currentUser = {
      id: 1, username: 'tester', displayName: '测试', securityVersion: 1,
      roles: [], permissions: ['projects.view'],
    }
    const wrapper = mount(CustomerFinancePanel, {
      props: { customerId: 7 },
      global: {
        directives: { loading: () => undefined },
        stubs: {
          'el-button': { template: '<button><slot /></button>' },
          'el-table': { template: '<section><slot /></section>' },
          'el-table-column': true,
          'el-empty': true,
          'el-alert': { props: ['title'], template: '<p>{{ title }}</p>' },
          'el-pagination': true,
        },
      },
    })
    await flushPromises()

    expect(getCustomerFinance).toHaveBeenCalledWith(7)
    expect(listCustomerProjectFinance).toHaveBeenCalledWith(7, expect.objectContaining({ page: 1, pageSize: 10 }))
    expect(wrapper.text()).toContain('累计到账')
    expect(wrapper.text()).toContain('¥100.00')
    expect(wrapper.text()).toContain('已核销实收')
    expect(wrapper.text()).toContain('¥80.00')
    expect(wrapper.text()).toContain('未分配收款')
    expect(wrapper.text()).toContain('¥20.00')
    expect(wrapper.text()).toContain('预计毛利率')
    expect(wrapper.text()).toContain('40.00%')
    expect(wrapper.text()).toContain('存在逾期应收')
  })

  it('does not request project finance rows without project permission', async () => {
    const wrapper = mount(CustomerFinancePanel, {
      props: { customerId: 7 },
      global: {
        directives: { loading: () => undefined },
        stubs: {
          'el-button': true,
          'el-alert': { props: ['title'], template: '<p>{{ title }}</p>' },
        },
      },
    })
    await flushPromises()

    expect(listCustomerProjectFinance).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('无项目查看权限')
  })
})
