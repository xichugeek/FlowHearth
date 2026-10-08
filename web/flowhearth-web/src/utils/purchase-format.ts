import type { PurchaseOrderItemInput, PurchaseOrderStatus } from '../types/finance'

export const purchaseStatusLabels: Record<PurchaseOrderStatus, string> = {
  Draft: '草稿',
  Ordered: '已下单',
  PartiallyReceived: '部分收货',
  Received: '已收货',
  Cancelled: '已取消',
}

export function calculatePurchaseLineAmount(quantity: number, unitPrice: number) {
  return Math.round((quantity * unitPrice + Number.EPSILON) * 100) / 100
}

export function createPurchaseItem(): PurchaseOrderItemInput {
  return { itemName: '', quantity: 1, unit: '台', unitPrice: 0 }
}

export function removePurchaseItem(items: PurchaseOrderItemInput[], index: number) {
  if (items.length <= 1) return false
  items.splice(index, 1)
  return true
}

export function getReceiptValidationError(
  rows: Array<{ remainingQuantity: number; quantityReceived: number }>,
) {
  const selected = rows.filter((row) => row.quantityReceived > 0)
  if (!selected.length) return '请至少填写一行本次收货数量。'
  if (selected.some((row) => row.quantityReceived > row.remainingQuantity)) {
    return '本次收货数量不能超过未收数量。'
  }
  if (selected.some((row) => Math.round(row.quantityReceived * 10000) !== row.quantityReceived * 10000)) {
    return '收货数量最多支持四位小数。'
  }
  return undefined
}
