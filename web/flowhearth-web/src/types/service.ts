import type { PagedResult } from './security'

export type ServicePriority = 'P1' | 'P2' | 'P3' | 'P4'
export type ServiceStatus = 'New' | 'InProgress' | 'Waiting' | 'Resolved' | 'Closed' | 'Cancelled'
export type ServiceRecordType = 'Note' | 'Diagnosis' | 'Action' | 'StatusChange' | 'Assignment'

export interface ServiceTicketSummary { id: number; code: string; customerId: number; customerCode: string; customerName: string; projectId?: number | null; projectCode?: string | null; projectName?: string | null; equipmentId?: number | null; equipmentCode?: string | null; equipmentName?: string | null; title: string; priority: ServicePriority; status: ServiceStatus; assignedUserId?: number | null; assignedDisplayName?: string | null; reportedAtUtc: string; downtimeMinutes: number; isArchived: boolean; recordCount: number; version: number; updatedAtUtc: string }
export interface ServiceRecordDetails { id: number; serviceTicketId: number; recordType: ServiceRecordType; content: string; fromStatus?: ServiceStatus | null; toStatus?: ServiceStatus | null; durationMinutes?: number | null; occurredAtUtc: string; createdByUserId?: number | null; createdByDisplayName?: string | null; version: number; createdAtUtc: string; updatedAtUtc: string }
export interface ServiceAssignee { userId: number; username: string; displayName: string; email?: string | null }
export interface ServiceTicketDetails extends ServiceTicketSummary { description?: string | null; respondedAtUtc?: string | null; resolvedAtUtc?: string | null; closedAtUtc?: string | null; rootCause?: string | null; solution?: string | null; archivedAtUtc?: string | null; createdAtUtc: string; records: ServiceRecordDetails[] }
export interface ServiceTicketInput { customerId: number; projectId?: number | null; equipmentId?: number | null; title: string; description?: string | null; priority: ServicePriority; reportedAtUtc?: string | null; assignedUserId?: number | null }
export interface UpdateServiceTicketInput extends Omit<ServiceTicketInput, 'assignedUserId'> { rootCause?: string | null; solution?: string | null; downtimeMinutes: number; version: number }
export interface ServiceRecordInput { recordType: 'Note' | 'Diagnosis' | 'Action'; content: string; durationMinutes?: number | null; occurredAtUtc?: string | null }
export interface UpdateServiceRecordInput extends ServiceRecordInput { occurredAtUtc: string; version: number }
export interface ServiceTicketListParameters { page: number; pageSize: number; search?: string; archive?: 'active' | 'archived' | 'all'; priority?: ServicePriority; status?: ServiceStatus; customerId?: number; projectId?: number; equipmentId?: number; assignedUserId?: number; sortBy?: 'code' | 'title' | 'priority' | 'status' | 'customer' | 'reportedAt' | 'updatedAt'; sortDescending?: boolean }
export type ServiceTicketPage = PagedResult<ServiceTicketSummary>
