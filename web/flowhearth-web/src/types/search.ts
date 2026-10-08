export type GlobalSearchKind =
  | 'Customer'
  | 'Contact'
  | 'Project'
  | 'Equipment'
  | 'ServiceTicket'
  | 'PurchaseOrder'
  | 'Payable'
  | 'Payment'
  | 'Shipment'

export type GlobalSearchTargetType =
  | 'Customer'
  | 'Project'
  | 'Equipment'
  | 'ServiceTicket'
  | 'PurchaseOrder'
  | 'Payable'
  | 'Payment'
  | 'Shipment'

export interface GlobalSearchResult {
  kind: GlobalSearchKind
  targetType: GlobalSearchTargetType
  targetId: number
  code: string
  title: string
  subtitle?: string | null
  isArchived: boolean
}
