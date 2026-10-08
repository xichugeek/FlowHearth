import { describe, expect, it } from 'vitest'

import PayablesViewSource from '../views/finance/PayablesView.vue?raw'
import PaymentsViewSource from '../views/finance/PaymentsView.vue?raw'

import type { PayableInput, PaymentInput } from '../types/finance'
import {
  getPayableValidationError,
  getPaymentAllocationValidationError,
  getPaymentValidationError,
  payableStatusLabel,
  payableTypeLabels,
  paymentMethodLabels,
} from './payable-format'

const payable: PayableInput = {
  supplierId: 1,
  projectId: 2,
  payableType: 'DeliveryPayment',
  title: '到货款',
  amount: 100,
  dueDate: '2026-09-02',
}
const payment: PaymentInput = {
  supplierId: 1,
  paymentDate: '2026-09-02',
  amount: 100,
  paymentMethod: 'BankTransfer',
}

describe('payable and payment entry helpers', () => {
  it('validates payable required relationships and amount', () => {
    expect(getPayableValidationError(payable)).toBeNull()
    expect(getPayableValidationError({ ...payable, supplierId: 0 })).toContain('供应商')
    expect(getPayableValidationError({ ...payable, amount: 0 })).toContain('金额')
  })

  it('validates payment required fields and amount', () => {
    expect(getPaymentValidationError(payment)).toBeNull()
    expect(getPaymentValidationError({ ...payment, paymentDate: '' })).toContain('付款日期')
  })

  it('requires at least one payment allocation', () => {
    expect(getPaymentAllocationValidationError(100, [{ amount: 0, payableRemaining: 100 }])).toContain('至少')
  })

  it('rejects allocation above a payable balance', () => {
    expect(getPaymentAllocationValidationError(100, [{ amount: 61, payableRemaining: 60 }])).toContain('应付未付')
  })

  it('rejects multi-allocation sum above payment balance', () => {
    expect(getPaymentAllocationValidationError(100, [
      { amount: 60, payableRemaining: 60 },
      { amount: 41, payableRemaining: 50 },
    ])).toContain('付款可用')
  })

  it('accepts a multi-allocation that exactly uses the remaining payment', () => {
    expect(getPaymentAllocationValidationError(100, [
      { amount: 60, payableRemaining: 60 },
      { amount: 40, payableRemaining: 50 },
    ])).toBeNull()
  })

  it('keeps status, type and method labels centralized', () => {
    expect(payableStatusLabel('PartiallyPaid', true)).toBe('部分付款逾期')
    expect(payableTypeLabels.RetentionPayment).toBe('质保款')
    expect(paymentMethodLabels.BankTransfer).toBe('银行转账')
  })

  it('keeps payable and payment tables horizontally scrollable on narrow screens', () => {
    expect(PayablesViewSource).toContain('payable-table-scroll')
    expect(PayablesViewSource).toContain('overflow-x: auto')
    expect(PaymentsViewSource).toContain('payment-table-scroll')
    expect(PaymentsViewSource).toContain('overflow-x: auto')
  })
})
