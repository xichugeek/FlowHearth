import { createPinia, setActivePinia } from 'pinia'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { createReceivablePlan, getProjectFinance } from '../../api/finance'
import CustomerFinanceSource from './CustomerFinancePanel.vue?raw'
import ProjectFinancePanel from './ProjectFinancePanel.vue'

vi.mock('../../api/finance', () => ({
  createReceivablePlan: vi.fn(),
  getProjectFinance: vi.fn(),
}))

describe('ProjectFinancePanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.mocked(createReceivablePlan).mockReset()
    vi.mocked(getProjectFinance).mockResolvedValue({
      projectId: 8,
      customerId: 7,
      projectCode: 'TN-2026-0008',
      projectName: '经营测试项目',
      customerCode: 'CU-2026-0007',
      customerName: '测试客户',
      contractAmount: 100,
      receivableAmount: 90,
      receivedAllocatedAmount: 40,
      receivableOutstandingAmount: 50,
      receivableOverdueAmount: 20,
      receivableNotDueAmount: 30,
      purchaseAmount: 120,
      purchaseOrderCount: 2,
      purchaseReceiptCount: 1,
      payableAmount: 80,
      paidAllocatedAmount: 30,
      payableOutstandingAmount: 50,
      payableOverdueAmount: 10,
      payableNotDueAmount: 40,
      shipmentCount: 2,
      receivedShipmentCount: 1,
      deliveredEquipmentCount: 3,
      lastShipmentDate: '2026-09-01',
      grossProfit: -20,
      grossMargin: -20,
      cashNetInflow: 10,
      warnings: [],
      receivables: [],
      purchases: [],
      payables: [],
      receiptAllocations: [{
        allocationId: 1, receiptId: 2, receiptCode: 'RC-2026-0001',
        receiptDate: '2026-09-01', receiptAmount: 50, receivableId: 3,
        receivableCode: 'AR-2026-0001', allocatedAmount: 40,
        allocatedAtUtc: '2026-09-01T01:00:00Z',
      }],
      paymentAllocations: [{
        allocationId: 4, paymentId: 5, paymentCode: 'PM-2026-0001',
        paymentDate: '2026-09-01', paymentAmount: 35, payableId: 6,
        payableCode: 'AP-2026-0001', allocatedAmount: 30,
        allocatedAtUtc: '2026-09-01T02:00:00Z',
      }],
      shipments: [{
        shipmentId: 7, shipmentCode: 'SH-2026-0001', shipmentDate: '2026-09-01',
        status: 'Received', itemCount: 2, equipmentCount: 1,
      }],
    })
  })

  it('renders allocation-based cash, negative gross profit and delivery facts', async () => {
    const wrapper = mount(ProjectFinancePanel, {
      props: { projectId: 8 },
      global: {
        directives: { loading: () => undefined },
        stubs: {
          'el-button': { template: '<button><slot /></button>' },
          'el-alert': { props: ['title'], template: '<p>{{ title }}</p>' },
          'el-table': { template: '<section><slot /></section>' },
          'el-table-column': true,
          'el-empty': true,
          'el-dialog': true,
        },
      },
    })
    await flushPromises()

    expect(getProjectFinance).toHaveBeenCalledWith(8)
    expect(wrapper.text()).toContain('已核销实收')
    expect(wrapper.text()).toContain('¥40.00')
    expect(wrapper.text()).toContain('已核销实付')
    expect(wrapper.text()).toContain('¥30.00')
    expect(wrapper.text()).toContain('项目现金净流入')
    expect(wrapper.text()).toContain('¥10.00')
    expect(wrapper.text()).toContain('-¥20.00')
    expect(wrapper.text()).toContain('-20.00%')
    expect(wrapper.text()).toContain('已签收出货')
    expect(wrapper.text()).toContain('2026-09-01')
    expect(wrapper.text()).toContain('存在逾期应收')
    expect(wrapper.text()).toContain('存在逾期应付')
    expect(wrapper.text()).toContain('收款核销')
    expect(wrapper.text()).toContain('付款核销')
    expect(wrapper.text()).toContain('出货记录')
  })

  it('keeps the customer project table horizontally scrollable', () => {
    expect(CustomerFinanceSource).toContain('finance-table-scroll')
    expect(CustomerFinanceSource).toContain('sortable="custom"')
  })
})
