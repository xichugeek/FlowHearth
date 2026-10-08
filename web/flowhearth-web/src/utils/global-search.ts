import type {
  GlobalSearchKind,
  GlobalSearchResult,
  GlobalSearchTargetType,
} from '../types/search'

const targetPaths: Record<GlobalSearchTargetType, string> = {
  Customer: '/customers',
  Project: '/projects',
  Equipment: '/equipment',
  ServiceTicket: '/service',
  PurchaseOrder: '/finance/purchases',
  Payable: '/finance/payables',
  Payment: '/finance/payments',
  Shipment: '/finance/shipments',
}

const kindLabels: Record<GlobalSearchKind, string> = {
  Customer: '客户',
  Contact: '联系人',
  Project: '项目',
  Equipment: '设备',
  ServiceTicket: '服务',
  PurchaseOrder: '采购单',
  Payable: '应付',
  Payment: '付款',
  Shipment: '出货',
}

export function routeForSearchResult(result: GlobalSearchResult) {
  return {
    path: targetPaths[result.targetType],
    query: { entityId: String(result.targetId) },
  }
}

export function globalSearchKindLabel(kind: GlobalSearchKind) {
  return kindLabels[kind]
}
