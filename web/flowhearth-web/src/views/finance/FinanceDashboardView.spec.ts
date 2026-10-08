import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { getFinanceDashboard, getFinanceProjectRanking } from '../../api/finance'
import FinanceDashboardView from './FinanceDashboardView.vue'

vi.mock('../../api/finance', () => ({
  getFinanceDashboard: vi.fn(),
  getFinanceProjectRanking: vi.fn(),
}))

const snapshot = {
  asOfDate: '2026-09-02',
  generatedAtUtc: '2026-09-02T03:00:00Z',
  summary: {
    asOfDate: '2026-09-02', receivableAmount: 100, receivableOutstanding: 40,
    receivableOverdue: 10, cashReceivedThisMonth: 80, cashReceivedYearToDate: 300,
    receivedAllocatedThisMonth: 60, receivedAllocatedYearToDate: 200,
    purchaseThisMonth: 50, purchaseYearToDate: 150, payableOutstanding: 20,
    payableOverdue: 5, cashPaidThisMonth: 30, cashPaidYearToDate: 100,
    paidAllocatedThisMonth: 20, paidAllocatedYearToDate: 80,
    activeProjectContractAmount: 100, estimatedGrossProfit: -20,
    estimatedGrossMargin: -20, shipmentThisMonth: 1, cashNetFlowThisMonth: 50,
    warnings: [],
  },
  receivableAging: { asOfDate: '2026-09-02', totalOutstanding: 40, buckets: [] },
  payableAging: { asOfDate: '2026-09-02', totalOutstanding: 20, buckets: [] },
  cashFlowTrend: [{ month: '2026-09', receivedAmount: 80, paidAmount: 30, netAmount: 50 }],
  risks: [{ code: 'overdue_receivable', title: '逾期应收', itemCount: 1, amount: 10, severity: 'Danger' as const, targetPath: '/finance/receivables' }],
  overdueReceivables: [],
  overduePayables: [],
  projectRanking: [],
  projectRankingMetric: 'contractAmount' as const,
  customerReceivableRanking: [],
  supplierPayableRanking: [],
}

describe('FinanceDashboardView', () => {
  beforeEach(() => {
    vi.mocked(getFinanceDashboard).mockReset().mockResolvedValue(snapshot)
    vi.mocked(getFinanceProjectRanking).mockReset().mockResolvedValue([])
  })

  it('renders server KPIs, definition notes, risks and responsive chart regions', async () => {
    const wrapper = mountView()
    await flushPromises()

    expect(getFinanceDashboard).toHaveBeenCalledOnce()
    expect(wrapper.text()).toContain('本月收款')
    expect(wrapper.text()).toContain('¥80.00')
    expect(wrapper.text()).toContain('本月净现金流')
    expect(wrapper.text()).toContain('预计项目毛利')
    expect(wrapper.text()).toContain('-¥20.00')
    expect(wrapper.text()).toContain('不含人工、差旅、税费及其他间接费用')
    expect(wrapper.text()).toContain('逾期应收')
    expect(wrapper.findAll('.finance-chart-stub')).toHaveLength(3)
    expect(wrapper.find('.finance-dashboard-table-scroll').exists()).toBe(true)
    const tableLabels = wrapper.findAll('el-table-column-stub')
      .map(column => column.attributes('label'))
    expect(tableLabels).toEqual(expect.arrayContaining([
      '合同金额', '应收余额', '采购金额', '预计毛利', '预计毛利率',
      '累计到账', '未分配收款', '累计付款', '未核销付款',
    ]))
  })

  it('loads a bounded server ranking when the metric changes', async () => {
    const wrapper = mountView()
    await flushPromises()

    await wrapper.get('[data-test="ranking-select"]').trigger('click')
    await flushPromises()

    expect(getFinanceProjectRanking).toHaveBeenCalledWith('grossProfit')
  })
})

function mountView() {
  return mount(FinanceDashboardView, {
    global: {
      directives: { loading: () => undefined },
      stubs: {
        RouterLink: { props: ['to'], template: '<a><slot /></a>' },
        FinanceChart: { template: '<div class="finance-chart-stub" />' },
        'el-button': { template: '<button><slot /></button>' },
        'el-icon': { template: '<i><slot /></i>' },
        'el-card': { template: '<section><slot name="header" /><slot /></section>' },
        'el-alert': { props: ['title'], template: '<p>{{ title }}</p>' },
        'el-empty': { props: ['description'], template: '<p>{{ description }}</p>' },
        'el-result': true,
        'el-tabs': { template: '<section><slot /></section>' },
        'el-tab-pane': { template: '<section><slot /></section>' },
        'el-table': { template: '<section><slot /></section>' },
        'el-table-column': true,
        'el-option': true,
        'el-select': {
          emits: ['change', 'update:modelValue'],
          template: '<button data-test="ranking-select" @click="$emit(\'change\', \'grossProfit\')"><slot /></button>',
        },
      },
    },
  })
}
