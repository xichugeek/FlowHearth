import type { PayableInput, PayableType, PaymentInput, PaymentMethod, PaymentStatus } from '../types/finance'

export const payableTypeLabels: Record<PayableType, string> = {
  AdvancePayment: '预付款',
  DeliveryPayment: '到货款',
  AcceptancePayment: '验收款',
  RetentionPayment: '质保款',
  PurchasePayment: '采购款',
  Other: '其它',
}

export const paymentStatusLabels: Record<PaymentStatus, string> = {
  Unpaid: '未付款',
  PartiallyPaid: '部分付款',
  Paid: '已付清',
}

export const paymentMethodLabels: Record<PaymentMethod, string> = {
  BankTransfer: '银行转账',
  Cash: '现金',
  Cheque: '支票',
  Other: '其它',
}

export function getPayableValidationError(input: PayableInput) {
  if (!input.supplierId || !input.projectId || !input.title.trim() || !input.dueDate || input.amount <= 0) {
    return '请完整填写供应商、项目、标题、金额和到期日。'
  }

  return null
}

export function getPaymentValidationError(input: PaymentInput) {
  if (!input.supplierId || !input.paymentDate || input.amount <= 0) {
    return '请完整填写供应商、付款日期和金额。'
  }

  return null
}

export function getPaymentAllocationValidationError(
  paymentRemaining: number,
  rows: Array<{ amount: number; payableRemaining: number }>,
) {
  const active = rows.filter(row => row.amount > 0)
  if (active.length === 0) return '请至少填写一笔核销金额。'
  if (active.some(row => row.amount > row.payableRemaining)) return '本次核销金额不能超过应付未付余额。'
  if (active.reduce((sum, row) => sum + row.amount, 0) > paymentRemaining) return '本次核销总额不能超过付款可用余额。'
  return null
}

export function payableStatusLabel(status: PaymentStatus, isOverdue: boolean) {
  return `${paymentStatusLabels[status]}${isOverdue ? '逾期' : ''}`
}
