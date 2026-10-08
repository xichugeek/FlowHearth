import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { getDashboard } from '../../api/dashboard'
import DashboardView from './DashboardView.vue'

vi.mock('../../api/dashboard', () => ({
  getDashboard: vi.fn(),
}))

describe('DashboardView', () => {
  beforeEach(() => {
    vi.mocked(getDashboard).mockResolvedValue({
      metrics: {
        totalCustomers: 12,
        newCustomersThisMonth: 2,
        activeOpportunities: 3,
        expectedOpportunityAmount: 450000,
        activeProjects: null,
        projectsNearingDelivery: null,
        openTickets: 5,
        openPriorityTickets: 2,
        dueFollowUpsToday: 1,
        nextSevenDaysFollowUps: 3,
      },
      opportunityStages: [{ key: 'Lead', count: 3 }],
      projectStatuses: [],
      monthlyNewCustomers: [
        { month: '2026-07', count: 1 },
        { month: '2026-08', count: 2 },
      ],
    })
  })

  it('renders permission-scoped server metrics and charts', async () => {
    const wrapper = mount(DashboardView, {
      global: {
        directives: { loading: () => undefined },
        stubs: {
          RouterLink: { template: '<a><slot /></a>' },
          'el-button': { template: '<button><slot /></button>' },
          'el-card': { template: '<section><slot name="header" /><slot /></section>' },
          'el-icon': { template: '<i><slot /></i>' },
          'el-empty': { template: '<div />' },
        },
      },
    })
    await flushPromises()

    expect(getDashboard).toHaveBeenCalledOnce()
    expect(wrapper.text()).toContain('客户总数')
    expect(wrapper.text()).toContain('12')
    expect(wrapper.text()).toContain('活跃商机金额')
    expect(wrapper.text()).toContain('商机阶段分布')
    expect(wrapper.text()).toContain('线索')
    expect(wrapper.text()).toContain('近 6 个月新增客户')
    expect(wrapper.text()).not.toContain('进行中项目')
  })
})
