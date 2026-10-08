import dayjs from 'dayjs'

import type {
  CustomerFollowUpMethod,
  CustomerLevel,
  CustomerStatus,
  CustomerSummary,
} from '../types/customers'

const methodLabels: Record<CustomerFollowUpMethod, string> = {
  Phone: '电话',
  Visit: '拜访',
  Email: '邮件',
  WeChat: '微信',
  Other: '其他',
}

export const customerStatusOptions: ReadonlyArray<{
  value: CustomerStatus
  label: string
}> = [
  { value: 'Prospect', label: '潜在客户' },
  { value: 'Active', label: '合作中' },
  { value: 'Dormant', label: '沉睡客户' },
  { value: 'Lost', label: '已流失' },
]

export const customerLevelOptions: ReadonlyArray<{
  value: CustomerLevel
  label: string
}> = [
  { value: 'Unrated', label: '未分级' },
  { value: 'A', label: 'A级 · 重点' },
  { value: 'B', label: 'B级 · 成长' },
  { value: 'C', label: 'C级 · 普通' },
]

const customerStatusLabels = Object.fromEntries(
  customerStatusOptions.map((item) => [item.value, item.label]),
) as Record<CustomerStatus, string>

const customerLevelLabels = Object.fromEntries(
  customerLevelOptions.map((item) => [item.value, item.label]),
) as Record<CustomerLevel, string>

export function formatChinaDateTime(value?: string | null) {
  if (!value) return '—'

  return dayjs(value).format('YYYY-MM-DD HH:mm')
}

export function followUpMethodLabel(method: CustomerFollowUpMethod) {
  return methodLabels[method]
}

export function customerStatusLabel(status: CustomerStatus) {
  return customerStatusLabels[status]
}

export function customerStatusTagType(status: CustomerStatus) {
  return {
    Prospect: 'warning',
    Active: 'success',
    Dormant: 'info',
    Lost: 'danger',
  }[status] as 'warning' | 'success' | 'info' | 'danger'
}

export function customerLevelLabel(level: CustomerLevel) {
  return customerLevelLabels[level]
}

export function customerContactMethod(
  customer: Partial<
    Pick<CustomerSummary, 'primaryContactMethod' | 'phone' | 'email'>
  >,
) {
  return (
    customer.primaryContactMethod || customer.phone || customer.email || '—'
  )
}
