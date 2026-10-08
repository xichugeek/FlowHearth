import { describe, expect, it } from 'vitest'

import {
  allocationDraftTotals,
  calculatePercentagePlan,
  formatFinanceAmount,
  formatFinanceDate,
  formatFinancePercentage,
  getReceivablePlanValidationError,
} from './finance-format'

describe('finance formatting and entry helpers', () => {
  it('formats money with two decimals', () => {
    expect(formatFinanceAmount(123456.7)).toContain('123,456.70')
    expect(formatFinanceAmount(0)).toContain('0.00')
    expect(formatFinanceAmount(null)).toBe('—')
  })

  it('formats nullable percentages and dates without NaN', () => {
    expect(formatFinancePercentage(32.58)).toBe('32.58%')
    expect(formatFinancePercentage(-8.42)).toBe('-8.42%')
    expect(formatFinancePercentage(null)).toBe('—')
    expect(formatFinancePercentage(Number.NaN)).toBe('—')
    expect(formatFinanceDate('2026-09-02T00:00:00Z')).toBe('2026-09-02')
    expect(formatFinanceDate(null)).toBe('—')
  })

  it('assigns percentage rounding remainder to the final plan item', () => {
    const amounts = calculatePercentagePlan(100, [33.33, 33.33, 33.34])
    expect(amounts).toEqual([33.33, 33.33, 33.34])
    expect(amounts.reduce((sum, value) => sum + value, 0)).toBe(100)
  })

  it('rejects an allocation draft above the receipt balance', () => {
    expect(allocationDraftTotals(100, [60, 40])).toEqual({ allocationAmount: 100, remainingAmount: 0, valid: true })
    expect(allocationDraftTotals(100, [60, 41])).toEqual({ allocationAmount: 101, remainingAmount: -1, valid: false })
  })

  it('requires every receivable plan stage to be complete', () => {
    expect(getReceivablePlanValidationError(100, [{
      receivableType: 'AdvancePayment',
      title: '',
      amount: 30,
      dueDate: '2026-09-10',
    }])).toBe('请完整填写每个阶段的类型、标题、金额和到期日。')
  })

  it('rejects a receivable plan above the remaining contract amount', () => {
    expect(getReceivablePlanValidationError(100, [{
      receivableType: 'AdvancePayment',
      title: '预付款',
      amount: 100.01,
      dueDate: '2026-09-10',
    }])).toBe('应收计划总额不能超过合同金额。')
  })
})
