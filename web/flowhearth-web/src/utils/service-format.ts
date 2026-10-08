import type { ServicePriority, ServiceRecordType, ServiceStatus } from '../types/service'

export const servicePriorities: ServicePriority[] = ['P1', 'P2', 'P3', 'P4']
export const serviceStatuses: ServiceStatus[] = ['New', 'InProgress', 'Waiting', 'Resolved', 'Closed', 'Cancelled']
const statusLabels: Record<ServiceStatus, string> = { New: '新建', InProgress: '处理中', Waiting: '等待中', Resolved: '已解决', Closed: '已关闭', Cancelled: '已取消' }
const recordLabels: Record<ServiceRecordType, string> = { Note: '备注', Diagnosis: '诊断', Action: '处理', StatusChange: '状态变化', Assignment: '指派变化' }
export function serviceStatusLabel(value: ServiceStatus) { return statusLabels[value] }
export function servicePriorityLabel(value: ServicePriority) { return ({ P1: 'P1 紧急', P2: 'P2 高', P3: 'P3 普通', P4: 'P4 低' } as const)[value] }
export function servicePriorityTagType(value: ServicePriority) { return value === 'P1' ? 'danger' : value === 'P2' ? 'warning' : value === 'P3' ? 'primary' : 'info' }
export function serviceStatusTagType(value: ServiceStatus) { return value === 'Resolved' || value === 'Closed' ? 'success' : value === 'Cancelled' ? 'info' : value === 'Waiting' ? 'warning' : 'primary' }
export function serviceRecordTypeLabel(value: ServiceRecordType) { return recordLabels[value] }
export function serviceStatusTargets(value: ServiceStatus): ServiceStatus[] { return ({ New: ['InProgress', 'Waiting', 'Resolved', 'Cancelled'], InProgress: ['Waiting', 'Resolved', 'Cancelled'], Waiting: ['InProgress', 'Resolved', 'Cancelled'], Resolved: ['InProgress', 'Closed'], Closed: [], Cancelled: [] } as Record<ServiceStatus, ServiceStatus[]>)[value] }
export function isTerminalServiceStatus(value: ServiceStatus) { return value === 'Closed' || value === 'Cancelled' }
export function formatDowntime(minutes: number) { const hours = Math.floor(minutes / 60); const rest = minutes % 60; return hours ? `${hours} 小时 ${rest} 分钟` : `${rest} 分钟` }
