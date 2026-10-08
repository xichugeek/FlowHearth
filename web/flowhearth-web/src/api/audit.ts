import type { AuditLogDetails, AuditLogPage, AuditLogQuery } from '../types/audit'
import { apiClient } from './client'

export async function listAuditLogs(query: AuditLogQuery) {
  const response = await apiClient.get<AuditLogPage>('/audit-logs', { params: query })
  return response.data
}

export async function getAuditLog(auditLogId: number) {
  const response = await apiClient.get<AuditLogDetails>(`/audit-logs/${auditLogId}`)
  return response.data
}
