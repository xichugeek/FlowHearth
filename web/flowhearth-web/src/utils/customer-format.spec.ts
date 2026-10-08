import { describe, expect, it } from 'vitest'

import {
  customerContactMethod,
  customerLevelLabel,
  customerStatusLabel,
  customerStatusTagType,
  followUpMethodLabel,
  formatChinaDateTime,
} from './customer-format'

describe('customer format helpers', () => {
  it('formats ISO timestamps for the local workbench', () => {
    expect(formatChinaDateTime('2026-08-29T13:30:00+08:00')).toBe(
      '2026-08-29 13:30',
    )
    expect(formatChinaDateTime(null)).toBe('—')
  })

  it('maps follow-up methods to user-facing labels', () => {
    expect(followUpMethodLabel('Phone')).toBe('电话')
    expect(followUpMethodLabel('Visit')).toBe('拜访')
  })

  it('maps customer classification to stable labels and tag styles', () => {
    expect(customerStatusLabel('Prospect')).toBe('潜在客户')
    expect(customerStatusLabel('Active')).toBe('合作中')
    expect(customerStatusLabel('Dormant')).toBe('沉睡客户')
    expect(customerStatusTagType('Lost')).toBe('danger')
    expect(customerLevelLabel('Unrated')).toBe('未分级')
    expect(customerLevelLabel('A')).toBe('A级 · 重点')
  })

  it('prefers the selected contact method and falls back to customer details', () => {
    const customer = {
      id: 1,
      code: 'CU-2026-0001',
      name: '测试客户',
      phone: '027-88888888',
      isArchived: false,
      contactCount: 1,
      version: 1,
      updatedAtUtc: '2026-09-01T00:00:00Z',
      primaryContactName: '张工',
      primaryContactMethod: '13800000000',
    }

    expect(customerContactMethod(customer)).toBe('13800000000')
    expect(customerContactMethod({ ...customer, primaryContactMethod: null })).toBe(
      '027-88888888',
    )
    expect(
      customerContactMethod({
        ...customer,
        primaryContactMethod: null,
        phone: null,
        email: null,
      }),
    ).toBe('—')
  })
})
