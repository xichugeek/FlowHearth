import { describe, expect, it } from 'vitest'

import type { FinanceAgingOverview, FinanceCashFlowPoint } from '../types/finance'
import {
  buildAgingOption,
  buildCashFlowOption,
  projectRankingValue,
} from './finance-dashboard'

describe('finance dashboard chart mapping', () => {
  it('preserves empty and negative cash-flow months across a year boundary', () => {
    const points: FinanceCashFlowPoint[] = [
      { month: '2025-12', receivedAmount: 0, paidAmount: 50, netAmount: -50 },
      { month: '2026-01', receivedAmount: 0, paidAmount: 0, netAmount: 0 },
    ]

    const option = buildCashFlowOption(points) as {
      xAxis: { data: string[] }
      series: Array<{ data: number[] }>
    }

    expect(option.xAxis.data).toEqual(['25/12', '26/01'])
    expect(option.series[0].data).toEqual([0, 0])
    expect(option.series[2].data).toEqual([-50, 0])
  })

  it('keeps every aging bucket when all balances are zero', () => {
    const aging: FinanceAgingOverview = {
      asOfDate: '2026-09-02',
      totalOutstanding: 0,
      buckets: ['NotDue', '1-30', '31-60', '61-90', '90+']
        .map(bucket => ({ bucket: bucket as FinanceAgingOverview['buckets'][number]['bucket'], amount: 0, itemCount: 0 })),
    }

    const option = buildAgingOption(aging) as {
      yAxis: { data: string[] }
      series: Array<{ data: Array<{ value: number }> }>
    }

    expect(option.yAxis.data).toHaveLength(5)
    expect(option.series[0].data.every(item => item.value === 0)).toBe(true)
  })

  it('formats negative project gross profit from server data', () => {
    const row = {
      projectId: 1,
      projectCode: 'TN-2026-0001',
      projectName: '负毛利项目',
      customerId: 2,
      customerName: '客户',
      projectStatus: 'Active' as const,
      contractAmount: 100,
      purchaseAmount: 120,
      grossProfit: -20,
      grossMargin: -20,
      receivableOutstandingAmount: 30,
      receivableOverdueAmount: 10,
    }

    expect(projectRankingValue(row, 'grossProfit')).toContain('-¥20.00')
    expect(projectRankingValue(row, 'grossMargin')).toBe('-20.00%')
  })
})
