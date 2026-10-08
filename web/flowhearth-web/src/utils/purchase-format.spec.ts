import { describe, expect, it } from 'vitest'

import {
  calculatePurchaseLineAmount,
  createPurchaseItem,
  getReceiptValidationError,
  purchaseStatusLabels,
  removePurchaseItem,
} from './purchase-format'

describe('purchase order helpers', () => {
  it('rounds line amounts to cents', () => expect(calculatePurchaseLineAmount(1.2345, 12.34)).toBe(15.23))
  it('adds and safely removes item rows', () => {
    const items = [createPurchaseItem(), createPurchaseItem()]
    expect(removePurchaseItem(items, 0)).toBe(true)
    expect(removePurchaseItem(items, 0)).toBe(false)
  })
  it('validates partial receipts without over-receiving', () => {
    expect(getReceiptValidationError([{ remainingQuantity: 2, quantityReceived: 0 }])).toContain('至少')
    expect(getReceiptValidationError([{ remainingQuantity: 2, quantityReceived: 3 }])).toContain('不能超过')
    expect(getReceiptValidationError([{ remainingQuantity: 2, quantityReceived: 1.5 }])).toBeUndefined()
  })
  it('provides all workflow labels', () => expect(purchaseStatusLabels.Received).toBe('已收货'))
})
