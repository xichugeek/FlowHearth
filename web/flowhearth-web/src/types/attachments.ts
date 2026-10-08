export type AttachmentEntityType =
  | 'Customer'
  | 'Opportunity'
  | 'Project'
  | 'Equipment'
  | 'ServiceTicket'
  | 'Supplier'
  | 'PurchaseOrder'
  | 'Shipment'

export interface AttachmentSummary {
  id: number
  entityType: AttachmentEntityType
  entityId: number
  entityCode?: string | null
  originalFileName: string
  contentType: string
  sizeBytes: number
  sha256: string
  description?: string | null
  version: number
  uploadedAtUtc: string
  uploadedByUserId?: number | null
  uploadedByDisplayName?: string | null
}
