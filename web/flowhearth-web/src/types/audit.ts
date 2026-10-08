export interface AuditLogSummary {
  id: number
  occurredAtUtc: string
  actorUserId?: number | null
  actorUsername?: string | null
  actorDisplayName?: string | null
  action: string
  entityType: string
  entityId: number
  entityCode?: string | null
  summary: string
  correlationId?: string | null
  hasBefore: boolean
  hasAfter: boolean
}

export interface AuditLogDetails extends Omit<AuditLogSummary, 'hasBefore' | 'hasAfter'> {
  beforeJson?: string | null
  afterJson?: string | null
}

export interface AuditLogPage {
  items: AuditLogSummary[]
  page: number
  pageSize: number
  total: number
}

export interface AuditLogQuery {
  page: number
  pageSize: number
  search?: string
  entityType?: string
  action?: string
  occurredFromUtc?: string
  occurredToUtc?: string
  sortBy?: string
  sortDescending?: boolean
}
